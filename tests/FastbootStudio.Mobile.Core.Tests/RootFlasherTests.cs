using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using FastbootEnhance.Core.Adb;
using Xunit;

namespace FastbootStudio.Mobile.Core.Tests
{
    /// <summary>
    /// Runs the flasher's scripts with the real /bin/sh against a temporary "by-name" folder
    /// of plain files, with a blockdev stand-in, and records every script.
    /// </summary>
    sealed class FilePhone : IRootShell, IDisposable
    {
        public readonly string Root = Path.Combine(Path.GetTempPath(), "fbsm-phone-" + Guid.NewGuid().ToString("N"));
        public string ByName => Path.Combine(Root, "by-name");
        public string Backups => Path.Combine(Root, "Backups");
        public readonly List<string> Scripts = new List<string>();
        public Func<string, ShellResult> Override;
        static readonly string Shims = Path.Combine(AppContext.BaseDirectory, "shims");

        public FilePhone(params (string name, int size)[] partitions)
        {
            Directory.CreateDirectory(ByName);
            foreach ((string name, int size) in partitions)
                File.WriteAllBytes(Path.Combine(ByName, name), Enumerable.Range(0, size).Select(i => (byte)(i % 199)).ToArray());
        }

        public RootFlasher Flasher() => new RootFlasher(this, () => new DateTime(2026, 10, 18, 10, 15, 44), new[] { "/nonexistent", ByName }, Backups);

        public byte[] Partition(string name) => File.ReadAllBytes(Path.Combine(ByName, name));

        public ShellResult Run(string script)
        {
            Scripts.Add(script);
            ShellResult forced = Override?.Invoke(script);
            if (forced != null)
                return forced;
            ProcessStartInfo info = new ProcessStartInfo("/bin/sh", new[] { "-c", script })
            {
                RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false,
            };
            info.Environment["PATH"] = Shims + ":" + Environment.GetEnvironmentVariable("PATH");
            using (Process p = Process.Start(info))
            {
                string output = p.StandardOutput.ReadToEnd(), error = p.StandardError.ReadToEnd();
                p.WaitForExit();
                return new ShellResult(p.ExitCode, output, error);
            }
        }

        public void Dispose()
        {
            try { Directory.Delete(Root, true); } catch (IOException) { }
        }
    }

    public class RootFlasherTests
    {
        static CheckResult Checked(string path, string partition, long size, PhoneIdentity phone = null)
            => ImageCheck.FromFile(path, partition, size, phone ?? Samples.Phone);

        [Fact]
        public void Flash_backs_up_writes_and_verifies()
        {
            using FilePhone phone = new FilePhone(("init_boot_a", 16384), ("init_boot_b", 16384), ("persist", 4096));
            using Samples s = new Samples();
            // The stock init_boot on the phone: a real image in the first bytes, the rest of the partition after it.
            byte[] stock = Samples.BootImage(false);
            byte[] partition = phone.Partition("init_boot_a");
            stock.CopyTo(partition, 0);
            partition[stock.Length + 5] = 0xEE;
            File.WriteAllBytes(Path.Combine(phone.ByName, "init_boot_a"), partition);
            byte[] before = phone.Partition("init_boot_a");
            byte[] patched = Samples.BootImage(false);
            patched[5000] ^= 0x5A;
            string image = s.File("magisk_patched.img", patched);
            CheckResult check = Checked(image, "init_boot_a", 16384);
            Assert.Equal(CheckLevel.Warn, check.Verdict);

            FlashOutcome outcome = phone.Flasher().Flash(image, "init_boot_a", check, new Consent { TypedName = "init_boot" }, Samples.Phone);
            Assert.Null(outcome.Error);
            Assert.True(outcome.Written);
            Assert.True(outcome.Verified);
            byte[] after = phone.Partition("init_boot_a");
            byte[] written = File.ReadAllBytes(image);
            Assert.Equal(written, after.Take(written.Length).ToArray());
            Assert.Equal(before.Skip(written.Length), after.Skip(written.Length));
            Assert.Equal(16384, phone.Partition("init_boot_b").Length);

            // The backup holds the partition as it was, in the Windows app's layout, and checks out.
            BackupFolder backup = BackupFolder.Read(outcome.BackupFolder);
            Assert.Equal("Xiaomi_13T_Pro_a1b2c3d4_20261018-101544", backup.Name);
            Assert.Equal("a1b2c3d4", backup.Serial);
            Assert.Equal(before, File.ReadAllBytes(Path.Combine(outcome.BackupFolder, "init_boot_a.img")));
            Assert.Equal(Sha256Sums.HashFile(Path.Combine(outcome.BackupFolder, "init_boot_a.img")), backup.Sums["init_boot_a.img"]);
            Assert.False(Directory.EnumerateDirectories(phone.Backups, ".partial-*").Any());

            // And it can be put back with the same checks.
            CheckResult restore = ImageCheck.FromBackup(backup, "init_boot_a.img", "init_boot_a", 16384, Samples.Phone);
            Assert.Equal(CheckLevel.Ok, restore.Verdict);
            FlashOutcome back = phone.Flasher().Flash(Path.Combine(outcome.BackupFolder, "init_boot_a.img"), "init_boot_a", restore, new Consent(), Samples.Phone, backupFirst: false);
            Assert.True(back.Verified);
            Assert.Equal(before, phone.Partition("init_boot_a"));
        }

