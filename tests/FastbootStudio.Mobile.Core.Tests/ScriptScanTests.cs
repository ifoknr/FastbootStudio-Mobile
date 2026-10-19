using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using Xunit;

namespace FastbootStudio.Mobile.Core.Tests
{
    public class ScriptScanTests
    {
        [Theory]
        [InlineData("dd if=/tmp/x.img of=/dev/block/by-name/modem", "write")]
        [InlineData("dd if=x of=/dev/block/bootdevice/by-name/persist bs=4096", "write")]
        [InlineData("cat x > /dev/block/platform/soc/1d84000.ufshc/by-name/xbl_a;", "write")]
        [InlineData("dd if=x of=/dev/block/mapper/system_a", "write")]
        [InlineData("flash_generic abl", "write")]
        [InlineData("mkfs.ext4 /dev/block/by-name/userdata", "wipe")]
        [InlineData("$BB blkdiscard /dev/block/sda", "wipe")]
        [InlineData("make_f2fs /dev/block/by-name/userdata", "wipe")]
        [InlineData("rm -rf /data", "delete")]
        [InlineData("rm -rf /sdcard/*", "delete")]
        [InlineData("rm -fr \"/storage/emulated/0\"", "delete")]
        [InlineData("cd /; rm -rf /*", "delete")]
        [InlineData("curl -o /tmp/x https://example.com/x", "network")]
        [InlineData("wget http://example.com/x -O /tmp/x", "network")]
        public void Finds_what_no_kernel_installer_does(string line, string reason)
        {
            ScriptScan.Hit hit = Assert.Single(ScriptScan.Scan("#!/sbin/sh\n" + line + "\n"));
            Assert.Equal(reason, hit.Reason);
        }

        // Lines from AnyKernel3's own anykernel.sh, ak3-core.sh and update-binary.
        [Theory]
        [InlineData("dd if=boot-new.img of=$BLOCK $CUSTOMDD;")]
        [InlineData("dd if=/dev/zero of=$imgblock 2>/dev/null;")]
        [InlineData("BLOCK=/dev/block/platform/omap/omap_hsmmc.0/by-name/boot;")]
        [InlineData("dd if=x of=/dev/block/by-name/boot_a")]
        [InlineData("flash_generic vendor_dlkm;")]
        [InlineData("insert_line init.tuna.rc \"nodiratime barrier=0\" after \"mount_all /fstab.tuna\" \"\\tmount ext4 /dev/block/platform/omap/omap_hsmmc.0/by-name/userdata /data remount\";")]
        [InlineData("rm -rf $AKHOME;")]
        [InlineData("rm -rf /apex/apexak3 /apex 2>/dev/null;")]
        [InlineData("[ -L /etc_link ] && rm -rf /etc/*;")]
        [InlineData("rm -rf /data/adb/modules/old_module")]
        [InlineData("# rm -rf /data  (a comment)")]
        [InlineData("mount -o ro -t auto /dev/block/$byname/system$slot /system_root")]
        [InlineData("ui_print \"Installing the kernel...\";")]
        public void Leaves_what_AnyKernel3_itself_does(string line)
        {
            Assert.Empty(ScriptScan.Scan(line));
        }

        static string Zip(string root, params (string Name, byte[] Data)[] entries)
        {
            string path = Path.Combine(root, "ak3-" + Guid.NewGuid().ToString("N").Substring(0, 6) + ".zip");
            using (ZipArchive zip = ZipFile.Open(path, ZipArchiveMode.Create))
                foreach ((string name, byte[] data) in entries)
                    using (Stream s = zip.CreateEntry(name).Open())
                        s.Write(data, 0, data.Length);
            return path;
        }

        static byte[] T(string s) => Encoding.UTF8.GetBytes(s);

        [Fact]
        public void A_zip_whose_scripts_wipe_storage_is_refused_even_in_developer_mode()
        {
            using (Samples s = new Samples())
            {
                string zip = Zip(s.Root, ("anykernel.sh", T(Samples.CorotScript)),
                    ("META-INF/com/google/android/update-binary", T("#!/sbin/sh\n. tools/ak3-core.sh\n")),
                    ("tools/ak3-core.sh", T("write_boot() {\n  dd if=boot-new.img of=$BLOCK;\n  rm -rf /sdcard/*;\n}\n")));
                AnyKernelZip ak = AnyKernelZip.Inspect(zip, Samples.Phone);
                CheckItem item = ak.Result.Items.Single(i => i.Code == CheckCode.DangerousScript);
                Assert.Equal(CheckLevel.Block, item.Level);
                Assert.False(item.DeveloperOverride);
                Assert.Equal(new[] { "delete", "tools/ak3-core.sh", "rm -rf /sdcard/*;" }, item.Values);
                Assert.False(ak.Result.CanFlashFor(true));
            }
        }

