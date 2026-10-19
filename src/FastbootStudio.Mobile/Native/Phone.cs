using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Android.Content;
using Android.OS;
using FastbootStudio.Mobile.Core;

namespace FastbootStudio.Mobile.Native
{
    /// <summary>This phone: what it is, its partitions, and the flashing and backups done on it as root.</summary>
    public sealed class Phone
    {
        readonly Context context;
        readonly SuShell su = SuShell.Instance;
        public RootFlasher Flasher { get; }
        public bool Root { get; private set; }
        public DeviceProps Props { get; private set; }
        public List<string> Partitions { get; private set; } = new List<string>();

        public Phone(Context context)
        {
            this.context = context;
            Flasher = new RootFlasher(su);
        }

        public string CacheDir => context.CacheDir.AbsolutePath;

        public PhoneIdentity Identity => Props?.Identity(Partitions.Any(p => FlashTargets.BaseName(p) == "init_boot"));

        /// <summary>Reads the phone again: getprop works without root; partitions, root manager and SELinux need it.</summary>
        public JsonObject Refresh()
        {
            Root = su.Available();
            // Apps cannot read ro.serialno since Android 10; root can, and backups are named by it.
            string props = Root ? su.Run("getprop").Output : "";
            Props = DeviceProps.Parse(props.Length > 0 ? props : Run("getprop"), Java.Lang.JavaSystem.GetProperty("os.version"));
            Partitions = Root ? Flasher.Partitions() : new List<string>();
            string manager = null, selinux = null, kernelArgs = null;
            if (Root)
            {
                manager = su.Run("if [ -x /data/adb/ksud ]; then echo \"KernelSU $(/data/adb/ksud -V 2>/dev/null | cut -d' ' -f2)\"; "
                    + "elif [ -x /data/adb/apd ]; then echo APatch; elif command -v magisk >/dev/null 2>&1; then echo \"Magisk $(magisk -v | cut -d: -f1)\"; fi").Output.Trim();
                selinux = su.Run("getenforce").Output.Trim();
                kernelArgs = su.Run("cat /proc/bootconfig 2>/dev/null; echo; cat /proc/cmdline 2>/dev/null").Output;
            }
            BatteryManager battery = (BatteryManager)context.GetSystemService(Context.BatteryService);
            int level = battery?.GetIntProperty((int)BatteryProperty.Capacity) ?? -1;
            string slot = Props.Slot;
            BootState boot = Props.Boot(Root, kernelArgs);
            JsonObject o = new JsonObject
            {
                ["root"] = Root, ["rootManager"] = manager, ["selinux"] = selinux,
                ["model"] = Props.Model, ["name"] = Props.Get("ro.product.marketname") ?? Props.Get("ro.product.vendor.marketname"), ["brand"] = Props.Brand, ["codename"] = Props.Device, ["product"] = Props.Product,
                ["serial"] = Props.Serial, ["android"] = Props.Android, ["sdk"] = Props.Sdk, ["patch"] = Props.PatchLevel,
                ["slot"] = slot, ["ab"] = Props.AB, ["dynamic"] = Props.Dynamic, ["platform"] = Props.Platform, ["abi"] = Props.Abi,
                ["vendor"] = Props.Vendor, ["kernel"] = Props.KernelRelease, ["kmi"] = Props.Kmi, ["avb"] = boot.Avb,
                ["unlocked"] = boot.Unlocked, ["bootHidden"] = boot.Hidden, ["battery"] = level,
                ["targets"] = Json.Strings(FlashTargets.Allowed.Where(b => FlashTargets.Resolve(b, slot, Partitions) != null)),
            };
            return o;
        }

        /// <summary>A plain command as the app (no root).</summary>
        static string Run(string command)
        {
            try
            {
                using (System.Diagnostics.Process p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("sh", new[] { "-c", command })
                {
                    RedirectStandardOutput = true, UseShellExecute = false,
                }))
                {
                    string output = p.StandardOutput.ReadToEnd();
                    p.WaitForExit();
                    return output;
                }
            }
            catch (Exception e)
            {
                Log.Add(command + " failed: " + e.Message);
                return "";
            }
        }

        /// <summary>The real partition for a base name on "current" or "other" slot, or null.</summary>
        public string Target(string baseName, bool otherSlot)
        {
            string slot = Props?.Slot;
            if (slot != null && otherSlot)
                slot = slot == "_a" ? "_b" : "_a";
            return FlashTargets.Resolve(baseName, slot, Partitions);
        }

        // ---------------------------------------------------------------- backups

        public List<BackupEntry> Backups()
        {
            if (!Root)
                return new List<BackupEntry>();
            return BackupListing.Parse(su.Run(BackupListing.Script(RootFlasher.BackupRoot)).Output);
        }

        public List<string> Verify(string name)
        {
            if (!BackupListing.IsName(name))
                return null;
            ShellResult r = su.Run("cd " + Sh.Q(RootFlasher.BackupRoot + "/" + name) + " && sha256sum -c SHA256SUMS 2>&1");
            return BackupListing.Failed(r.Output + "\n" + r.Error);
        }

        public bool Delete(string name)
        {
            if (!BackupListing.IsName(name))
                return false;
            return su.Run("rm -rf " + Sh.Q(RootFlasher.BackupRoot + "/" + name)).Ok;
        }

        /// <summary>
        /// Copies one image of a backup, with its SHA256SUMS and backup-info.txt, into the app's
        /// cache, so the check and the flash both use that copy and nothing can change in between.
        /// </summary>
        public BackupFolder Fetch(string name, string file)
        {
            if (!BackupListing.IsName(name) || string.IsNullOrEmpty(file) || file.Contains('/') || file.StartsWith("."))
                return null;
            string local = Path.Combine(CacheDir, "backup", name);
            if (Directory.Exists(local))
                Directory.Delete(local, true);
            string remote = RootFlasher.BackupRoot + "/" + name + "/";
            foreach (string f in new[] { file, "SHA256SUMS", "backup-info.txt" })
                if (!su.CopyOut(remote + f, Path.Combine(local, f)) && f != "backup-info.txt")
                    return null;
            return BackupFolder.Read(local);
        }
    }
}
