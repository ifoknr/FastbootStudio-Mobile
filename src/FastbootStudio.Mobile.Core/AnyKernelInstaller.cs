using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FastbootEnhance.Core.Adb;

namespace FastbootStudio.Mobile.Core
{
    public sealed class KernelOutcome
    {
        public bool Installed { get; internal set; }

        /// <summary>True when the boot partition the zip writes is different afterwards.</summary>
        public bool Changed { get; internal set; }

        public string BackupFolder { get; internal set; }
        public string Error { get; internal set; }

        /// <summary>The installer's ui_print lines and the app's own steps.</summary>
        public List<string> Log { get; } = new List<string>();
    }

    /// <summary>
    /// Runs an AnyKernel3 zip's installer as root, the way kernel flashing apps do
    /// (update-binary 3 1 zip), after the same consent gate as an image and a backup of the
    /// partitions it may write.
    /// </summary>
    public sealed class AnyKernelInstaller
    {
        public const string DefaultWorkDir = "/data/local/tmp/fbs-ak3";

        readonly IRootShell shell;
        readonly RootFlasher flasher;
        readonly string WorkDir;

        public AnyKernelInstaller(IRootShell shell, RootFlasher flasher, string workDir = null)
        {
            this.shell = shell;
            this.flasher = flasher;
            WorkDir = workDir ?? DefaultWorkDir;
        }

        /// <summary>The name typed on a Warn: the partition the zip writes ("boot" for "auto").</summary>
        public static string TypedNameFor(AnyKernelZip zip)
        {
            string block = zip?.Block;
            return string.IsNullOrEmpty(block) || block == "auto" ? "boot" : FlashTargets.BaseName(block);
        }

        public KernelOutcome Install(string zipPath, AnyKernelZip zip, Consent consent, PhoneIdentity phone, string slot, bool backupFirst = true, Action<string> progress = null)
        {
            KernelOutcome outcome = new KernelOutcome();
            void Step(string line) { outcome.Log.Add(line); progress?.Invoke(line); }
            KernelOutcome Fail(string error) { outcome.Error = error; Step("error: " + error); return outcome; }

            if (zip == null || !RootFlasher.Allowed(zip.Result, TypedNameFor(zip), consent))
                return Fail("not-confirmed");
            if (!File.Exists(zipPath) || Sha256Sums.HashFile(zipPath) != zip.Sha256)
                return Fail("file-changed");

            List<string> names = flasher.Partitions();
            List<string> touched = new[] { "boot", "init_boot", "vendor_boot", "vendor_kernel_boot", "dtbo" }
                .Select(b => names.Contains(b + slot) ? b + slot : names.Contains(b) ? b : null)
                .Where(n => n != null).ToList();
            string target = touched.FirstOrDefault(n => FlashTargets.BaseName(n) == TypedNameFor(zip));
            if (target == null)
                return Fail("no-partition");

            if (backupFirst)
            {
                outcome.BackupFolder = flasher.Backup(touched, phone, "before-kernel", Step);
                if (outcome.BackupFolder == null)
                    return Fail("backup-failed");
            }
            string before = Hash(target);

            Step("install " + Path.GetFileName(zipPath));
            string binary = WorkDir + "/META-INF/com/google/android/update-binary";
            ShellResult run = shell.Run("rm -rf " + Sh.Q(WorkDir) + " && mkdir -p " + Sh.Q(WorkDir) + " && cd " + Sh.Q(WorkDir)
                + " && unzip -o -q " + Sh.Q(zipPath) + " META-INF/com/google/android/update-binary"
                + " && sh " + Sh.Q(binary) + " 3 1 " + Sh.Q(zipPath) + "; code=$?; cd /; rm -rf " + Sh.Q(WorkDir) + "; exit $code");
            foreach (string line in Sh.Lines(run.Output))
                Step(line.StartsWith("ui_print", StringComparison.Ordinal) ? line.Substring(8).Trim() : line);
            if (!run.Ok)
                return Fail("install-failed: " + (Sh.Lines(run.Error).LastOrDefault() ?? "exit " + run.ExitCode));
            outcome.Installed = true;
            outcome.Changed = Hash(target) != before;
            Step(outcome.Changed ? "done " + target + " changed" : "done, but " + target + " is unchanged");
            return outcome;
        }

        string Hash(string partition) => Sh.Sha(shell.Run("sha256sum " + Sh.Q(flasher.ByNameDir + "/" + partition)).Output);
    }
}