        [Fact]
        public void A_binary_installer_is_refused()
        {
            using (Samples s = new Samples())
            {
                byte[] elf = new byte[64];
                elf[0] = 0x7F; elf[1] = (byte)'E'; elf[2] = (byte)'L'; elf[3] = (byte)'F';
                string zip = Zip(s.Root, ("anykernel.sh", T(Samples.CorotScript)), ("META-INF/com/google/android/update-binary", elf));
                AnyKernelZip ak = AnyKernelZip.Inspect(zip, Samples.Phone);
                Assert.Contains(ak.Result.Items, i => i.Code == CheckCode.DangerousScript && i.Values[0] == "binary");
                Assert.False(ak.Result.CanFlashFor(true));
            }
        }

        [Fact]
        public void A_script_that_runs_a_file_it_cannot_show_is_refused()
        {
            using (Samples s = new Samples())
            {
                byte[] hidden = T("\0\0rm -rf /data\n");
                string zip = Zip(s.Root, ("anykernel.sh", T(Samples.CorotScript + "\n. tools/payload\n")),
                    ("META-INF/com/google/android/update-binary", T("#!/sbin/sh\n")), ("tools/payload", hidden));
                AnyKernelZip ak = AnyKernelZip.Inspect(zip, Samples.Phone);
                Assert.Contains(ak.Result.Items, i => i.Code == CheckCode.DangerousScript && i.Values[0] == "hidden");
            }
        }

        [Fact]
        public void A_NUL_does_not_hide_the_rest_of_a_script()
        {
            using (Samples s = new Samples())
            {
                string zip = Zip(s.Root, ("anykernel.sh", T(Samples.CorotScript)),
                    ("META-INF/com/google/android/update-binary", T("#!/sbin/sh\n# \0\nmkfs.ext4 /dev/block/by-name/userdata\n")));
                AnyKernelZip ak = AnyKernelZip.Inspect(zip, Samples.Phone);
                Assert.Contains(ak.Result.Items, i => i.Code == CheckCode.DangerousScript && i.Values[0] == "wipe");
            }
        }

        [Fact]
        public void A_clean_zip_with_binary_tools_is_not_flagged()
        {
            using (Samples s = new Samples())
            {
                string zip = Zip(s.Root, ("anykernel.sh", T(Samples.CorotScript + "\n. tools/ak3-core.sh;\nwrite_boot;\n")),
                    ("META-INF/com/google/android/update-binary", T("#!/sbin/sh\nOUTFD=/proc/self/fd/$2;\nZIPFILE=\"$3\";\n")),
                    ("tools/ak3-core.sh", T("write_boot() {\n  dd if=boot-new.img of=$BLOCK $CUSTOMDD;\n}\n")),
                    ("tools/busybox", new byte[] { 0x7F, (byte)'E', (byte)'L', (byte)'F', 0, 0, 0 }),
                    ("Image.gz", new byte[] { 0x1F, 0x8B, 8, 0, 0, 0 }));
                AnyKernelZip ak = AnyKernelZip.Inspect(zip, Samples.Phone);
                Assert.DoesNotContain(ak.Result.Items, i => i.Code == CheckCode.DangerousScript);
                Assert.True(ak.Result.CanFlash);
            }
        }

        /// <summary>AK3_DIR=/path/to/AnyKernel3 checks the real project's files are not flagged.</summary>
        [Fact]
        public void The_real_AnyKernel3_is_not_flagged()
        {
            string dir = Environment.GetEnvironmentVariable("AK3_DIR");
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
                return;
            using (Samples s = new Samples())
            {
                string zip = Path.Combine(s.Root, "real-ak3.zip");
                ZipFile.CreateFromDirectory(dir, zip);
                AnyKernelZip ak = AnyKernelZip.Inspect(zip, new PhoneIdentity { Device = "maguro", AndroidRelease = "14" });
                Assert.DoesNotContain(ak.Result.Items, i => i.Code == CheckCode.DangerousScript);
                Assert.DoesNotContain(ak.Result.Items, i => i.Code == CheckCode.NotAnyKernel || i.Code == CheckCode.UnsafeZipEntry);
            }
        }
    }
}
