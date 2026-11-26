using System;
using System.Collections.Generic;
using System.Linq;

namespace FastbootStudio.Mobile.Core
{
    /// <summary>One image of a backup in a restore plan: where it would go, and whether it goes.</summary>
    public sealed class RestoreItem
    {
        public string File { get; internal set; }

        /// <summary>The partition it is written to: the one it was copied from ("init_boot_a.img" goes to init_boot_a).</summary>
        public string Partition { get; internal set; }

        /// <summary>"include" (written unless unticked), "optional" (written only when ticked) or "skipped" (never).</summary>
        public string State { get; internal set; }

        /// <summary>Why it is optional or skipped: "other-slot", "verified-boot", "not-allowed", "no-partition", "not-an-image", "check-failed".</summary>
        public string Reason { get; internal set; }

        /// <summary>The same check a single flash gets, once the image has been read; null for a skipped file.</summary>
        public CheckResult Check { get; set; }

        public bool Writable => State != "skipped";
    }

    public sealed class RestoreOutcome
    {
        /// <summary>Written and (when read-back is on) verified, in order.</summary>
        public List<string> Written { get; } = new List<string>();

        /// <summary>The partition whose write or read-back failed, if any: it may hold part of the image.</summary>
        public string Failed { get; internal set; }

        /// <summary>Chosen but not reached, because the restore stops at the first failure.</summary>
        public List<string> NotWritten { get; } = new List<string>();

        /// <summary>The backup of what was about to be overwritten, taken first.</summary>
        public string BackupFolder { get; internal set; }

        public string Error { get; internal set; }
        public List<string> Log { get; } = new List<string>();
    }

    /// <summary>
    /// Putting a whole backup back in one go. Stricter than a single flash, because several
    /// partitions change before the person sees a result:
    /// only a backup the app made of this very phone (its own record knows it, the serial is
    /// the phone's, and SHA256SUMS still says what the record says); only the partitions the
    /// app writes at all; every image through the same <see cref="ImageCheck"/> as a single
    /// flash, with nothing a check refuses or wants the name typed for, and no developer mode;
    /// the other slot and the vbmeta partitions only when the person ticks them.
    /// </summary>
    public static class BackupRestore
    {
        /// <summary>
        /// Null when the backup may be restored as a whole, else why not: "not-recorded" (the app
        /// did not make it), "other-phone", "no-serial" (it cannot be proven to be this phone's),
        /// "tampered" (SHA256SUMS no longer says what the app recorded).
        /// </summary>
        public static string Refusal(BackupEntry backup, IReadOnlyDictionary<string, string> recorded, PhoneIdentity phone)
        {
            if (backup == null || recorded == null || recorded.Count == 0)
                return "not-recorded";
            if (string.IsNullOrEmpty(backup.Serial) || string.IsNullOrEmpty(phone?.Serial))
                return "no-serial";
            if (backup.Serial != phone.Serial)
                return "other-phone";
            if (BackupListing.NotAsRecorded(backup.Sums, recorded).Count > 0)
                return "tampered";
            return null;
        }

        /// <summary>
        /// The plan for the files of a backup, in the order they would be written (the order of
        /// <see cref="FlashTargets.Allowed"/>, the active slot first). <paramref name="slot"/> is
        /// the phone's active slot ("_a", "_b" or null).
        /// </summary>
        public static List<RestoreItem> Plan(IEnumerable<string> files, ICollection<string> byName, string slot)
        {
            List<RestoreItem> plan = new List<RestoreItem>();
            foreach (string file in files ?? Enumerable.Empty<string>())
            {
                string partition = BackupFolder.PartitionOf(file);
                RestoreItem item = new RestoreItem { File = file, Partition = partition };
                string baseName = FlashTargets.BaseName(partition);
                string itsSlot = FlashTargets.SlotOf(partition);
                if (partition == null || !Sh.IsPartitionName(partition))
                    Skip(item, "not-an-image");
                else if (!FlashTargets.IsAllowed(baseName))
                    // The bootloader chain, modem, persist...: the app never writes them, one at a time or all together.
                    Skip(item, "not-allowed");
                else if (byName == null || !byName.Contains(partition))
                    Skip(item, "no-partition");
                else if (itsSlot != null && slot != null && itsSlot != slot)
                    // The slot the phone is not running from: left alone unless asked for.
                    Optional(item, "other-slot");
                else if (baseName == "vbmeta" || baseName == "vbmeta_system")
                    // vbmeta decides whether the other images are accepted at all: an older one beside a patched boot does not start.
                    Optional(item, "verified-boot");
                else
                    item.State = "include";
                plan.Add(item);
            }
            int Rank(RestoreItem i)
            {
                int index = FlashTargets.Allowed.ToList().IndexOf(FlashTargets.BaseName(i.Partition ?? ""));
                return (index < 0 ? 1000 : index * 2) + (i.Reason == "other-slot" ? 1 : 0);
            }
            return plan.OrderBy(i => i.State == "skipped" ? 1 : 0).ThenBy(Rank).ThenBy(i => i.File, StringComparer.Ordinal).ToList();
        }

