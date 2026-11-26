using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using FastbootEnhance.Core.Adb;
using Xunit;

namespace FastbootStudio.Mobile.Core.Tests
{
    public class BackupRestoreTests
    {
        static BackupEntry Entry(string serial, params (string file, string sha)[] sums)
        {
            BackupEntry e = new BackupEntry { Name = "Xiaomi_13T_Pro_a1b2c3d4_20261018-101544", Serial = serial, Model = "Xiaomi 13T Pro" };
            foreach ((string file, string sha) in sums)
                e.Sums[file] = sha;
            return e;
        }

        [Fact]
        public void Only_a_backup_the_app_recorded_for_this_phone_is_restored_whole()
        {
            string a = new string('a', 64), b = new string('b', 64);
            var recorded = new Dictionary<string, string> { ["boot_a.img"] = a };
            Assert.Null(BackupRestore.Refusal(Entry("a1b2c3d4", ("boot_a.img", a)), recorded, Samples.Phone));
            // The Windows app's or the Companion's own backup: the app has no record of it.
            Assert.Equal("not-recorded", BackupRestore.Refusal(Entry("a1b2c3d4", ("boot_a.img", a)), null, Samples.Phone));
            Assert.Equal("other-phone", BackupRestore.Refusal(Entry("9f8e7d6c", ("boot_a.img", a)), recorded, Samples.Phone));
            Assert.Equal("no-serial", BackupRestore.Refusal(Entry(null, ("boot_a.img", a)), recorded, Samples.Phone));
            Assert.Equal("no-serial", BackupRestore.Refusal(Entry("a1b2c3d4", ("boot_a.img", a)), recorded, new PhoneIdentity { Model = "Xiaomi 13T Pro" }));
            // SHA256SUMS was rewritten after the backup, or a recorded image is gone from it.
            Assert.Equal("tampered", BackupRestore.Refusal(Entry("a1b2c3d4", ("boot_a.img", b)), recorded, Samples.Phone));
            Assert.Equal("tampered", BackupRestore.Refusal(Entry("a1b2c3d4"), recorded, Samples.Phone));
        }

        [Fact]
        public void The_plan_writes_what_the_app_writes_and_leaves_the_rest()
        {
            string[] phone = { "boot_a", "boot_b", "init_boot_a", "init_boot_b", "dtbo_a", "dtbo_b", "vbmeta_a", "vbmeta_b", "modem_a", "persist" };
            string[] files = { "vbmeta_a.img", "modem_a.img", "boot_b.img", "boot_a.img", "persist.img", "init_boot_a.img", "recovery.img", "SHA256SUMS", "dtbo_a.img" };
            List<RestoreItem> plan = BackupRestore.Plan(files, phone, "_a");
            string State(string file) => plan.Single(i => i.File == file).State + ":" + plan.Single(i => i.File == file).Reason;
            Assert.Equal("include:", State("boot_a.img"));
            Assert.Equal("include:", State("init_boot_a.img"));
            Assert.Equal("include:", State("dtbo_a.img"));
            // The other slot and vbmeta: only when the person ticks them.
            Assert.Equal("optional:other-slot", State("boot_b.img"));
            Assert.Equal("optional:verified-boot", State("vbmeta_a.img"));
            // What the app never writes, what the phone does not have, what is not an image.
            Assert.Equal("skipped:not-allowed", State("modem_a.img"));
            Assert.Equal("skipped:not-allowed", State("persist.img"));
            Assert.Equal("skipped:no-partition", State("recovery.img"));
            Assert.Equal("skipped:not-an-image", State("SHA256SUMS"));
            // Written in the app's own fixed order, the active slot before the other one.
            Assert.Equal(new[] { "boot_a", "boot_b", "init_boot_a", "dtbo_a", "vbmeta_a" }, plan.Where(i => i.Writable).Select(i => i.Partition));
            Assert.Equal(plan.Count(i => i.Writable), plan.TakeWhile(i => i.Writable).Count());

            // A phone without slots: nothing is "the other slot".
            Assert.Equal("include:", BackupRestore.Plan(new[] { "boot.img" }, new[] { "boot" }, null).Single().State + ":");
        }

