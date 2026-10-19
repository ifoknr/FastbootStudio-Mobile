using System.IO;
using System.Linq;
using Xunit;

namespace FastbootStudio.Mobile.Core.Tests
{
    public class BackupListingTests
    {
        [Fact]
        public void The_listing_script_finds_finished_backups_newest_first()
        {
            using FilePhone phone = new FilePhone();
            using Samples s = new Samples();
            string root = phone.Backups;
            Directory.CreateDirectory(root);
            void Make(string name, string serial, string model, string mode = "mobile")
            {
                string d = Path.Combine(root, name);
                Directory.CreateDirectory(d);
                File.WriteAllBytes(Path.Combine(d, "boot_a.img"), new byte[2048]);
                File.WriteAllText(Path.Combine(d, "SHA256SUMS"), FastbootEnhance.Core.Adb.Sha256Sums.HashFile(Path.Combine(d, "boot_a.img")) + "  boot_a.img\n");
                File.WriteAllText(Path.Combine(d, "backup-info.txt"), "Fastboot Studio backup\ndate\t2026-10-15\nserial\t" + serial + "\nmodel\t" + model + "\nproduct\tcorot_global\nmode\t" + mode + "\nset\tboot\n\npartition\tbytes\tsha256\n");
            }
            Make("Xiaomi_13T_Pro_a1b2c3d4_20261009-091233", "a1b2c3d4", "Xiaomi 13T Pro");
            Make("Xiaomi_13T_Pro_a1b2c3d4_20261015-221804", "a1b2c3d4", "Xiaomi 13T Pro", "companion");
            Make("Redmi_Note_12_9f8e7d6c_20260830-120501", "9f8e7d6c", "Redmi Note 12");
            Make(".partial-Xiaomi_13T_Pro_a1b2c3d4_20261018-101544", "a1b2c3d4", "Xiaomi 13T Pro");

            var list = BackupListing.Parse(phone.Run(BackupListing.Script(root)).Output);
            Assert.Equal(new[] { "Xiaomi_13T_Pro_a1b2c3d4_20261015-221804", "Xiaomi_13T_Pro_a1b2c3d4_20261009-091233", "Redmi_Note_12_9f8e7d6c_20260830-120501" },
                list.Select(b => b.Name));
            Assert.True(list[0].IsFrom(Samples.Phone));
            Assert.False(list[2].IsFrom(Samples.Phone));
            Assert.Single(list[0].Sums);
            Assert.Equal("companion", list[0].Mode);
            Assert.Equal("mobile", list[1].Mode);
            Assert.Equal("boot", list[0].Set);
            Assert.True(list[0].Kb > 0);

            File.AppendAllText(Path.Combine(root, list[1].Name, "boot_a.img"), "x");
            string check = phone.Run("cd " + Sh.Q(Path.Combine(root, list[1].Name)) + " && sha256sum -c SHA256SUMS 2>&1").Output;
            Assert.Equal(new[] { "boot_a.img" }, BackupListing.Failed(check));
        }

        [Theory]
        [InlineData("Xiaomi_13T_Pro_a1b2c3d4_20261015-221804", true)]
        [InlineData(".partial-x", false)]
        [InlineData("../x", false)]
        [InlineData("a/b", false)]
        [InlineData("", false)]
        public void Only_plain_folder_names_are_acted_on(string name, bool ok) => Assert.Equal(ok, BackupListing.IsName(name));
    }
}
