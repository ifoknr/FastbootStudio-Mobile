using System.IO;
using System.Linq;
using Xunit;

namespace FastbootStudio.Mobile.Core.Tests
{
    public class FlashTargetsTests
    {
        [Theory]
        [InlineData("boot", true)]
        [InlineData("init_boot", true)]
        [InlineData("vendor_boot", true)]
        [InlineData("dtbo", true)]
        [InlineData("vbmeta", true)]
        [InlineData("vbmeta_system", true)]
        [InlineData("recovery", true)]
        [InlineData("system", false)]
        [InlineData("super", false)]
        [InlineData("userdata", false)]
        [InlineData("modem", false)]
        [InlineData("persist", false)]
        [InlineData("nvram", false)]
        [InlineData("abl", false)]
        [InlineData("xbl", false)]
        [InlineData("preloader", false)]
        [InlineData("vbmeta_vendor", false)]
        [InlineData("", false)]
        [InlineData(null, false)]
        public void Only_the_listed_partitions_can_be_written(string name, bool allowed)
        {
            Assert.Equal(allowed, FlashTargets.IsAllowed(name));
        }

        [Fact]
        public void Resolve_picks_the_slot_and_refuses_what_the_phone_does_not_have()
        {
            string[] ab = { "boot_a", "boot_b", "init_boot_a", "init_boot_b", "dtbo_a", "dtbo_b", "persist", "modem_a" };
            Assert.Equal("init_boot_a", FlashTargets.Resolve("init_boot", "_a", ab));
            Assert.Equal("boot_b", FlashTargets.Resolve("boot", "_b", ab));
            Assert.Null(FlashTargets.Resolve("vendor_boot", "_a", ab));
            Assert.Null(FlashTargets.Resolve("persist", null, ab));
            Assert.Null(FlashTargets.Resolve("modem", "_a", ab));
            Assert.Null(FlashTargets.Resolve("boot", "_c", ab));
            Assert.Null(FlashTargets.Resolve("boot", null, ab));

            string[] legacy = { "boot", "recovery", "dtbo", "vbmeta" };
            Assert.Equal("recovery", FlashTargets.Resolve("recovery", null, legacy));
            Assert.Equal("boot", FlashTargets.Resolve("boot", null, legacy));
            Assert.Null(FlashTargets.Resolve("init_boot", null, legacy));
        }
    }

    public class BackupCheckTests
    {
        [Fact]
        public void This_phones_backup_with_a_matching_hash_can_be_flashed()
        {
            using Samples s = new Samples();
            BackupFolder backup = s.Backup("a1b2c3d4", "Xiaomi 13T Pro", "corot_global", ("init_boot_a.img", Samples.BootImage(false)));
            CheckResult r = ImageCheck.FromBackup(backup, "init_boot_a.img", "init_boot_a", 8 << 20, Samples.Phone);
            Assert.Equal(CheckLevel.Ok, r.Verdict);
            Assert.True(r.CanFlash);
            Assert.True(r.Has(CheckCode.HashMatches));
            Assert.True(r.Has(CheckCode.SamePhone));
        }

        [Fact]
        public void Another_phones_backup_is_refused()
        {
            using Samples s = new Samples();
            BackupFolder backup = s.Backup("9f8e7d6c", "Redmi Note 12", "tapas_global", ("init_boot_a.img", Samples.BootImage(false)));
            CheckResult r = ImageCheck.FromBackup(backup, "init_boot_a.img", "init_boot_a", 8 << 20, Samples.Phone);
            Assert.Equal(CheckLevel.Block, r.Verdict);
            Assert.False(r.CanFlash);
            Assert.Contains(r.Items, i => i.Code == CheckCode.OtherPhone && i.Values[0].Contains("9f8e7d6c") && i.Values[1].Contains("a1b2c3d4"));
        }

        [Fact]
        public void A_changed_or_unlisted_image_is_refused()
        {
            using Samples s = new Samples();
            BackupFolder backup = s.Backup("a1b2c3d4", "Xiaomi 13T Pro", "corot_global", ("init_boot_a.img", Samples.BootImage(false)));
            File.AppendAllText(Path.Combine(backup.Path, "init_boot_a.img"), "x");
            CheckResult changed = ImageCheck.FromBackup(backup, "init_boot_a.img", "init_boot_a", 8 << 20, Samples.Phone);
            Assert.False(changed.CanFlash);
            Assert.True(changed.Has(CheckCode.HashMismatch));

            File.WriteAllBytes(Path.Combine(backup.Path, "boot_a.img"), Samples.BootImage(true));
            CheckResult unlisted = ImageCheck.FromBackup(backup, "boot_a.img", "boot_a", 64 << 20, Samples.Phone);
            Assert.False(unlisted.CanFlash);
            Assert.True(unlisted.Has(CheckCode.HashMissing));
        }

