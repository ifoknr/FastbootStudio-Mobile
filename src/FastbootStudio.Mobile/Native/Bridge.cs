using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Android.Webkit;
using Java.Interop;
using FastbootEnhance.Core.Adb;
using FastbootStudio.Mobile.Core;

namespace FastbootStudio.Mobile.Native
{
    /// <summary>An error the page shows by its code ("no-root", "busy"...).</summary>
    public sealed class BridgeError : Exception
    {
        public BridgeError(string code) : base(code) { }
    }

    /// <summary>
    /// The page's only way to the phone: window.fbsNative.call(id, method, json). Every answer
    /// comes back through window.fbsReply(id, {ok, data | error}); steps come through
    /// window.fbsEvent(name, data).
    ///
    /// The page never names a path or decides a check. Files it picked are known by a token,
    /// a check is kept here and only its id goes to the page, and a flash re-checks the
    /// consent against that kept result with developer mode read from the app's own settings.
    /// </summary>
    public sealed class Bridge : Java.Lang.Object
    {
        readonly MainActivity activity;
        readonly Phone phone;
        readonly Settings settings;
        readonly Otg otg;
        readonly Companion companion = new Companion(SuShell.Instance);
        readonly SemaphoreSlim busy = new SemaphoreSlim(1, 1);
        readonly Dictionary<string, Picked> picked = new Dictionary<string, Picked>(StringComparer.Ordinal);
        Pending pending;

        /// <summary>The file a running flash reads: never cleaned up under it.</summary>
        volatile string inUse;

        public Bridge(MainActivity activity, Phone phone, Settings settings, Otg otg)
        {
            this.activity = activity;
            this.phone = phone;
            this.settings = settings;
            this.otg = otg;
        }

        sealed class Picked
        {
            public string Path, Name, Folder;
            public long Size;

            /// <summary>For a folder picked over the storage picker: its files, by name.</summary>
            public Dictionary<string, Android.Net.Uri> Files;
        }

        /// <summary>The last check, the only one a flash may use.</summary>
        sealed class Pending
        {
            public string Id = Guid.NewGuid().ToString("N"), Kind, Path, Partition;
            public CheckResult Check;
            public AnyKernelZip Zip;
        }

        [JavascriptInterface]
        [Export("call")]
        public void Call(string id, string method, string args)
        {
            Task.Run(async () =>
            {
                JsonObject reply;
                try
                {
                    JsonObject a = string.IsNullOrEmpty(args) ? new JsonObject() : JsonNode.Parse(args) as JsonObject ?? new JsonObject();
                    reply = new JsonObject { ["ok"] = true, ["data"] = await Dispatch(method ?? "", a) };
                }
                catch (BridgeError e)
                {
                    reply = new JsonObject { ["ok"] = false, ["error"] = e.Message };
                }
                catch (Exception e)
                {
                    Log.Add(method + " failed: " + e);
                    reply = new JsonObject { ["ok"] = false, ["error"] = "internal: " + e.Message };
                }
                activity.Js("window.fbsReply(" + JsonValue.Create(id ?? "").ToJsonString() + "," + reply.ToJsonString() + ")");
            });
        }

        public void Event(string name, JsonNode data) =>
            activity.Js("window.fbsEvent && window.fbsEvent(" + JsonValue.Create(name).ToJsonString() + "," + (data?.ToJsonString() ?? "null") + ")");

