namespace FastbootStudio.Mobile.Core
{
    /// <summary>What the app knows about the phone it is checking a file against.</summary>
    public sealed class PhoneIdentity
    {
        /// <summary>ro.serialno (adb), or the fastboot serial over OTG.</summary>
        public string Serial { get; set; }

        /// <summary>ro.product.model, e.g. "Xiaomi 13T Pro".</summary>
        public string Model { get; set; }

        /// <summary>ro.product.device, the codename AnyKernel3 checks, e.g. "corot".</summary>
        public string Device { get; set; }

        /// <summary>ro.product.name, e.g. "corot_global".</summary>
        public string Product { get; set; }

        /// <summary>ro.build.version.release, e.g. "14".</summary>
        public string AndroidRelease { get; set; }

        /// <summary>The running kernel's KMI, e.g. "android13-5.15", when known.</summary>
        public string Kmi { get; set; }

        /// <summary>The running kernel release, e.g. "5.15.123-android13-8-...".</summary>
        public string KernelRelease { get; set; }

        /// <summary>ro.build.version.security_patch, e.g. "2026-09-05".</summary>
        public string PatchLevel { get; set; }

        /// <summary>True when the phone has an init_boot partition.</summary>
        public bool HasInitBoot { get; set; }
    }
}
