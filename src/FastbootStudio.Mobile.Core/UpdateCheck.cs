using System;
using System.Linq;
using System.Text.Json;

namespace FastbootStudio.Mobile.Core
{
    /// <summary>
    /// The app's update check: the newest GitHub release of FastbootStudio-Mobile against the
    /// installed version. A beta is offered newer betas and releases; a release only releases.
    /// It only reads; the person opens the release page and installs by hand.
    /// </summary>
    public static class UpdateCheck
    {
        /// <summary>The release list: "releases/latest" leaves pre-releases out, and the betas are pre-releases.</summary>
        public const string Api = "https://api.github.com/repos/ifoknr/FastbootStudio-Mobile/releases?per_page=20";
        public const string Releases = "https://github.com/ifoknr/FastbootStudio-Mobile/releases";

        /// <summary>At most one automatic check a day.</summary>
        public static readonly TimeSpan Every = TimeSpan.FromHours(24);

        public sealed class Release
        {
            public string Version;   // "1.0.1" or "1.0.0-beta.2"
            public string Page;      // the release page on github.com/ifoknr/
            public string Apk;       // the APK asset's name, or null
            public long ApkBytes;
            public bool Prerelease;
        }

        /// <summary>
        /// The newest release in the API's list that suits <paramref name="installed"/>: pre-releases
        /// count only when the installed version is one too. Null when there is none.
        /// </summary>
        public static Release Newest(string json, string installed)
        {
            bool beta = IsPrerelease(installed);
            try
            {
                using (JsonDocument doc = JsonDocument.Parse(json ?? ""))
                {
                    if (doc.RootElement.ValueKind == JsonValueKind.Object)
                        return Read(doc.RootElement);
                    if (doc.RootElement.ValueKind != JsonValueKind.Array)
                        return null;
                    Release best = null;
                    foreach (JsonElement e in doc.RootElement.EnumerateArray())
                    {
                        Release r = Read(e);
                        if (r == null || (r.Prerelease && !beta))
                            continue;
                        if (best == null || Compare(r.Version, best.Version) > 0)
                            best = r;
                    }
                    return best;
                }
            }
            catch (JsonException) { return null; }
        }

        /// <summary>One release from the API's JSON, or null when it is not one of the project's own, readable releases.</summary>
        public static Release Parse(string json)
        {
            try
            {
                using (JsonDocument doc = JsonDocument.Parse(json ?? ""))
                    return Read(doc.RootElement);
            }
            catch (JsonException) { return null; }
        }

        static Release Read(JsonElement r)
        {
            if (r.ValueKind != JsonValueKind.Object || !r.TryGetProperty("tag_name", out JsonElement tag) || tag.ValueKind != JsonValueKind.String)
                return null;
            if (r.TryGetProperty("draft", out JsonElement draft) && draft.ValueKind == JsonValueKind.True)
                return null;
            string version = tag.GetString().Trim().TrimStart('v', 'V');
            if (Numbers(version) == null)
                return null;
            string page = r.TryGetProperty("html_url", out JsonElement u) && u.ValueKind == JsonValueKind.String ? u.GetString() : null;
            // The page opened is always the project's own, whatever the reply says.
            if (page == null || !page.StartsWith(Releases + "/", StringComparison.Ordinal))
                page = Releases;
            Release rel = new Release
            {
                Version = version,
                Page = page,
                // A tag such as "1.0.0-beta.1" is a beta even if it was not marked as a pre-release.
                Prerelease = (r.TryGetProperty("prerelease", out JsonElement pre) && pre.ValueKind == JsonValueKind.True) || IsPrerelease(version),
            };
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

        /// <summary>True when <paramref name="latest"/> ("1.0.10") is newer than <paramref name="installed"/> ("1.0.9").</summary>
        public static bool IsNewer(string latest, string installed) =>
            Numbers(latest) != null && Numbers(installed) != null && Compare(latest, installed) > 0;

        /// <summary>
        /// Versions by their numbers, then a release above its betas ("1.0.0" &gt; "1.0.0-beta.2"
        /// &gt; "1.0.0-beta.1"). Unreadable versions sort first.
        /// </summary>
        public static int Compare(string a, string b)
        {
            int[] x = Numbers(a), y = Numbers(b);
            if (x == null || y == null)
                return x == null ? (y == null ? 0 : -1) : 1;
            for (int i = 0; i < Math.Max(x.Length, y.Length); i++)
            {
                int p = i < x.Length ? x[i] : 0, q = i < y.Length ? y[i] : 0;
                if (p != q)
                    return p.CompareTo(q);
            }
            string sa = Suffix(a), sb = Suffix(b);
            if (sa == sb)
                return 0;
            if (sa == null || sb == null)
                return sa == null ? 1 : -1;
            string[] ia = sa.Split('.'), ib = sb.Split('.');
            for (int i = 0; i < Math.Min(ia.Length, ib.Length); i++)
            {
                bool na = int.TryParse(ia[i], out int va), nb = int.TryParse(ib[i], out int vb);
                int c = na && nb ? va.CompareTo(vb) : na != nb ? (na ? -1 : 1) : string.CompareOrdinal(ia[i], ib[i]);
                if (c != 0)
                    return Math.Sign(c);
            }
            return ia.Length.CompareTo(ib.Length);
        }

        /// <summary>"1.0.0-beta.1" is a pre-release; "1.0.0" is not.</summary>
        public static bool IsPrerelease(string version) => Suffix(version) != null;

        static string Suffix(string version)
        {
            string v = (version ?? "").Trim().TrimStart('v', 'V').Split('+', ' ')[0];
            int dash = v.IndexOf('-');
            return dash < 0 || dash == v.Length - 1 ? null : v.Substring(dash + 1);
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