        async Task<JsonNode> Dispatch(string method, JsonObject a)
        {
            switch (method)
            {
                case "init":
                    return new JsonObject
                    {
                        ["settings"] = settings.ToJson(), ["version"] = activity.Version, ["systemDark"] = activity.SystemDark,
                        ["backupRoot"] = RootFlasher.BackupRoot,
                    };
                case "settings.set":
                    settings.Set(Json.Str(a, "key"), a["value"]?.DeepClone());
                    return settings.ToJson();
                case "ui.bars":
                    activity.Bars(Json.Bool(a, "dark"));
                    return null;
                case "device.refresh":
                    return phone.Refresh();
                case "pick":
                    return await Pick();
                case "pickFolder":
                    return await PickFolder();
                case "flash.check":
                    return FlashCheck(a);
                case "flash.run":
                    return Exclusive(() => FlashRun(a));
                case "kernel.check":
                    return KernelCheck(a);
                case "kernel.run":
                    return Exclusive(() => KernelRun(a));
                case "companion.find":
                    // Only with root: without it the module is of no use, so it is never mentioned.
                    return phone.Root ? companion.Find() : new JsonObject { ["installed"] = false };
                case "companion.backup":
                    return Exclusive(() => CompanionBackup(a));
                case "companion.bundle":
                    RequireRoot();
                    return new JsonObject { ["file"] = companion.Bundle() };
                case "backup.list":
                    return BackupList();
                case "backup.create":
                    return Exclusive(() => BackupCreate(a));
                case "backup.verify":
                    return BackupVerify(a);
                case "backup.delete":
                    return Exclusive(() => BackupDelete(a));
                case "reboot":
                    return Reboot(a);
                case "logs":
                    return Json.Strings(Log.All());
                case "copy":
                    activity.Copy(Json.Str(a, "text") ?? "");
                    return null;
                case "open":
                    activity.Open(Json.Str(a, "url"));
                    return null;
                case "otg.scan":
                    return otg.Scan();
                case "otg.connect":
                    return (await otg.Connect(Json.Str(a, "id"))).ToJson();
                case "otg.check":
                    return OtgCheck(a);
                case "otg.flash":
                    return Exclusive(() => OtgFlash(a));
                case "otg.reboot":
                    string target = Json.Str(a, "target");
                    if (target != null && target != "bootloader" && target != "recovery" && target != "fastboot")
                        throw new BridgeError("bad-target");
                    otg.Reboot(target);
                    return null;
                case "otg.close":
                    otg.Close();
                    return null;
                default:
                    throw new BridgeError("unknown-method");
            }
        }

        /// <summary>One write at a time, with the screen kept on while it runs.</summary>
        JsonNode Exclusive(Func<JsonNode> work)
        {
            if (!busy.Wait(0))
                throw new BridgeError("busy");
            activity.KeepAwake(true);
            try
            {
                return work();
            }
            finally
            {
                inUse = null;
                activity.KeepAwake(false);
                busy.Release();
            }
        }

        void RequireRoot()
        {
            if (!phone.Root || phone.Props == null)
                throw new BridgeError("no-root");
        }

        void RequireBattery()
        {
            int level = activity.Battery;
            if (level >= 0 && level < settings.MinBattery)
                throw new BridgeError("low-battery");
        }

        static Consent ConsentFrom(JsonObject a, Settings settings) => new Consent
        {
            NoticeAccepted = Json.Bool(a, "notice"), TypedName = Json.Str(a, "typed"), DeveloperMode = settings.DeveloperMode,
        };

        Pending Take(JsonObject a, string kind)
        {
            lock (picked)
            {
                Pending p = pending;
                if (p == null || p.Kind != kind || p.Id != Json.Str(a, "checkId"))
                    throw new BridgeError("check-expired");
                pending = null;
                return p;
            }
        }

        JsonObject Keep(Pending p, string typed)
        {
            lock (picked)
                pending = p;
            Log.Add("check " + Path.GetFileName(p.Path) + " -> " + p.Partition + ": " + p.Check.VerdictFor(settings.DeveloperMode));
            foreach (CheckItem i in p.Check.Items)
                Log.Add("  " + i);
            return new JsonObject
            {
                ["checkId"] = p.Id, ["partition"] = p.Partition, ["typed"] = typed,
                ["check"] = Json.Check(p.Check, settings.DeveloperMode),
            };
        }

        // ---------------------------------------------------------------- files

        static string SafeName(string name)
        {
            string s = new string((name ?? "").Select(c => char.IsLetterOrDigit(c) || c == '.' || c == '-' || c == '_' ? c : '_').ToArray()).TrimStart('.');
            if (s.Length > 120)
                s = s.Substring(s.Length - 120);
            return s.Length == 0 ? "file" : s;
        }

