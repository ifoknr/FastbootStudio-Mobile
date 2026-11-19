using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Android.App;
using Android.Content;
using Android.Hardware.Usb;
using Android.OS;
using FastbootEnhance.Core.Adb;
using FastbootStudio.Mobile.Core;

namespace FastbootStudio.Mobile.Native
{
    /// <summary>
    /// The OTG menu: another phone in fastboot on the USB port. Every command goes through
    /// the one connection opened to the phone the person picked, whose serial was read when
    /// it was opened and is read again before a flash.
    /// </summary>
    public sealed class Otg
    {
        public const string PermissionAction = "com.ifoknr.fastbootstudio.mobile.USB_PERMISSION";

        readonly Context activity;
        readonly UsbManager usb;
        readonly object gate = new object();
        TaskCompletionSource<bool> permission;

        UsbDevice device;
        UsbDeviceConnection connection;
        UsbInterface claimed;
        FastbootClient client;

        // Read without the gate, which a flash holds from its first byte to the bootloader's answer:
        // the open device's name, the send that can still be stopped, and whether the phone was unplugged.
        volatile string openName;
        volatile CancellationTokenSource sending;
        volatile bool unplugged;

        static Otg shared;

        /// <summary>
        /// One for the whole app: the connection and a flash running on it outlive the activity
        /// when Android makes it again.
        /// </summary>
        public static Otg Shared(Context context)
        {
            lock (typeof(Otg))
                return shared ??= new Otg(context.ApplicationContext ?? context);
        }

        public Otg(Context activity)
        {
            this.activity = activity;
            usb = (UsbManager)activity.GetSystemService(Context.UsbService);
        }

        /// <summary>What the connected phone said when it was opened; null when nothing is open.</summary>
        public OtgPhone Phone { get; private set; }

        /// <summary>The fastboot interface: class 255, subclass 66, protocol 3, with a bulk in and a bulk out.</summary>
        static bool FastbootInterface(UsbDevice d, out UsbInterface iface, out UsbEndpoint input, out UsbEndpoint output)
        {
            iface = null; input = null; output = null;
            for (int i = 0; i < d.InterfaceCount; i++)
            {
                UsbInterface f = d.GetInterface(i);
                if ((int)f.InterfaceClass != 255 || (int)f.InterfaceSubclass != 66 || (int)f.InterfaceProtocol != 3)
                    continue;
                UsbEndpoint @in = null, @out = null;
                for (int e = 0; e < f.EndpointCount; e++)
                {
                    UsbEndpoint ep = f.GetEndpoint(e);
                    if (ep.Type != UsbAddressing.XferBulk)
                        continue;
                    if (ep.Direction == UsbAddressing.In) @in ??= ep; else @out ??= ep;
                }
                if (@in != null && @out != null)
                {
                    iface = f; input = @in; output = @out;
                    return true;
                }
            }
            return false;
        }

        /// <summary>The phones in fastboot on the port now.</summary>
        public JsonArray Scan()
        {
            JsonArray list = new JsonArray();
            foreach (UsbDevice d in usb.DeviceList?.Values ?? Enumerable.Empty<UsbDevice>())
            {
                if (!FastbootInterface(d, out _, out _, out _))
                    continue;
                list.Add(new JsonObject
                {
                    ["id"] = d.DeviceName, ["name"] = d.ProductName ?? "", ["maker"] = d.ManufacturerName ?? "",
                    ["vid"] = d.VendorId.ToString("x4"), ["pid"] = d.ProductId.ToString("x4"),
                    ["permitted"] = usb.HasPermission(d), ["open"] = openName != null && openName == d.DeviceName,
                });
            }
            return list;
        }

        /// <summary>Called by the activity's receiver with the answer to the USB permission request.</summary>
        public void PermissionAnswered(bool granted) => permission?.TrySetResult(granted);

