using System.Collections.Generic;
using System.Linq;

namespace FastbootStudio.Mobile.Core
{
    public enum CheckLevel
    {
        /// <summary>Checked and fine.</summary>
        Ok,

        /// <summary>Cannot be confirmed or looks unusual: flashing needs the partition name typed.</summary>
        Warn,

        /// <summary>Damaged, for another phone or the wrong partition: never flashed.</summary>
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
        public CheckItem(CheckLevel level, CheckCode code, params string[] values)
        {
            Level = level;
            Code = code;
            Values = values ?? new string[0];
        }

        public CheckLevel Level { get; }
        public CheckCode Code { get; }

        /// <summary>The parts the message is built from, in the order of its {0}, {1}, ...</summary>
        public IReadOnlyList<string> Values { get; }

        public override string ToString() => Level + " " + Code + (Values.Count > 0 ? " " + string.Join(" | ", Values) : "");
    }

    /// <summary>All the checks on one file, and what the app does with it.</summary>
    public sealed class CheckResult
    {
        public List<CheckItem> Items { get; } = new List<CheckItem>();

        /// <summary>The worst level among the items: Block wins over Warn over Ok.</summary>
        public CheckLevel Verdict => Items.Count == 0 ? CheckLevel.Block : Items.Max(i => i.Level);

        /// <summary>True only when nothing blocks. A Warn still needs the partition name typed.</summary>
        public bool CanFlash => Items.Count > 0 && Verdict != CheckLevel.Block;

        /// <summary>The SHA-256 of the file as read for these checks, the one the flash is checked against.</summary>
        public string Sha256 { get; internal set; }

        internal void Add(CheckLevel level, CheckCode code, params string[] values) => Items.Add(new CheckItem(level, code, values));

        public bool Has(CheckCode code) => Items.Any(i => i.Code == code);
    }
}
