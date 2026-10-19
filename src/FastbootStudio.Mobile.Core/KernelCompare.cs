using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using FastbootEnhance.Core.Images;

namespace FastbootStudio.Mobile.Core
{
    /// <summary>
    /// A kernel in a file against the one the phone runs. Another series (5.10 on a 6.1
    /// phone) does not start: refused, and only developer mode lets it through. The same series
    /// with another interface (android13-5.15 on android14-5.15) may not load the phone's
    /// modules: the name is typed. A newer or older build of the same series (6.1.75 → 6.1.162)
    /// is normal for custom kernels: a light notice.
    /// </summary>
    public static class KernelCompare
    {
        static readonly Regex Release = new Regex(@"^(\d+)\.(\d+)(?:\.(\d+))?(?:-(android\d+))?");
        static readonly Regex InText = new Regex(@"(?<![\d.])(\d+)\.(\d+)\.(\d+)(?:-(android\d+))?");

        public sealed class Kernel
        {
            /// <summary>"6.1.162"</summary>
            public string Numbers;
            /// <summary>"6.1"</summary>
            public string Series;
            /// <summary>"android14-6.1", or null when the release does not say.</summary>
            public string Kmi;
            /// <summary>"image" when read from the kernel itself, "name" when only the file's name or kernel.string says it.</summary>
            public string Source;
        }

        /// <summary>A release as uname -r prints it ("6.1.162-android14-5-g58715584"), or null.</summary>
        public static Kernel Parse(string release, string source = "image")
        {
            Match m = Release.Match((release ?? "").Trim());
            return m.Success ? Make(m, source) : null;
        }

        /// <summary>A version written somewhere in a name ("5.10.107-android13-2022-05-KernelSU-AnyKernel3.zip"), or null.</summary>
        public static Kernel FromText(string text)
        {
            Match m = InText.Match(text ?? "");
            return m.Success ? Make(m, "name") : null;
        }

        static Kernel Make(Match m, string source)
        {
            string series = m.Groups[1].Value + "." + m.Groups[2].Value;
            return new Kernel
            {
                Series = series,
                Numbers = m.Groups[3].Success && m.Groups[3].Value.Length > 0 ? series + "." + m.Groups[3].Value : series,
                Kmi = m.Groups[4].Success && m.Groups[4].Value.Length > 0 ? m.Groups[4].Value + "-" + series : null,
                Source = source,
            };
        }

        /// <summary>The check items for a file's kernel against the phone's running one.</summary>
        public static void Add(CheckResult result, Kernel file, string phoneRelease)
        {
            Kernel phone = Parse(phoneRelease);
            if (file == null || phone == null)
                return;
            if (file.Series != phone.Series)
                result.AddOverridable(CheckCode.KernelSeriesDiffers, file.Numbers, phone.Numbers, file.Source);
            else if (file.Kmi != null && phone.Kmi != null && file.Kmi != phone.Kmi)
                result.Add(CheckLevel.Warn, CheckCode.KernelInterfaceDiffers, file.Kmi, phone.Kmi, file.Source);
            else if (file.Numbers != phone.Numbers && file.Numbers.Length > file.Series.Length)
                result.Add(CheckLevel.Notice, CheckCode.KernelVersionDiffers, file.Numbers, phone.Numbers);
            else
                result.Add(CheckLevel.Ok, CheckCode.KernelSame, file.Numbers, file.Source);
        }

        /// <summary>The largest kernel read, packed or unpacked: kernels are 20–60 MiB, and a phone has little memory to spare.</summary>
        public const int MaxKernel = 96 << 20;

        /// <summary>
        /// The "Linux version x.y.z..." release of a kernel image (Image, Image.gz, Image.lz4,
        /// Image.gz-dtb...), or null when it cannot be read.
        /// </summary>
        public static string ReadRelease(Stream stream, long length)
        {
            if (length <= 0 || length > MaxKernel)
                return null;
            byte[] raw = new byte[length];
            int got = 0, n;
            using (stream)
                while (got < raw.Length && (n = stream.Read(raw, got, raw.Length - got)) > 0)
                    got += n;
            CompressionKind kind = Compression.Detect(raw, 0, got);
            byte[] kernel;
            try
            {
                kernel = kind == CompressionKind.Gzip ? Gunzip(raw, got)
                    : kind == CompressionKind.Unknown ? raw
                    : Compression.Decompress(raw, 0, got, kind, MaxKernel);
            }
            catch (InvalidDataException) { kernel = null; }
            if (kernel == null)
                return null;
            byte[] marker = Encoding.ASCII.GetBytes("Linux version ");
            int at = IndexOf(kernel, marker);
            if (at < 0)
                return null;
            int start = at + marker.Length, end = start;
            while (end < kernel.Length && end - start < 128 && kernel[end] > 32 && kernel[end] < 127)
                end++;
            return Encoding.ASCII.GetString(kernel, start, end - start);
        }

        /// <summary>gunzip that keeps what it read when a dtb follows the gzip data (Image.gz-dtb).</summary>
        static byte[] Gunzip(byte[] raw, int length)
        {
            using (MemoryStream output = new MemoryStream())
            {
                try
                {
                    using (GZipStream gz = new GZipStream(new MemoryStream(raw, 0, length, false), CompressionMode.Decompress))
                    {
                        byte[] buffer = new byte[1 << 16];
                        int n;
                        while ((n = gz.Read(buffer, 0, buffer.Length)) > 0 && output.Length < MaxKernel)
                            output.Write(buffer, 0, n);
                    }
                }
                catch (InvalidDataException) { }
                return output.Length > 0 ? output.ToArray() : null;
            }
        }

        static int IndexOf(byte[] data, byte[] needle)
        {
            for (int i = 0; i <= data.Length - needle.Length; i++)
            {
                int j = 0;
                while (j < needle.Length && data[i + j] == needle[j])
                    j++;
                if (j == needle.Length)
                    return i;
            }
            return -1;
        }
    }
}
