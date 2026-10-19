using Xunit;

namespace FastbootStudio.Mobile.Core.Tests
{
    public class DevicePropsTests
    {
        const string Getprop = @"[ro.product.model]: [Xiaomi 13T Pro]
[ro.product.brand]: [Xiaomi]
[ro.product.device]: [corot]
[ro.product.name]: [corot_global]
[ro.build.product]: [corot]
[ro.product.vendor.device]: [corot_ext]
[ro.serialno]: [a1b2c3d4]
[ro.build.version.release]: [14]
[ro.build.version.sdk]: [34]
[ro.build.version.security_patch]: [2026-09-05]
[ro.boot.slot_suffix]: [_a]
[ro.boot.dynamic_partitions]: [true]
[ro.board.platform]: [mt6985]
[ro.soc.manufacturer]: [Mediatek]
[ro.boot.verifiedbootstate]: [orange]
[ro.boot.vbmeta.device_state]: [unlocked]
[ro.empty]: []
";

        [Fact]
        public void Getprop_becomes_the_device_page_and_the_identity()
        {
            DeviceProps d = DeviceProps.Parse(Getprop, "6.1.75-android14-11-g1234\n");
            Assert.Equal("Xiaomi 13T Pro", d.Model);
            Assert.Equal("corot", d.Device);
            Assert.Equal("_a", d.Slot);
            Assert.True(d.AB);
            Assert.True(d.Dynamic);
            Assert.Equal("MediaTek", d.Vendor);
            Assert.True(d.Unlocked);
            Assert.Equal("android14-6.1", d.Kmi);
            Assert.Null(d.Get("ro.empty"));

            PhoneIdentity id = d.Identity(true);
            Assert.Equal("a1b2c3d4", id.Serial);
            Assert.Equal("6.1.75-android14-11-g1234", id.KernelRelease);
            Assert.Contains("corot_ext", id.DeviceNames);
            Assert.True(id.HasInitBoot);
        }

        [Fact]
        public void A_locked_or_silent_bootloader_is_told_apart()
        {
            Assert.False(DeviceProps.Parse("[ro.boot.vbmeta.device_state]: [locked]", "").Unlocked);
            Assert.False(DeviceProps.Parse("[ro.boot.verifiedbootstate]: [green]", "").Unlocked);
            Assert.Null(DeviceProps.Parse("", "").Unlocked);
        }

        // An Infinix X6878 with KernelSU and a module that passes Play Integrity.
        const string Spoofed = "[ro.boot.vbmeta.device_state]: [locked]\n[ro.boot.verifiedbootstate]: [green]\n[ro.boot.flash.locked]: [1]\n";

        [Fact]
        public void Working_root_means_unlocked_even_when_a_module_says_locked()
        {
            BootState b = DeviceProps.Parse(Spoofed, "").Boot(true, "");
            Assert.True(b.Unlocked);
            Assert.True(b.Hidden);
            Assert.Equal("orange", b.Avb);
        }

        [Fact]
        public void The_kernel_arguments_give_the_real_state()
        {
            string bootconfig = "androidboot.verifiedbootstate = \"orange\"\nandroidboot.vbmeta.device_state = \"unlocked\"\n";
            BootState noRoot = DeviceProps.Parse(Spoofed, "").Boot(false, bootconfig);
            Assert.True(noRoot.Unlocked);
            Assert.Equal("orange", noRoot.Avb);
            BootState cmdline = DeviceProps.Parse("", "").Boot(false, "console=ttyMSM0 androidboot.vbmeta.device_state=locked androidboot.verifiedbootstate=green");
            Assert.False(cmdline.Unlocked);
            Assert.False(cmdline.Hidden);
        }

        [Fact]
        public void Without_root_or_kernel_arguments_the_properties_decide()
        {
            BootState b = DeviceProps.Parse(Spoofed, "").Boot(false, null);
            Assert.False(b.Unlocked);
            Assert.False(b.Hidden);
            Assert.True(DeviceProps.Parse("[ro.boot.vbmeta.device_state]: [unlocked]", "").Boot(true, "").Unlocked);
            Assert.False(DeviceProps.Parse("[ro.boot.vbmeta.device_state]: [unlocked]", "").Boot(true, "").Hidden);
        }
    }
}
