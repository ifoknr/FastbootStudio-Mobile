# Fastboot Studio Mobile

Fastboot Studio for Android, by IFOKNR. **Not released yet: in testing.**

- Flash boot, init_boot, vendor_boot, dtbo, vbmeta, vbmeta_system and recovery on the
  phone itself (root), and AnyKernel3 zips.
- Restore Fastboot Studio backups (the same layout as the Windows app and the Companion).
- OTG rescue: flash another phone over a USB cable, by its serial only.

Every file is checked before it is written:

| Level | When | What happens |
| --- | --- | --- |
| Ok | This phone's backup, SHA-256 matches | Flash |
| Notice | Another kernel version, an older patch level, no published hash pasted | Continue or Cancel |
| Warn | A file from outside the backups, a zip with device check off | Type the partition name |
| Block | Damaged, tampered or harmful, another phone's backup, wrong image or partition | Never flashed |

An AnyKernel3 zip's installer runs as root, so every script in it is read first. A zip
whose scripts write a partition no kernel needs (modem, persist, the bootloader chain,
userdata...), format or wipe storage, delete the data or the shared storage, use the
network, or run a file that cannot be read, is a Block. This catches what a harmful zip
plainly does; it cannot prove a zip is safe, which is why the backup is taken first and the
developer's SHA-256 can be pasted.

Developer mode (Settings) turns only "made for another device, Android version or kernel
interface" into a Warn. Damaged, tampered or harmful files stay blocked.

Built on the Fastboot Studio Core library (`external/FastbootStudio`, a submodule), which
is based on Fastboot Enhance by LibXZR. MIT License.

## Building

- Core and tests (any OS): `dotnet test tests/FastbootStudio.Mobile.Core.Tests`
- The APK: `dotnet workload install android`, then
  `dotnet build src/FastbootStudio.Mobile -c Release -p:AndroidSdkDirectory=... -p:JavaSdkDirectory=...`.
  CI builds a test APK on every push (the `FastbootStudio-Mobile-test-apk` artifact).
- The pages alone: open `src/FastbootStudio.Mobile/ui/index.html` in a browser; `demo.js`
  stands in for the phone (`?lang=en&theme=light&root=0&dev=1&otg=1`). It is not packed
  into the APK.