        /// <summary>Copies a file the person picked into the app's cache; the checks and the flash use that copy.</summary>
        async Task<JsonNode> Pick()
        {
            Android.Net.Uri uri = await activity.PickDocument();
            if (uri == null)
                return null;
            string name = activity.DisplayName(uri) ?? "file";
            string token = NewPickFolder(out string folder);
            string path = Path.Combine(folder, SafeName(name));
            if (!activity.CopyTo(uri, path))
                throw new BridgeError("read-failed");
            long size = new FileInfo(path).Length;
            lock (picked)
                picked[token] = new Picked { Path = path, Name = name, Size = size };
            Log.Add("picked " + name + " " + size + " bytes");
            return new JsonObject { ["token"] = token, ["name"] = name, ["size"] = size, ["zip"] = IsZip(path) };
        }

        /// <summary>A backup folder picked over the storage picker (no root needed): its SHA256SUMS and backup-info.txt are read now.</summary>
        async Task<JsonNode> PickFolder()
        {
            Android.Net.Uri tree = await activity.PickTree();
            if (tree == null)
                return null;
            Dictionary<string, Android.Net.Uri> files = activity.ListTree(tree, out string folderName);
            string token = NewPickFolder(out string folder);
            string local = Path.Combine(folder, BackupListing.IsName(folderName) ? folderName : "backup");
            Directory.CreateDirectory(local);
            foreach (string meta in new[] { "SHA256SUMS", BackupFolder.InfoFile })
                if (files.TryGetValue(meta, out Android.Net.Uri u) && !activity.CopyTo(u, Path.Combine(local, meta)))
                    throw new BridgeError("read-failed");
            BackupFolder backup = BackupFolder.Read(local);
            lock (picked)
                picked[token] = new Picked { Folder = local, Name = folderName, Files = files };
            JsonArray images = new JsonArray();
            foreach (string f in files.Keys.Where(f => f.EndsWith(".img", StringComparison.OrdinalIgnoreCase)).OrderBy(f => f))
                images.Add(new JsonObject
                {
                    ["name"] = f, ["partition"] = BackupFolder.PartitionOf(f), ["listed"] = backup.Sums.ContainsKey(f),
                });
            Log.Add("picked folder " + folderName + " (" + images.Count + " images)");
            return new JsonObject
            {
                ["token"] = token, ["name"] = folderName, ["serial"] = backup.Serial, ["model"] = backup.Model,
                ["date"] = backup.Date, ["hasSums"] = backup.HasSums, ["files"] = images,
            };
        }

        string NewPickFolder(out string folder)
        {
            string root = Path.Combine(phone.CacheDir, "pick");
            lock (picked)
            // Only the files the page can still use stay: the last few picks and the pending check's.
            if (Directory.Exists(root))
                foreach (string old in Directory.GetDirectories(root).OrderByDescending(Directory.GetCreationTimeUtc).Skip(3))
                {
                    string t = Path.GetFileName(old);
                    if ((pending?.Path != null && pending.Path.StartsWith(old + "/", StringComparison.Ordinal))
                        || (inUse != null && inUse.StartsWith(old + "/", StringComparison.Ordinal)))
                        continue;
                    try { Directory.Delete(old, true); } catch (IOException) { }
                    picked.Remove(t);
                }
            string token = Guid.NewGuid().ToString("N");
            folder = Path.Combine(root, token);
            Directory.CreateDirectory(folder);
            return token;
        }

        Picked PickedFile(JsonObject a)
        {
            string token = Json.Str(a, "token");
            Picked p = null;
            lock (picked)
                if (token == null || !picked.TryGetValue(token, out p))
                    p = null;
            if (p == null || p.Path == null || !File.Exists(p.Path))
                throw new BridgeError("no-file");
            return p;
        }

        static bool IsZip(string path)
        {
            byte[] head = new byte[4];
            using (FileStream f = File.OpenRead(path))
                return f.Read(head, 0, 4) == 4 && head[0] == 'P' && head[1] == 'K' && head[2] == 3 && head[3] == 4;
        }

        // ---------------------------------------------------------------- this phone

