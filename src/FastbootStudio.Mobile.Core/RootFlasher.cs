using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using FastbootEnhance.Core.Adb;

namespace FastbootStudio.Mobile.Core
{
    /// <summary>What the person agreed to on the warning sheet.</summary>
    public sealed class Consent
    {
        /// <summary>Continue was pressed on a Notice.</summary>
        public bool NoticeAccepted { get; set; }

        /// <summary>The partition name typed on a Warn ("init_boot").</summary>
        public string TypedName { get; set; }

        public bool DeveloperMode { get; set; }
    }

    public sealed class FlashOutcome
    {
        public bool Written { get; internal set; }
        public bool Verified { get; internal set; }

        /// <summary>The backup of the partition taken before writing, to put it back.</summary>
        public string BackupFolder { get; internal set; }

        public string Error { get; internal set; }
        public List<string> Log { get; } = new List<string>();
    }

    /// <summary>
    /// Writes an image to a partition of this phone as root: checks the consent against the
    /// check result again, backs the partition up in the Fastboot Studio layout, writes with
    /// dd, then reads the partition back and compares its SHA-256 with the file's.
    /// </summary>
    public sealed class RootFlasher
    {
        public static readonly string[] ByNameDirs = { "/dev/block/by-name", "/dev/block/bootdevice/by-name" };
        public const string BackupRoot = "/sdcard/FastbootStudio/Backups";

        readonly IRootShell shell;
        readonly Func<DateTime> now;
        readonly string[] byNameDirs;
        readonly string backupRoot;
        string byName;

        /// <param name="byNameDirs">Where to look for partitions (the phone's by-name folders unless a test says otherwise).</param>
        /// <param name="backupRoot">Where backups go (Backups on the shared storage unless a test says otherwise).</param>
        public RootFlasher(IRootShell shell, Func<DateTime> now = null, string[] byNameDirs = null, string backupRoot = null)
        {
            this.shell = shell;
            this.now = now ?? (() => DateTime.Now);
            this.byNameDirs = byNameDirs ?? ByNameDirs;
            this.backupRoot = backupRoot ?? BackupRoot;
        }

        /// <summary>The by-name folder found by <see cref="Partitions"/>, or null before.</summary>
        public string ByNameDir => byName;

        /// <summary>The by-name folder and the partitions in it.</summary>
        public List<string> Partitions()
        {
            foreach (string dir in byNameDirs)
            {
                ShellResult r = shell.Run("ls " + Sh.Q(dir));
                List<string> names = Sh.Lines(r.Output).Where(Sh.IsPartitionName).ToList();
                if (r.Ok && names.Count > 0)
                {
                    byName = dir;
                    return names;
                }
            }
            return new List<string>();
        }

        public long? PartitionBytes(string partition)
        {
            if (!Sh.IsPartitionName(partition) || (byName == null && Partitions().Count == 0))
                return null;
            ShellResult r = shell.Run("blockdev --getsize64 " + Sh.Q(byName + "/" + partition));
            return r.Ok && long.TryParse(r.Output.Trim(), out long bytes) ? bytes : (long?)null;
        }

        /// <summary>
        /// Whether the consent covers the result: never a Block; a Warn needs the base name
        /// typed exactly; a Notice needs Continue. Developer mode only matters for overridable Blocks.
        /// </summary>
        public static bool Allowed(CheckResult check, string partition, Consent consent)
        {
            if (check == null || consent == null || !check.CanFlashFor(consent.DeveloperMode))
                return false;
            switch (check.VerdictFor(consent.DeveloperMode))
            {
                case CheckLevel.Ok: return true;
                case CheckLevel.Notice: return consent.NoticeAccepted;
                case CheckLevel.Warn: return (consent.TypedName ?? "").Trim() == FlashTargets.BaseName(partition);
                default: return false;
            }
        }

