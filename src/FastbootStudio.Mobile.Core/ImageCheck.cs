using System;
using System.IO;
using System.Linq;
using FastbootEnhance.Core.Adb;
using FastbootEnhance.Core.Fastboot;
using FastbootEnhance.Core.Images;

namespace FastbootStudio.Mobile.Core
{
    /// <summary>
    /// Decides whether an image may be written to a partition: from a backup it must be this
    /// phone's and match its SHA-256 (otherwise it is refused); any other file can only be
    /// checked for what it is, so it needs the partition name typed.
    /// </summary>
    public static class ImageCheck
    {
        /// <summary>
        /// An image from a backup folder. <paramref name="partition"/> is the partition it
        /// would be written to ("init_boot_a"); <paramref name="partitionBytes"/> its size when
        /// known.
        /// </summary>
        public static CheckResult FromBackup(BackupFolder backup, string fileName, string partition, long? partitionBytes, PhoneIdentity phone)
        {
            CheckResult result = new CheckResult();
            string path = Path.Combine(backup.Path, fileName ?? "");
            if (!CheckTarget(result, partition))
                return result;
            CheckPhone(result, backup, phone);

            string from = BackupFolder.PartitionOf(fileName);
            if (from == null || FlashTargets.BaseName(from) != FlashTargets.BaseName(partition))
                result.Add(CheckLevel.Block, CheckCode.OtherPartition, from ?? fileName ?? "", partition);
            else
                result.Add(CheckLevel.Ok, CheckCode.SamePartition, from, partition);

            if (!File.Exists(path))
            {
                result.Add(CheckLevel.Block, CheckCode.EmptyFile, fileName ?? "");
                return result;
            }
            result.Sha256 = Sha256Sums.HashFile(path);
            string expected;
            if (!backup.Sums.TryGetValue(fileName, out expected))
                result.Add(CheckLevel.Block, CheckCode.HashMissing, fileName);
            else if (!string.Equals(expected, result.Sha256, StringComparison.OrdinalIgnoreCase))
                result.Add(CheckLevel.Block, CheckCode.HashMismatch, expected, result.Sha256);
            else
                result.Add(CheckLevel.Ok, CheckCode.HashMatches, result.Sha256);

            CheckContent(result, path, partition, partitionBytes, phone);
            return result;
        }

        /// <summary>
        /// Any other image (a patched boot from Magisk, a dtbo from firmware...). It cannot be
        /// proven to be the right one, so the best it gets is Warn. When the user pasted the
        /// SHA-256 the developer published, a mismatch refuses it and a match is shown.
        /// </summary>
        public static CheckResult FromFile(string path, string partition, long? partitionBytes, PhoneIdentity phone, string expectedSha256 = null)
        {
            CheckResult result = new CheckResult();
            if (!CheckTarget(result, partition))
                return result;
            if (path == null || !File.Exists(path))
            {
                result.Add(CheckLevel.Block, CheckCode.EmptyFile, Path.GetFileName(path ?? ""));
                return result;
            }
            result.Sha256 = Sha256Sums.HashFile(path);
            AddExpected(result, expectedSha256);
            result.Add(CheckLevel.Warn, CheckCode.NoFingerprint, Path.GetFileName(path));
            CheckContent(result, path, partition, partitionBytes, phone);
            return result;
        }

        internal static void AddExpected(CheckResult result, string expectedSha256)
        {
            string expected = (expectedSha256 ?? "").Trim().ToLowerInvariant();
            if (expected.Length == 0)
                return;
            if (expected == result.Sha256)
                result.Add(CheckLevel.Ok, CheckCode.ExpectedHashMatches, result.Sha256);
            else
                result.Add(CheckLevel.Block, CheckCode.ExpectedHashMismatch, expected, result.Sha256);
        }

        static bool CheckTarget(CheckResult result, string partition)
        {
            if (!FlashTargets.IsAllowed(FlashTargets.BaseName(partition)))
            {
                result.Add(CheckLevel.Block, CheckCode.NotAllowed, partition ?? "");
                return false;
            }
            return true;
        }

