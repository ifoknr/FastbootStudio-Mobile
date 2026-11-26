using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace FastbootStudio.Mobile.Core
{
    /// <summary>What the app knows right before the first write of a session.</summary>
    public sealed class PreflightFacts
    {
        /// <summary>"image", "kernel", "restore" (this phone, as root), or "otg", "sideload" (another phone on the cable).</summary>
        public string Kind { get; set; }

        /// <summary>This phone's battery, 0-100, or -1 when unknown.</summary>
        public int Battery { get; set; } = -1;
        public int MinBattery { get; set; }
        public bool Charging { get; set; }

        public bool Root { get; set; }

        /// <summary>This phone's bootloader: true unlocked, false locked, null unknown.</summary>
        public bool? Unlocked { get; set; }

        /// <summary>The setting "back up before every flash".</summary>
        public bool BackupFirst { get; set; }

        /// <summary>The app's own record has a backup from today holding the target partition.</summary>
        public bool BackupToday { get; set; }

        /// <summary>What the automatic backup takes and what is free where backups go; null when unknown.</summary>
        public long? NeedBytes { get; set; }
        public long? FreeBytes { get; set; }

        /// <summary>The phone on the cable: fastboot's "unlocked" ("yes", "no" or null) and its serial.</summary>
        public string OtgUnlocked { get; set; }
        public string OtgSerial { get; set; }

        public bool OtherPhone => Kind == "otg" || Kind == "sideload";
    }

    public sealed class PreflightItem
    {
        public PreflightItem(string code, string level, params string[] values) { Code = code; Level = level; Values = values; }

        /// <summary>"battery", "root", "bootloader", "backup", "space", "otg-bootloader", "otg-serial".</summary>
        public string Code { get; }

        /// <summary>"pass", "warn" (needs a tick) or "fail" (blocks).</summary>
        public string Level { get; }

        /// <summary>What the page puts in its sentence: a detail code first, then numbers.</summary>
        public string[] Values { get; }
    }

    /// <summary>
    /// The rule for one run of the app: the list is shown before the first write, and once it
    /// has passed (nothing failed, the warnings ticked) it is not shown again until the app
    /// starts anew. A failure always brings it back, and takes the pass away until it is
    /// read again. No write happens before a pass.
    /// </summary>
    public sealed class PreflightSession
    {
        readonly object gate = new object();
        bool passed;

        /// <summary>A write may go ahead: the list passed in this run of the app, and nothing has failed since.</summary>
        public bool MayWrite { get { lock (gate) return passed; } }

        /// <summary>Whether the list has to be shown now, for what it says at this moment.</summary>
        public bool Needed(IEnumerable<PreflightItem> items)
        {
            lock (gate)
            {
                if (Preflight.Blocked(items))
                    passed = false;
                return !passed;
            }
        }

        /// <summary>
        /// The person read the list and goes on. False (and no pass) when something on it fails,
        /// or when it has warnings and they were not ticked.
        /// </summary>
        public bool Acknowledge(IEnumerable<PreflightItem> items, bool warningsAccepted)
        {
            lock (gate)
            {
                List<PreflightItem> list = items.ToList();
                passed = !Preflight.Blocked(list) && (Preflight.AllPass(list) || warningsAccepted);
                return passed;
            }
        }
    }

    /// <summary>
    /// The list shown before the first write of a session. It adds to the checks every flash
    /// already gets and replaces none of them: a pass here writes nothing by itself.
    /// </summary>
    public static class Preflight
    {
        public static List<PreflightItem> Evaluate(PreflightFacts f)
        {
            List<PreflightItem> items = new List<PreflightItem>();
            string level = f.Battery.ToString(CultureInfo.InvariantCulture), min = f.MinBattery.ToString(CultureInfo.InvariantCulture);
            string power = f.Charging ? "charging" : "on-battery";
            if (f.Battery < 0)
                items.Add(new PreflightItem("battery", "warn", "unknown", "", min, power));
            else if (f.Battery < f.MinBattery)
                items.Add(new PreflightItem("battery", "fail", "low", level, min, power));
            else
                items.Add(new PreflightItem("battery", "pass", "ok", level, min, power));

            if (f.OtherPhone)
            {
                // The serial is what every OTG write is tied to.
                items.Add(string.IsNullOrEmpty(f.OtgSerial)
                    ? new PreflightItem("otg-serial", f.Kind == "sideload" ? "warn" : "fail", "none")
                    : new PreflightItem("otg-serial", "pass", "ok", f.OtgSerial));
                if (f.Kind == "otg")
                    items.Add(f.OtgUnlocked == "yes" ? new PreflightItem("otg-bootloader", "pass", "unlocked")
                        : new PreflightItem("otg-bootloader", "warn", f.OtgUnlocked == "no" ? "locked" : "unknown"));
                return items;
            }

            items.Add(f.Root ? new PreflightItem("root", "pass", "ok") : new PreflightItem("root", "fail", "none"));
            items.Add(f.Unlocked == true ? new PreflightItem("bootloader", "pass", "unlocked")
                : new PreflightItem("bootloader", "warn", f.Unlocked == false ? "locked" : "unknown"));

            // A backup from today, or one about to be taken by itself; with neither, the person says so with a tick.
            if (f.BackupToday)
                items.Add(new PreflightItem("backup", "pass", "today"));
            else if (f.BackupFirst)
                items.Add(new PreflightItem("backup", "pass", "automatic"));
            else
                items.Add(new PreflightItem("backup", "warn", "none"));

            if (f.BackupFirst)
            {
                // The automatic backup is not optional once it is on: without room for it the write is refused anyway.
                if (f.NeedBytes == null || f.FreeBytes == null)
                    items.Add(new PreflightItem("space", "warn", "unknown"));
                else
                {
                    string need = f.NeedBytes.Value.ToString(CultureInfo.InvariantCulture), free = f.FreeBytes.Value.ToString(CultureInfo.InvariantCulture);
                    items.Add(new PreflightItem("space", f.NeedBytes.Value + RootFlasher.BackupMargin > f.FreeBytes.Value ? "fail" : "pass",
                        f.NeedBytes.Value + RootFlasher.BackupMargin > f.FreeBytes.Value ? "short" : "ok", need, free));
                }
            }
            return items;
        }

        public static bool Blocked(IEnumerable<PreflightItem> items) => items.Any(i => i.Level == "fail");

        public static bool AllPass(IEnumerable<PreflightItem> items) => items.All(i => i.Level == "pass");

        /// <summary>True when a backup's folder name carries this date ("..._20261126-101544").</summary>
        public static bool IsFrom(string backupName, System.DateTime day) =>
            (backupName ?? "").Contains("_" + day.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + "-");
    }
}