        [Fact]
        public void An_image_goes_only_to_its_own_partition()
        {
            using Samples s = new Samples();
            BackupFolder backup = s.Backup("a1b2c3d4", "Xiaomi 13T Pro", "corot_global", ("init_boot_a.img", Samples.BootImage(false)));
            CheckResult r = ImageCheck.FromBackup(backup, "init_boot_a.img", "boot_a", 64 << 20, Samples.Phone);
            Assert.False(r.CanFlash);
            Assert.True(r.Has(CheckCode.OtherPartition));
            // The other slot of the same partition is fine.
            Assert.True(ImageCheck.FromBackup(backup, "init_boot_a.img", "init_boot_b", 8 << 20, Samples.Phone).CanFlash);
        }

        [Fact]
        public void A_backup_without_a_serial_is_only_a_warning_when_model_and_product_agree()
        {
            using Samples s = new Samples();
            BackupFolder same = s.Backup("", "Xiaomi 13T Pro", "corot_global", ("dtbo_a.img", Samples.Dtbo()));
            CheckResult r = ImageCheck.FromBackup(same, "dtbo_a.img", "dtbo_a", 8 << 20, Samples.Phone);
            Assert.Equal(CheckLevel.Warn, r.Verdict);
            Assert.True(r.Has(CheckCode.PhoneUnknown));

            BackupFolder other = s.Backup("", "Redmi Note 12", "tapas_global", ("dtbo_a.img", Samples.Dtbo()));
            Assert.False(ImageCheck.FromBackup(other, "dtbo_a.img", "dtbo_a", 8 << 20, Samples.Phone).CanFlash);
        }

        [Fact]
        public void A_forbidden_partition_is_refused_before_anything_is_read()
        {
            using Samples s = new Samples();
            BackupFolder backup = s.Backup("a1b2c3d4", "Xiaomi 13T Pro", "corot_global", ("persist.img", new byte[4096]));
            CheckResult r = ImageCheck.FromBackup(backup, "persist.img", "persist", 32 << 20, Samples.Phone);
            Assert.False(r.CanFlash);
            Assert.Single(r.Items);
            Assert.Equal(CheckCode.NotAllowed, r.Items[0].Code);
        }
    }

    public class FileCheckTests
    {
        [Fact]
        public void An_outside_file_always_needs_the_name_typed()
        {
            using Samples s = new Samples();
            string patched = s.File("magisk_patched-28100_Xk2.img", Samples.BootImage(false));
            CheckResult r = ImageCheck.FromFile(patched, "init_boot_a", 8 << 20, Samples.Phone);
            Assert.Equal(CheckLevel.Warn, r.Verdict);
            Assert.True(r.CanFlash);
            Assert.True(r.Has(CheckCode.NoFingerprint));
        }

        [Fact]
        public void The_wrong_kind_of_image_is_refused()
        {
            using Samples s = new Samples();
            Assert.False(ImageCheck.FromFile(s.File("vbmeta.img", Samples.Vbmeta()), "boot_a", 64 << 20, Samples.Phone).CanFlash);
            Assert.False(ImageCheck.FromFile(s.File("dtbo.img", Samples.Dtbo()), "vbmeta_a", 1 << 20, Samples.Phone).CanFlash);
            Assert.False(ImageCheck.FromFile(s.File("junk.img", new byte[8192]), "dtbo_a", 8 << 20, Samples.Phone).CanFlash);
            // A boot image with a kernel has no place in init_boot's slot for boot... and an init_boot has no kernel for boot.
            CheckResult noKernel = ImageCheck.FromFile(s.File("init.img", Samples.BootImage(false)), "boot_a", 64 << 20, Samples.Phone);
            Assert.False(noKernel.CanFlash);
            Assert.True(ImageCheck.FromFile(s.File("v.img", Samples.Vbmeta()), "vbmeta_a", 1 << 20, Samples.Phone).CanFlash);
            Assert.True(ImageCheck.FromFile(s.File("d.img", Samples.Dtbo()), "dtbo_b", 8 << 20, Samples.Phone).CanFlash);
        }

        [Fact]
        public void An_image_bigger_than_the_partition_or_empty_is_refused()
        {
            using Samples s = new Samples();
            Assert.True(ImageCheck.FromFile(s.File("big.img", Samples.Vbmeta()), "vbmeta_a", 1024, Samples.Phone).Has(CheckCode.TooBig));
            Assert.False(ImageCheck.FromFile(s.File("empty.img", new byte[0]), "vbmeta_a", 1 << 20, Samples.Phone).CanFlash);
            Assert.False(ImageCheck.FromFile(Path.Combine(s.Root, "missing.img"), "vbmeta_a", 1 << 20, Samples.Phone).CanFlash);
        }

