using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Content.Res;
using Android.Database;
using Android.Graphics;
using Android.Hardware.Usb;
using Android.OS;
using Android.Provider;
using Android.Views;
using Android.Webkit;
using FastbootStudio.Mobile.Native;
using Settings = FastbootStudio.Mobile.Native.Settings;

namespace FastbootStudio.Mobile
{
    [Activity(Label = "@string/app_name", MainLauncher = true, Exported = true, Theme = "@style/AppTheme",
        LaunchMode = LaunchMode.SingleTask,
        ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize
            | ConfigChanges.KeyboardHidden | ConfigChanges.Keyboard | ConfigChanges.UiMode | ConfigChanges.Density | ConfigChanges.Locale)]
    [IntentFilter(new[] { UsbManager.ActionUsbDeviceAttached })]
    [MetaData(UsbManager.ActionUsbDeviceAttached, Resource = "@xml/usb_fastboot")]
    public class MainActivity : Activity
    {
        const string Page = "file:///android_asset/ui/index.html";
        const int PickCode = 41, TreeCode = 42;

        WebView web;
        Bridge bridge;
        Otg otg;
        UsbReceiver receiver;
        TaskCompletionSource<Android.Net.Uri> pick;

        public string Version => PackageManager.GetPackageInfo(PackageName, 0).VersionName;

        public bool SystemDark => (Resources.Configuration.UiMode & UiMode.NightMask) == UiMode.NightYes;

        public int Battery => ((BatteryManager)GetSystemService(BatteryService))?.GetIntProperty((int)BatteryProperty.Capacity) ?? -1;

        protected override void OnCreate(Bundle savedInstanceState)
        {
            base.OnCreate(savedInstanceState);
            Settings settings = new Settings(this);
            otg = new Otg(this);
            bridge = new Bridge(this, new Phone(this), settings, otg);

            web = new WebView(this);
            web.SetBackgroundColor(Color.ParseColor(settings.Theme == "light" || (settings.Theme == "auto" && !SystemDark) ? "#FDF8FF" : "#2A2A2C"));
            WebSettings s = web.Settings;
            s.JavaScriptEnabled = true;
            // Assets load whatever these say; the page reads no file and no content:// itself.
            s.AllowFileAccess = false;
            s.AllowContentAccess = false;
            s.AllowFileAccessFromFileURLs = false;
            s.AllowUniversalAccessFromFileURLs = false;
            s.SetGeolocationEnabled(false);
            s.SaveFormData = false;
            s.SetSupportZoom(false);
            web.SetWebViewClient(new Client(this));
            web.SetWebChromeClient(new Chrome());
            web.AddJavascriptInterface(bridge, "fbsNative");
            SetContentView(web);
            // After SetContentView: the window has no decor view (nor insets controller) before it.
            Bars(settings.Theme == "light" ? false : settings.Theme == "dark" || SystemDark);
            web.LoadUrl(Page);

            receiver = new UsbReceiver(this);
            IntentFilter filter = new IntentFilter(Otg.PermissionAction);
            filter.AddAction(UsbManager.ActionUsbDeviceAttached);
            filter.AddAction(UsbManager.ActionUsbDeviceDetached);
            if (OperatingSystem.IsAndroidVersionAtLeast(33))
                RegisterReceiver(receiver, filter, ReceiverFlags.NotExported);
            else
                RegisterReceiver(receiver, filter);
        }

        protected override void OnDestroy()
        {
            try { UnregisterReceiver(receiver); } catch (Java.Lang.IllegalArgumentException) { }
            otg?.Close();
            base.OnDestroy();
        }

        protected override void OnNewIntent(Intent intent)
        {
            base.OnNewIntent(intent);
            if (intent?.Action == UsbManager.ActionUsbDeviceAttached)
                bridge.Event("otg", new System.Text.Json.Nodes.JsonObject { ["attached"] = true });
        }

        public override void OnConfigurationChanged(Configuration newConfig)
        {
            base.OnConfigurationChanged(newConfig);
            bridge.Event("system", new System.Text.Json.Nodes.JsonObject { ["dark"] = SystemDark });
        }

#pragma warning disable CA1422, CS0672
        public override void OnBackPressed()
        {
            web.EvaluateJavascript("window.fbsBack ? window.fbsBack() : false", new JsCallback(result =>
            {
                if (result != "true")
                    MoveTaskToBack(true);
            }));
        }
#pragma warning restore CA1422, CS0672