        /// <summary>Called when a USB device goes away: a connection to it is closed, never reused.</summary>
        /// <param name="then">Run once the connection is closed (or at once when the device was not the open one).</param>
        public void Detached(UsbDevice d, Action then = null)
        {
            // Called on the main thread, which must never wait for a flash: the gate is taken on
            // another thread. A send in progress is told now, so it ends as failed without
            // waiting for the USB timeout.
            string open = openName;
            if (d == null || open == null || d.DeviceName != open)
            {
                then?.Invoke();
                return;
            }
            unplugged = true;
            try { sending?.Cancel(); } catch (ObjectDisposedException) { }
            Task.Run(() =>
            {
                lock (gate)
                {
                    if (openName == open)
                    {
                        Log.Add("otg: " + (Phone?.Serial ?? "device") + " unplugged");
                        CloseLocked();
                    }
                }
                then?.Invoke();
            });
        }

        /// <summary>
        /// Stops a send that has not finished: the image has only reached the bootloader's memory,
        /// so nothing is written. False once the bootloader is writing, which cannot be stopped.
        /// </summary>
        public bool Cancel()
        {
            CancellationTokenSource s = sending;
            if (s == null)
                return false;
            try { s.Cancel(); } catch (ObjectDisposedException) { return false; }
            Log.Add("otg: cancel asked");
            return true;
        }

        /// <summary>Asks for the USB permission when needed, opens the phone and reads what it is.</summary>
        public async Task<OtgPhone> Connect(string id)
        {
            UsbDevice d = usb.DeviceList?.Values.FirstOrDefault(x => x.DeviceName == id);
            if (d == null || !FastbootInterface(d, out _, out _, out _))
                throw new BridgeError("no-device");
            if (!usb.HasPermission(d))
            {
                // Answered on the main thread; opening the phone and reading it goes on elsewhere.
                permission = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                Intent intent = new Intent(PermissionAction);
                intent.SetPackage(activity.PackageName);
                PendingIntentFlags flags = PendingIntentFlags.UpdateCurrent | (OperatingSystem.IsAndroidVersionAtLeast(31) ? PendingIntentFlags.Mutable : 0);
                usb.RequestPermission(d, PendingIntent.GetBroadcast(activity, 0, intent, flags));
                Task done = await Task.WhenAny(permission.Task, Task.Delay(120000));
                if (done != permission.Task || !permission.Task.Result || !usb.HasPermission(d))
                    throw new BridgeError("usb-denied");
            }
            lock (gate)
            {
                CloseLocked();
                FastbootInterface(d, out UsbInterface iface, out UsbEndpoint input, out UsbEndpoint output);
                UsbDeviceConnection c = usb.OpenDevice(d);
                if (c == null)
                    throw new BridgeError("usb-open-failed");
                if (!c.ClaimInterface(iface, true))
                {
                    c.Close();
                    throw new BridgeError("usb-open-failed");
                }
                device = d; connection = c; claimed = iface; openName = d.DeviceName; unplugged = false;
                client = new FastbootClient(new UsbTransport(c, input, output));
                try
                {
                    Phone = Read(d);
                }
                catch (Exception e) when (e is FastbootException || e is BridgeError)
                {
                    CloseLocked();
                    throw e is BridgeError ? e : new BridgeError("otg-failed: " + e.Message);
                }
                Log.Add("otg: open " + Phone.Serial + " product " + Phone.Product + " slot " + (Phone.Slot ?? "-"));
                return Phone;
            }
        }