        [Fact]
        public void An_image_its_check_refuses_or_cannot_confirm_leaves_the_plan()
        {
            using Samples s = new Samples();
            BackupFolder b = s.Backup("a1b2c3d4", "Xiaomi 13T Pro", "corot_global", ("init_boot_a.img", Samples.BootImage(false)), ("dtbo_a.img", Samples.Dtbo()));
            List<RestoreItem> plan = BackupRestore.Plan(b.Sums.Keys, new[] { "init_boot_a", "dtbo_a" }, "_a");
            RestoreItem good = plan.Single(i => i.File == "init_boot_a.img"), bad = plan.Single(i => i.File == "dtbo_a.img");
            BackupRestore.Checked(good, ImageCheck.FromBackup(b, "init_boot_a.img", "init_boot_a", 8 << 20, Samples.Phone, b.Sums));
            Assert.Equal("include", good.State);
            // The image changed after the backup: its check blocks it, and so does the plan.
            File.AppendAllText(Path.Combine(b.Path, "dtbo_a.img"), "x");
            BackupRestore.Checked(bad, ImageCheck.FromBackup(b, "dtbo_a.img", "dtbo_a", 8 << 20, Samples.Phone, b.Sums));
            Assert.Equal("skipped:check-failed", bad.State + ":" + bad.Reason);
            // A file from outside a backup only ever gets a Warn (the name typed): never part of a restore.
            RestoreItem outside = BackupRestore.Plan(new[] { "dtbo_a.img" }, new[] { "dtbo_a" }, "_a").Single();
            BackupRestore.Checked(outside, ImageCheck.FromFile(s.File("dtbo.img", Samples.Dtbo()), "dtbo_a", 8 << 20, Samples.Phone));
            Assert.Equal("skipped", outside.State);
        }

        /// <summary>A backup of the phone as it is, then the partitions changed, then the plan checked like the app does.</summary>
        static (BackupFolder backup, List<RestoreItem> plan) Prepared(FilePhone phone, Samples s, params string[] partitions)
        {
            foreach (string p in partitions)
            {
                byte[] image = p.StartsWith("vbmeta") ? Samples.Vbmeta() : p.StartsWith("dtbo") ? Samples.Dtbo() : Samples.BootImage(p.StartsWith("boot"), "6.1.75-android14-11-gabc");
                byte[] whole = phone.Partition(p);
                image.CopyTo(whole, 0);
                File.WriteAllBytes(Path.Combine(phone.ByName, p), whole);
            }
            RootFlasher flasher = new RootFlasher(phone, () => new DateTime(2026, 10, 17, 9, 0, 0), new[] { phone.ByName }, phone.Backups);
            BackupFolder backup = BackupFolder.Read(flasher.Backup(partitions, Samples.Phone, "boot"));
            foreach (string p in partitions)
            {
                byte[] changed = phone.Partition(p);
                changed[changed.Length - 1] ^= 0x55;
                changed[100] ^= 0x55;
                File.WriteAllBytes(Path.Combine(phone.ByName, p), changed);
            }
            List<RestoreItem> plan = BackupRestore.Plan(backup.Sums.Keys, flasher.Partitions(), "_a");
            foreach (RestoreItem i in plan.Where(i => i.Writable))
                BackupRestore.Checked(i, ImageCheck.FromBackup(backup, i.File, i.Partition, flasher.PartitionBytes(i.Partition), Samples.Phone, backup.Sums));
            return (backup, plan);
        }

        [Fact]
        public void A_whole_backup_goes_back_partition_by_partition_after_a_backup_of_what_is_there()
        {
            using FilePhone phone = new FilePhone(("boot_a", 16384), ("init_boot_a", 16384), ("dtbo_a", 8192), ("vbmeta_a", 8192), ("persist", 4096));
            using Samples s = new Samples();
            (BackupFolder backup, List<RestoreItem> plan) = Prepared(phone, s, "boot_a", "init_boot_a", "dtbo_a", "vbmeta_a");
            byte[] persist = phone.Partition("persist"), vbmetaNow = phone.Partition("vbmeta_a"), bootNow = phone.Partition("boot_a");
            List<RestoreItem> chosen = plan.Where(i => i.State == "include").ToList();
            Assert.Equal(new[] { "boot_a", "init_boot_a", "dtbo_a" }, chosen.Select(i => i.Partition));

            RestoreOutcome o = BackupRestore.Run(chosen, f => Path.Combine(backup.Path, f), phone.Flasher(), Samples.Phone);
            Assert.Null(o.Error);
            Assert.Equal(new[] { "boot_a", "init_boot_a", "dtbo_a" }, o.Written);
            Assert.Empty(o.NotWritten);
            Assert.Null(o.Failed);
            foreach (string p in o.Written)
                Assert.Equal(File.ReadAllBytes(Path.Combine(backup.Path, p + ".img")), phone.Partition(p));
            // What was not chosen is untouched, and what was overwritten was copied first.
            Assert.Equal(vbmetaNow, phone.Partition("vbmeta_a"));
            Assert.Equal(persist, phone.Partition("persist"));
            Assert.Equal(bootNow, File.ReadAllBytes(Path.Combine(o.BackupFolder, "boot_a.img")));
            Assert.Equal(new[] { "boot_a.img", "dtbo_a.img", "init_boot_a.img" }, BackupFolder.Read(o.BackupFolder).Sums.Keys.OrderBy(k => k));
            Assert.Contains("restore 2/3 init_boot_a", o.Log);
        }

