using System.Text.Json;
using System.Text.Json.Nodes;
using Android.Content;

namespace FastbootStudio.Mobile.Native
{
    /// <summary>The Settings page, kept by the app (not the page), so developer mode is never taken from the WebView.</summary>
    public sealed class Settings
    {
        readonly ISharedPreferences prefs;

        public Settings(Context context)
        {
            prefs = context.GetSharedPreferences("settings", FileCreationMode.Private);
        }

        /// <summary>"auto" (like the phone), "dark" or "light".</summary>
        public string Theme => prefs.GetString("theme", "auto");

        /// <summary>"ar" or "en"; the phone's language the first time.</summary>
        public string Lang => prefs.GetString("lang", Java.Util.Locale.Default.Language == "ar" ? "ar" : "en");

        public bool DeveloperMode => prefs.GetBoolean("dev", false);
        public bool BackupFirst => prefs.GetBoolean("backupFirst", true);
        public bool ReadBack => prefs.GetBoolean("readBack", true);

        /// <summary>The lowest battery level a flash on this phone starts at.</summary>
        public int MinBattery => prefs.GetInt("minBattery", 50);

        /// <summary>Text size on every page: "s", "m" (the default), "l" or "xl".</summary>
        public string TextSize => prefs.GetString("textSize", "m");

        /// <summary>Look for a new version once a day; off, the person checks from Settings.</summary>
        public bool AutoUpdate => prefs.GetBoolean("autoUpdate", true);

        /// <summary>When the update check last ran (UTC ticks), so it runs at most once a day.</summary>
        public long LastUpdateCheck
        {
            get => prefs.GetLong("lastUpdateCheck", 0);
            set { using (ISharedPreferencesEditor e = prefs.Edit()) { e.PutLong("lastUpdateCheck", value); e.Apply(); } }
        }

        /// <summary>Changes one setting; anything not listed here, or out of range, is ignored.</summary>
        public void Set(string key, JsonNode value)
        {
            using (ISharedPreferencesEditor e = prefs.Edit())
            {
                switch (key)
                {
                    case "theme":
                        string theme = Text(value);
                        if (theme == "auto" || theme == "dark" || theme == "light")
                            e.PutString("theme", theme);
                        break;
                    case "lang":
                        string lang = Text(value);
                        if (lang == "ar" || lang == "en")
                            e.PutString("lang", lang);
                        break;
                    case "textSize":
                        string size = Text(value);
                        if (size == "s" || size == "m" || size == "l" || size == "xl")
                            e.PutString("textSize", size);
                        break;
                    case "dev":
                    case "autoUpdate":
                    case "backupFirst":
                    case "readBack":
                        if (value != null && (value.GetValueKind() == JsonValueKind.True || value.GetValueKind() == JsonValueKind.False))
                            e.PutBoolean(key, value.GetValue<bool>());
                        break;
                    case "minBattery":
                        if (value != null && value.GetValueKind() == JsonValueKind.Number)
                        {
                            int level = value.GetValue<int>();
                            if (level >= 20 && level <= 90)
                                e.PutInt("minBattery", level);
                        }
                        break;
                }
                e.Apply();
            }
            Log.Add("setting " + key + " = " + value?.ToJsonString());
        }

        static string Text(JsonNode n) => n != null && n.GetValueKind() == JsonValueKind.String ? n.GetValue<string>() : null;

        public JsonObject ToJson() => new JsonObject
        {
            ["theme"] = Theme, ["lang"] = Lang, ["dev"] = DeveloperMode, ["backupFirst"] = BackupFirst,
            ["readBack"] = ReadBack, ["minBattery"] = MinBattery, ["autoUpdate"] = AutoUpdate,
            ["textSize"] = TextSize,
        };
    }
}