        /// <summary>The serial decides; without one on either side, the model and product must agree and it is only a Warn.</summary>
        static void CheckPhone(CheckResult result, BackupFolder backup, PhoneIdentity phone)
        {
            string backupPhone = Join(backup.Model, backup.Serial);
            string thisPhone = Join(phone?.Model, phone?.Serial);
            if (!string.IsNullOrEmpty(backup.Serial) && !string.IsNullOrEmpty(phone?.Serial))
            {
                if (backup.Serial == phone.Serial)
                    result.Add(CheckLevel.Ok, CheckCode.SamePhone, thisPhone);
                else
                    result.Add(CheckLevel.Block, CheckCode.OtherPhone, backupPhone, thisPhone);
                return;
            }
            bool modelKnown = !string.IsNullOrEmpty(backup.Model) && !string.IsNullOrEmpty(phone?.Model);
            bool productKnown = !string.IsNullOrEmpty(backup.Product) && !string.IsNullOrEmpty(phone?.Product);
            if ((modelKnown && backup.Model != phone.Model) || (productKnown && backup.Product != phone.Product))
                result.Add(CheckLevel.Block, CheckCode.OtherPhone, backupPhone, thisPhone);
            else
                result.Add(CheckLevel.Warn, CheckCode.PhoneUnknown, backupPhone, thisPhone);
        }

        /// <summary>What the file is, its size, and for boot images the kernel against the phone's.</summary>
        static void CheckContent(CheckResult result, string path, string partition, long? partitionBytes, PhoneIdentity phone)
        {
            long length = new FileInfo(path).Length;
            if (length == 0)
            {
                result.Add(CheckLevel.Block, CheckCode.EmptyFile, Path.GetFileName(path));
                return;
            }
            if (partitionBytes.HasValue && length > partitionBytes.Value)
                result.Add(CheckLevel.Block, CheckCode.TooBig, length.ToString(), partitionBytes.Value.ToString());

            ImageKind kind;
            bool sparse;
            using (FileStream file = File.OpenRead(path))
            {
                sparse = SparseImage.IsSparse(file);
                kind = sparse ? ImageKind.Unknown : ImageProbe.Kind(file);
            }
            string target = FlashTargets.BaseName(partition);
            ImageKind wanted = Expected(target);
            if (sparse || kind != wanted)
            {
                result.Add(CheckLevel.Block, CheckCode.WrongImageKind, target, sparse ? "sparse" : kind.ToString());
                return;
            }
            if (kind != ImageKind.BootImage && kind != ImageKind.VendorBootImage)
                return;

            BootImageAnalysis image = BootImageAnalysis.Analyze(path);
            DeviceFacts facts = phone == null ? null : new DeviceFacts
            {
                Serial = phone.Serial, Kernel = phone.KernelRelease, Patch = phone.PatchLevel, Model = phone.Model, Android = phone.AndroidRelease,
            };
            foreach (Finding finding in BootFlashCheck.Check(image, partition, facts, phone?.HasInitBoot ?? false))
            {
                CheckLevel level = finding.Level == FindingLevel.Block ? CheckLevel.Block
                    : finding.Level == FindingLevel.Warn ? CheckLevel.Warn : CheckLevel.Ok;
                result.Add(level, CheckCode.BootCheck, new[] { finding.Code.ToString() }.Concat(finding.Values).ToArray());
            }
        }

        static ImageKind Expected(string target)
        {
            switch (target)
            {
                case "vendor_boot": return ImageKind.VendorBootImage;
                case "vbmeta":
                case "vbmeta_system": return ImageKind.Vbmeta;
                case "dtbo": return ImageKind.Dtbo;
                default: return ImageKind.BootImage;
            }
        }

        static string Join(string model, string serial)
        {
            if (string.IsNullOrEmpty(model)) return serial ?? "";
            if (string.IsNullOrEmpty(serial)) return model;
            return model + " · " + serial;
        }
    }
}