        [Fact]
        public void A_restore_stops_at_the_first_failure_and_says_what_was_written()
        {
            using FilePhone phone = new FilePhone(("boot_a", 16384), ("init_boot_a", 16384), ("dtbo_a", 8192));
            using Samples s = new Samples();
            (BackupFolder backup, List<RestoreItem> plan) = Prepared(phone, s, "boot_a", "init_boot_a", "dtbo_a");
            byte[] dtboNow = phone.Partition("dtbo_a");
            // The second write fails half-way.
            phone.Override = script => script.StartsWith("dd if=") && script.Contains("conv=notrunc") && script.Contains("/init_boot_a'") ? new ShellResult(1, "", "I/O error") : null;
            RestoreOutcome o = BackupRestore.Run(plan.Where(i => i.Writable).ToList(), f => Path.Combine(backup.Path, f), phone.Flasher(), Samples.Phone);
            Assert.StartsWith("write-failed", o.Error);
            Assert.Equal(new[] { "boot_a" }, o.Written);
            Assert.Equal("init_boot_a", o.Failed);
            Assert.Equal(new[] { "dtbo_a" }, o.NotWritten);
            Assert.Equal(dtboNow, phone.Partition("dtbo_a"));
            Assert.NotNull(o.BackupFolder);
            Assert.DoesNotContain(phone.Scripts, x => x.StartsWith("dd if=") && x.Contains("conv=notrunc") && x.Contains("/dtbo_a'"));
        }

        [Fact]
        public void Nothing_is_written_when_the_backup_cannot_be_taken_or_an_image_did_not_pass()
        {
            using FilePhone phone = new FilePhone(("boot_a", 16384), ("init_boot_a", 16384));
            using Samples s = new Samples();
            (BackupFolder backup, List<RestoreItem> plan) = Prepared(phone, s, "boot_a", "init_boot_a");
            byte[] boot = phone.Partition("boot_a");
            List<RestoreItem> chosen = plan.Where(i => i.Writable).ToList();

            phone.Override = script => script.StartsWith("df ") ? new ShellResult(0, "Filesystem 1024-blocks Used Available Capacity Mounted on\n/dev/fuse 100 90 10 90% /sdcard\n", "") : null;
            RestoreOutcome noRoom = BackupRestore.Run(chosen, f => Path.Combine(backup.Path, f), phone.Flasher(), Samples.Phone);
            Assert.Equal("no-space", noRoom.Error);
            Assert.Empty(noRoom.Written);
            Assert.Equal(boot, phone.Partition("boot_a"));
            phone.Override = null;

            // One image without a passing check: the whole restore is refused, not only that image.
            chosen[1].Check = null;
            RestoreOutcome unchecked1 = BackupRestore.Run(chosen, f => Path.Combine(backup.Path, f), phone.Flasher(), Samples.Phone);
            Assert.Equal("not-confirmed", unchecked1.Error);
            Assert.Equal(boot, phone.Partition("boot_a"));
            Assert.Equal("nothing-to-restore", BackupRestore.Run(new List<RestoreItem>(), f => f, phone.Flasher(), Samples.Phone).Error);

            // A copy that changed between the plan and the write is not written (the flasher hashes it again).
            (BackupFolder again, List<RestoreItem> plan2) = Prepared(phone, s, "boot_a");
            byte[] now = phone.Partition("boot_a");
            string swapped = s.File("swapped.img", Samples.BootImage(true, "5.10.1-android12-9"));
            RestoreOutcome changed = BackupRestore.Run(plan2.Where(i => i.Writable).ToList(), f => swapped, phone.Flasher(), Samples.Phone, backupFirst: false);
            Assert.Equal("file-changed", changed.Error);
            Assert.Null(changed.Failed);
            Assert.Equal(now, phone.Partition("boot_a"));
        }
    }

    public class PreflightTests
    {
        static PreflightFacts Good(string kind = "image") => new PreflightFacts
        {
            Kind = kind, Battery = 82, MinBattery = 50, Root = true, Unlocked = true, BackupFirst = true, NeedBytes = 8 << 20, FreeBytes = 1L << 30,
            OtgSerial = "9f8e7d6c", OtgUnlocked = "yes",
        };