        [Fact]
        public void Nothing_is_written_without_the_consent_the_check_asks_for()
        {
            using FilePhone phone = new FilePhone(("init_boot_a", 16384));
            using Samples s = new Samples();
            byte[] before = phone.Partition("init_boot_a");
            string image = s.File("patched.img", Samples.BootImage(false));
            CheckResult warn = Checked(image, "init_boot_a", 16384);
            RootFlasher flasher = phone.Flasher();
            Assert.Equal("not-confirmed", flasher.Flash(image, "init_boot_a", warn, new Consent(), Samples.Phone).Error);
            Assert.Equal("not-confirmed", flasher.Flash(image, "init_boot_a", warn, new Consent { TypedName = "boot" }, Samples.Phone).Error);
            Assert.Equal("not-confirmed", flasher.Flash(image, "init_boot_a", warn, new Consent { NoticeAccepted = true }, Samples.Phone).Error);
            Assert.Equal(before, phone.Partition("init_boot_a"));
            Assert.DoesNotContain(phone.Scripts, x => x.Contains("dd if="));
        }

        [Fact]
        public void A_blocked_file_is_never_written_whatever_was_typed()
        {
            using FilePhone phone = new FilePhone(("boot_a", 16384), ("persist", 8192));
            using Samples s = new Samples();
            string vbmeta = s.File("vbmeta.img", Samples.Vbmeta());
            CheckResult block = Checked(vbmeta, "boot_a", 16384);
            Assert.Equal(CheckLevel.Block, block.Verdict);
            RootFlasher flasher = phone.Flasher();
            Assert.Equal("not-confirmed", flasher.Flash(vbmeta, "boot_a", block, new Consent { TypedName = "boot", NoticeAccepted = true, DeveloperMode = true }, Samples.Phone).Error);
            CheckResult persist = Checked(vbmeta, "persist", 8192);
            Assert.Equal("not-allowed", flasher.Flash(vbmeta, "persist", persist, new Consent { TypedName = "persist", DeveloperMode = true }, Samples.Phone).Error);
            Assert.DoesNotContain(phone.Scripts, x => x.Contains("dd if="));
        }

        [Fact]
        public void A_file_changed_after_the_check_is_not_written()
        {
            using FilePhone phone = new FilePhone(("dtbo_a", 16384));
            using Samples s = new Samples();
            string dtbo = s.File("dtbo.img", Samples.Dtbo());
            CheckResult check = Checked(dtbo, "dtbo_a", 16384);
            File.AppendAllText(dtbo, "tampered");
            Assert.Equal("file-changed", phone.Flasher().Flash(dtbo, "dtbo_a", check, new Consent { TypedName = "dtbo" }, Samples.Phone).Error);
            Assert.DoesNotContain(phone.Scripts, x => x.Contains("dd if="));
        }