        OtgPhone Read(UsbDevice d)
        {
            string usbSerial = null;
            try { usbSerial = d.SerialNumber; } catch (Java.Lang.SecurityException) { }
            string serial = Clean(client.TryGetVar("serialno"));
            // The bootloader and the USB descriptor name the same phone; if not, nothing is written to it.
            if (!string.IsNullOrEmpty(serial) && !string.IsNullOrEmpty(usbSerial) && serial != usbSerial)
                throw new BridgeError("serial-mismatch");
            serial = string.IsNullOrEmpty(serial) ? usbSerial : serial;
            if (string.IsNullOrEmpty(serial))
                throw new BridgeError("no-serial");

            OtgPhone p = new OtgPhone
            {
                Serial = serial,
                Product = Clean(client.TryGetVar("product")),
                Unlocked = Clean(client.TryGetVar("unlocked")),
                Userspace = Clean(client.TryGetVar("is-userspace")) == "yes",
                Bootloader = Clean(client.TryGetVar("version-bootloader")),
                MaxDownload = client.MaxDownloadSize(),
            };
            string slot = Clean(client.TryGetVar("current-slot"))?.TrimStart('_');
            p.Slot = slot == "a" || slot == "b" ? "_" + slot : null;
            // With their sizes, or -1 each when this bootloader answers no partition-size at all.
            foreach (KeyValuePair<string, long> part in client.AllowedPartitions())
                p.Partitions[part.Key] = part.Value;
            return p;
        }

        static string Clean(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();


        /// <summary>
        /// Sends a checked file and writes it. The serial is read again first: if the cable
        /// now leads to another phone, or none, nothing is sent.
        /// </summary>
        public void Flash(string path, string partition, string sha256, Action<long, long> progress)
        {
            lock (gate)
            {
                if (client == null || Phone == null)
                    throw new BridgeError("usb-gone");
                if (!FlashTargets.IsAllowed(FlashTargets.BaseName(partition)) || !Phone.Partitions.ContainsKey(partition))
                    throw new BridgeError("no-partition");
                long length = new FileInfo(path).Length;
                if (Phone.MaxDownload.HasValue && length > Phone.MaxDownload.Value)
                    throw new BridgeError("too-big-for-download");
                if (Sha256Sums.HashFile(path) != sha256)
                    throw new BridgeError("file-changed");
                string serial;
                try { serial = Clean(client.TryGetVar("serialno")); }
                catch (FastbootException) { serial = null; }
                if (serial != null && serial != Phone.Serial)
                    throw new BridgeError("serial-mismatch");
                CancellationTokenSource stop = new CancellationTokenSource();
                try
                {
                    Log.Add("otg: send " + Path.GetFileName(path) + " " + length + " bytes to " + Phone.Serial);
                    sending = stop;
                    if (unplugged)
                        throw new BridgeError("usb-gone");
                    using (FileStream file = File.OpenRead(path))
                        client.Download(file, length, sent => progress?.Invoke(sent, length), stop.Token);
                    // From here the bootloader writes: that is not stopped half-way.
                    sending = null;
                    Log.Add("otg: flash:" + partition);
                    client.Flash(partition);
                    foreach (string info in client.Info)
                        Log.Add("otg: " + info);
                    Log.Add("otg: done " + partition);
                }
                catch (System.OperationCanceledException)
                {
                    // The bootloader still waits for the rest of the image: the connection is of no more use.
                    Log.Add(unplugged ? "otg: unplugged while sending" : "otg: cancelled while sending, nothing written");
                    bool gone = unplugged;
                    CloseLocked();
                    throw new BridgeError(gone ? "usb-gone" : "cancelled");
                }
                catch (FastbootException e)
                {
                    Log.Add("otg: failed " + e.Message);
                    if (unplugged)
                    {
                        CloseLocked();
                        throw new BridgeError("usb-gone");
                    }
                    throw new BridgeError("otg-failed: " + e.Message);
                }
                finally
                {
                    sending = null;
                    stop.Dispose();
                }
            }
        }

        public void Reboot(string target)
        {
            lock (gate)
            {
                if (client == null)
                    throw new BridgeError("usb-gone");
                try
                {
                    Log.Add("otg: reboot " + (target ?? ""));
                    client.Reboot(target);
                }
                catch (FastbootException e)
                {
                    throw new BridgeError("otg-failed: " + e.Message);
                }
                finally
                {
                    // The phone leaves the bus; it is opened again when it comes back.
                    CloseLocked();
                }
            }
        }

        public void Close()
        {
            lock (gate)
                CloseLocked();
        }

        void CloseLocked()
        {
            try
            {
                if (connection != null && claimed != null)
                    connection.ReleaseInterface(claimed);
                connection?.Close();
            }
            catch (Exception) { }
            connection = null; claimed = null; client = null; device = null; Phone = null; openName = null;
        }

        /// <summary>Bulk transfers in pieces the USB host stack accepts on every Android version.</summary>
        sealed class UsbTransport : IFastbootTransport
        {
            readonly UsbDeviceConnection c;
            readonly UsbEndpoint input, output;
            readonly byte[] chunk = new byte[OperatingSystem.IsAndroidVersionAtLeast(28) ? 131072 : 16384];
            readonly byte[] packet;

            public UsbTransport(UsbDeviceConnection c, UsbEndpoint input, UsbEndpoint output)
            {
                this.c = c; this.input = input; this.output = output;
                packet = new byte[Math.Max(512, input.MaxPacketSize)];
            }

            public void Write(byte[] buffer, int offset, int count)
            {
                while (count > 0)
                {
                    int n = Math.Min(count, chunk.Length);
                    byte[] piece = n == chunk.Length ? chunk : new byte[n];
                    Buffer.BlockCopy(buffer, offset, piece, 0, n);
                    int written = c.BulkTransfer(output, piece, n, 30000);
                    if (written <= 0)
                        throw new FastbootException("USB write failed");
                    offset += written;
                    count -= written;
                }
            }

            public int Read(byte[] buffer, int timeoutMs)
            {
                int read = c.BulkTransfer(input, packet, packet.Length, timeoutMs);
                if (read <= 0)
                    return read;
                int n = Math.Min(read, buffer.Length);
                Buffer.BlockCopy(packet, 0, buffer, 0, n);
                return n;
            }
        }
    }