        // ---------------------------------------------------------------- for the bridge

        public void Js(string script) => RunOnUiThread(() => web?.EvaluateJavascript(script, null));

        public void KeepAwake(bool on) => RunOnUiThread(() =>
        {
            if (on) Window.AddFlags(WindowManagerFlags.KeepScreenOn);
            else Window.ClearFlags(WindowManagerFlags.KeepScreenOn);
        });

        /// <summary>Status and navigation bars in the page's colours (--bg and --rail in ui/style.css),
        /// so the page runs from the top of the screen to the bottom.</summary>
        public void Bars(bool dark) => RunOnUiThread(() =>
        {
            Color color = Color.ParseColor(dark ? "#2A2A2C" : "#FDF8FF");
            Window.SetStatusBarColor(color);
            Window.SetNavigationBarColor(Color.ParseColor(dark ? "#2A2A2C" : "#F1ECF4"));
            web?.SetBackgroundColor(color);
            if (OperatingSystem.IsAndroidVersionAtLeast(30))
            {
                int light = (int)(WindowInsetsControllerAppearance.LightStatusBars | WindowInsetsControllerAppearance.LightNavigationBars);
                Window.InsetsController?.SetSystemBarsAppearance(dark ? 0 : light, light);
            }
            else
            {
#pragma warning disable CA1422
                StatusBarVisibility flags = (StatusBarVisibility)(dark ? 0 : (int)(SystemUiFlags.LightStatusBar | SystemUiFlags.LightNavigationBar));
                Window.DecorView.SystemUiFlags = (SystemUiFlags)(int)flags;
#pragma warning restore CA1422
            }
        });

        public void Copy(string text) => RunOnUiThread(() =>
        {
            ClipboardManager clipboard = (ClipboardManager)GetSystemService(ClipboardService);
            clipboard.PrimaryClip = ClipData.NewPlainText("Fastboot Studio", text);
        });

        /// <summary>Opens a link in the browser: only the project's own pages.</summary>
        public void Open(string url)
        {
            if (url == null || !(url == "https://github.com/ifoknr" || url.StartsWith("https://github.com/ifoknr/", StringComparison.Ordinal) || url.StartsWith("https://t.me/", StringComparison.Ordinal)))
                throw new BridgeError("bad-link");
            RunOnUiThread(() =>
            {
                try { StartActivity(new Intent(Intent.ActionView, Android.Net.Uri.Parse(url))); }
                catch (ActivityNotFoundException) { }
            });
        }

        public Task<Android.Net.Uri> PickDocument()
        {
            Intent intent = new Intent(Intent.ActionOpenDocument);
            intent.AddCategory(Intent.CategoryOpenable);
            intent.SetType("*/*");
            return StartPick(intent, PickCode);
        }

        public Task<Android.Net.Uri> PickTree() => StartPick(new Intent(Intent.ActionOpenDocumentTree), TreeCode);

        Task<Android.Net.Uri> StartPick(Intent intent, int code)
        {
            TaskCompletionSource<Android.Net.Uri> tcs = new TaskCompletionSource<Android.Net.Uri>();
            RunOnUiThread(() =>
            {
                pick?.TrySetResult(null);
                pick = tcs;
                try { StartActivityForResult(intent, code); }
                catch (ActivityNotFoundException) { tcs.TrySetResult(null); }
            });
            return tcs.Task;
        }

        protected override void OnActivityResult(int requestCode, Result resultCode, Intent data)
        {
            base.OnActivityResult(requestCode, resultCode, data);
            if (requestCode == PickCode || requestCode == TreeCode)
                pick?.TrySetResult(resultCode == Result.Ok ? data?.Data : null);
        }

        public string DisplayName(Android.Net.Uri uri)
        {
            using (ICursor c = ContentResolver.Query(uri, new[] { IOpenableColumns.DisplayName }, null, null, null))
                return c != null && c.MoveToFirst() ? c.GetString(0) : null;
        }

