using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using Xunit;

namespace FastbootStudio.Mobile.Core.Tests
{
    public class KernelCompareTests
    {
        // The Infinix X6878 IFOKNR tested on: 6.1.162, android14.
        static readonly PhoneIdentity Phone = new PhoneIdentity
        {
            Serial = "1667470619007178", Model = "Infinix X6878", Device = "Infinix-X6878", AndroidRelease = "16",
            KernelRelease = "6.1.162-android14-5-g58715584-ab59957831", HasInitBoot = true,
        };

        // A zip like 5.10.107-android13-2022-05-KernelSU-AnyKernel3.zip: device check off, kernel.string without a version.
        const string Script = "properties() { '\nkernel.string=Wild Kernels by TheWildJames\ndo.devicecheck=0\n'; }\nBLOCK=boot;\nIS_SLOT_DEVICE=auto;\n";

        static byte[] KernelImage(string release, bool gzip, bool dtbAfter = false)
        {
            byte[] image = new byte[200000];
            new Random(1).NextBytes(image);
            byte[] line = Encoding.ASCII.GetBytes("Linux version " + release + " (build@host) (clang) #1 SMP PREEMPT\n");
            line.CopyTo(image, 120000);
            if (!gzip)
                return image;
            using (MemoryStream m = new MemoryStream())
            {
                using (GZipStream gz = new GZipStream(m, CompressionLevel.Fastest, true))
                    gz.Write(image, 0, image.Length);
                if (dtbAfter)
                    m.Write(new byte[] { 0xD0, 0x0D, 0xFE, 0xED, 0, 0, 0, 0x40, 1, 2, 3, 4 }, 0, 12);
                return m.ToArray();
            }
        }

        static string Zip(string root, string name, string kernelEntry, byte[] kernel)
        {
            string path = Path.Combine(root, name);
            using (ZipArchive zip = ZipFile.Open(path, ZipArchiveMode.Create))
            {
                void Put(string n, byte[] d) { using (Stream s = zip.CreateEntry(n).Open()) s.Write(d, 0, d.Length); }
                Put("anykernel.sh", Encoding.UTF8.GetBytes(Script));
                Put("META-INF/com/google/android/update-binary", Encoding.UTF8.GetBytes("#!/sbin/sh\n"));
                if (kernelEntry != null)
                    Put(kernelEntry, kernel);
            }
            return path;
        }

        [Theory]
        [InlineData("Image", false, false)]
        [InlineData("Image.gz", true, false)]
        [InlineData("Image.gz-dtb", true, true)]
        public void A_5_10_kernel_for_a_6_1_phone_is_refused(string entry, bool gzip, bool dtb)
        {
            using Samples s = new Samples();
            string zip = Zip(s.Root, "kernel.zip", entry, KernelImage("5.10.107-android13-4-g0a1b2c", gzip, dtb));
            AnyKernelZip ak = AnyKernelZip.Inspect(zip, Phone);
            Assert.Equal("5.10.107", ak.Kernel.Numbers);
            CheckItem item = Assert.Single(ak.Result.Items, i => i.Code == CheckCode.KernelSeriesDiffers);
            Assert.Equal(new[] { "5.10.107", "6.1.162", "image" }, item.Values);
            Assert.Equal(CheckLevel.Block, ak.Result.Verdict);
            Assert.True(item.DeveloperOverride);
        }

        [Fact]
        public void Without_a_readable_kernel_the_zip_name_tells_the_version()
        {
            using Samples s = new Samples();
            string zip = Zip(s.Root, "5.10.107-android13-2022-05-KernelSU-AnyKernel3.zip", "Image.lz4", new byte[] { 1, 2, 3, 4, 5 });
            AnyKernelZip ak = AnyKernelZip.Inspect(zip, Phone);
            CheckItem item = Assert.Single(ak.Result.Items, i => i.Code == CheckCode.KernelSeriesDiffers);
            Assert.Equal(new[] { "5.10.107", "6.1.162", "name" }, item.Values);
        }

        [Fact]
        public void A_newer_build_of_the_same_kernel_is_a_notice_and_the_same_one_is_ok()
        {
            using Samples s = new Samples();
            AnyKernelZip newer = AnyKernelZip.Inspect(Zip(s.Root, "a.zip", "Image.gz", KernelImage("6.1.170-android14-11-gffff", true)), Phone);
            Assert.Contains(newer.Result.Items, i => i.Code == CheckCode.KernelVersionDiffers && i.Level == CheckLevel.Notice);
            Assert.DoesNotContain(newer.Result.Items, i => i.Code == CheckCode.KernelSeriesDiffers);

            AnyKernelZip same = AnyKernelZip.Inspect(Zip(s.Root, "b.zip", "Image", KernelImage("6.1.162-android14-11-gffff", false)), Phone);
            Assert.Contains(same.Result.Items, i => i.Code == CheckCode.KernelSame && i.Level == CheckLevel.Ok);
        }

        [Fact]
        public void A_zip_that_says_nothing_about_its_kernel_gets_no_kernel_item()
        {
            using Samples s = new Samples();
            AnyKernelZip ak = AnyKernelZip.Inspect(Zip(s.Root, "plain.zip", null, null), Phone);
            Assert.DoesNotContain(ak.Result.Items, i => i.Code == CheckCode.KernelSeriesDiffers || i.Code == CheckCode.KernelSame || i.Code == CheckCode.KernelVersionDiffers);
        }

        [Theory]
        [InlineData("6.1.162-android14-5-g58715584-ab59957831", "6.1.162", "6.1", "android14-6.1")]
        [InlineData("5.10.107", "5.10.107", "5.10", null)]
        [InlineData("4.19.157-perf+", "4.19.157", "4.19", null)]
        public void Releases_are_read(string release, string numbers, string series, string kmi)
        {
            KernelCompare.Kernel k = KernelCompare.Parse(release);
            Assert.Equal(numbers, k.Numbers);
            Assert.Equal(series, k.Series);
            Assert.Equal(kmi, k.Kmi);
        }
    }
}