        [Fact]
        public void A_partition_the_phone_does_not_have_or_too_small_is_refused()
        {
            using FilePhone phone = new FilePhone(("dtbo_a", 1024));
            using Samples s = new Samples();
            string dtbo = s.File("dtbo.img", Samples.Dtbo());
            Assert.Equal("no-partition", phone.Flasher().Flash(dtbo, "dtbo_b", Checked(dtbo, "dtbo_b", 16384), new Consent { TypedName = "dtbo" }, Samples.Phone).Error);
            Assert.Equal("too-big", phone.Flasher().Flash(dtbo, "dtbo_a", Checked(dtbo, "dtbo_a", 16384), new Consent { TypedName = "dtbo" }, Samples.Phone).Error);
        }

        [Fact]
        public void A_failed_read_back_is_reported_with_the_backup_to_restore()
        {
            using FilePhone phone = new FilePhone(("vbmeta_a", 16384));
            using Samples s = new Samples();
            string vbmeta = s.File("vbmeta.img", Samples.Vbmeta());
            phone.Override = script => script.StartsWith("head -c") ? new ShellResult(0, new string('0', 64) + "  -\n", "") : null;
            FlashOutcome outcome = phone.Flasher().Flash(vbmeta, "vbmeta_a", Checked(vbmeta, "vbmeta_a", 16384), new Consent { TypedName = "vbmeta" }, Samples.Phone);
            Assert.True(outcome.Written);
            Assert.False(outcome.Verified);
            Assert.StartsWith("verify-failed", outcome.Error);
            Assert.NotNull(outcome.BackupFolder);
        }

        [Fact]
        public void A_failed_backup_stops_before_writing()
        {
            using FilePhone phone = new FilePhone(("vbmeta_a", 16384));
            using Samples s = new Samples();
            string vbmeta = s.File("vbmeta.img", Samples.Vbmeta());
            phone.Override = script => script.StartsWith("sha256sum") ? new ShellResult(1, "", "no space") : null;
            FlashOutcome outcome = phone.Flasher().Flash(vbmeta, "vbmeta_a", Checked(vbmeta, "vbmeta_a", 16384), new Consent { TypedName = "vbmeta" }, Samples.Phone);
            Assert.Equal("backup-failed", outcome.Error);
            Assert.False(outcome.Written);
            Assert.False(Directory.Exists(phone.Backups) && Directory.EnumerateFileSystemEntries(phone.Backups).Any());
        }

        [Theory]
        [InlineData("init_boot_a", true)]
        [InlineData("boot", true)]
        [InlineData("boot_a; reboot", false)]
        [InlineData("../persist", false)]
        [InlineData("BOOT_A", false)]
        [InlineData("", false)]
        public void Only_plain_partition_names_reach_a_script(string name, bool ok)
        {
            Assert.Equal(ok, Sh.IsPartitionName(name));
        }

        [Fact]
        public void Quoting_keeps_values_literal()
        {
            using FilePhone phone = new FilePhone();
            Assert.Equal("a'b $(reboot) `x`\n", phone.Run("printf '%s\\n' " + Sh.Q("a'b $(reboot) `x`")).Output);
        }
    }
}

namespace FastbootStudio.Mobile.Core.Tests
{
    public class AnyKernelInstallerTests
    {
        /// <summary>An AnyKernel3-shaped zip whose installer writes a marker into boot_a, as a real one rewrites boot.</summary>
        static string Zip(Samples s, FilePhone phone, string script = null, string installer = null)
        {
            string path = Path.Combine(s.Root, "ak3.zip");
            using (System.IO.Compression.ZipArchive zip = System.IO.Compression.ZipFile.Open(path, System.IO.Compression.ZipArchiveMode.Create))
            {
                void Write(string name, string text)
                {
                    using StreamWriter w = new StreamWriter(zip.CreateEntry(name).Open());
                    w.Write(text);
                }
                Write("anykernel.sh", script ?? Samples.CorotScript);
                Write("META-INF/com/google/android/update-binary", installer ??
                    "#!/sbin/sh\necho \"ui_print AnyKernel3 test\"\necho \"ui_print zip=$3\"\nprintf 'NEWKERNEL' | dd of='" + phone.ByName + "/boot_a' bs=1 seek=64 conv=notrunc 2>/dev/null\nexit 0\n");
            }
            return path;
        }