        static void Skip(RestoreItem item, string reason) { item.State = "skipped"; item.Reason = reason; }
        static void Optional(RestoreItem item, string reason) { item.State = "optional"; item.Reason = reason; }

        /// <summary>
        /// Records an image's check in the plan: anything short of Ok or Notice (with developer
        /// mode off, always) takes the image out of the plan.
        /// </summary>
        public static void Checked(RestoreItem item, CheckResult check)
        {
            item.Check = check;
            if (item.State == "skipped")
                return;
            if (check == null || !check.CanFlashFor(false) || check.VerdictFor(false) > CheckLevel.Notice)
                Skip(item, "check-failed");
        }

        /// <summary>
        /// Writes the chosen images one after another and stops at the first failure.
        /// <paramref name="fetch"/> hands over a readable copy of a backup file (null when it
        /// cannot); the flasher checks that copy against the check's SHA-256 again before writing.
        /// </summary>
        public static RestoreOutcome Run(IReadOnlyList<RestoreItem> chosen, Func<string, string> fetch, RootFlasher flasher, PhoneIdentity phone,
            bool backupFirst = true, bool readBack = true, Action<string> progress = null)
        {
            RestoreOutcome outcome = new RestoreOutcome();
            void Step(string line) { outcome.Log.Add(line); progress?.Invoke(line); }
            RestoreOutcome Fail(string error) { outcome.Error = error; Step("error: " + error); return outcome; }

            chosen = chosen ?? new List<RestoreItem>();
            outcome.NotWritten.AddRange(chosen.Select(i => i.Partition));
            if (chosen.Count == 0)
                return Fail("nothing-to-restore");
            // Nothing is written unless every chosen image passed its check: no half of a restore because the third image was bad.
            if (chosen.Any(i => !i.Writable || i.Check == null || !i.Check.CanFlashFor(false) || i.Check.VerdictFor(false) > CheckLevel.Notice
                    || !FlashTargets.IsAllowed(FlashTargets.BaseName(i.Partition))))
                return Fail("not-confirmed");
            if (chosen.Select(i => i.Partition).Distinct().Count() != chosen.Count)
                return Fail("not-confirmed");

            if (backupFirst)
            {
                string folder = flasher.Backup(chosen.Select(i => i.Partition), phone, "before-restore", Step);
                if (folder == null)
                    return Fail(flasher.BackupError ?? "backup-failed");
                outcome.BackupFolder = folder;
            }

            // The person confirmed the whole plan on one sheet: that is the Continue a Notice asks for.
            Consent consent = new Consent { NoticeAccepted = true };
            for (int n = 0; n < chosen.Count; n++)
            {
                RestoreItem item = chosen[n];
                Step("restore " + (n + 1) + "/" + chosen.Count + " " + item.Partition);
                string path = fetch(item.File);
                FlashOutcome one = path == null
                    ? null
                    : flasher.Flash(path, item.Partition, item.Check, consent, phone, backupFirst: false, readBack: readBack, progress: Step);
                if (one == null || one.Error != null)
                {
                    // Only a write that started can have changed the partition.
                    if (one != null && one.Touched)
                        outcome.Failed = item.Partition;
                    if (outcome.Failed != null)
                        outcome.NotWritten.Remove(item.Partition);
                    outcome.Error = one == null ? "backup-unreadable" : one.Error;
                    Step("restore stopped at " + item.Partition);
                    return outcome;
                }
                outcome.Written.Add(item.Partition);
                outcome.NotWritten.Remove(item.Partition);
            }
            Step("restore done " + outcome.Written.Count);
            return outcome;
        }
    }
}
