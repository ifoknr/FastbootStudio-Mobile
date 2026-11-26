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
        readonly Ledger ledger;
        // One for the whole app: when Android makes the activity again, a write started before still holds it.
        static readonly SemaphoreSlim busy = new SemaphoreSlim(1, 1);
        readonly Dictionary<string, Picked> picked = new Dictionary<string, Picked>(StringComparer.Ordinal);
        Pending pending;
        PendingRestore restore;

        /// <summary>A restore plan as it was shown: the only one a restore may run.</summary>
        sealed class PendingRestore
        {
            public string Id = Guid.NewGuid().ToString("N"), Name;
            public List<RestoreItem> Items;
        }

        // The list shown before the first write of a session (Core's Preflight). Kept here, for the
        // whole app, so the page cannot skip it: a write is refused until the list was shown and
        // nothing on it failed. "skip" is set only when everything passed and the person asked
        // not to see it again; otherwise each showing allows one write.
        static readonly object preflightGate = new object();
        static bool preflightSkip, preflightShown;

        /// <summary>The file a running flash reads: never cleaned up under it.</summary>
        static volatile string inUse;

        // ---------------------------------------------------------------- the page and the running job
        //
        // Android can make the activity (and so the page and this bridge) again while a write
        // runs: a language or font-size change, a window resize. The job keeps running on its
        // thread, so what the page needs to show it lives here, for the whole app: the page
        // that comes back asks "run.state" and goes on from the steps so far; the end comes as
        // the "runEnd" event.

        static readonly object liveGate = new object();
        static MainActivity live;
        static bool running;

        static readonly object runGate = new object();
        static string runMeta, runResult, runError;
        static bool runEnded;
        static long runSent, runTotal;
        static readonly List<string> runLines = new List<string>();

        /// <summary>The activity showing the page now; the screen stays on for it while a job runs.</summary>
        public static void Attach(MainActivity activity)
        {
            lock (liveGate)
            {
                live = activity;
                if (running)
                    activity.KeepAwake(true);
            }
        }

        public static void Detach(MainActivity activity)
        {
            lock (liveGate)
                if (live == activity)
                    live = null;
        }

        static void Send(string name, JsonNode data)
        {
            MainActivity a;
            lock (liveGate)
                a = live;
            a?.Js("window.fbsEvent && window.fbsEvent(" + JsonValue.Create(name).ToJsonString() + "," + (data?.ToJsonString() ?? "null") + ")");
        }

        /// <summary>A step of the running job: kept for a page that comes back, and sent to the page there is.</summary>
        static void Progress(string line)
        {
            lock (runGate)
                if (runMeta != null && !runEnded)
                    runLines.Add(line);
            JobService.Step(null, line);
            Send("progress", new JsonObject { ["line"] = line });
        }

        static JsonNode RunState()
        {
            lock (runGate)
            {
                if (runMeta == null)
                    return null;
                return new JsonObject
                {
                    ["meta"] = JsonNode.Parse(runMeta), ["lines"] = Json.Strings(runLines), ["sent"] = runSent, ["total"] = runTotal,
                    ["state"] = runEnded ? "ended" : "running", ["result"] = runResult == null ? null : JsonNode.Parse(runResult), ["error"] = runError,
                };
            }
        }

        public Bridge(MainActivity activity, Phone phone, Settings settings, Otg otg)
        {
            this.activity = activity;
            this.phone = phone;
            this.settings = settings;
            this.otg = otg;
            ledger = new Ledger(activity.FilesDir.AbsolutePath);
        }

        /// <summary>Keeps, in the app's own storage, the SHA-256s of a backup the app (or the module, from the app) just made.</summary>
        void RecordBackup(string folder)
        {
            string name = folder == null ? null : Path.GetFileName(folder);
            BackupEntry e = name == null ? null : phone.Backups().FirstOrDefault(b => b.Name == name);
            if (e != null)
                ledger.Record(name, e.Sums);
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

            /// <summary>For an OTG check: the serial of the phone it was checked against.</summary>
            public string Serial;
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

        public void Event(string name, JsonNode data) => Send(name, data);

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
                    return Exclusive(() => FlashRun(a), a);
                case "kernel.check":
                    return KernelCheck(a);
                case "kernel.run":
                    return Exclusive(() => KernelRun(a), a);
                case "companion.find":
                    // Only with root: without it the module is of no use, so it is never mentioned.
                    return phone.Root ? companion.Find() : new JsonObject { ["installed"] = false };
                case "companion.backup":
                    return Exclusive(() => CompanionBackup(a), a);
                case "companion.bundle":
                    RequireRoot();
                    return new JsonObject { ["file"] = companion.Bundle() };
                case "backup.list":
                    return BackupList();
                case "backup.create":
                    return Exclusive(() => BackupCreate(a), a);
                case "backup.space":
                    return BackupSpace();
                case "backup.export":
                    return await BackupExport(a);
                case "logs.export":
                    return await SupportExport();
                case "restore.plan":
                    return RestorePlan(a);
                case "restore.run":
                    return Exclusive(() => RestoreRun(a), a);
                case "preflight":
                    return PreflightList(a);
                case "preflight.ack":
                    return PreflightAck(a);
                case "sideload.run":
                    // The USB permission is asked for first; the send itself runs like every other job.
                    await otg.Permit(Json.Str(a, "id"));
                    return Exclusive(() => SideloadRun(a), a);
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
                case "update.check":
                    return await UpdateCheckRun(Json.Bool(a, "force"));
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
                    return Exclusive(() => OtgFlash(a), a);
                case "otg.cancel":
                    return new JsonObject { ["cancelled"] = otg.Cancel() };
                case "run.state":
                    return RunState();
                case "run.clear":
                    // The page has shown the result: a page made later starts clean.
                    lock (runGate)
                        if (runEnded)
                        {
                            runMeta = null;
                            runLines.Clear();
                        }
                    return null;
                case "logs.save":
                    return new JsonObject { ["saved"] = await activity.SaveText("fastboot-studio-log-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture) + ".txt", string.Join("\n", Log.All()) + "\n") };
                case "otg.reboot":
                    string target = Json.Str(a, "target");
                    if (target != null && target != "bootloader" && target != "recovery" && target != "fastboot")
                        throw new BridgeError("bad-target");
                    return new JsonObject { ["answered"] = otg.Reboot(target) };
                case "otg.close":
                    otg.Close();
                    return null;
                default:
                    throw new BridgeError("unknown-method");
            }
        }

        /// <summary>One write at a time, with the screen kept on while it runs.</summary>
        /// <param name="a">The call's arguments: with a "run" object (what the page shows for this job), the job's steps and end are kept for a page that comes back.</param>
        JsonNode Exclusive(Func<JsonNode> work, JsonObject a = null)
        {
            if (!busy.Wait(0))
                throw new BridgeError("busy");
            string meta = (a?["run"] as JsonObject)?.ToJsonString();
            // Only a job the page shows as one is kept; a quick one without "run" (deleting a backup) leaves the last result alone.
            bool tracked = meta != null;
            if (tracked)
                lock (runGate)
                {
                    runMeta = meta;
                    runLines.Clear();
                    runResult = runError = null;
                    runEnded = false;
                    runSent = runTotal = 0;
                }
            lock (liveGate)
            {
                running = true;
                live?.KeepAwake(true);
            }
            if (tracked)
            {
                // Android keeps the process while this shows; if it cannot be started, the job runs as before (JobService catches it all).
                JsonObject run = a["run"] as JsonObject;
                JobService.Begin(activity, Json.Str(run, "kind"), Json.Str(run, "partition"), settings.Lang);
                activity.AskNotifications(settings);
            }
            string error = null;
            JsonNode result = null;
            try
            {
                result = work();
                return result;
            }
            catch (BridgeError e)
            {
                error = e.Message;
                throw;
            }
            catch (Exception e)
            {
                error = "internal: " + e.Message;
                throw;
            }
            finally
            {
                inUse = null;
                JsonNode end = null;
                lock (runGate)
                    if (tracked && runMeta != null)
                    {
                        runEnded = true;
                        runError = error;
                        runResult = error == null ? result?.ToJsonString() : null;
                        end = new JsonObject { ["result"] = runResult == null ? null : JsonNode.Parse(runResult), ["error"] = runError };
                    }
                lock (liveGate)
                {
                    running = false;
                    live?.KeepAwake(false);
                }
                if (tracked)
                    JobService.End();
                busy.Release();
                if (end != null)
                    Send("runEnd", end);
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

        // ---------------------------------------------------------------- before the first write

        /// <summary>The partitions a job of this kind is about to write, as far as the app knows them now.</summary>
        List<string> PreflightTargets(string kind)
        {
            List<string> targets = new List<string>();
            if (kind == "image" && pending?.Kind == "image")
                targets.Add(pending.Partition);
            else if (kind == "kernel")
                targets.AddRange(new[] { "boot", "init_boot", "vendor_boot", "dtbo" }.Select(b => phone.Target(b, false)).Where(p => p != null));
            else if (kind == "restore" && restore != null)
                targets.AddRange(restore.Items.Where(i => i.Writable).Select(i => i.Partition));
            return targets;
        }

        List<PreflightItem> PreflightItems(string kind)
        {
            PreflightFacts f = new PreflightFacts
            {
                Kind = kind, Battery = activity.Battery, MinBattery = settings.MinBattery, Charging = phone.Charging,
                Root = phone.Root && phone.Props != null, BackupFirst = settings.BackupFirst,
            };
            if (f.OtherPhone)
            {
                f.OtgSerial = kind == "otg" ? otg.Phone?.Serial : null;
                f.OtgUnlocked = kind == "otg" ? otg.Phone?.Unlocked : null;
                return Preflight.Evaluate(f);
            }
            if (f.Root)
            {
                f.Unlocked = phone.Unlocked;
                List<string> targets = PreflightTargets(kind);
                // A backup from today that the app itself recorded, holding every partition about to be written.
                f.BackupToday = targets.Count > 0 && ledger.Names().Any(n => Preflight.IsFrom(n, DateTime.Now)
                    && ledger.Get(n) is IReadOnlyDictionary<string, string> sums && targets.All(t => sums.ContainsKey(t + ".img")));
                if (f.BackupFirst && targets.Count > 0)
                {
                    SuShell.Instance.Run("mkdir -p " + Sh.Q(RootFlasher.BackupRoot));
                    f.NeedBytes = phone.Flasher.BackupBytes(targets);
                    f.FreeBytes = phone.Flasher.FreeBytes(RootFlasher.BackupRoot);
                }
            }
            return Preflight.Evaluate(f);
        }

        static string PreflightKind(JsonObject a)
        {
            string kind = Json.Str(a, "kind");
            if (kind != "image" && kind != "kernel" && kind != "restore" && kind != "otg" && kind != "sideload")
                throw new BridgeError("bad-kind");
            return kind;
        }

        /// <summary>{needed: false} once the person asked not to see it again after a clean pass; else the list.</summary>
        JsonNode PreflightList(JsonObject a)
        {
            string kind = PreflightKind(a);
            lock (preflightGate)
                if (preflightSkip)
                    return new JsonObject { ["needed"] = false };
            List<PreflightItem> items = PreflightItems(kind);
            return new JsonObject
            {
                ["needed"] = true, ["blocked"] = Preflight.Blocked(items), ["allPass"] = Preflight.AllPass(items),
                ["items"] = new JsonArray(items.Select(i => (JsonNode)new JsonObject { ["code"] = i.Code, ["level"] = i.Level, ["values"] = Json.Strings(i.Values) }).ToArray()),
            };
        }

        /// <summary>
        /// The person read the list and goes on. Judged again here, not taken from the page: a
        /// fail still blocks, a warn needs the tick ("accept"), and "not again this session" is
        /// kept only when everything passed.
        /// </summary>
        JsonNode PreflightAck(JsonObject a)
        {
            List<PreflightItem> items = PreflightItems(PreflightKind(a));
            if (Preflight.Blocked(items))
                throw new BridgeError("preflight-blocked");
            bool clean = Preflight.AllPass(items);
            if (!clean && !Json.Bool(a, "accept"))
                throw new BridgeError("not-confirmed");
            lock (preflightGate)
            {
                preflightShown = true;
                if (clean && Json.Bool(a, "skip"))
                    preflightSkip = true;
            }
            Log.Add("pre-flight: " + string.Join(", ", items.Select(i => i.Code + " " + i.Level)));
            return null;
        }

        /// <summary>No write before the list was shown; one showing, one write.</summary>
        static void RequirePreflight()
        {
            lock (preflightGate)
            {
                if (preflightSkip)
                    return;
                if (!preflightShown)
                    throw new BridgeError("preflight-needed");
                preflightShown = false;
            }
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
                BackupFolder folder = phone.Fetch(name, file, inUse) ?? throw new BridgeError("backup-unreadable");
                p.Path = Path.Combine(folder.Path, file);
                p.Check = ImageCheck.FromBackup(folder, file, partition, bytes, id, ledger.Get(name));
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
            // Before the check is taken: a low battery leaves it to be used once the phone is charged.
            RequireBattery();
            RequirePreflight();
            Pending p = Take(a, "image");
            inUse = p.Path;
            FlashOutcome o = phone.Flasher.Flash(p.Path, p.Partition, p.Check, ConsentFrom(a, settings), phone.Identity,
                settings.BackupFirst, settings.ReadBack, Progress);
            RecordBackup(o.BackupFolder);
            return new JsonObject
            {
                ["partition"] = p.Partition, ["written"] = o.Written, ["touched"] = o.Touched, ["verified"] = o.Verified, ["error"] = o.Error,
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
            r["kernelVersion"] = zip.Kernel?.Numbers;
            r["kernelKmi"] = zip.Kernel?.Kmi;
            r["kernelSource"] = zip.Kernel?.Source;
            return r;
        }

        JsonNode KernelRun(JsonObject a)
        {
            RequireRoot();
            RequireBattery();
            RequirePreflight();
            Pending p = Take(a, "kernel");
            inUse = p.Path;
            AnyKernelInstaller installer = new AnyKernelInstaller(SuShell.Instance, phone.Flasher);
            KernelOutcome o = installer.Install(p.Path, p.Zip, ConsentFrom(a, settings), phone.Identity, phone.Props.Slot,
                settings.BackupFirst, Progress);
            RecordBackup(o.BackupFolder);
            return new JsonObject
            {
                ["installed"] = o.Installed, ["changed"] = o.Changed, ["ran"] = o.Ran, ["error"] = o.Error,
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

        /// <summary>The partitions a boot backup copies: the allowed ones on the current slot, or on both.</summary>
        List<string> BackupParts(bool both)
        {
            List<string> parts = new List<string>();
            foreach (string b in FlashTargets.Allowed)
                foreach (string p in both ? new[] { phone.Target(b, false), phone.Target(b, true) } : new[] { phone.Target(b, false) })
                    if (p != null && !parts.Contains(p))
                        parts.Add(p);
            return parts;
        }

        /// <summary>What a new backup takes and what is free where backups go, in bytes (null when it cannot be read).</summary>
        JsonNode BackupSpace()
        {
            RequireRoot();
            // df needs a folder that exists; the first backup makes it anyway.
            SuShell.Instance.Run("mkdir -p " + Sh.Q(RootFlasher.BackupRoot));
            return new JsonObject
            {
                ["free"] = phone.Flasher.FreeBytes(RootFlasher.BackupRoot),
                ["need"] = phone.Flasher.BackupBytes(BackupParts(false)), ["needBoth"] = phone.Flasher.BackupBytes(BackupParts(true)),
            };
        }

        JsonNode BackupCreate(JsonObject a)
        {
            RequireRoot();
            bool both = Json.Bool(a, "both");
            List<string> parts = BackupParts(both);
            string folder = phone.Flasher.Backup(parts, phone.Identity, "boot", Progress);
            if (folder == null)
                throw new BridgeError(phone.Flasher.BackupError ?? "backup-failed");
            RecordBackup(folder);
            return new JsonObject { ["name"] = Path.GetFileName(folder) };
        }

        JsonNode CompanionBackup(JsonObject a)
        {
            RequireRoot();
            if (!Json.Bool(companion.Find(), "usable"))
                throw new BridgeError("companion-missing");
            string folder = companion.Backup(Json.Str(a, "set"), Progress);
            RecordBackup(folder);
            return new JsonObject { ["name"] = folder };
        }

        // ---------------------------------------------------------------- a whole backup back

        /// <summary>
        /// The plan for putting a backup back as a whole: refused outright unless the app's own
        /// record says it is this phone's and unchanged; then every image the app may write is
        /// copied in turn and given the check a single flash gets. Nothing is written here.
        /// </summary>
        JsonNode RestorePlan(JsonObject a)
        {
            RequireRoot();
            if (busy.CurrentCount == 0)
                throw new BridgeError("busy");
            string name = Json.Str(a, "name");
            if (!BackupListing.IsName(name))
                throw new BridgeError("not-allowed");
            BackupEntry e = phone.Backups().FirstOrDefault(b => b.Name == name) ?? throw new BridgeError("backup-unreadable");
            PhoneIdentity id = phone.Identity;
            IReadOnlyDictionary<string, string> recorded = ledger.Get(name);
            string refusal = BackupRestore.Refusal(e, recorded, id);
            if (refusal != null)
            {
                Log.Add("restore " + name + " refused: " + refusal);
                return new JsonObject { ["refused"] = refusal, ["name"] = name, ["serial"] = e.Serial, ["model"] = e.Model };
            }
            List<RestoreItem> plan = BackupRestore.Plan(e.Sums.Keys, phone.Partitions, phone.Props?.Slot);
            List<RestoreItem> toCheck = plan.Where(i => i.Writable).ToList();
            for (int n = 0; n < toCheck.Count; n++)
            {
                RestoreItem item = toCheck[n];
                Event("restorePlan", new JsonObject { ["file"] = item.File, ["n"] = n + 1, ["total"] = toCheck.Count });
                BackupFolder folder = phone.Fetch(name, item.File, inUse);
                BackupRestore.Checked(item, folder == null ? null
                    : ImageCheck.FromBackup(folder, item.File, item.Partition, phone.Flasher.PartitionBytes(item.Partition), id, recorded));
                // The copy was only for the check: the write fetches it again and the flasher compares it with this check's SHA-256.
                try { if (folder != null) File.Delete(Path.Combine(folder.Path, item.File)); } catch (IOException) { }
            }
            PendingRestore p = new PendingRestore { Name = name, Items = plan };
            lock (picked)
                restore = p;
            Log.Add("restore plan " + name + ": " + string.Join(", ", plan.Select(i => i.File + " " + i.State + (i.Reason == null ? "" : " (" + i.Reason + ")"))));
            List<string> writable = plan.Where(i => i.Writable).Select(i => i.Partition).ToList();
            JsonArray items = new JsonArray();
            foreach (RestoreItem i in plan)
                items.Add(new JsonObject
                {
                    ["file"] = i.File, ["partition"] = i.Partition, ["state"] = i.State, ["reason"] = i.Reason,
                    ["bytes"] = i.Writable ? phone.Flasher.PartitionBytes(i.Partition) : null,
                    ["check"] = i.Check == null ? null : Json.Check(i.Check, false),
                });
            return new JsonObject
            {
                ["planId"] = p.Id, ["name"] = name, ["serial"] = id?.Serial, ["model"] = id?.Model, ["slot"] = phone.Props?.Slot, ["items"] = items,
                ["backupFirst"] = settings.BackupFirst, ["free"] = phone.Flasher.FreeBytes(RootFlasher.BackupRoot),
            };
        }

        JsonNode RestoreRun(JsonObject a)
        {
            RequireRoot();
            RequireBattery();
            RequirePreflight();
            PendingRestore p;
            lock (picked)
            {
                p = restore;
                if (p == null || p.Id != Json.Str(a, "planId"))
                    throw new BridgeError("check-expired");
                restore = null;
            }
            HashSet<string> files = new HashSet<string>((a["files"] as JsonArray ?? new JsonArray()).Select(f => f?.GetValue<string>()), StringComparer.Ordinal);
            // In the plan's own order, and only what the plan offered: a name the plan did not offer refuses the whole restore.
            List<RestoreItem> chosen = p.Items.Where(i => i.Writable && files.Contains(i.File)).ToList();
            if (chosen.Count != files.Count || chosen.Count == 0)
                throw new BridgeError("not-confirmed");
            // The backup is still what the plan was made from.
            BackupEntry e = phone.Backups().FirstOrDefault(b => b.Name == p.Name);
            if (BackupRestore.Refusal(e, ledger.Get(p.Name), phone.Identity) != null)
                throw new BridgeError("check-expired");
            inUse = Path.Combine(phone.CacheDir, "backup", p.Name) + "/";
            RestoreOutcome o = BackupRestore.Run(chosen, file =>
            {
                BackupFolder folder = phone.Fetch(p.Name, file);
                return folder == null ? null : Path.Combine(folder.Path, file);
            }, phone.Flasher, phone.Identity, settings.BackupFirst, settings.ReadBack, Progress);
            RecordBackup(o.BackupFolder);
            return new JsonObject
            {
                ["written"] = Json.Strings(o.Written), ["failed"] = o.Failed, ["notWritten"] = Json.Strings(o.NotWritten), ["error"] = o.Error,
                ["backup"] = o.BackupFolder == null ? null : Path.GetFileName(o.BackupFolder), ["name"] = p.Name,
            };
        }

        // ---------------------------------------------------------------- one file to keep or send

        /// <summary>
        /// A backup folder as one tar, written where the person chooses (the system's "save"
        /// page), then read back from there: every image in the archive must have the SHA-256
        /// SHA256SUMS (and the app's own record) gives it, or the file is removed again.
        /// </summary>
        async Task<JsonNode> BackupExport(JsonObject a)
        {
            RequireRoot();
            string name = Json.Str(a, "name");
            BackupEntry e = BackupListing.IsName(name) ? phone.Backups().FirstOrDefault(b => b.Name == name) : null;
            if (e == null || e.Sums.Count == 0)
                throw new BridgeError("backup-unreadable");
            Android.Net.Uri uri = await activity.CreateDocument(name + ".tar", "application/x-tar");
            if (uri == null)
                return new JsonObject { ["saved"] = false };
            if (!busy.Wait(0))
                throw new BridgeError("busy");
            try
            {
                bool written;
                using (Stream output = activity.OpenOutput(uri))
                    written = phone.ExportTar(name, output);
                List<string> bad = new List<string> { "SHA256SUMS" };
                if (written)
                    using (Stream input = activity.OpenInput(uri))
                    {
                        Dictionary<string, string> inside = Tar.Hashes(input);
                        bad = Tar.Mismatches(inside, e.Sums);
                        if (ledger.Get(name) is IReadOnlyDictionary<string, string> recorded)
                            bad.AddRange(Tar.Mismatches(inside, recorded).Where(f => !bad.Contains(f)));
                    }
                if (!written || bad.Count > 0)
                {
                    activity.Discard(uri);
                    Log.Add("export " + name + " failed: " + string.Join(", ", bad));
                    throw new BridgeError("export-failed: " + string.Join(", ", bad));
                }
                Log.Add("export " + name + ": " + e.Sums.Count + " images in one archive, hashes checked");
                return new JsonObject { ["saved"] = true, ["files"] = e.Sums.Count };
            }
            catch (Exception x) when (x is IOException || x is InvalidDataException || x is Java.IO.IOException)
            {
                activity.Discard(uri);
                Log.Add("export " + name + " failed: " + x.Message);
                throw new BridgeError("export-failed");
            }
            finally
            {
                busy.Release();
            }
        }

        /// <summary>The app's log and the phone's details (serial masked) as one small tar, for a support message.</summary>
        async Task<JsonNode> SupportExport()
        {
            string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture);
            Android.Net.Uri uri = await activity.CreateDocument("fastboot-studio-support-" + stamp + ".tar", "application/x-tar");
            if (uri == null)
                return new JsonObject { ["saved"] = false };
            try
            {
                byte[] log = System.Text.Encoding.UTF8.GetBytes(string.Join("\n", Log.All()) + "\n");
                byte[] device = System.Text.Encoding.UTF8.GetBytes("Fastboot Studio Mobile " + activity.Version + "\n" + phone.LastDetails + "\n");
                using (Stream output = activity.OpenOutput(uri))
                {
                    Tar.WriteFile(output, "log.txt", new MemoryStream(log), log.Length, DateTime.Now);
                    Tar.WriteFile(output, "device.txt", new MemoryStream(device), device.Length, DateTime.Now);
                    Tar.WriteEnd(output);
                }
                using (Stream input = activity.OpenInput(uri))
                    if (Tar.Hashes(input).Count != 2)
                        throw new InvalidDataException("not what was written");
                return new JsonObject { ["saved"] = true };
            }
            catch (Exception x) when (x is IOException || x is InvalidDataException || x is Java.IO.IOException)
            {
                activity.Discard(uri);
                Log.Add("support archive failed: " + x.Message);
                throw new BridgeError("export-failed");
            }
        }

        // ---------------------------------------------------------------- sideload (experimental)

        JsonNode SideloadRun(JsonObject a)
        {
            RequireBattery();
            RequirePreflight();
            Picked f = PickedFile(a);
            if (!IsZip(f.Path))
                throw new BridgeError("not-a-zip");
            inUse = f.Path;
            long last = -1;
            string mode = otg.Sideload(Json.Str(a, "id"), f.Path, (sent, total) =>
            {
                long percent = sent * 100 / Math.Max(1, total);
                if (percent == last)
                    return;
                last = percent;
                lock (runGate)
                {
                    runSent = sent;
                    runTotal = total;
                }
                JobService.Step(null, null, (int)percent);
                Event("otgProgress", new JsonObject { ["sent"] = sent, ["total"] = total });
            }, () => Progress("approve"));
            return new JsonObject { ["mode"] = mode, ["size"] = f.Size };
        }

        JsonNode BackupVerify(JsonObject a)
        {
            RequireRoot();
            string name = Json.Str(a, "name");
            List<string> failed = phone.Verify(name) ?? throw new BridgeError("not-allowed");
            // SHA256SUMS sits beside the images on shared storage; the app's own record of a backup it made does not.
            IReadOnlyDictionary<string, string> recorded = ledger.Get(name);
            if (recorded != null)
            {
                BackupEntry e = phone.Backups().FirstOrDefault(b => b.Name == name);
                foreach (string f in BackupListing.NotAsRecorded(e?.Sums, recorded))
                    if (!failed.Contains(f))
                        failed.Add(f);
            }
            return new JsonObject { ["failed"] = Json.Strings(failed), ["recorded"] = recorded != null };
        }

        JsonNode BackupDelete(JsonObject a)
        {
            RequireRoot();
            if (!phone.Delete(Json.Str(a, "name")))
                throw new BridgeError("delete-failed");
            ledger.Forget(Json.Str(a, "name"));
            return null;
        }

        JsonNode Reboot(JsonObject a)
        {
            RequireRoot();
            string target = Json.Str(a, "target");
            if (target != null && target != "bootloader" && target != "recovery")
                throw new BridgeError("bad-target");
            // Never under a running write or backup.
            if (busy.CurrentCount == 0)
                throw new BridgeError("busy");
            SuShell.Instance.Run(target == null ? "svc power reboot || reboot" : "reboot " + target);
            return null;
        }

        // ---------------------------------------------------------------- updates

        static readonly System.Net.Http.HttpClient Http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(15) };

        /// <summary>
        /// The latest release against this version. Automatic checks run once a day and only
        /// when allowed in Settings; "Check now" always runs. Nothing is downloaded here.
        /// </summary>
        async Task<JsonNode> UpdateCheckRun(bool force)
        {
            long now = DateTime.UtcNow.Ticks;
            if (!force && (!settings.AutoUpdate || now - settings.LastUpdateCheck < UpdateCheck.Every.Ticks))
                return new JsonObject { ["skipped"] = true };
            string installed = activity.Version;
            using (System.Net.Http.HttpRequestMessage request = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Get, UpdateCheck.Api))
            {
                request.Headers.UserAgent.ParseAdd("FastbootStudio-Mobile/" + installed);
                request.Headers.Accept.ParseAdd("application/vnd.github+json");
                System.Net.Http.HttpResponseMessage response;
                try { response = await Http.SendAsync(request); }
                // Android's handler reports no network as a WebException or a Java IOException, not an HttpRequestException.
                catch (Exception e) when (e is System.Net.Http.HttpRequestException || e is TaskCanceledException
                    || e is System.Net.WebException || e is IOException || e is Java.IO.IOException)
                {
                    Log.Add("update check: " + e.Message);
                    throw new BridgeError("update-offline");
                }
                using (response)
                {
                    if (!response.IsSuccessStatusCode && response.StatusCode != System.Net.HttpStatusCode.NotFound)
                        throw new BridgeError("update-failed: " + (int)response.StatusCode);
                    // Only an answer counts as today's check: offline, the next start tries again.
                    settings.LastUpdateCheck = now;
                    if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                        return new JsonObject { ["installed"] = installed, ["none"] = true };
                    UpdateCheck.Release r = UpdateCheck.Newest(await response.Content.ReadAsStringAsync(), installed);
                    if (r == null)
                        return new JsonObject { ["installed"] = installed, ["none"] = true };
                    bool newer = UpdateCheck.IsNewer(r.Version, installed);
                    Log.Add("update check: latest " + r.Version + ", installed " + installed + (newer ? " (newer)" : ""));
                    return new JsonObject
                    {
                        ["installed"] = installed, ["latest"] = r.Version, ["newer"] = newer, ["page"] = r.Page,
                        ["apk"] = r.Apk, ["bytes"] = r.ApkBytes,
                    };
                }
            }
        }

        // ---------------------------------------------------------------- OTG

        JsonNode OtgCheck(JsonObject a)
        {
            OtgPhone target = otg.Phone ?? throw new BridgeError("usb-gone");
            string baseName = Json.Str(a, "base");
            if (!FlashTargets.IsAllowed(baseName))
                throw new BridgeError("not-allowed");
            string partition = target.Target(baseName, Json.Bool(a, "other")) ?? throw new BridgeError("no-partition");
            long? bytes = target.Partitions.TryGetValue(partition, out long b) && b >= 0 ? b : (long?)null;
            Pending p = new Pending { Kind = "otg", Partition = partition, Serial = target.Serial };
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
                p.Check = ImageCheck.FromBackup(BackupFolder.Read(f.Folder), file, partition, bytes, target.Identity, ledger.Get(f.Name));
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
            // This phone's battery: it is the one sending, and it must not go off half-way.
            RequireBattery();
            RequirePreflight();
            Pending p = Take(a, "otg");
            inUse = p.Path;
            // The check was made against one phone: it is not used for another one opened since.
            OtgPhone now = otg.Phone ?? throw new BridgeError("usb-gone");
            if (now.Serial != p.Serial)
                throw new BridgeError("serial-mismatch");
            if (!RootFlasher.Allowed(p.Check, p.Partition, ConsentFrom(a, settings)))
                throw new BridgeError("not-confirmed");
            long last = -1;
            otg.Flash(p.Path, p.Partition, p.Check.Sha256, (sent, total) =>
            {
                long percent = sent * 100 / Math.Max(1, total);
                if (percent == last)
                    return;
                last = percent;
                lock (runGate)
                {
                    runSent = sent;
                    runTotal = total;
                }
                JobService.Step(null, null, (int)percent);
                Event("otgProgress", new JsonObject { ["sent"] = sent, ["total"] = total });
            });
            return new JsonObject { ["partition"] = p.Partition, ["sha256"] = p.Check.Sha256 };
        }
    }
}