        public bool CopyTo(Android.Net.Uri uri, string path)
        {
            try
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
                using (Stream input = ContentResolver.OpenInputStream(uri))
                using (FileStream output = File.Create(path))
                    input.CopyTo(output, 1 << 20);
                return true;
            }
            catch (Exception e)
            {
                Native.Log.Add("read " + uri + " failed: " + e.Message);
                return false;
            }
        }

        /// <summary>The files directly in a picked folder, by name.</summary>
        public Dictionary<string, Android.Net.Uri> ListTree(Android.Net.Uri tree, out string folderName)
        {
            Dictionary<string, Android.Net.Uri> files = new Dictionary<string, Android.Net.Uri>(StringComparer.Ordinal);
            string root = DocumentsContract.GetTreeDocumentId(tree);
            folderName = DisplayName(DocumentsContract.BuildDocumentUriUsingTree(tree, root)) ?? "backup";
            string[] columns = { DocumentsContract.Document.ColumnDocumentId, DocumentsContract.Document.ColumnDisplayName, DocumentsContract.Document.ColumnMimeType };
            using (ICursor c = ContentResolver.Query(DocumentsContract.BuildChildDocumentsUriUsingTree(tree, root), columns, null, null, null))
                while (c != null && c.MoveToNext())
                {
                    string name = c.GetString(1);
                    if (name == null || name.Contains('/') || c.GetString(2) == DocumentsContract.Document.MimeTypeDir)
                        continue;
                    files[name] = DocumentsContract.BuildDocumentUriUsingTree(tree, c.GetString(0));
                }
            return files;
        }

        // ---------------------------------------------------------------- helpers

        sealed class Client : WebViewClient
        {
            readonly MainActivity activity;

            public Client(MainActivity activity) { this.activity = activity; }

            /// <summary>The page never leaves the app's own assets; links go to the browser through Open.</summary>
            public override bool ShouldOverrideUrlLoading(WebView view, IWebResourceRequest request)
            {
                string url = request?.Url?.ToString() ?? "";
                if (!url.StartsWith("file:///android_asset/ui/", StringComparison.Ordinal))
                {
                    try { activity.Open(url); } catch (BridgeError) { }
                    return true;
                }
                return false;
            }
        }

        sealed class Chrome : WebChromeClient
        {
            public override bool OnConsoleMessage(ConsoleMessage message)
            {
                if (message.InvokeMessageLevel() == ConsoleMessage.MessageLevel.Error)
                    Native.Log.Add("page: " + message.Message() + " (" + message.SourceId() + ":" + message.LineNumber() + ")");
                return true;
            }
        }

        sealed class JsCallback : Java.Lang.Object, IValueCallback
        {
            readonly Action<string> done;

            public JsCallback(Action<string> done) { this.done = done; }

            public void OnReceiveValue(Java.Lang.Object value) => done(value?.ToString());
        }

        sealed class UsbReceiver : BroadcastReceiver
        {
            readonly MainActivity activity;

            public UsbReceiver(MainActivity activity) { this.activity = activity; }

            public override void OnReceive(Context context, Intent intent)
            {
                switch (intent?.Action)
                {
                    case Otg.PermissionAction:
                        activity.otg.PermissionAnswered(intent.GetBooleanExtra(UsbManager.ExtraPermissionGranted, false));
                        break;
                    case UsbManager.ActionUsbDeviceDetached:
                        UsbDevice gone = OperatingSystem.IsAndroidVersionAtLeast(33)
                            ? (UsbDevice)intent.GetParcelableExtra(UsbManager.ExtraDevice, Java.Lang.Class.FromType(typeof(UsbDevice)))
#pragma warning disable CA1422
                            : (UsbDevice)intent.GetParcelableExtra(UsbManager.ExtraDevice);
#pragma warning restore CA1422
                        activity.otg.Detached(gone);
                        activity.bridge.Event("otg", new System.Text.Json.Nodes.JsonObject { ["attached"] = false });
                        break;
                    case UsbManager.ActionUsbDeviceAttached:
                        activity.bridge.Event("otg", new System.Text.Json.Nodes.JsonObject { ["attached"] = true });
                        break;
                }
            }
        }
    }
}
