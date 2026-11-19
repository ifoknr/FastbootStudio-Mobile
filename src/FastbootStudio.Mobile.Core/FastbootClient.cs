using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;

namespace FastbootStudio.Mobile.Core
{
    /// <summary>One USB connection to one phone in fastboot: bulk out and bulk in.</summary>
    public interface IFastbootTransport
    {
        /// <summary>Writes all of <paramref name="count"/> bytes or throws.</summary>
        void Write(byte[] buffer, int offset, int count);

        /// <summary>Reads one response packet (up to 64 bytes for a reply); returns the bytes read.</summary>
        int Read(byte[] buffer, int timeoutMs);
    }

    public sealed class FastbootException : Exception
    {
        public FastbootException(string message) : base(message) { }

        public FastbootException(string message, bool noReply) : base(message) { NoReply = noReply; }

        /// <summary>The phone answered nothing at all (the link is gone or silent), as against answering FAIL.</summary>
        public bool NoReply { get; }
    }

    /// <summary>
    /// The fastboot USB protocol (system/core/fastboot/README.md): a command of at most 64
    /// ASCII bytes, answered by INFO lines and one OKAY, FAIL or DATA. Only what the app needs:
    /// getvar, download, flash, reboot. The transport is one phone's connection, so every
    /// command goes to that phone and no other.
    /// </summary>
    public sealed class FastbootClient
    {
        public const int MaxCommand = 64;
        const int ReplyTimeout = 30000;
        const int FlashTimeout = 120000;
        const int RebootTimeout = 5000;

        readonly IFastbootTransport transport;

        public FastbootClient(IFastbootTransport transport)
        {
            this.transport = transport ?? throw new ArgumentNullException(nameof(transport));
        }

        /// <summary>INFO lines of the last command, in order.</summary>
        public List<string> Info { get; } = new List<string>();

        public string GetVar(string name)
        {
            // Names such as "has-slot:boot" or "partition-size:init_boot_a": letters, digits and - _ : . only.
            if (string.IsNullOrEmpty(name) || !System.Text.RegularExpressions.Regex.IsMatch(name, "^[A-Za-z0-9_.-]+(:[A-Za-z0-9_.-]+)?$"))
                throw new ArgumentException("bad variable name", nameof(name));
            return Command("getvar:" + name, ReplyTimeout);
        }

        /// <summary>getvar, or null when the bootloader does not know the variable.</summary>
        public string TryGetVar(string name)
        {
            try { return GetVar(name); }
            catch (FastbootException) { return null; }
        }

        /// <summary>max-download-size: hex ("0x10000000") or decimal; null when not reported.</summary>
        public long? MaxDownloadSize()
        {
            return ParseSize(TryGetVar("max-download-size"));
        }

        /// <summary>
        /// A size as a bootloader prints it: hex ("0x10000000") or decimal. Sizes have no sign,
        /// so "0xFFFFFFFFFFFFFFFF" is the largest size there is, not -1; null when it is not a number.
        /// </summary>
        public static long? ParseSize(string value)
        {
            value = (value ?? "").Trim();
            bool hex = value.StartsWith("0x", StringComparison.OrdinalIgnoreCase);
            if (hex)
                value = value.Substring(2);
            if (!ulong.TryParse(value, hex ? NumberStyles.AllowHexSpecifier : NumberStyles.None, CultureInfo.InvariantCulture, out ulong size))
                return null;
            return size > long.MaxValue ? long.MaxValue : (long)size;
        }

        /// <summary>
        /// The allowed partitions the bootloader has, with their sizes ("partition-size:boot_a").
        /// A bootloader that answers no partition-size at all still says which partitions have
        /// slots: then those it knows are listed with size -1 (unknown), and the bootloader
        /// itself refuses a flash to one it does not have.
        /// </summary>
        public Dictionary<string, long> AllowedPartitions()
        {
            Dictionary<string, long> sized = new Dictionary<string, long>(StringComparer.Ordinal);
            List<string> known = new List<string>();
            foreach (string b in FlashTargets.Allowed)
            {
                string hasSlot = (TryGetVar("has-slot:" + b) ?? "").Trim();
                string[] names = hasSlot == "yes" ? new[] { b + "_a", b + "_b" } : new[] { b };
                if (hasSlot == "yes" || hasSlot == "no")
                    known.AddRange(names);
                foreach (string name in names)
                {
                    long? bytes = ParseSize(TryGetVar("partition-size:" + name));
                    if (bytes.HasValue)
                        sized[name] = bytes.Value;
                }
            }
            if (sized.Count == 0)
                foreach (string name in known)
                    sized[name] = -1;
            return sized;
        }

