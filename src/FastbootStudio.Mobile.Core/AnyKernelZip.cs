using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;
using FastbootEnhance.Core.Adb;

namespace FastbootStudio.Mobile.Core
{
    /// <summary>
    /// What an AnyKernel3 zip says about itself (anykernel.sh), checked against the phone
    /// before its installer runs. The installer is a root script, so a zip that is not plainly
    /// AnyKernel3, was made for another device, or does not match a pasted SHA-256 is refused.
    /// </summary>
    public sealed class AnyKernelZip
    {
        public const string Script = "anykernel.sh";
        public const string Installer = "META-INF/com/google/android/update-binary";

        public string KernelString { get; private set; }
        public bool DeviceCheck { get; private set; }
        public List<string> Devices { get; } = new List<string>();

        /// <summary>supported.versions as written, e.g. "13 - 15"; empty for any.</summary>
        public string SupportedVersions { get; private set; }

        /// <summary>The partition the script writes ("boot", "vendor_boot", "init_boot"), or "auto".</summary>
        public string Block { get; private set; }

        public string Sha256 { get; private set; }

        /// <summary>The kernel inside ("5.10.107", "android13-5.10"), from the kernel image or, failing that, the zip's name.</summary>
        public KernelCompare.Kernel Kernel { get; private set; }

        public CheckResult Result { get; } = new CheckResult();

        static readonly Regex Property = new Regex(@"^\s*([a-z][a-z0-9_.]*)\s*=\s*(.*?)\s*;?\s*$", RegexOptions.IgnoreCase);

        public static AnyKernelZip Inspect(string zipPath, PhoneIdentity phone, string expectedSha256 = null)
        {
            AnyKernelZip zip = new AnyKernelZip();
            CheckResult result = zip.Result;
            if (zipPath == null || !File.Exists(zipPath) || new FileInfo(zipPath).Length == 0)
            {
                result.Add(CheckLevel.Block, CheckCode.EmptyFile, Path.GetFileName(zipPath ?? ""));
                return zip;
            }
            zip.Sha256 = Sha256Sums.HashFile(zipPath);
            result.Sha256 = zip.Sha256;
            ImageCheck.AddExpected(result, expectedSha256);

            string script;
            try
            {
                using (ZipArchive archive = ZipFile.OpenRead(zipPath))
                {
                    foreach (ZipArchiveEntry entry in archive.Entries)
                    {
                        if (!SafeEntry(entry.FullName))
                        {
                            result.Add(CheckLevel.Block, CheckCode.UnsafeZipEntry, entry.FullName);
                            return zip;
                        }
                    }
                    ZipArchiveEntry scriptEntry = archive.GetEntry(Script);
                    if (scriptEntry == null || archive.GetEntry(Installer) == null || scriptEntry.Length > (1 << 20))
                    {
                        result.Add(CheckLevel.Block, CheckCode.NotAnyKernel, Path.GetFileName(zipPath));
                        return zip;
                    }
                    using (StreamReader reader = new StreamReader(scriptEntry.Open()))
                        script = reader.ReadToEnd();
                    if (!ScanScripts(archive, result))
                        return zip;
                    zip.Kernel = ReadKernel(archive);
                }
            }
            catch (InvalidDataException)
            {
                result.Add(CheckLevel.Block, CheckCode.NotAnyKernel, Path.GetFileName(zipPath));
                return zip;
            }

            zip.Parse(script);
            if (zip.KernelString == null && zip.Block == null)
            {
                result.Add(CheckLevel.Block, CheckCode.NotAnyKernel, Path.GetFileName(zipPath));
                return zip;
            }
            zip.CheckDevice(phone);
            zip.CheckAndroid(phone);
            // The kernel image says which kernel it is; when it cannot be read, the zip's name or kernel.string often do.
            zip.Kernel ??= KernelCompare.FromText(Path.GetFileName(zipPath)) ?? KernelCompare.FromText(zip.KernelString);
            KernelCompare.Add(result, zip.Kernel, phone?.KernelRelease);
            if (string.IsNullOrEmpty((expectedSha256 ?? "").Trim()))
                result.Add(CheckLevel.Notice, CheckCode.NoFingerprint, Path.GetFileName(zipPath));
            return zip;
        }

        /// <summary>The kernel image at the top of the zip (Image, Image.gz, Image.lz4, Image.gz-dtb, zImage...), read.</summary>
        static KernelCompare.Kernel ReadKernel(ZipArchive archive)
        {
            foreach (ZipArchiveEntry e in archive.Entries)
            {
                string n = e.FullName;
                if (n.Contains('/') || !(n.StartsWith("Image", StringComparison.Ordinal) || n.StartsWith("zImage", StringComparison.Ordinal) || n == "kernel"))
                    continue;
                string release = KernelCompare.ReadRelease(e.Open(), e.Length);
                KernelCompare.Kernel k = KernelCompare.Parse(release);
                if (k != null)
                    return k;
            }
            return null;
        }

