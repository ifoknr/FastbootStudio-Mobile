using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;

namespace FastbootStudio.Mobile.Native
{
    /// <summary>
    /// The SHA-256 of every image in every backup the app made, kept in the app's own storage.
    /// Backups live on shared storage, where another app can change both an image and its
    /// SHA256SUMS; this record it cannot change, so a restore finds out.
    /// </summary>
    public sealed class Ledger
    {
        readonly string path;
        readonly object gate = new object();

        public Ledger(string filesDir)
        {
            path = Path.Combine(filesDir, "backups.json");
        }

        JsonObject Load()
        {
            try { return File.Exists(path) ? JsonNode.Parse(File.ReadAllText(path)) as JsonObject ?? new JsonObject() : new JsonObject(); }
            catch (Exception e) when (e is IOException || e is System.Text.Json.JsonException) { return new JsonObject(); }
        }

        public void Record(string backup, IReadOnlyDictionary<string, string> sums)
        {
            if (string.IsNullOrEmpty(backup) || sums == null || sums.Count == 0)
                return;
            lock (gate)
            {
                JsonObject all = Load();
                JsonObject files = new JsonObject();
                foreach (KeyValuePair<string, string> s in sums)
                    files[s.Key] = s.Value;
                all[backup] = files;
                string tmp = path + ".tmp";
                File.WriteAllText(tmp, all.ToJsonString());
                File.Move(tmp, path, true);
            }
            Log.Add("ledger: kept " + sums.Count + " SHA-256 for " + backup);
        }

        /// <summary>The SHA-256s kept for a backup, or null when the app did not make it.</summary>
        public IReadOnlyDictionary<string, string> Get(string backup)
        {
            lock (gate)
            {
                if (string.IsNullOrEmpty(backup) || !(Load()[backup] is JsonObject files))
                    return null;
                return files.Where(f => f.Value != null).ToDictionary(f => f.Key, f => f.Value.GetValue<string>(), StringComparer.Ordinal);
            }
        }

        public void Forget(string backup)
        {
            lock (gate)
            {
                JsonObject all = Load();
                if (all.Remove(backup))
                    File.WriteAllText(path, all.ToJsonString());
            }
        }
    }
}