        /// <summary>Sends an image into the bootloader's buffer ("download:%08x", DATA, the bytes, OKAY).</summary>
        /// <param name="cancel">Checked before every piece: a cancelled download throws OperationCanceledException with nothing written to any partition (the image only ever reached the bootloader's memory).</param>
        public void Download(Stream data, long length, Action<long> progress = null, CancellationToken cancel = default)
        {
            if (length <= 0 || length > uint.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(length));
            Send("download:" + length.ToString("x8"));
            string reply = ReadReply(ReplyTimeout, out string kind);
            if (kind != "DATA")
                throw new FastbootException(kind == "FAIL" ? reply : "unexpected reply to download: " + kind + reply);
            long accepted;
            try { accepted = Convert.ToInt64(reply.Trim(), 16); }
            catch (Exception e) when (e is FormatException || e is OverflowException || e is ArgumentException)
            {
                throw new FastbootException("unexpected reply to download: DATA" + reply);
            }
            if (accepted != length)
                throw new FastbootException("the phone accepts " + accepted + " bytes, not " + length);

            byte[] buffer = new byte[1 << 20];
            long sent = 0;
            while (sent < length)
            {
                cancel.ThrowIfCancellationRequested();
                int want = (int)Math.Min(buffer.Length, length - sent);
                int read = ReadFully(data, buffer, want);
                if (read != want)
                    throw new FastbootException("the file ended early");
                transport.Write(buffer, 0, read);
                sent += read;
                progress?.Invoke(sent);
            }
            Expect(ReadReply(ReplyTimeout, out kind), kind);
        }

        /// <summary>Writes the downloaded image to a partition, named with its slot ("boot_a").</summary>
        public void Flash(string partition)
        {
            if (!FlashTargets.IsAllowed(FlashTargets.BaseName(partition)))
                throw new FastbootException("refusing to flash " + partition);
            Command("flash:" + partition, FlashTimeout);
        }

        /// <returns>
        /// True when the bootloader answered OKAY. False when the command went out and the phone
        /// then said nothing: some bootloaders restart at once without answering, and the link
        /// dropping right after "reboot" is that restart. A FAIL is still an error.
        /// </returns>
        public bool Reboot(string target = null)
        {
            if (target != null && target != "bootloader" && target != "recovery" && target != "fastboot")
                throw new ArgumentException("bad reboot target", nameof(target));
            try
            {
                Command(target == null ? "reboot" : "reboot-" + target, RebootTimeout);
                return true;
            }
            catch (FastbootException e) when (e.NoReply)
            {
                return false;
            }
        }

        /// <summary>
        /// The real partition name for a base name: with the active slot ("_a") when the
        /// bootloader says the partition has slots, as the fastboot tool does.
        /// </summary>
        public string SlottedName(string baseName)
        {
            string hasSlot = TryGetVar("has-slot:" + baseName);
            if (hasSlot != "yes")
                return baseName;
            string slot = (TryGetVar("current-slot") ?? "").Trim().TrimStart('_');
            if (slot != "a" && slot != "b")
                throw new FastbootException("the phone did not say which slot is active");
            return baseName + "_" + slot;
        }

        string Command(string command, int timeoutMs)
        {
            Send(command);
            string reply = ReadReply(timeoutMs, out string kind);
            Expect(reply, kind);
            return reply;
        }

        void Expect(string reply, string kind)
        {
            if (kind == "FAIL")
                throw new FastbootException(reply.Length > 0 ? reply : "the phone refused");
            if (kind != "OKAY")
                throw new FastbootException("unexpected reply: " + kind + reply);
        }

        void Send(string command)
        {
            byte[] bytes = Encoding.ASCII.GetBytes(command);
            if (bytes.Length > MaxCommand)
                throw new ArgumentException("fastboot commands are at most 64 bytes", nameof(command));
            Info.Clear();
            transport.Write(bytes, 0, bytes.Length);
        }

        /// <summary>Reads until OKAY, FAIL or DATA; INFO (and TEXT) lines are kept in <see cref="Info"/>.</summary>
        string ReadReply(int timeoutMs, out string kind)
        {
            byte[] packet = new byte[256];
            for (int lines = 0; lines < 10000; lines++)
            {
                int read = transport.Read(packet, timeoutMs);
                if (read < 4)
                    throw new FastbootException(read <= 0 ? "no reply from the phone" : "short reply from the phone", read <= 0);
                string text = Encoding.ASCII.GetString(packet, 0, read);
                kind = text.Substring(0, 4);
                string rest = text.Substring(4);
                if (kind == "INFO" || kind == "TEXT")
                {
                    Info.Add(rest);
                    continue;
                }
                if (kind == "OKAY" || kind == "FAIL" || kind == "DATA")
                    return rest;
                throw new FastbootException("unknown reply: " + text);
            }
            throw new FastbootException("too many INFO lines");
        }

        static int ReadFully(Stream stream, byte[] buffer, int count)
        {
            int total = 0;
            while (total < count)
            {
                int n = stream.Read(buffer, total, count - total);
                if (n <= 0)
                    break;
                total += n;
            }
            return total;
        }
    }
}