        JsonNode FlashCheck(JsonObject a)
        {
            RequireRoot();
            string baseName = Json.Str(a, "base");
            if (!FlashTargets.IsAllowed(baseName))
                throw new BridgeError("not-allowed");
            string partition = phone.Target(baseName, Json.Bool(a, "other"));
            if (partition == null)
                throw new BridgeError("no-partition");
            long? bytes = phone.Flasher.PartitionBytes(partition);
            PhoneIdentity id = phone.Identity;
            Pending p = new Pending { Kind = "image", Partition = partition };
            if (Json.Str(a, "source") == "backup")
            {
                string name = Json.Str(a, "name"), file = Json.Str(a, "file");
                BackupFolder folder = phone.Fetch(name, file) ?? throw new BridgeError("backup-unreadable");
                p.Path = Path.Combine(folder.Path, file);
                p.Check = ImageCheck.FromBackup(folder, file, partition, bytes, id);
            }
            else
            {
                Picked f = PickedFile(a);
                p.Path = f.Path;
                p.Check = ImageCheck.FromFile(f.Path, partition, bytes, id, Json.Str(a, "expected"));
            }
            return Keep(p, baseName);
        }

        JsonNode FlashRun(JsonObject a)
        {
            RequireRoot();
            Pending p = Take(a, "image");
            RequireBattery();
            inUse = p.Path;
            FlashOutcome o = phone.Flasher.Flash(p.Path, p.Partition, p.Check, ConsentFrom(a, settings), phone.Identity,
                settings.BackupFirst, settings.ReadBack, line => Event("progress", new JsonObject { ["line"] = line }));
            return new JsonObject
            {
                ["partition"] = p.Partition, ["written"] = o.Written, ["verified"] = o.Verified, ["error"] = o.Error,
                ["backup"] = o.BackupFolder == null ? null : Path.GetFileName(o.BackupFolder), ["sha256"] = p.Check.Sha256,
            };
        }

        JsonNode KernelCheck(JsonObject a)
        {
            RequireRoot();
            Picked f = PickedFile(a);
            AnyKernelZip zip = AnyKernelZip.Inspect(f.Path, phone.Identity, Json.Str(a, "expected"));
            string typed = AnyKernelInstaller.TypedNameFor(zip);
            JsonObject r = Keep(new Pending { Kind = "kernel", Path = f.Path, Partition = typed, Check = zip.Result, Zip = zip }, typed);
            r["kernel"] = zip.KernelString;
            r["devices"] = Json.Strings(zip.Devices);
            r["deviceCheck"] = zip.DeviceCheck;
            r["block"] = zip.Block;
            r["versions"] = zip.SupportedVersions;
            r["sha256"] = zip.Sha256;
            return r;
        }

        JsonNode KernelRun(JsonObject a)
        {
            RequireRoot();
            Pending p = Take(a, "kernel");
            RequireBattery();
            inUse = p.Path;
            AnyKernelInstaller installer = new AnyKernelInstaller(SuShell.Instance, phone.Flasher);
            KernelOutcome o = installer.Install(p.Path, p.Zip, ConsentFrom(a, settings), phone.Identity, phone.Props.Slot,
                settings.BackupFirst, line => Event("progress", new JsonObject { ["line"] = line }));
            return new JsonObject
            {
                ["installed"] = o.Installed, ["changed"] = o.Changed, ["error"] = o.Error,
                ["backup"] = o.BackupFolder == null ? null : Path.GetFileName(o.BackupFolder),
            };
        }

        JsonNode BackupList()
        {
            if (!phone.Root)
                return new JsonArray();
            PhoneIdentity id = phone.Identity;
            JsonArray list = new JsonArray();
            foreach (BackupEntry e in phone.Backups())
                list.Add(new JsonObject
                {
                    ["name"] = e.Name, ["serial"] = e.Serial, ["model"] = e.Model, ["product"] = e.Product, ["date"] = e.Date,
                    ["set"] = e.Set, ["mode"] = e.Mode, ["kb"] = e.Kb, ["mine"] = id != null && e.IsFrom(id),
                    ["files"] = new JsonArray(e.Sums.Keys.OrderBy(k => k).Select(k => (JsonNode)new JsonObject
                    {
                        ["name"] = k, ["sha256"] = e.Sums[k], ["partition"] = BackupFolder.PartitionOf(k),
                    }).ToArray()),
                });
            return list;
        }

        JsonNode BackupCreate(JsonObject a)
        {
            RequireRoot();
            bool both = Json.Bool(a, "both");
            List<string> parts = new List<string>();
            foreach (string b in FlashTargets.Allowed)
                foreach (string p in both ? new[] { phone.Target(b, false), phone.Target(b, true) } : new[] { phone.Target(b, false) })
                    if (p != null && !parts.Contains(p))
                        parts.Add(p);
            string folder = phone.Flasher.Backup(parts, phone.Identity, "boot", line => Event("progress", new JsonObject { ["line"] = line }));
            if (folder == null)
                throw new BridgeError("backup-failed");
            return new JsonObject { ["name"] = Path.GetFileName(folder) };
        }