        /// <summary>
        /// Flashes <paramref name="imagePath"/> (a copy in the app's own folder, which root can
        /// read) to <paramref name="partition"/> ("init_boot_a").
        /// </summary>
        public FlashOutcome Flash(string imagePath, string partition, CheckResult check, Consent consent, PhoneIdentity phone,
            bool backupFirst = true, bool readBack = true, Action<string> progress = null)
        {
            FlashOutcome outcome = new FlashOutcome();
            void Step(string line) { outcome.Log.Add(line); progress?.Invoke(line); }
            FlashOutcome Fail(string error) { outcome.Error = error; Step("error: " + error); return outcome; }

            if (!FlashTargets.IsAllowed(FlashTargets.BaseName(partition)) || !Sh.IsPartitionName(partition))
                return Fail("not-allowed");
            if (!Allowed(check, partition, consent))
                return Fail("not-confirmed");
            if (!File.Exists(imagePath))
                return Fail("no-file");
            // The file flashed is the file that was checked: hashed again right before writing.
            string sha = Sha256Sums.HashFile(imagePath);
            if (sha != check.Sha256)
                return Fail("file-changed");
            long length = new FileInfo(imagePath).Length;

            List<string> names = Partitions();
            if (!names.Contains(partition))
                return Fail("no-partition");
            string device = byName + "/" + partition;
            long? size = PartitionBytes(partition);
            if (size == null)
                return Fail("no-size");
            if (length > size.Value)
                return Fail("too-big");
            Step("check ok " + Path.GetFileName(imagePath) + " -> " + partition + " sha " + sha);

            if (backupFirst)
            {
                string folder = Backup(new[] { partition }, phone, "before-flash", Step);
                if (folder == null)
                    return Fail("backup-failed");
                outcome.BackupFolder = folder;
            }

            Step("write " + partition + " " + length + " bytes");
            ShellResult write = shell.Run("dd if=" + Sh.Q(imagePath) + " of=" + Sh.Q(device) + " bs=4194304 conv=notrunc,fsync && sync");
            if (!write.Ok)
                return Fail("write-failed: " + write.Error.Trim());
            outcome.Written = true;

            if (readBack)
            {
                ShellResult back = shell.Run("head -c " + length.ToString(CultureInfo.InvariantCulture) + " " + Sh.Q(device) + " | sha256sum");
                string got = Sh.Sha(back.Output);
                if (got != sha)
                    return Fail("verify-failed: " + (got ?? "no hash"));
                outcome.Verified = true;
                Step("verify ok " + got);
            }
            Step("done " + partition);
            return outcome;
        }

        /// <summary>
        /// Copies partitions to a new folder under Backups in the layout the Windows app and the
        /// Companion use: a hidden .partial- folder renamed when every image and its hash are in.
        /// Returns the folder, or null.
        /// </summary>
        public string Backup(IEnumerable<string> partitions, PhoneIdentity phone, string set, Action<string> progress = null)
        {
            List<string> names = Partitions();
            string model = Safe(phone?.Model ?? "device"), serial = Safe(phone?.Serial ?? "unknown");
            string name = model + "_" + serial + "_" + now().ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            string partial = backupRoot + "/.partial-" + name, final = backupRoot + "/" + name;
            if (!shell.Run("mkdir -p " + Sh.Q(partial)).Ok)
                return null;

            List<string> sums = new List<string>(), table = new List<string>();
            foreach (string partition in partitions)
            {
                if (!Sh.IsPartitionName(partition) || !names.Contains(partition))
                    continue;
                string image = partial + "/" + partition + ".img";
                progress?.Invoke("backup " + partition);
                ShellResult copy = shell.Run("dd if=" + Sh.Q(byName + "/" + partition) + " of=" + Sh.Q(image) + " bs=4194304");
                string sha = copy.Ok ? Sh.Sha(shell.Run("sha256sum " + Sh.Q(image)).Output) : null;
                ShellResult bytes = shell.Run("stat -c %s " + Sh.Q(image));
                if (sha == null || !bytes.Ok)
                {
                    shell.Run("rm -rf " + Sh.Q(partial));
                    return null;
                }
                sums.Add(sha + "  " + partition + ".img");
                table.Add(partition + "\t" + bytes.Output.Trim() + "\t" + sha);
            }
            if (sums.Count == 0)
            {
                shell.Run("rm -rf " + Sh.Q(partial));
                return null;
            }

            string info = "Fastboot Studio backup\n"
                + "date\t" + now().ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture) + "\n"
                + "serial\t" + (phone?.Serial ?? "") + "\nmodel\t" + (phone?.Model ?? "") + "\nproduct\t" + (phone?.Product ?? "")
                + "\nmode\tmobile\nset\t" + set + "\nsource\t" + byName + "\n\npartition\tbytes\tsha256\n" + string.Join("\n", table) + "\n";
            ShellResult written = shell.Run("printf '%s' " + Sh.Q(string.Join("\n", sums) + "\n") + " > " + Sh.Q(partial + "/SHA256SUMS")
                + " && printf '%s' " + Sh.Q(info) + " > " + Sh.Q(partial + "/backup-info.txt")
                + " && mv " + Sh.Q(partial) + " " + Sh.Q(final));
            if (!written.Ok)
            {
                shell.Run("rm -rf " + Sh.Q(partial));
                return null;
            }
            progress?.Invoke("backup ok " + final);
            return final;
        }

        static string Safe(string value)
        {
            string s = new string(value.Select(c => char.IsLetterOrDigit(c) || c == '-' ? c : '_').ToArray()).Trim('_');
            return s.Length == 0 ? "unknown" : s;
        }
    }
}
