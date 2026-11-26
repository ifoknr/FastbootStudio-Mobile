using System;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Runtime;

namespace FastbootStudio.Mobile.Native
{
    /// <summary>
    /// Keeps the app's process alive while a flash, a kernel install, a backup or an OTG send
    /// runs: a foreground service with an ongoing notification that names the job and its
    /// step. Android does not kill a process that has one, so leaving the app or the phone
    /// running low on memory does not cut a write in half.
    ///
    /// The service does none of the work. The job runs on its own thread as before, and
    /// nothing here may ever stop it: every call from the job (Begin, Step, End) catches
    /// whatever goes wrong, logs it and returns.
    ///
    /// It is declared by hand in AndroidManifest.xml under the name given to [Register], with
    /// foregroundServiceType "specialUse": the work is neither a sync nor always a USB device
    /// (a flash of this phone goes through root), and the other types need conditions these
    /// jobs do not meet.
    /// </summary>
    [Register("com.ifoknr.fastbootstudio.mobile.JobService")]
    public sealed class JobService : Service
    {
        const string ChannelId = "job";
        const int NotificationId = 71;

        static readonly object gate = new object();
        static JobService running;     // the service once Android has started it
        static bool wanted;            // a job is running
        static bool arabic;
        static string title = "", step = "";
        static PowerManager.WakeLock awake;

        // ---------------------------------------------------------------- called by the job

        /// <summary>A job starts. <paramref name="kind"/> is "image", "kernel", "backup" or "otg".</summary>
        public static void Begin(Context context, string kind, string partition, string lang)
        {
            try
            {
                Context app = context.ApplicationContext ?? context;
                lock (gate)
                {
                    wanted = true;
                    arabic = lang == "ar";
                    title = JobText.Title(arabic, kind, partition);
                    step = JobText.Wait(arabic);
                }
                Intent intent = new Intent(app, typeof(JobService));
                if (OperatingSystem.IsAndroidVersionAtLeast(26))
                    app.StartForegroundService(intent);
                else
                    app.StartService(intent);
            }
            catch (Exception e)
            {
                // Refused (the app is not in front, the phone forbids it...): the job runs all the same, as it did before there was a service.
                Log.Add("job service not started: " + e.Message);
            }
        }

        /// <summary>A step of the job, as its progress line ("backup boot_a", "written init_boot_a") or, for an OTG send, its percentage.</summary>
        public static void Step(Context context, string line, int percent = -1)
        {
            try
            {
                string text = percent >= 0 ? JobText.Sending(arabic, percent) : JobText.Step(arabic, line);
                JobService s;
                lock (gate)
                {
                    if (text == null || text == step || !wanted)
                        return;
                    step = text;
                    s = running;
                }
                if (s != null)
                    ((NotificationManager)s.GetSystemService(NotificationService))?.Notify(NotificationId, s.Build());
            }
            catch (Exception e)
            {
                Log.Add("job notification not updated: " + e.Message);
            }
        }

        /// <summary>The job ended, however it ended.</summary>
        public static void End()
        {
            try
            {
                JobService s;
                lock (gate)
                {
                    wanted = false;
                    s = running;
                }
                // Not started by Android yet: it stops itself as soon as it is (OnStartCommand), which is the only safe moment.
                s?.Finish();
            }
            catch (Exception e)
            {
                Log.Add("job service not stopped: " + e.Message);
            }
        }

        // ---------------------------------------------------------------- the service

        public override IBinder OnBind(Intent intent) => null;

        public override StartCommandResult OnStartCommand(Intent intent, StartCommandFlags flags, int startId)
        {
            try
            {
                Channel();
                // Always, and first: a service started as a foreground one must show its notification, even to stop right after.
                if (OperatingSystem.IsAndroidVersionAtLeast(34))
                    StartForeground(NotificationId, Build(), ForegroundService.TypeSpecialUse);
                else
                    StartForeground(NotificationId, Build());
            }
            catch (Exception e)
            {
                Log.Add("job service not in the foreground: " + e.Message);
            }
            bool stop;
            lock (gate)
            {
                running = this;
                stop = !wanted;
            }
            if (stop)
                Finish();
            else
                KeepAwake(true);
            // Never restarted on its own: without the job there is nothing to show.
            return StartCommandResult.NotSticky;
        }

        public override void OnDestroy()
        {
            lock (gate)
                if (running == this)
                    running = null;
            KeepAwake(false);
            base.OnDestroy();
        }

        void Finish()
        {
            try
            {
                KeepAwake(false);
                if (OperatingSystem.IsAndroidVersionAtLeast(24))
                    StopForeground(StopForegroundFlags.Remove);
                else
#pragma warning disable CA1422
                    StopForeground(true);
#pragma warning restore CA1422
                StopSelf();
            }
            catch (Exception e)
            {
                Log.Add("job service not stopped: " + e.Message);
            }
        }