        JsonNode CompanionBackup(JsonObject a)
        {
            RequireRoot();
            if (!Json.Bool(companion.Find(), "usable"))
                throw new BridgeError("companion-missing");
            string folder = companion.Backup(Json.Str(a, "set"), line => Event("progress", new JsonObject { ["line"] = line }));
            return new JsonObject { ["name"] = folder };
        }

        JsonNode BackupVerify(JsonObject a)
        {
            RequireRoot();
            List<string> failed = phone.Verify(Json.Str(a, "name")) ?? throw new BridgeError("not-allowed");
            return new JsonObject { ["failed"] = Json.Strings(failed) };
        }

        JsonNode BackupDelete(JsonObject a)
        {
            RequireRoot();
            if (!phone.Delete(Json.Str(a, "name")))
                throw new BridgeError("delete-failed");
            return null;
        }

        JsonNode Reboot(JsonObject a)
        {
            RequireRoot();
            string target = Json.Str(a, "target");
            if (target != null && target != "bootloader" && target != "recovery")
                throw new BridgeError("bad-target");
            SuShell.Instance.Run(target == null ? "svc power reboot || reboot" : "reboot " + target);
            return null;
        }

        // ---------------------------------------------------------------- OTG

        JsonNode OtgCheck(JsonObject a)
        {
            OtgPhone target = otg.Phone ?? throw new BridgeError("usb-gone");
            string baseName = Json.Str(a, "base");
            if (!FlashTargets.IsAllowed(baseName))
                throw new BridgeError("not-allowed");
            string partition = target.Target(baseName, Json.Bool(a, "other")) ?? throw new BridgeError("no-partition");
            long? bytes = target.Partitions.TryGetValue(partition, out long b) ? b : (long?)null;
            Pending p = new Pending { Kind = "otg", Partition = partition };
            string token = Json.Str(a, "token");
            if (Json.Str(a, "source") == "folder")
            {
                Picked f = null;
                lock (picked)
                    if (token == null || !picked.TryGetValue(token, out f))
                        f = null;
                if (f?.Folder == null)
                    throw new BridgeError("no-file");
                string file = Json.Str(a, "file");
                if (file == null || !f.Files.TryGetValue(file, out Android.Net.Uri uri) || file == "SHA256SUMS" || file == BackupFolder.InfoFile)
                    throw new BridgeError("no-file");
                p.Path = Path.Combine(f.Folder, SafeName(file));
                if (SafeName(file) != file || (!File.Exists(p.Path) && !activity.CopyTo(uri, p.Path)))
                    throw new BridgeError("read-failed");
                p.Check = ImageCheck.FromBackup(BackupFolder.Read(f.Folder), file, partition, bytes, target.Identity);
            }
            else
            {
                Picked f = PickedFile(a);
                p.Path = f.Path;
                p.Check = ImageCheck.FromFile(f.Path, partition, bytes, target.Identity, Json.Str(a, "expected"));
            }
            JsonObject r = Keep(p, baseName);
            long length = new FileInfo(p.Path).Length;
            r["tooBigForDownload"] = target.MaxDownload.HasValue && length > target.MaxDownload.Value;
            r["size"] = length;
            return r;
        }

        JsonNode OtgFlash(JsonObject a)
        {
            Pending p = Take(a, "otg");
            inUse = p.Path;
            if (!RootFlasher.Allowed(p.Check, p.Partition, ConsentFrom(a, settings)))
                throw new BridgeError("not-confirmed");
            long last = -1;
            otg.Flash(p.Path, p.Partition, p.Check.Sha256, (sent, total) =>
            {
                long percent = sent * 100 / Math.Max(1, total);
                if (percent == last)
                    return;
                last = percent;
                Event("otgProgress", new JsonObject { ["sent"] = sent, ["total"] = total });
            });
            return new JsonObject { ["partition"] = p.Partition, ["sha256"] = p.Check.Sha256 };
        }
    }
}
