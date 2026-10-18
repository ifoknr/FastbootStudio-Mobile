using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using FastbootEnhance.Core.Adb;

namespace FastbootStudio.Mobile.Core.Tests
{
    /// <summary>Small images, backup folders and AnyKernel3 zips written to a temporary folder.</summary>
    sealed class Samples : IDisposable
    {
        public readonly string Root = Path.Combine(Path.GetTempPath(), "fbsm-" + Guid.NewGuid().ToString("N"));

        public Samples() { Directory.CreateDirectory(Root); }

        public void Dispose()
        {
            try { Directory.Delete(Root, true); } catch (IOException) { }
        }

        public static readonly PhoneIdentity Phone = new PhoneIdentity
        {
            Serial = "a1b2c3d4", Model = "Xiaomi 13T Pro", Device = "corot", Product = "corot_global",
            AndroidRelease = "14", HasInitBoot = true, PatchLevel = "2026-09-05",
        };

        /// <summary>A header-version-4 boot image: with a kernel it is a boot image, without one an init_boot.</summary>
        public static byte[] BootImage(bool kernel)
        {
            const int page = 4096;
            byte[] head = new byte[page];
            Encoding.ASCII.GetBytes("ANDROID!").CopyTo(head, 0);
            BitConverter.GetBytes((uint)(kernel ? page : 0)).CopyTo(head, 8);
            BitConverter.GetBytes((uint)page).CopyTo(head, 12);
            BitConverter.GetBytes(4u).CopyTo(head, 40);
            byte[] image = new byte[page * (kernel ? 3 : 2)];
            head.CopyTo(image, 0);
            for (int i = page; i < image.Length; i++)
                image[i] = (byte)(i * 7);
            return image;
        }

        public static byte[] Vbmeta() { byte[] b = new byte[4096]; Encoding.ASCII.GetBytes("AVB0").CopyTo(b, 0); return b; }

        public static byte[] Dtbo() { byte[] b = new byte[4096]; b[0] = 0xD7; b[1] = 0xB7; b[2] = 0xAB; b[3] = 0x1E; return b; }

        public string File(string name, byte[] data)
        {
            string path = Path.Combine(Root, name);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            System.IO.File.WriteAllBytes(path, data);
            return path;
        }

        /// <summary>A backup folder of this phone (or another) with the given images, SHA256SUMS and backup-info.txt.</summary>
        public BackupFolder Backup(string serial, string model, string product, params (string name, byte[] data)[] images)
        {
            string folder = Path.Combine(Root, "backup-" + Guid.NewGuid().ToString("N").Substring(0, 6));
            Directory.CreateDirectory(folder);
            StringBuilder sums = new StringBuilder();
            foreach ((string name, byte[] data) in images)
            {
                System.IO.File.WriteAllBytes(Path.Combine(folder, name), data);
                sums.Append(Sha256Sums.HashFile(Path.Combine(folder, name))).Append("  ").Append(name).Append('\n');
            }
            System.IO.File.WriteAllText(Path.Combine(folder, "SHA256SUMS"), sums.ToString());
            System.IO.File.WriteAllText(Path.Combine(folder, "backup-info.txt"),
                "Fastboot Studio backup\ndate\t2026-10-15 22:18:04 +03:00\nserial\t" + serial + "\nmodel\t" + model
                + "\nproduct\t" + product + "\nmode\tcompanion\nset\tboot\n\npartition\tbytes\tsha256\n");
            return BackupFolder.Read(folder);
        }

        /// <summary>An AnyKernel3-shaped zip with the given anykernel.sh, plus any extra entries.</summary>
        public string AnyKernel(string script, bool installer = true, params string[] extraEntries)
        {
            string path = Path.Combine(Root, "ak3-" + Guid.NewGuid().ToString("N").Substring(0, 6) + ".zip");
            using (ZipArchive zip = ZipFile.Open(path, ZipArchiveMode.Create))
            {
                if (script != null)
                    Write(zip, "anykernel.sh", script);
                if (installer)
                    Write(zip, "META-INF/com/google/android/update-binary", "#!/sbin/sh\n");
                Write(zip, "Image.gz", "kernel");
                foreach (string extra in extraEntries)
                    Write(zip, extra, "x");
            }
            return path;
        }

        static void Write(ZipArchive zip, string name, string text)
        {
            using (StreamWriter w = new StreamWriter(zip.CreateEntry(name).Open()))
                w.Write(text);
        }

        public const string CorotScript = @"### AnyKernel3 Ramdisk Mod Script
properties() { '
kernel.string=Kernel v4.2 by dev
do.devicecheck=1
do.modules=0
device.name1=corot
device.name2=corotin
supported.versions=13 - 15
'; } # end properties

BLOCK=boot;
IS_SLOT_DEVICE=auto;
";
    }
}