    /// <summary>The phone on the OTG cable as its bootloader described it.</summary>
    public sealed class OtgPhone
    {
        public string Serial { get; set; }
        public string Product { get; set; }
        public string Unlocked { get; set; }
        public bool Userspace { get; set; }
        public string Bootloader { get; set; }
        public long? MaxDownload { get; set; }
        public string Slot { get; set; }

        /// <summary>Allowed partitions the phone has, with their sizes (-1 when the bootloader does not give sizes).</summary>
        public Dictionary<string, long> Partitions { get; } = new Dictionary<string, long>(StringComparer.Ordinal);

        /// <summary>
        /// The identity checks compare files with. The fastboot "product" is a codename, not
        /// ro.product.name, so it is used as the device only: backups are matched by serial.
        /// </summary>
        public PhoneIdentity Identity => new PhoneIdentity
        {
            // Only a partition the bootloader gave a size for is known to be there.
            Serial = Serial, Device = Product, HasInitBoot = Partitions.Any(k => k.Value >= 0 && FlashTargets.BaseName(k.Key) == "init_boot"),
        };

        /// <summary>The partition for a base name on the active slot or the other one; null when the phone has none.</summary>
        public string Target(string baseName, bool otherSlot)
        {
            if (!FlashTargets.IsAllowed(baseName))
                return null;
            string slot = Slot;
            if (slot != null && otherSlot)
                slot = slot == "_a" ? "_b" : "_a";
            return FlashTargets.Resolve(baseName, slot, Partitions.Keys.ToList());
        }

        public JsonObject ToJson() => new JsonObject
        {
            ["serial"] = Serial, ["product"] = Product, ["unlocked"] = Unlocked, ["userspace"] = Userspace,
            ["bootloader"] = Bootloader, ["maxDownload"] = MaxDownload, ["slot"] = Slot,
            ["targets"] = Json.Strings(FlashTargets.Allowed.Where(b => Target(b, false) != null)),
        };
    }
}