        /// <summary>
        /// The processor stays on while the job runs, also with the screen off or the app left
        /// (the screen itself is only kept on while the page shows). Released at the end, and
        /// by itself after half an hour whatever happens.
        /// </summary>
        void KeepAwake(bool on)
        {
            try
            {
                lock (gate)
                {
                    if (on && awake == null)
                    {
                        PowerManager power = (PowerManager)GetSystemService(PowerService);
                        awake = power?.NewWakeLock(WakeLockFlags.Partial, "FastbootStudio:job");
                        awake?.SetReferenceCounted(false);
                        awake?.Acquire(30 * 60 * 1000L);
                    }
                    else if (!on && awake != null)
                    {
                        if (awake.IsHeld)
                            awake.Release();
                        awake = null;
                    }
                }
            }
            catch (Exception e)
            {
                Log.Add("wake lock: " + e.Message);
            }
        }

        void Channel()
        {
            if (!OperatingSystem.IsAndroidVersionAtLeast(26))
                return;
            // Low: shown, silent. Made again each time, which only renames it when the app's language changed.
            NotificationChannel channel = new NotificationChannel(ChannelId, JobText.Channel(arabic), NotificationImportance.Low);
            channel.SetShowBadge(false);
            ((NotificationManager)GetSystemService(NotificationService))?.CreateNotificationChannel(channel);
        }

        Notification Build()
        {
            string t, s;
            lock (gate)
            {
                t = title;
                s = step;
            }
            Notification.Builder b = OperatingSystem.IsAndroidVersionAtLeast(26)
                ? new Notification.Builder(this, ChannelId)
#pragma warning disable CA1422
                : new Notification.Builder(this);
#pragma warning restore CA1422
            // One of Android's own icons: a notification icon must be a plain shape, which the app's launcher icon is not.
            b.SetSmallIcon(Android.Resource.Drawable.StatNotifySync)
                .SetContentTitle(t)
                .SetContentText(s)
                .SetOngoing(true)
                .SetOnlyAlertOnce(true)
                .SetShowWhen(false);
            if (OperatingSystem.IsAndroidVersionAtLeast(31))
                b.SetForegroundServiceBehavior((int)NotificationForegroundService.Immediate);
            Intent open = PackageManager?.GetLaunchIntentForPackage(PackageName);
            if (open != null)
                b.SetContentIntent(PendingIntent.GetActivity(this, 0, open, PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable));
            return b.Build();
        }
    }

    /// <summary>What the job's notification says, in Arabic (Gulf) and English, like the page.</summary>
    static class JobText
    {
        static string T(bool arabic, string ar, string en) => arabic ? ar : en;

        public static string Channel(bool a) => T(a, "العمليات الجارية", "Running jobs");

        public static string Wait(bool a) => T(a, "لا تقفل التطبيق لين تخلص", "Do not close the app until it finishes");

        public static string Title(bool a, string kind, string partition)
        {
            string p = string.IsNullOrEmpty(partition) ? "" : " " + partition;
            switch (kind)
            {
                case "image": return T(a, "يفلّش" + p, "Flashing" + p);
                case "kernel": return T(a, "يثبّت الكيرنل", "Installing the kernel");
                case "backup": return T(a, "ياخذ نسخة احتياطية", "Backing up");
                case "restore": return T(a, "يرجّع نسخة احتياطية", "Restoring a backup");
                case "sideload": return T(a, "يرسل ملف للريكفري", "Sending a package to recovery");
                case "otg": return T(a, "يفلّش" + p + " على الجوال الموصول", "Flashing" + p + " on the connected phone");
                default: return "Fastboot Studio";
            }
        }

        public static string Sending(bool a, int percent) => T(a, "يرسل الملف ", "Sending the file ") + percent.ToString(System.Globalization.CultureInfo.InvariantCulture) + "%";

        /// <summary>The step for a progress line, or null for a line that is not a step (the installer's own output).</summary>
        public static string Step(bool a, string line)
        {
            line = line ?? "";
            bool Is(string prefix) => line.StartsWith(prefix, StringComparison.Ordinal);
            if (Is("check ok")) return T(a, "تحقق من الملف", "File checked");
            if (Is("backup ok")) return T(a, "انحفظت النسخة الاحتياطية", "Backup saved");
            if (Is("backup needs")) return null;
            if (Is("backup ")) return T(a, "ينسخ ", "Copying ") + line.Substring(7);
            if (Is("written ")) return T(a, "انكتب القسم", "Partition written");
            if (Is("write ")) return T(a, "يكتب على القسم", "Writing the partition");
            if (Is("verify ok")) return T(a, "القسم مطابق للملف", "The partition matches the file");
            if (Is("verify ")) return T(a, "يقرا القسم ويقارنه بالملف", "Reading it back to compare");
            if (Is("restore stopped")) return null;
            if (Is("restore done")) return null;
            if (Is("restore ")) return T(a, "يرجّع ", "Restoring ") + line.Substring(8);
            if (Is("install ")) return T(a, "يشغّل AnyKernel3", "Running AnyKernel3");
            return null;
        }
    }
}
