using System;
using System.Collections.Generic;
using System.IO;
using FastbootEnhance.Core.Adb;

namespace FastbootStudio.Mobile.Core
{
    /// <summary>
    /// A backup folder in the layout Fastboot Studio (Windows) and its Companion write:
    /// partition images, SHA256SUMS and backup-info.txt, whose header names the phone.
    /// </summary>
    public sealed class BackupFolder
    {
        public const string InfoFile = "backup-info.txt";

        public string Path { get; private set; }
        public string Name => System.IO.Path.GetFileName(Path);
        public string Serial { get; private set; }
        public string Model { get; private set; }
        public string Product { get; private set; }
        public string Date { get; private set; }
        public string Set { get; private set; }

        /// <summary>File name ("init_boot_a.img") to its SHA-256, from SHA256SUMS; empty when there is none.</summary>
        public IReadOnlyDictionary<string, string> Sums { get; private set; }

        public bool HasSums => Sums.Count > 0;

        /// <summary>Reads a backup folder. Missing files leave their fields empty; nothing is trusted yet.</summary>
        public static BackupFolder Read(string folder)
        {
            BackupFolder backup = new BackupFolder { Path = folder };
            Dictionary<string, string> sums = new Dictionary<string, string>(StringComparer.Ordinal);
            string sumsPath = System.IO.Path.Combine(folder, Sha256Sums.FileName);
            if (File.Exists(sumsPath))
                foreach (KeyValuePair<string, string> entry in Sha256Sums.Parse(File.ReadAllText(sumsPath)))
                    sums[entry.Key] = entry.Value.ToLowerInvariant();
            backup.Sums = sums;

            string infoPath = System.IO.Path.Combine(folder, InfoFile);
            if (File.Exists(infoPath))
                backup.ParseInfo(File.ReadAllText(infoPath));
            return backup;
        }

        /// <summary>The "key\tvalue" header of backup-info.txt, up to the blank line before the table.</summary>
        internal void ParseInfo(string text)
        {
            foreach (string raw in text.Replace("\r", "").Split('\n'))
            {
                if (raw.Length == 0)
                    break;
                int tab = raw.IndexOf('\t');
                if (tab <= 0)
                    continue;
                string value = raw.Substring(tab + 1).Trim();
                switch (raw.Substring(0, tab))
                {
                    case "serial": Serial = value; break;
                    case "model": Model = value; break;
                    case "product": Product = value; break;
                    case "date": Date = value; break;
                    case "set": Set = value; break;
                }
            }
        }

        /// <summary>The partition an image in this folder holds: "init_boot_a.img" is "init_boot_a".</summary>
        public static string PartitionOf(string fileName)
        {
            if (fileName == null || !fileName.EndsWith(".img", StringComparison.OrdinalIgnoreCase))
                return null;
            return fileName.Substring(0, fileName.Length - 4);
        }
    }
}