        static string Level(List<PreflightItem> items, string code) => items.Single(i => i.Code == code).Level;

        [Fact]
        public void A_ready_phone_passes_everything()
        {
            List<PreflightItem> items = Preflight.Evaluate(Good());
            Assert.Equal(new[] { "battery", "root", "bootloader", "backup", "space" }, items.Select(i => i.Code));
            Assert.True(Preflight.AllPass(items));
            Assert.False(Preflight.Blocked(items));
            Assert.Equal(new[] { "ok", "82", "50", "on-battery" }, items[0].Values);
        }

        [Fact]
        public void A_fail_blocks_and_a_warn_does_not_pass()
        {
            PreflightFacts f = Good();
            f.Battery = 31;
            Assert.Equal("fail", Level(Preflight.Evaluate(f), "battery"));
            Assert.True(Preflight.Blocked(Preflight.Evaluate(f)));
            f = Good(); f.Battery = -1;
            Assert.Equal("warn", Level(Preflight.Evaluate(f), "battery"));
            f = Good(); f.Root = false;
            Assert.Equal("fail", Level(Preflight.Evaluate(f), "root"));
            f = Good(); f.Unlocked = false;
            Assert.Equal("warn", Level(Preflight.Evaluate(f), "bootloader"));
            Assert.False(Preflight.AllPass(Preflight.Evaluate(f)));
            Assert.False(Preflight.Blocked(Preflight.Evaluate(f)));
            f = Good(); f.Unlocked = null;
            Assert.Equal("warn", Level(Preflight.Evaluate(f), "bootloader"));
        }

        [Fact]
        public void A_backup_from_today_or_the_automatic_one_passes_and_neither_needs_a_tick()
        {
            PreflightFacts f = Good();
            Assert.Equal("automatic", Preflight.Evaluate(f).Single(i => i.Code == "backup").Values[0]);
            f.BackupToday = true;
            Assert.Equal("today", Preflight.Evaluate(f).Single(i => i.Code == "backup").Values[0]);
            f = Good(); f.BackupFirst = false;
            List<PreflightItem> none = Preflight.Evaluate(f);
            Assert.Equal("warn", Level(none, "backup"));
            // No automatic backup, so no room is needed for one.
            Assert.DoesNotContain(none, i => i.Code == "space");
            f = Good(); f.FreeBytes = 4 << 20;
            Assert.Equal("fail", Level(Preflight.Evaluate(f), "space"));
            f = Good(); f.FreeBytes = null;
            Assert.Equal("warn", Level(Preflight.Evaluate(f), "space"));
            Assert.True(Preflight.IsFrom("Xiaomi_13T_Pro_a1b2c3d4_20261126-101544", new DateTime(2026, 11, 26)));
            Assert.False(Preflight.IsFrom("Xiaomi_13T_Pro_a1b2c3d4_20261125-235959", new DateTime(2026, 11, 26)));
        }

        [Fact]
        public void Another_phone_on_the_cable_is_judged_by_its_own_state()
        {
            List<PreflightItem> otg = Preflight.Evaluate(Good("otg"));
            Assert.Equal(new[] { "battery", "otg-serial", "otg-bootloader" }, otg.Select(i => i.Code));
            Assert.True(Preflight.AllPass(otg));
            PreflightFacts f = Good("otg"); f.OtgUnlocked = "no"; f.Root = false;
            Assert.Equal("warn", Level(Preflight.Evaluate(f), "otg-bootloader"));
            Assert.False(Preflight.Blocked(Preflight.Evaluate(f)));
            f = Good("otg"); f.OtgSerial = null;
            Assert.True(Preflight.Blocked(Preflight.Evaluate(f)));
            // A recovery in sideload says nothing about its bootloader, and often gives no serial.
            f = Good("sideload"); f.OtgSerial = null;
            Assert.Equal(new[] { "battery", "otg-serial" }, Preflight.Evaluate(f).Select(i => i.Code));
            Assert.Equal("warn", Level(Preflight.Evaluate(f), "otg-serial"));
        }
    }

    public class TarTests
    {
        static string Sha(byte[] data)
        {
            using var sha = System.Security.Cryptography.SHA256.Create();
            return BitConverter.ToString(sha.ComputeHash(data)).Replace("-", "").ToLowerInvariant();
        }

