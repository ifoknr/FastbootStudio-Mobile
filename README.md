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

Developer mode (Settings) turns only "made for another device, Android version or kernel
interface" into a Warn. Damaged, tampered or harmful files stay blocked.

Built on the Fastboot Studio Core library (`external/FastbootStudio`, a submodule), which
is based on Fastboot Enhance by LibXZR. MIT License.
