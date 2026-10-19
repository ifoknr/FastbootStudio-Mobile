using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using FastbootStudio.Mobile.Core;

namespace FastbootStudio.Mobile.Native
{
    /// <summary>JSON for the WebView, built by hand so nothing depends on reflection.</summary>
    static class Json
    {
        public static JsonObject Check(CheckResult r, bool developerMode)
        {
            JsonArray items = new JsonArray();
            foreach (CheckItem i in r.Items)
                items.Add(new JsonObject
                {
                    ["level"] = i.Effective(developerMode).ToString().ToLowerInvariant(),
                    ["original"] = i.Level.ToString().ToLowerInvariant(),
                    ["override"] = i.DeveloperOverride,
                    ["code"] = i.Code.ToString(),
                    ["values"] = new JsonArray(i.Values.Select(v => (JsonNode)JsonValue.Create(v)).ToArray()),
                });
            return new JsonObject
            {
                ["verdict"] = r.VerdictFor(developerMode).ToString().ToLowerInvariant(),
                ["canFlash"] = r.CanFlashFor(developerMode),
                ["sha256"] = r.Sha256,
                ["items"] = items,
            };
        }

        public static JsonArray Strings(IEnumerable<string> values) => new JsonArray(values.Select(v => (JsonNode)JsonValue.Create(v)).ToArray());

        public static string Str(JsonObject o, string key) => o != null && o.TryGetPropertyValue(key, out JsonNode n) && n != null ? n.GetValue<string>() : null;

        public static bool Bool(JsonObject o, string key) => o != null && o.TryGetPropertyValue(key, out JsonNode n) && n != null && n.GetValueKind() == JsonValueKind.True;
    }
}
