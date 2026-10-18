using System;
using System.Collections.Generic;
using System.Linq;

namespace FastbootStudio.Mobile.Core
{
    /// <summary>
    /// The only partitions the app writes. Anything else (system, super, userdata, modem,
    /// persist, the bootloader chain...) is refused here, not just hidden in the UI.
    /// </summary>
    public static class FlashTargets
    {
        public static readonly IReadOnlyList<string> Allowed = new[]
        {
            "boot", "init_boot", "vendor_boot", "dtbo", "vbmeta", "vbmeta_system", "recovery",
        };

        /// <summary>True for an allowed base name ("init_boot", not "init_boot_a").</summary>
        public static bool IsAllowed(string baseName)
        {
            return baseName != null && Allowed.Contains(baseName, StringComparer.Ordinal);
        }

        /// <summary>"init_boot_a" is "init_boot"; a name without a slot is returned as it is.</summary>
        public static string BaseName(string partition)
        {
            if (string.IsNullOrEmpty(partition))
                return partition;
            return partition.EndsWith("_a", StringComparison.Ordinal) || partition.EndsWith("_b", StringComparison.Ordinal)
                ? partition.Substring(0, partition.Length - 2)
                : partition;
        }

        /// <summary>"_a" or "_b", or null for a name without a slot.</summary>
        public static string SlotOf(string partition)
        {
            if (string.IsNullOrEmpty(partition))
                return null;
            if (partition.EndsWith("_a", StringComparison.Ordinal))
                return "_a";
            if (partition.EndsWith("_b", StringComparison.Ordinal))
                return "_b";
            return null;
        }

        /// <summary>
        /// The real partition to write for an allowed base name on a slot ("_a", "_b" or null
        /// on phones without slots), among the names the phone has under by-name. Returns null
        /// when the base is not allowed, the slot is not "_a"/"_b"/null, or the phone has no
        /// such partition. A slotted phone that also has an unslotted one (some recovery
        /// partitions) gets the slotted one only when it exists.
        /// </summary>
        public static string Resolve(string baseName, string slot, ICollection<string> byName)
        {
            if (!IsAllowed(baseName) || byName == null)
                return null;
            if (slot != null && slot != "_a" && slot != "_b")
                return null;
            if (slot != null && byName.Contains(baseName + slot))
                return baseName + slot;
            if (byName.Contains(baseName) && !byName.Contains(baseName + "_a") && !byName.Contains(baseName + "_b"))
                return baseName;
            return null;
        }
    }
}