        [Fact]
        public void A_pasted_hash_refuses_a_different_file_and_confirms_the_same()
        {
            using Samples s = new Samples();
            string path = s.File("d.img", Samples.Dtbo());
            string real = FastbootEnhance.Core.Adb.Sha256Sums.HashFile(path);
            CheckResult wrong = ImageCheck.FromFile(path, "dtbo_a", 8 << 20, Samples.Phone, new string('0', 64));
            Assert.False(wrong.CanFlash);
            Assert.True(wrong.Has(CheckCode.ExpectedHashMismatch));
            CheckResult right = ImageCheck.FromFile(path, "dtbo_a", 8 << 20, Samples.Phone, " " + real.ToUpperInvariant() + " ");
            Assert.True(right.CanFlash);
            Assert.True(right.Has(CheckCode.ExpectedHashMatches));
            Assert.Equal(real, right.Sha256);
        }
    }

    public class AnyKernelTests
    {
        [Fact]
        public void A_zip_for_this_phone_is_read_and_accepted()
        {
            using Samples s = new Samples();
            AnyKernelZip zip = AnyKernelZip.Inspect(s.AnyKernel(Samples.CorotScript), Samples.Phone);
            Assert.Equal("Kernel v4.2 by dev", zip.KernelString);
            Assert.True(zip.DeviceCheck);
            Assert.Equal(new[] { "corot", "corotin" }, zip.Devices);
            Assert.Equal("boot", zip.Block);
            Assert.True(zip.Result.CanFlash);
            Assert.True(zip.Result.Has(CheckCode.DeviceListed));
            Assert.True(zip.Result.Has(CheckCode.AndroidSupported));
            // No published hash pasted: it still asks for the name to be typed.
            Assert.Equal(CheckLevel.Warn, zip.Result.Verdict);
        }

        [Fact]
        public void A_zip_for_another_device_or_android_is_refused()
        {
            using Samples s = new Samples();
            string other = Samples.CorotScript.Replace("device.name1=corot", "device.name1=sweet").Replace("device.name2=corotin", "device.name2=sweetin");
            Assert.True(AnyKernelZip.Inspect(s.AnyKernel(other), Samples.Phone).Result.Has(CheckCode.DeviceNotListed));
            Assert.False(AnyKernelZip.Inspect(s.AnyKernel(other), Samples.Phone).Result.CanFlash);

            string old = Samples.CorotScript.Replace("supported.versions=13 - 15", "supported.versions=11-12");
            Assert.False(AnyKernelZip.Inspect(s.AnyKernel(old), Samples.Phone).Result.CanFlash);
        }

        [Fact]
        public void A_zip_without_device_check_is_only_a_warning()
        {
            using Samples s = new Samples();
            string off = Samples.CorotScript.Replace("do.devicecheck=1", "do.devicecheck=0");
            AnyKernelZip zip = AnyKernelZip.Inspect(s.AnyKernel(off), Samples.Phone);
            Assert.True(zip.Result.Has(CheckCode.DeviceCheckOff));
            Assert.Equal(CheckLevel.Warn, zip.Result.Verdict);
        }

        [Fact]
        public void Not_anykernel_or_unsafe_zips_are_refused()
        {
            using Samples s = new Samples();
            Assert.True(AnyKernelZip.Inspect(s.AnyKernel(null), Samples.Phone).Result.Has(CheckCode.NotAnyKernel));
            Assert.True(AnyKernelZip.Inspect(s.AnyKernel(Samples.CorotScript, installer: false), Samples.Phone).Result.Has(CheckCode.NotAnyKernel));
            Assert.True(AnyKernelZip.Inspect(s.AnyKernel(Samples.CorotScript, true, "../../etc/evil"), Samples.Phone).Result.Has(CheckCode.UnsafeZipEntry));
            Assert.True(AnyKernelZip.Inspect(s.File("fake.zip", new byte[] { 1, 2, 3 }), Samples.Phone).Result.Has(CheckCode.NotAnyKernel));
            Assert.False(AnyKernelZip.Inspect(Path.Combine(s.Root, "none.zip"), Samples.Phone).Result.CanFlash);
        }

        [Fact]
        public void A_pasted_hash_that_differs_refuses_the_zip()
        {
            using Samples s = new Samples();
            AnyKernelZip zip = AnyKernelZip.Inspect(s.AnyKernel(Samples.CorotScript), Samples.Phone, new string('a', 64));
            Assert.False(zip.Result.CanFlash);
            Assert.True(zip.Result.Has(CheckCode.ExpectedHashMismatch));
        }

        [Theory]
        [InlineData("anykernel.sh", true)]
        [InlineData("tools/ak3-core.sh", true)]
        [InlineData("../evil", false)]
        [InlineData("a/../../evil", false)]
        [InlineData("/system/bin/sh", false)]
        [InlineData("C:/x", false)]
        public void Zip_entries_stay_inside_the_folder(string name, bool safe)
        {
            Assert.Equal(safe, AnyKernelZip.SafeEntry(name));
        }
    }

    public class CheckResultTests
    {
        [Fact]
        public void An_empty_result_never_allows_a_flash()
        {
            CheckResult r = new CheckResult();
            Assert.False(r.CanFlash);
            Assert.Equal(CheckLevel.Block, r.Verdict);
        }
    }
}
