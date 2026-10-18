using System.Collections.Generic;
using System.Linq;

namespace FastbootStudio.Mobile.Core
{
    public enum CheckLevel
    {
        /// <summary>Checked and fine.</summary>
        Ok,

        /// <summary>Worth knowing (a different kernel version, an older patch level): a plain Continue / Cancel.</summary>
        Notice,

        /// <summary>Cannot be confirmed (a file from outside the backups): flashing needs the partition name typed.</summary>
        Warn,

        /// <summary>Damaged, harmful, for another phone or the wrong partition: never flashed.</summary>
        Block,
    }

    public enum CheckCode
    {
        HashMatches,
        HashMismatch,
        HashMissing,
        ExpectedHashMatches,
        ExpectedHashMismatch,
        NoFingerprint,
        SamePhone,
        OtherPhone,
        PhoneUnknown,
        SamePartition,
        OtherPartition,
        NotAllowed,
        NoSuchPartition,
        WrongImageKind,
        TooBig,
        EmptyFile,
        BootCheck,
        KernelVersionDiffers,
        NotAnyKernel,
        UnsafeZipEntry,
        DeviceListed,
        DeviceNotListed,
        DeviceCheckOff,
        AndroidSupported,
        AndroidNotSupported,
    }

    public sealed class CheckItem
    {
        public CheckItem(CheckLevel level, CheckCode code, params string[] values) : this(level, false, code, values) { }

        public CheckItem(CheckLevel level, bool developerOverride, CheckCode code, params string[] values)
        {
            Level = level;
            DeveloperOverride = developerOverride && level == CheckLevel.Block;
            Code = code;
            Values = values ?? new string[0];
        }

        public CheckLevel Level { get; }

        /// <summary>
        /// A Block that developer mode turns into a Warn: the file is not damaged or harmful,
        /// it was only made for another device or Android version. Damaged, tampered or harmful
        /// files are never overridable.
        /// </summary>
        public bool DeveloperOverride { get; }

        /// <summary>The level once developer mode is taken into account.</summary>
        public CheckLevel Effective(bool developerMode) => developerMode && DeveloperOverride ? CheckLevel.Warn : Level;
        public CheckCode Code { get; }

        /// <summary>The parts the message is built from, in the order of its {0}, {1}, ...</summary>
        public IReadOnlyList<string> Values { get; }

        public override string ToString() => Level + " " + Code + (Values.Count > 0 ? " " + string.Join(" | ", Values) : "");
    }

    /// <summary>All the checks on one file, and what the app does with it.</summary>
    public sealed class CheckResult
    {
        public List<CheckItem> Items { get; } = new List<CheckItem>();

        /// <summary>The worst level among the items: Block over Warn over Notice over Ok. No items is a Block.</summary>
        public CheckLevel Verdict => VerdictFor(false);

        /// <summary>The verdict with developer mode on or off.</summary>
        public CheckLevel VerdictFor(bool developerMode) => Items.Count == 0 ? CheckLevel.Block : Items.Max(i => i.Effective(developerMode));

        /// <summary>True only when nothing blocks. A Notice asks Continue / Cancel; a Warn needs the partition name typed.</summary>
        public bool CanFlash => CanFlashFor(false);

        public bool CanFlashFor(bool developerMode) => Items.Count > 0 && VerdictFor(developerMode) != CheckLevel.Block;

        /// <summary>The SHA-256 of the file as read for these checks, the one the flash is checked against.</summary>
        public string Sha256 { get; internal set; }

        internal void Add(CheckLevel level, CheckCode code, params string[] values) => Items.Add(new CheckItem(level, code, values));

        /// <summary>A Block that developer mode may turn into a Warn.</summary>
        internal void AddOverridable(CheckCode code, params string[] values) => Items.Add(new CheckItem(CheckLevel.Block, true, code, values));

        public bool Has(CheckCode code) => Items.Any(i => i.Code == code);
    }
}