        [Fact]
        public void A_fitting_zip_is_installed_after_a_backup_and_the_change_is_seen()
        {
            using FilePhone phone = new FilePhone(("boot_a", 8192), ("init_boot_a", 4096), ("vendor_boot_a", 4096), ("dtbo_a", 4096));
            using Samples s = new Samples();
            string path = Zip(s, phone);
            AnyKernelZip zip = AnyKernelZip.Inspect(path, Samples.Phone);
            Assert.Equal(CheckLevel.Notice, zip.Result.Verdict);
            byte[] before = phone.Partition("boot_a");
            AnyKernelInstaller installer = new AnyKernelInstaller(phone, phone.Flasher(), Path.Combine(phone.Root, "work"));
            Assert.Equal("not-confirmed", installer.Install(path, zip, new Consent(), Samples.Phone, "_a").Error);

            KernelOutcome outcome = installer.Install(path, zip, new Consent { NoticeAccepted = true }, Samples.Phone, "_a");
            Assert.Null(outcome.Error);
            Assert.True(outcome.Installed);
            Assert.True(outcome.Changed);
            Assert.Contains("AnyKernel3 test", outcome.Log);
            Assert.Equal("NEWKERNEL", System.Text.Encoding.ASCII.GetString(phone.Partition("boot_a"), 64, 9));
            BackupFolder backup = BackupFolder.Read(outcome.BackupFolder);
            Assert.Equal(new[] { "boot_a.img", "dtbo_a.img", "init_boot_a.img", "vendor_boot_a.img" }, backup.Sums.Keys.OrderBy(k => k));
            Assert.Equal(before, File.ReadAllBytes(Path.Combine(outcome.BackupFolder, "boot_a.img")));
            Assert.False(Directory.Exists(Path.Combine(phone.Root, "work")));
        }

        [Fact]
        public void A_zip_for_another_device_needs_developer_mode_and_the_name()
        {
            using FilePhone phone = new FilePhone(("boot_a", 8192));
            using Samples s = new Samples();
            string other = Samples.CorotScript.Replace("device.name1=corot", "device.name1=sweet").Replace("device.name2=corotin", "device.name2=sweetin");
            string path = Zip(s, phone, other);
            AnyKernelZip zip = AnyKernelZip.Inspect(path, Samples.Phone);
            AnyKernelInstaller installer = new AnyKernelInstaller(phone, phone.Flasher(), Path.Combine(phone.Root, "work"));
            Assert.Equal("not-confirmed", installer.Install(path, zip, new Consent { TypedName = "boot", NoticeAccepted = true }, Samples.Phone, "_a").Error);
            Assert.Equal("not-confirmed", installer.Install(path, zip, new Consent { DeveloperMode = true }, Samples.Phone, "_a").Error);
            Assert.True(installer.Install(path, zip, new Consent { DeveloperMode = true, TypedName = "boot" }, Samples.Phone, "_a").Installed);
        }

        [Fact]
        public void A_failing_installer_is_reported_and_a_changed_zip_is_not_run()
        {
            using FilePhone phone = new FilePhone(("boot_a", 8192));
            using Samples s = new Samples();
            string path = Zip(s, phone, installer: "#!/sbin/sh\necho 'ui_print Unsupported device. Aborting...'\necho 'abort' >&2\nexit 1\n");
            AnyKernelZip zip = AnyKernelZip.Inspect(path, Samples.Phone);
            AnyKernelInstaller installer = new AnyKernelInstaller(phone, phone.Flasher(), Path.Combine(phone.Root, "work"));
            KernelOutcome failed = installer.Install(path, zip, new Consent { NoticeAccepted = true }, Samples.Phone, "_a");
            Assert.False(failed.Installed);
            Assert.StartsWith("install-failed", failed.Error);
            Assert.Contains("Unsupported device. Aborting...", failed.Log);

            File.AppendAllText(path, "x");
            Assert.Equal("file-changed", installer.Install(path, zip, new Consent { NoticeAccepted = true }, Samples.Phone, "_a").Error);
        }
    }
}
