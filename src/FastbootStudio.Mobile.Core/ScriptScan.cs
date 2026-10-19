using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace FastbootStudio.Mobile.Core
{
    /// <summary>
    /// Reads the shell scripts of an AnyKernel3 zip for what no kernel installer does: writing
    /// the bootloader, modem, persist or data partitions, formatting or wiping storage,
    /// deleting the data or the shared storage, or fetching anything from the network.
    /// AnyKernel3's own scripts write only the partitions they find by variable ($BLOCK,
    /// $imgblock), so a partition named in a write is the zip's own doing.
    /// </summary>
    public static class ScriptScan
    {
        /// <summary>Partitions a kernel zip never writes: the boot chain, radio, calibration, keys and data.</summary>
        static readonly string Forbidden =
            "xbl|xbl_config|xbl_sc|abl|aboot|sbl1|tz|hyp|rpm|keymaster|cmnlib|cmnlib64|devcfg|qupfw|uefisecapp|aop|"
            + "modem|modemst1|modemst2|fsg|fsc|persist|efs|sec_efs|nvram|nvdata|nvcfg|proinfo|protect1|protect2|"
            + "preloader|preloader_a|preloader_b|lk|tee1|tee2|seccfg|frp|devinfo|param|up_param|bootloader|"
            + "super|userdata|metadata|system|vendor|product|odm|system_ext";

        static readonly Regex[] Writes =
        {
            // dd ... of=/dev/block/.../modem, or > /dev/block/.../persist
            new Regex(@"\bof=\S*/dev/block/\S*?/(?:by-name/)?(" + Forbidden + @")(_a|_b)?(?=[\s;""')&|]|$)", RegexOptions.IgnoreCase),
            new Regex(@">\s*\S*/dev/block/\S*?/(?:by-name/)?(" + Forbidden + @")(_a|_b)?(?=[\s;""')&|]|$)", RegexOptions.IgnoreCase),
            // AnyKernel3's own helper, named with a partition it must not touch
            new Regex(@"\bflash_generic\s+[""']?(" + Forbidden + @")\b", RegexOptions.IgnoreCase),
        };

        static readonly Regex Wipe = new Regex(@"(^|[\s;&|(`])(blkdiscard|mkfs(\.\w+)?|mke2fs|make_ext4fs|make_f2fs|mkfs\.f2fs|sgdisk|parted|wipefs|sfdisk|fdisk)\b");

        static readonly Regex Network = new Regex(@"(^|[\s;&|(`])(curl|wget|nc|ncat|telnet|ftpget|tftp)\s");

        /// <summary>rm -rf of a whole tree the phone needs: /, /data, the shared storage, /system...</summary>
        static readonly Regex Delete = new Regex(
            @"(^|[\s;&|(`])rm\s+(-[a-zA-Z]+\s+)*[""']?(/|/\*|/data|/data/media(/0)?|/sdcard|/storage/emulated(/0)?|/storage|/system|/vendor|/persist|/mnt/vendor/persist|/metadata|/efs)/?\*?[""']?(?=[\s;&|)]|$)");

        public sealed class Hit
        {
            public Hit(string reason, string line) { Reason = reason; Line = line; }

            /// <summary>"write", "wipe", "delete" or "network".</summary>
            public string Reason { get; }
            public string Line { get; }
        }

        public static List<Hit> Scan(string script)
        {
            List<Hit> hits = new List<Hit>();
            foreach (string raw in (script ?? "").Replace("\r", "").Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal))
                    continue;
                // No installer writes a line this long: it would only hide a command (and slow every pattern down).
                if (line.Length > MaxLine)
                {
                    hits.Add(new Hit("hidden", line.Substring(0, 80) + "…"));
                    continue;
                }
                string reason = Writes.Any(r => r.IsMatch(line)) ? "write"
                    : Wipe.IsMatch(line) ? "wipe"
                    : Delete.IsMatch(line) ? "delete"
                    : Network.IsMatch(line) ? "network"
                    : null;
                if (reason != null)
                    hits.Add(new Hit(reason, line.Length > 160 ? line.Substring(0, 160) + "…" : line));
            }
            return hits;
        }

        public const int MaxText = 4 << 20;
        public const int MaxLine = 8192;

        /// <summary>
        /// The entry as text, or null for a binary (a NUL in its first 8 KiB: busybox,
        /// magiskboot, the kernel). A text file over <see cref="MaxText"/> is not read.
        /// </summary>
        public static string ReadText(System.IO.Stream stream, long length, bool script, out bool tooBig)
        {
            tooBig = false;
            using (stream)
            {
                byte[] head = new byte[(int)Math.Min(8192, Math.Max(0, length))];
                int got = 0, n;
                while (got < head.Length && (n = stream.Read(head, got, head.Length - got)) > 0)
                    got += n;
                // A script is always read: a NUL in it must not hide the rest.
                if (!script && Array.IndexOf(head, (byte)0, 0, got) >= 0)
                    return null;
                if (length > MaxText)
                {
                    tooBig = true;
                    return null;
                }
                using (System.IO.MemoryStream all = new System.IO.MemoryStream())
                {
                    all.Write(head, 0, got);
                    stream.CopyTo(all);
                    return System.Text.Encoding.UTF8.GetString(all.ToArray()).Replace('\0', ' ');
                }
            }
        }

        static readonly Regex Source = new Regex(@"(^|[\s;&|(])(\.|source|sh|ash|bash|exec)\s+[""']?([^\s;""'&|)]+)");

        /// <summary>The file names a script sources or runs ("tools/ak3-core.sh" is "ak3-core.sh").</summary>
        public static IEnumerable<string> Sourced(string script)
        {
            foreach (string raw in (script ?? "").Replace("\r", "").Split('\n'))
            {
                string line = raw.Trim();
                if (line.StartsWith("#", StringComparison.Ordinal))
                    continue;
                foreach (Match m in Source.Matches(line))
                {
                    string path = m.Groups[3].Value;
                    int slash = path.LastIndexOf('/');
                    yield return slash >= 0 ? path.Substring(slash + 1) : path;
                }
            }
        }

        /// <summary>The files read: the installer, anykernel.sh and every .sh in the zip.</summary>
        public static bool IsScript(string entry)
        {
            string n = (entry ?? "").Replace('\\', '/');
            return n == AnyKernelZip.Script || n == AnyKernelZip.Installer || n == "META-INF/com/google/android/updater-script"
                || n.EndsWith(".sh", StringComparison.OrdinalIgnoreCase);
        }
    }
}