        /// <summary>
        /// The installer runs as root, so its scripts are read before anything runs: a binary
        /// installer cannot be read and is refused, and so is a script that does what no kernel
        /// installer does (<see cref="ScriptScan"/>). Never overridable.
        /// </summary>
        static bool ScanScripts(ZipArchive archive, CheckResult result)
        {
            ZipArchiveEntry installer = archive.GetEntry(Installer);
            using (Stream s = installer.Open())
            {
                byte[] head = new byte[4];
                int n = s.Read(head, 0, 4);
                if (n == 4 && head[0] == 0x7F && head[1] == (byte)'E' && head[2] == (byte)'L' && head[3] == (byte)'F')
                {
                    result.Add(CheckLevel.Block, CheckCode.DangerousScript, "binary", Installer, "");
                    return false;
                }
            }
            bool clean = true;
            List<string> texts = new List<string>();
            HashSet<string> binaries = new HashSet<string>(StringComparer.Ordinal);
            // Every text file, not only *.sh: anykernel.sh can source any file it carries.
            foreach (ZipArchiveEntry entry in archive.Entries.Where(e => e.Length > 0 && !e.FullName.EndsWith("/", StringComparison.Ordinal)))
            {
                bool script = ScriptScan.IsScript(entry.FullName);
                string text = ScriptScan.ReadText(entry.Open(), entry.Length, script, out bool tooBig);
                if (tooBig && script)
                {
                    result.Add(CheckLevel.Block, CheckCode.DangerousScript, "too-big", entry.FullName, "");
                    clean = false;
                    continue;
                }
                if (text == null)
                {
                    binaries.Add(entry.Name);
                    continue;
                }
                texts.Add(text);
                foreach (ScriptScan.Hit hit in ScriptScan.Scan(text).Take(3))
                {
                    result.Add(CheckLevel.Block, CheckCode.DangerousScript, hit.Reason, entry.FullName, hit.Line);
                    clean = false;
                }
            }
            // A script that runs a file which could not be read as text hides what it does.
            foreach (string text in texts)
                foreach (string line in ScriptScan.Sourced(text).Where(binaries.Contains).Take(1))
                {
                    result.Add(CheckLevel.Block, CheckCode.DangerousScript, "hidden", line, "");
                    clean = false;
                }
            return clean;
        }

        /// <summary>Entries stay inside the folder the zip is unpacked to: no "..", no absolute paths.</summary>
        internal static bool SafeEntry(string name)
        {
            if (string.IsNullOrEmpty(name))
                return false;
            string n = name.Replace('\\', '/');
            if (n.StartsWith("/", StringComparison.Ordinal) || n.Contains(':'))
                return false;
            return n.Split('/').All(part => part != "..");
        }

        internal void Parse(string script)
        {
            foreach (string raw in script.Replace("\r", "").Split('\n'))
            {
                string line = raw.Trim();
                if (line.StartsWith("#", StringComparison.Ordinal))
                    continue;
                Match m = Property.Match(line);
                if (!m.Success)
                    continue;
                string key = m.Groups[1].Value, value = Unquote(m.Groups[2].Value);
                if (key.Equals("kernel.string", StringComparison.OrdinalIgnoreCase))
                    KernelString = value;
                else if (key.Equals("do.devicecheck", StringComparison.OrdinalIgnoreCase))
                    DeviceCheck = value == "1";
                else if (key.StartsWith("device.name", StringComparison.OrdinalIgnoreCase) && value.Length > 0)
                    Devices.Add(value);
                else if (key.Equals("supported.versions", StringComparison.OrdinalIgnoreCase))
                    SupportedVersions = value;
                else if (key.Equals("block", StringComparison.OrdinalIgnoreCase) || key.Equals("BLOCK", StringComparison.Ordinal))
                    Block = BlockName(value);
            }
        }

        static string Unquote(string value)
        {
            value = value.Trim().TrimEnd(';').Trim();
            if (value.Length >= 2 && (value[0] == '"' || value[0] == '\'') && value[value.Length - 1] == value[0])
                value = value.Substring(1, value.Length - 2);
            return value;
        }

        /// <summary>"/dev/block/bootdevice/by-name/boot" or "boot" is "boot".</summary>
        static string BlockName(string value)
        {
            int slash = value.LastIndexOf('/');
            return slash >= 0 ? value.Substring(slash + 1) : value;
        }

        void CheckDevice(PhoneIdentity phone)
        {
            if (!DeviceCheck || Devices.Count == 0)
            {
                Result.Add(CheckLevel.Warn, CheckCode.DeviceCheckOff, phone?.Device ?? "");
                return;
            }
            string listed = string.Join(", ", Devices);
            // The names AnyKernel3's own device check compares, so this is never stricter than the zip.
            string match = phone?.DeviceNames.FirstOrDefault(name => !string.IsNullOrEmpty(name) && Devices.Contains(name, StringComparer.OrdinalIgnoreCase));
            if (match != null)
                Result.Add(CheckLevel.Ok, CheckCode.DeviceListed, listed, match);
            else
                Result.AddOverridable(CheckCode.DeviceNotListed, listed, phone?.Device ?? "");
        }

        /// <summary>supported.versions: "13", "11 - 14", "11-14"; empty means any Android.</summary>
        void CheckAndroid(PhoneIdentity phone)
        {
            string range = (SupportedVersions ?? "").Replace(" ", "");
            int phoneVersion;
            if (range.Length == 0 || !TryMajor(phone?.AndroidRelease, out phoneVersion))
                return;
            string[] parts = range.Split('-');
            int low, high;
            if (!TryMajor(parts[0], out low) || !TryMajor(parts[parts.Length - 1], out high))
                return;
            if (phoneVersion >= low && phoneVersion <= high)
                Result.Add(CheckLevel.Ok, CheckCode.AndroidSupported, phone.AndroidRelease, SupportedVersions);
            else
                Result.AddOverridable(CheckCode.AndroidNotSupported, phone.AndroidRelease, SupportedVersions);
        }

        static bool TryMajor(string version, out int major)
        {
            major = 0;
            if (string.IsNullOrEmpty(version))
                return false;
            string digits = new string(version.TakeWhile(char.IsDigit).ToArray());
            return int.TryParse(digits, out major);
        }
    }
}
