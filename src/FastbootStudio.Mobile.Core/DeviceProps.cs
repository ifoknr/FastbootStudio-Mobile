using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using FastbootEnhance.Core.Images;

namespace FastbootStudio.Mobile.Core
{
    /// <summary>The phone as getprop and the kernel describe it: what the Device page shows.</summary>
    public sealed class DeviceProps
    {
        static readonly Regex Line = new Regex(@"^\[([^\]]+)\]: \[(.*)\]$");

        public Dictionary<string, string> Props { get; } = new Dictionary<string, string>(StringComparer.Ordinal);
        public string KernelRelease { get; set; }

        /// <summary>Reads "getprop" output: "[key]: [value]" per line.</summary>
        public static DeviceProps Parse(string getprop, string kernelRelease)
        {
            DeviceProps d = new DeviceProps { KernelRelease = (kernelRelease ?? "").Trim() };
            foreach (string raw in (getprop ?? "").Replace("\r", "").Split('\n'))
            {
                Match m = Line.Match(raw.Trim());
                if (m.Success)
                    d.Props[m.Groups[1].Value] = m.Groups[2].Value;
            }
            return d;
        }

        public string Get(string key) => Props.TryGetValue(key, out string v) && v.Length > 0 ? v : null;

        string First(params string[] keys)
        {
            foreach (string k in keys)
            {
                string v = Get(k);
                if (v != null) return v;
            }
            return null;
        }

        public string Model => First("ro.product.model", "ro.product.vendor.model");
        public string Brand => First("ro.product.brand", "ro.product.vendor.brand");
        public string Device => First("ro.product.device", "ro.product.vendor.device");
        public string Product => First("ro.product.name", "ro.product.vendor.name");
        public string Serial => First("ro.serialno", "ro.boot.serialno");
        public string Android => Get("ro.build.version.release");
        public string Sdk => Get("ro.build.version.sdk");
        public string PatchLevel => Get("ro.build.version.security_patch");
        public string Slot => First("ro.boot.slot_suffix");
        public bool AB => Slot == "_a" || Slot == "_b";
        public bool Dynamic => Get("ro.boot.dynamic_partitions") == "true";
        public string Platform => First("ro.board.platform", "ro.hardware.chipname", "ro.hardware");
        public string Abi => Get("ro.product.cpu.abi");

        /// <summary>Qualcomm, MediaTek, Unisoc, Exynos, Tensor, or null.</summary>
        public string Vendor
        {
            get
            {
                string m = (First("ro.soc.manufacturer") ?? "").ToLowerInvariant();
                string p = (Platform ?? "").ToLowerInvariant();
                if (m.Contains("qti") || m.Contains("qualcomm")) return "Qualcomm";
                if (m.Contains("mediatek") || p.StartsWith("mt")) return "MediaTek";
                if (m.Contains("unisoc") || m.Contains("spreadtrum") || p.StartsWith("ums") || p.StartsWith("sp9")) return "Unisoc";
                if (m.Contains("samsung") || p.StartsWith("exynos") || p.StartsWith("s5e")) return "Exynos";
                if (m.Contains("google") || p.StartsWith("gs") || p.StartsWith("zuma")) return "Tensor";
                if (Get("ro.hardware") == "qcom" || p.StartsWith("sm") || p.StartsWith("msm") || p.StartsWith("sdm")) return "Qualcomm";
                return null;
            }
        }

        /// <summary>orange (unlocked), green (locked), yellow, red, or null.</summary>
        public string VerifiedBootState => Get("ro.boot.verifiedbootstate");

        /// <summary>True when the bootloader is unlocked, false when locked, null when the phone does not say.</summary>
        public bool? Unlocked
        {
            get
            {
                string state = Get("ro.boot.vbmeta.device_state");
                if (state == "unlocked") return true;
                if (state == "locked") return false;
                string flash = Get("ro.boot.flash.locked");
                if (flash == "0") return true;
                if (flash == "1") return false;
                string vb = VerifiedBootState;
                if (vb == "orange") return true;
                if (vb == "green" || vb == "yellow") return false;
                return null;
            }
        }

        public string Kmi => KernelInfo.FromRelease(KernelRelease)?.Kmi;

        /// <summary>The identity the checks compare files against.</summary>
        public PhoneIdentity Identity(bool hasInitBoot)
        {
            PhoneIdentity id = new PhoneIdentity
            {
                Serial = Serial, Model = Model, Device = Device, Product = Product, AndroidRelease = Android,
                Kmi = Kmi, KernelRelease = KernelRelease, PatchLevel = PatchLevel, HasInitBoot = hasInitBoot,
            };
            // The codenames AnyKernel3 compares as well; custom ROMs change some of them.
            foreach (string key in new[] { "ro.build.product", "ro.product.vendor.device", "ro.product.system.device", "ro.product.odm.device", "ro.vendor.product.device" })
            {
                string v = Get(key);
                if (v != null && !id.OtherDeviceNames.Contains(v))
                    id.OtherDeviceNames.Add(v);
            }
            return id;
        }
    }
}
