using System;
using System.Linq;
using System.Text.Json;

namespace FastbootStudio.Mobile.Core
{
    /// <summary>
    /// The app's update check: the latest GitHub release of FastbootStudio-Mobile against the
    /// installed version. It only reads; the person opens the release page and installs by hand.
    /// </summary>
    public static class UpdateCheck
    {
        public const string Api = "https://api.github.com/repos/ifoknr/FastbootStudio-Mobile/releases/latest";
        public const string Releases = "https://github.com/ifoknr/FastbootStudio-Mobile/releases";

        /// <summary>At most one automatic check a day.</summary>
        public static readonly TimeSpan Every = TimeSpan.FromHours(24);

        public sealed class Release
        {
            public string Version;   // "1.0.1"
            public string Page;      // the release page on github.com/ifoknr/
            public string Apk;       // the APK asset's name, or null
            public long ApkBytes;
        }

        /// <summary>The release from the API's JSON, or null when it is not one of the project's own, readable releases.</summary>
        public static Release Parse(string json)
        {
            try
            {
                using (JsonDocument doc = JsonDocument.Parse(json ?? ""))
                {
                    JsonElement r = doc.RootElement;
                    if (r.ValueKind != JsonValueKind.Object || !r.TryGetProperty("tag_name", out JsonElement tag) || tag.ValueKind != JsonValueKind.String)
                        return null;
                    if (r.TryGetProperty("draft", out JsonElement draft) && draft.ValueKind == JsonValueKind.True)
                        return null;
                    string version = tag.GetString().TrimStart('v', 'V');
                    if (Numbers(version) == null)
                        return null;
                    string page = r.TryGetProperty("html_url", out JsonElement u) && u.ValueKind == JsonValueKind.String ? u.GetString() : null;
                    // The page opened is always the project's own, whatever the reply says.
                    if (page == null || !page.StartsWith(Releases + "/", StringComparison.Ordinal))
                        page = Releases;
                    Release rel = new Release { Version = version, Page = page };
                    if (r.TryGetProperty("assets", out JsonElement assets) && assets.ValueKind == JsonValueKind.Array)
                        foreach (JsonElement a in assets.EnumerateArray())
                        {
                            string name = a.TryGetProperty("name", out JsonElement n) && n.ValueKind == JsonValueKind.String ? n.GetString() : null;
                            if (name != null && name.EndsWith(".apk", StringComparison.OrdinalIgnoreCase))
                            {
                                rel.Apk = name;
                                rel.ApkBytes = a.TryGetProperty("size", out JsonElement sz) && sz.ValueKind == JsonValueKind.Number ? sz.GetInt64() : 0;
                                break;
                            }
                        }
                    return rel;
                }
            }
            catch (JsonException) { return null; }
        }

        /// <summary>True when <paramref name="latest"/> ("1.0.10") is newer than <paramref name="installed"/> ("1.0.9").</summary>
        public static bool IsNewer(string latest, string installed)
        {
            int[] a = Numbers(latest), b = Numbers(installed);
            if (a == null || b == null)
                return false;
            for (int i = 0; i < Math.Max(a.Length, b.Length); i++)
            {
                int x = i < a.Length ? a[i] : 0, y = i < b.Length ? b[i] : 0;
                if (x != y)
                    return x > y;
            }
            return false;
        }

        static int[] Numbers(string version)
        {
            string core = (version ?? "").Trim().TrimStart('v', 'V').Split('-', '+', ' ')[0];
            string[] parts = core.Split('.');
            if (parts.Length == 0 || parts.Length > 4)
                return null;
            int[] n = new int[parts.Length];
            for (int i = 0; i < parts.Length; i++)
                if (!int.TryParse(parts[i], out n[i]) || n[i] < 0)
                    return null;
            return n;
        }
    }
}