        [Fact]
        public void An_archive_made_by_tar_is_read_back_member_by_member()
        {
            using Samples s = new Samples();
            string folder = Path.Combine(s.Root, "Xiaomi_13T_Pro_a1b2c3d4_20261018-101544");
            Directory.CreateDirectory(folder);
            byte[] boot = Enumerable.Range(0, 70001).Select(i => (byte)(i * 31)).ToArray(), dtbo = new byte[1024], empty = new byte[0];
            File.WriteAllBytes(Path.Combine(folder, "boot_a.img"), boot);
            File.WriteAllBytes(Path.Combine(folder, "dtbo_a.img"), dtbo);
            File.WriteAllBytes(Path.Combine(folder, "empty.img"), empty);
            string longName = new string('n', 130) + ".img";
            File.WriteAllBytes(Path.Combine(folder, longName), new byte[] { 1, 2, 3 });
            string archive = Path.Combine(s.Root, "backup.tar");
            using (Process p = Process.Start(new ProcessStartInfo("tar", new[] { "-cf", archive, "-C", s.Root, Path.GetFileName(folder) }) { UseShellExecute = false }))
            {
                p.WaitForExit();
                Assert.Equal(0, p.ExitCode);
            }
            Dictionary<string, string> hashes;
            using (FileStream f = File.OpenRead(archive))
                hashes = Tar.Hashes(f);
            string dir = Path.GetFileName(folder) + "/";
            Assert.Equal(Sha(boot), hashes[dir + "boot_a.img"]);
            Assert.Equal(Sha(dtbo), hashes[dir + "dtbo_a.img"]);
            Assert.Equal(Sha(empty), hashes[dir + "empty.img"]);
            Assert.Equal(Sha(new byte[] { 1, 2, 3 }), hashes[dir + longName]);
            Assert.Equal(4, hashes.Count);

            var expected = new Dictionary<string, string> { ["boot_a.img"] = Sha(boot), ["dtbo_a.img"] = Sha(dtbo) };
            Assert.Empty(Tar.Mismatches(hashes, expected));
            expected["dtbo_a.img"] = Sha(boot);
            expected["init_boot_a.img"] = Sha(dtbo);
            Assert.Equal(new[] { "dtbo_a.img", "init_boot_a.img" }, Tar.Mismatches(hashes, expected));

            // An archive cut short is not a shorter archive: it is an error.
            byte[] all = File.ReadAllBytes(archive);
            Assert.Throws<InvalidDataException>(() => Tar.Hashes(new MemoryStream(all, 0, 600)));
            Assert.Throws<InvalidDataException>(() => Tar.Hashes(new MemoryStream(Enumerable.Repeat((byte)'x', 1024).ToArray())));
            Assert.Empty(Tar.Hashes(new MemoryStream()));
        }

        [Fact]
        public void An_archive_the_app_writes_is_one_tar_reads()
        {
            using Samples s = new Samples();
            byte[] log = System.Text.Encoding.UTF8.GetBytes("10:15:41 $ id -u\n10:15:44 done\n"), device = Enumerable.Range(0, 1300).Select(i => (byte)('a' + i % 26)).ToArray();
            string archive = Path.Combine(s.Root, "support.tar");
            using (FileStream f = File.Create(archive))
            {
                Tar.WriteFile(f, "log.txt", new MemoryStream(log), log.Length, new DateTime(2026, 11, 26, 9, 0, 0, DateTimeKind.Utc));
                Tar.WriteFile(f, "device.txt", new MemoryStream(device), device.Length, new DateTime(2026, 11, 26, 9, 0, 0, DateTimeKind.Utc));
                Tar.WriteEnd(f);
            }
            using (FileStream f = File.OpenRead(archive))
                Assert.Equal(new Dictionary<string, string> { ["log.txt"] = Sha(log), ["device.txt"] = Sha(device) }, Tar.Hashes(f));
            string outDir = Path.Combine(s.Root, "out");
            Directory.CreateDirectory(outDir);
            using (Process p = Process.Start(new ProcessStartInfo("tar", new[] { "-xf", archive, "-C", outDir }) { UseShellExecute = false }))
            {
                p.WaitForExit();
                Assert.Equal(0, p.ExitCode);
            }
            Assert.Equal(log, File.ReadAllBytes(Path.Combine(outDir, "log.txt")));
            Assert.Equal(device, File.ReadAllBytes(Path.Combine(outDir, "device.txt")));
            Assert.Throws<ArgumentException>(() => Tar.WriteFile(new MemoryStream(), "../x", new MemoryStream(), 0, DateTime.UtcNow));
            Assert.Throws<EndOfStreamException>(() => Tar.WriteFile(new MemoryStream(), "short.txt", new MemoryStream(new byte[3]), 10, DateTime.UtcNow));
        }
    }
}
