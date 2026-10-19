using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using FastbootStudio.Mobile.Core;

namespace FastbootStudio.Mobile.Native
{
    /// <summary>
    /// The Fastboot Studio Companion module, when it is installed on this rooted phone. The two
    /// stay separate; the app only runs the module's own fbs command, which never writes a
    /// partition: its backups land in the same Backups folder the app reads, so the app lists,
    /// checks and flashes them like its own. It backs up what the app does not: the bootloader
    /// chain, modem and persist (the "critical" set), or everything but userdata.
    /// </summary>
    public sealed class Companion
    {
        public const string Id = "fastboot_studio_companion";
        public const string Dir = "/data/adb/modules/" + Id;
        public const string Fbs = Dir + "/bin/fbs";

        /// <summary>The sets fbs backup takes.</summary>
        public static readonly string[] Sets = { "boot", "critical", "full" };

        readonly SuShell su;

        public Companion(SuShell su) { this.su = su; }

        /// <summary>{installed, version, enabled, usable}; installed is false when the module is not there.</summary>
        public JsonObject Find()
        {
            ShellResult r = su.Run("d=" + Sh.Q(Dir) + "; [ -f \"$d/module.prop\" ] || exit 0; echo installed=1; "
                + "grep -E '^(version|versionCode)=' \"$d/module.prop\"; [ -e \"$d/disable\" ] && echo disabled=1; "
                + "[ -e \"$d/remove\" ] && echo remove=1; [ -f \"$d/bin/fbs\" ] && echo fbs=1; true");
            Dictionary<string, string> v = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string line in Sh.Lines(r.Output))
            {
                int eq = line.IndexOf('=');
                if (eq > 0)
                    v[line.Substring(0, eq)] = line.Substring(eq + 1).Trim();
            }
            bool installed = v.ContainsKey("installed");
            bool enabled = installed && !v.ContainsKey("disabled") && !v.ContainsKey("remove");
            return new JsonObject
            {
                ["installed"] = installed,
                ["version"] = v.TryGetValue("version", out string ver) ? ver : null,
                ["enabled"] = enabled,
                ["usable"] = enabled && v.ContainsKey("fbs"),
            };
        }

        /// <summary>
        /// fbs backup set: its JSON events become the same progress lines as the app's own
        /// backup ("backup boot_a", "backup ok folder"). Returns the folder name, or throws.
        /// </summary>
        public string Backup(string set, Action<string> progress)
        {
            if (!Sets.Contains(set))
                throw new BridgeError("bad-set");
            string folder = null, error = null;
            int saved = 0, failed = 0;
            su.RunLines("sh " + Sh.Q(Fbs) + " backup " + set, line =>
            {
                JsonObject e;
                try { e = JsonNode.Parse(line) as JsonObject; }
                catch (JsonException) { return; }
                switch (Json.Str(e, "event"))
                {
                    case "plan": progress?.Invoke("plan " + Number(e, "count")); break;
                    case "start": progress?.Invoke("backup " + Json.Str(e, "name")); break;
                    case "fail": progress?.Invoke("failed " + Json.Str(e, "name")); break;
                    case "finish":
                        folder = Json.Str(e, "folder");
                        saved = Number(e, "saved");
                        failed = Number(e, "failed");
                        break;
                    case "error": error = Json.Str(e, "msg"); break;
                }
            });
            if (error != null)
                throw new BridgeError("companion-" + (error.StartsWith("usage", StringComparison.Ordinal) ? "failed" : error));
            if (folder == null || saved == 0 || !BackupListing.IsName(folder))
                throw new BridgeError("companion-failed");
            progress?.Invoke("backup ok " + RootFlasher.BackupRoot + "/" + folder);
            if (failed > 0)
                Log.Add("companion: " + failed + " partitions could not be copied");
            return folder;
        }

        /// <summary>fbs bundle: logs and device info for a bug report, IMEI and serial masked. Returns the file.</summary>
        public string Bundle()
        {
            ShellResult r = su.Run("sh " + Sh.Q(Fbs) + " bundle");
            JsonObject o = null;
            try { o = JsonNode.Parse(Sh.Lines(r.Output).LastOrDefault() ?? "") as JsonObject; }
            catch (JsonException) { }
            if (o == null || !Json.Bool(o, "ok"))
                throw new BridgeError("companion-failed");
            return Json.Str(o, "file");
        }

        static int Number(JsonObject o, string key)
        {
            if (o == null || !o.TryGetPropertyValue(key, out JsonNode n) || n == null)
                return 0;
            if (n.GetValueKind() == JsonValueKind.Number)
                return n.GetValue<int>();
            return n.GetValueKind() == JsonValueKind.String && int.TryParse(n.GetValue<string>(), out int i) ? i : 0;
        }
    }
}
