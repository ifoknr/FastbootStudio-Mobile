using System;
using System.Collections.Generic;
using System.Linq;
using FastbootEnhance.Core.Adb;

namespace FastbootStudio.Mobile.Core
{
    /// <summary>One backup folder as the listing script reports it (read as root from shared storage).</summary>
    public sealed class BackupEntry
    {
        public string Name { get; set; }
        public string Serial { get; set; }
        public string Model { get; set; }
        public string Product { get; set; }
        public string Date { get; set; }
        public string Set { get; set; }

        /// <summary>Who made it: "mobile" (this app), "companion" (the module), or the Windows app's mode.</summary>
        public string Mode { get; set; }
        public long Kb { get; set; }

        /// <summary>File name to SHA-256, from SHA256SUMS.</summary>
        public Dictionary<string, string> Sums { get; } = new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>True when the folder names this phone (by serial; or model and product when it has no serial).</summary>
        public bool IsFrom(PhoneIdentity phone)
        {
            if (!string.IsNullOrEmpty(Serial) && !string.IsNullOrEmpty(phone?.Serial))
                return Serial == phone.Serial;
            return !string.IsNullOrEmpty(Model) && Model == phone?.Model && (string.IsNullOrEmpty(Product) || Product == phone?.Product);
        }
    }

    public static class BackupListing
    {
        /// <summary>
        /// Lists every finished backup under the root, newest first. Hidden .partial- folders are
        /// not matched by the glob, so a backup still being written never shows.
        /// </summary>
        public static string Script(string root) =>
            "for d in " + Sh.Q(root) + "/*/; do [ -d \"$d\" ] || continue; n=${d%/}; n=${n##*/}; echo \"@@name $n\"; "
            + "head -n 12 \"$d/backup-info.txt\" 2>/dev/null; echo '@@sums'; cat \"$d/SHA256SUMS\" 2>/dev/null; "
            + "echo \"@@kb $(du -sk \"$d\" | cut -f1)\"; done";

        public static List<BackupEntry> Parse(string output)
        {
            List<BackupEntry> list = new List<BackupEntry>();
            BackupEntry current = null;
            bool sums = false;
            List<string> sumLines = new List<string>();
            void Close()
            {
                if (current == null) return;
                foreach (KeyValuePair<string, string> e in Sha256Sums.Parse(string.Join("\n", sumLines)))
                    current.Sums[e.Key] = e.Value;
                list.Add(current);
            }
            foreach (string raw in (output ?? "").Replace("\r", "").Split('\n'))
            {
                if (raw.StartsWith("@@name ", StringComparison.Ordinal))
                {
                    Close();
                    current = new BackupEntry { Name = raw.Substring(7).Trim() };
                    sums = false;
                    sumLines.Clear();
                    continue;
                }
                if (current == null)
                    continue;
                if (raw == "@@sums") { sums = true; continue; }
                if (raw.StartsWith("@@kb ", StringComparison.Ordinal))
                {
                    long.TryParse(raw.Substring(5).Trim(), out long kb);
                    current.Kb = kb;
                    sums = false;
                    continue;
                }
                if (sums) { sumLines.Add(raw); continue; }
                int tab = raw.IndexOf('\t');
                if (tab <= 0) continue;
                string value = raw.Substring(tab + 1).Trim();
                switch (raw.Substring(0, tab))
                {
                    case "serial": current.Serial = value; break;
                    case "model": current.Model = value; break;
                    case "product": current.Product = value; break;
                    case "date": current.Date = value; break;
                    case "set": current.Set = value; break;
                    case "mode": current.Mode = value; break;
                }
            }
            Close();
            // Names end with yyyyMMdd-HHmmss: newest first.
            return list.Where(b => IsName(b.Name)).OrderByDescending(b => b.Name.Length >= 15 ? b.Name.Substring(b.Name.Length - 15) : b.Name).ToList();
        }

        /// <summary>A folder name the app may act on: no path, nothing hidden.</summary>
        public static bool IsName(string name) =>
            !string.IsNullOrEmpty(name) && !name.StartsWith(".", StringComparison.Ordinal) && name.IndexOf('/') < 0 && name != "..";

        /// <summary>"sha256sum -c" output: the files reported FAILED (changed, missing or unreadable).</summary>
        public static List<string> Failed(string checkOutput) =>
            Sh.Lines(checkOutput).Where(l => l.Contains(": FAILED")).Select(l => l.Substring(0, l.IndexOf(": FAILED", StringComparison.Ordinal))).ToList();
    }
}
