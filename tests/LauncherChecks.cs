using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using KebuzLoncher;
using Newtonsoft.Json;

internal static class LauncherChecks
{
    private static readonly Assembly Launcher = typeof(Form1).Assembly;
    private static readonly Type CacheType = Launcher.GetType("KebuzLoncher.ThumbnailCache", true);
    private static readonly BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private static int assertions;
    private static int exitCode;
    private static string directory;

    private sealed class QuietLauncher : Form1
    {
        protected override void OnShown(EventArgs e) { } // No real catalog/network requests.
    }

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr handle, int index);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr handle, out NativeRect rect);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr handle, int message, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }

    [STAThread]
    private static int Main(string[] args)
    {
        directory = Path.GetFullPath(args[0]);
        Directory.CreateDirectory(directory);
        using (var host = new Form { Opacity = 0, ShowInTaskbar = false })
        {
            host.Shown += async (s, e) =>
            {
                try
                {
                    CheckVersions();
                    CheckReleaseAssetsAndMigration();
                    await CheckFeeds();
                    await CheckImages();
                    await CheckWindowAndBanner();
                    Console.WriteLine("PASS: " + assertions + " assertions (separate feeds, versions, persistent dismissal, native frame, layout, preview cache).");
                }
                catch (Exception ex)
                {
                    exitCode = 1;
                    Console.WriteLine(ex.ToString());
                }
                finally { host.Close(); }
            };
            Application.Run(host);
        }
        return exitCode;
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        assertions++;
    }

    private static bool Notify(string next, string dismissed)
    {
        var type = Launcher.GetType("KebuzLoncher.LauncherVersion", true);
        return (bool)type.GetMethod("ShouldNotify").Invoke(null, new object[] { next, dismissed });
    }

    private static void CheckVersions()
    {
        Check(!Notify("1.1.9", null), "Older version must stay hidden");
        Check(!Notify("v1.2", null), "Normalized current version must stay hidden");
        Check(Notify("v1.3.0", null), "New stable version must notify");
        Check(!Notify("1.3.0", "v1.3"), "Persisted dismissal must normalize versions");
        Check(!Notify("1.3.0", "1.4.0"), "Feed rollback must stay hidden");
        Check(Notify("1.4.0", "1.3.0"), "Next version must notify again");
        Check(!Notify("1.3.0-beta", null), "Unsupported prerelease must stay hidden");
        Check(!Notify(null, null) && !Notify("broken", null), "Missing/invalid versions must stay hidden");
    }

    private static void CheckReleaseAssetsAndMigration()
    {
        var data = Launcher.GetType("KebuzLoncher.LauncherData", true);
        string expected = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Kebuz", "KebuzLoncher");
        Check((string)data.GetProperty("Root").GetValue(null) == expected, "Launcher storage must resolve the canonical per-user directory");
        var migrate = data.GetMethod("Migrate", BindingFlags.NonPublic | BindingFlags.Static);
        string migrationRoot = Path.Combine(directory, "migration-" + Guid.NewGuid().ToString("N"));
        string legacy = Path.Combine(migrationRoot, "legacy-data");
        string target = Path.Combine(migrationRoot, "migrated-data");
        Directory.CreateDirectory(Path.Combine(legacy, "backups", "fixture"));
        Directory.CreateDirectory(Path.Combine(legacy, "cache"));
        Directory.CreateDirectory(Path.Combine(target, "backups", "fixture"));
        string oldSettings = JsonConvert.SerializeObject(new LauncherSettings { GamePaths = new Dictionary<string, string> { ["fixture"] = "C:\\Games\\Fixture" }, InstalledVersions = new Dictionary<string, string> { ["fixture"] = "2.0" } });
        string newSettings = JsonConvert.SerializeObject(new LauncherSettings { Theme = "Dark" });
        File.WriteAllText(Path.Combine(legacy, "settings.json"), oldSettings);
        File.WriteAllText(Path.Combine(target, "settings.json"), newSettings);
        File.WriteAllText(Path.Combine(legacy, "cache", "games_config.cache"), "cached catalog");
        File.WriteAllText(Path.Combine(legacy, "backups", "fixture", "old.zip"), "old backup");
        File.WriteAllText(Path.Combine(legacy, "backups", "fixture", "existing.zip"), "legacy backup");
        File.WriteAllText(Path.Combine(target, "backups", "fixture", "existing.zip"), "new backup");
        File.WriteAllText(Path.Combine(legacy, "launcher.log"), "legacy log");
        migrate.Invoke(null, new object[] { legacy, target });
        Check(File.ReadAllText(Path.Combine(target, "settings.json")) == newSettings && File.ReadAllText(Path.Combine(target, "backups", "fixture", "existing.zip")) == "new backup",
            "Migration must preserve existing destination settings and backups");
        Check(File.ReadAllText(Path.Combine(legacy, "settings.json")) == oldSettings && File.Exists(Path.Combine(legacy, "backups", "fixture", "old.zip")),
            "Migration must preserve all original settings and backups");
        Check(File.ReadAllText(Path.Combine(target, "cache", "games_config.cache")) == "cached catalog" && File.ReadAllText(Path.Combine(target, "backups", "fixture", "old.zip")) == "old backup",
            "Missing offline caches and backups must migrate intact");
        Check(File.ReadAllText(Path.Combine(target, "logs", "launcher.log")) == "legacy log" && Directory.Exists(Path.Combine(target, "logs", "crashes")),
            "Logs and crash reports must use their designated directories");
        Check(File.Exists(Path.Combine(target, ".migration-v1")), "Migration completion must be recorded only after copying succeeds");
        File.WriteAllText(Path.Combine(legacy, "cache", "games_config.cache"), "later legacy data");
        migrate.Invoke(null, new object[] { legacy, target });
        Check(File.ReadAllText(Path.Combine(target, "cache", "games_config.cache")) == "cached catalog", "Repeated migration must not overwrite active destination data");
        string fresh = Path.Combine(migrationRoot, "fresh-migration");
        migrate.Invoke(null, new object[] { legacy, fresh });
        var imported = JsonConvert.DeserializeObject<LauncherSettings>(File.ReadAllText(Path.Combine(fresh, "settings.json")));
        Check(imported.GamePaths["fixture"] == "C:\\Games\\Fixture" && imported.InstalledVersions["fixture"] == "2.0", "A first migration must retain game paths and installed-version records");

        string broken = Path.Combine(migrationRoot, "migration-failure");
        Directory.CreateDirectory(broken);
        File.WriteAllText(Path.Combine(broken, "backups"), "blocked directory");
        bool failed = false;
        try { migrate.Invoke(null, new object[] { legacy, broken }); }
        catch (TargetInvocationException) { failed = true; }
        Check(failed && !File.Exists(Path.Combine(broken, ".migration-v1")) && File.ReadAllText(Path.Combine(legacy, "settings.json")) == oldSettings,
            "Interrupted migration must retain originals and remain retryable");
        File.Delete(Path.Combine(broken, "backups"));
        migrate.Invoke(null, new object[] { legacy, broken });
        Check(File.Exists(Path.Combine(broken, ".migration-v1")) && File.ReadAllText(Path.Combine(broken, "settings.json")) == oldSettings, "A failed migration must complete safely on retry");

        var legal = Launcher.GetType("KebuzLoncher.LegalNotices", true);
        string text = (string)legal.GetMethod("ReadText", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, null);
        Check(Launcher.GetManifestResourceNames().Count(name => name.StartsWith("KebuzLoncher.Licenses.")) == 5 && text.Contains("MIT License") && text.Contains("Newtonsoft") && text.Contains("Costura"),
            "The single EXE must embed launcher and dependency license notices");
        foreach (string theme in new[] { "Classic", "Dark" })
        using (var dialog = (Form)legal.GetMethod("Create").Invoke(null, new object[] { theme }))
            Check(dialog.Controls.OfType<TextBox>().Single().Text == text && dialog.CancelButton != null, "Embedded licenses must be accessible in both themes without external files");
    }

    private static async Task CheckFeeds()
    {
        var type = Launcher.GetType("KebuzLoncher.LauncherFeeds", true);
        string catalogUrl = (string)type.GetField("CatalogUrl", BindingFlags.Static | BindingFlags.NonPublic).GetRawConstantValue();
        string manifestUrl = (string)type.GetField("ManifestUrl", BindingFlags.Static | BindingFlags.NonPublic).GetRawConstantValue();
        Check(catalogUrl == "https://raw.githubusercontent.com/Kebuzya/KebuzLoncher/catalog/config.json" &&
            manifestUrl == "https://raw.githubusercontent.com/Kebuzya/KebuzLoncher/catalog/manifest.json", "Both live feeds must belong to the catalog branch");
        string folder = Path.Combine(directory, "feeds-" + Guid.NewGuid().ToString("N"));
        string catalogJson = JsonConvert.SerializeObject(new GameConfig { games = new[] { new Game { id = "letcomp", mod_pack_version = "2.0", mod_pack_status = "outdated", mod_pack_warning = "Awaiting compatibility testing." } } });
        string manifestJson = JsonConvert.SerializeObject(new LauncherManifest { launcher_version = "1.3.0", launcher_download_url = "https://example.test/launcher" });
        Func<object, Task<GameConfig>> loadCatalog = feed => (Task<GameConfig>)type.GetMethod("LoadCatalogAsync").Invoke(feed, new object[] { CancellationToken.None });
        Func<object, Task<LauncherManifest>> loadManifest = feed => (Task<LauncherManifest>)type.GetMethod("LoadManifestAsync").Invoke(feed, new object[] { CancellationToken.None });
        var requested = new List<string>();
        using (var client = new HttpClient(new FeedHandler((request, token) =>
        {
            string url = request.RequestUri.AbsoluteUri;
            requested.Add(url);
            return Task.FromResult(JsonResponse(url == catalogUrl ? catalogJson : url == manifestUrl ? manifestJson : null));
        })))
        {
            var feed = Activator.CreateInstance(type, client, folder);
            var catalog = loadCatalog(feed);
            var manifest = loadManifest(feed);
            await Task.WhenAll(catalog, manifest);
            Check(catalog.Result.games[0].id == "letcomp" && manifest.Result.launcher_version == "1.3.0", "Catalog and launcher manifest must use independent primary feeds");
            Check(requested.Count == 2 && requested.All(url => url == catalogUrl || url == manifestUrl), "Only the two catalog branch feeds must be queried");
        }

        using (var client = new HttpClient(new FeedHandler((request, token) => Task.FromResult(JsonResponse(null)))))
        {
            var feed = Activator.CreateInstance(type, client, folder);
            var cachedGame = (await loadCatalog(feed)).games[0];
            Check(cachedGame.id == "letcomp", "Offline catalog must use its own cache");
            Check(cachedGame.mod_pack_status == "outdated" && cachedGame.mod_pack_warning == "Awaiting compatibility testing.", "Offline catalog caching must preserve compatibility warnings");
            Check((await loadManifest(feed)).launcher_version == "1.3.0", "Offline launcher updates must use their own cache");
        }

        requested.Clear();
        using (var client = new HttpClient(new FeedHandler((request, token) =>
        {
            requested.Add(request.RequestUri.AbsoluteUri);
            return Task.FromResult(JsonResponse("broken JSON"));
        })))
        {
            var feed = Activator.CreateInstance(type, client, folder);
            Check((await loadCatalog(feed)).games[0].id == "letcomp", "Invalid remote catalogs must preserve the last valid cached catalog");
            Check((await loadManifest(feed)).launcher_version == "1.3.0", "Invalid remote manifests must preserve the last valid cached version");
            Check(requested.Count == 2 && requested.All(url => url == catalogUrl || url == manifestUrl), "Feed failures must not query obsolete main branch files");
        }

        var gate = new TaskCompletionSource<bool>();
        using (var client = new HttpClient(new FeedHandler(async (request, token) =>
        {
            if (request.RequestUri.AbsoluteUri == catalogUrl) await gate.Task;
            return JsonResponse(request.RequestUri.AbsoluteUri == manifestUrl ? manifestJson : catalogJson);
        })))
        {
            var feed = Activator.CreateInstance(type, client, folder);
            var blocked = loadCatalog(feed);
            try
            {
                var manifest = await loadManifest(feed);
                Check(manifest.launcher_version == "1.3.0" && !blocked.IsCompleted, "Slow catalog downloads must not block launcher update checks");
            }
            finally { gate.TrySetResult(true); }
            await blocked;
        }

        using (var client = new HttpClient(new FeedHandler(async (request, token) =>
        {
            if (request.RequestUri.AbsoluteUri == manifestUrl) await gate.Task;
            return JsonResponse(request.RequestUri.AbsoluteUri == catalogUrl ? catalogJson : manifestJson);
        })))
        {
            gate = new TaskCompletionSource<bool>();
            var feed = Activator.CreateInstance(type, client, folder);
            var blocked = loadManifest(feed);
            try
            {
                var catalog = await loadCatalog(feed);
                Check(catalog.games[0].id == "letcomp" && !blocked.IsCompleted, "Slow update checks must not block the game catalog");
            }
            finally { gate.TrySetResult(true); }
            await blocked;
        }

        using (var cancellation = new CancellationTokenSource())
        using (var client = new HttpClient(new FeedHandler(async (request, token) =>
        {
            await Task.Delay(Timeout.Infinite, token);
            return JsonResponse(null);
        })))
        {
            var feed = Activator.CreateInstance(type, client, folder);
            var pending = (Task<GameConfig>)type.GetMethod("LoadCatalogAsync").Invoke(feed, new object[] { cancellation.Token });
            cancellation.Cancel();
            bool cancelled = false;
            try { await pending; }
            catch (OperationCanceledException) { cancelled = true; }
            Check(cancelled, "Closing the launcher must cancel feed requests rather than start fallback downloads");
        }
    }

    private static HttpResponseMessage JsonResponse(string json) => new HttpResponseMessage(json == null ? HttpStatusCode.NotFound : HttpStatusCode.OK)
    {
        Content = new StringContent(json ?? "", System.Text.Encoding.UTF8, "application/json")
    };

    private sealed class FeedHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handle;
        public FeedHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handle) { this.handle = handle; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => handle(request, token);
    }

    private static object NewCache(HttpClient client, string folder) => Activator.CreateInstance(CacheType, client, folder);
    private static Task<Image> GetImage(object cache, string url) => (Task<Image>)CacheType.GetMethod("GetAsync").Invoke(cache, new object[] { url });

    private static async Task CheckImages()
    {
        string folder = Path.Combine(directory, "previews-" + Guid.NewGuid().ToString("N"));
        var handler = new ImageHandler();
        using (var client = new HttpClient(handler))
        using (var cache = (IDisposable)NewCache(client, folder))
        {
            var first = GetImage(cache, "https://example.test/0");
            Check(ReferenceEquals(first, GetImage(cache, "https://example.test/0")), "Duplicate URL must share a request");
            var tasks = Enumerable.Range(1, 7).Select(i => GetImage(cache, "https://example.test/" + i)).Concat(new[] { first }).ToArray();
            var images = await Task.WhenAll(tasks);
            Check(handler.Requests == 8 && handler.MaximumConcurrency == 4, "Downloads must be parallel and bounded by four");
            Check(images.All(i => i != null && i.Width <= 460 && i.Height <= 220), "Decoded images must survive stream disposal and be downscaled");
            Check(Directory.GetFiles(folder, "*.png").Length == 8, "Successful previews must persist");
            Check(await GetImage(cache, "file:///secret") == null, "Non-HTTP image URLs must be rejected");
        }

        var offline = new ImageHandler { Fail = true };
        using (var client = new HttpClient(offline))
        using (var cache = (IDisposable)NewCache(client, folder))
        {
            Check(await GetImage(cache, "https://example.test/0") != null && offline.Requests == 0, "A fresh cache instance must load previews entirely from disk offline");
        }

        foreach (string file in Directory.GetFiles(folder, "*.png")) File.WriteAllText(file, "damaged cache");
        var retry = new ImageHandler();
        using (var client = new HttpClient(retry))
        using (var cache = (IDisposable)NewCache(client, folder))
        {
            Check(await GetImage(cache, "https://example.test/0") != null && retry.Requests == 1, "A damaged cached image must be downloaded again");
        }

        using (var client = new HttpClient(new ImageHandler { Delay = 1000 }))
        {
            var cache = (IDisposable)NewCache(client, Path.Combine(folder, "cancelled"));
            var pending = GetImage(cache, "https://example.test/cancel");
            cache.Dispose();
            Check(await pending == null, "Closing/clearing during a download must cancel without returning a disposed image");
        }

        var transient = new ImageHandler { Fail = true };
        using (var client = new HttpClient(transient))
        using (var cache = (IDisposable)NewCache(client, Path.Combine(folder, "transient")))
        {
            Check(await GetImage(cache, "https://example.test/retry") == null, "Network failure must be nonfatal");
            transient.Fail = false;
            Check(await GetImage(cache, "https://example.test/retry") != null, "Refreshing after a transient failure must retry the image");
        }
    }

    private static object Field(object target, string name) => typeof(Form1).GetField(name, PrivateInstance).GetValue(target);
    private static void SetField(object target, string name, object value) => typeof(Form1).GetField(name, PrivateInstance).SetValue(target, value);
    private static object Call(object target, string name, params object[] args) => typeof(Form1).GetMethod(name, PrivateInstance | BindingFlags.Static).Invoke(target, args);

    private static async Task CheckWindowAndBanner()
    {
        using (var form = new QuietLauncher { Opacity = 0, ShowInTaskbar = false })
        {
            var settings = new LauncherSettings();
            SetField(form, "settings", settings);
            var manager = (SettingsManager)Field(form, "settingsManager");
            Check((string)typeof(SafeModManager).GetField("backupsDir", PrivateInstance).GetValue(Field(form, "modManager")) == manager.GetBackupsDir(),
                "Mod installation and backup management must use the same migrated data directory");
            foreach (string name in new[] { "appDataDir", "cacheDir", "backupsDir" })
                typeof(SettingsManager).GetField(name, PrivateInstance).SetValue(manager, directory);
            typeof(SettingsManager).GetField("settingsPath", PrivateInstance).SetValue(manager, Path.Combine(directory, "settings.json"));
            form.Show();
            var main = (Control)Field(form, "mainPanel");
            var title = (Control)Field(form, "titleBar");
            var menu = (Control)Field(form, "menuStrip");
            var games = (Control)Field(form, "gamesPanel");
            var gameArea = (Control)Field(form, "gamesHost");
            var banner = (Control)Field(form, "_updateBanner");
            Check(main.Top == title.Bottom && menu.Top == 0, "Title and menu must meet without artificial padding");
            Check(gameArea.Top >= menu.Bottom, "Menu must not overlap the game list");
            GetWindowRect(form.Handle, out var bounds);
            var clientOrigin = form.PointToScreen(Point.Empty);
            Check(clientOrigin.Y == bounds.Top, "Native top strip must be removed");
            CheckNonClientRectangle(form, false);
            int style = GetWindowLong(form.Handle, -16);
            Check((style & 0x00CF0000) == 0x00CF0000, "Caption, resize frame, minimize/maximize and system-menu styles must remain");

            SetField(form, "launcherManifest", new LauncherManifest { launcher_version = "v1.3.0", launcher_download_url = "https://example.test/release" });
            Call(form, "CheckLauncherUpdate");
            Check(banner.Visible && banner.Top >= menu.Bottom && gameArea.Top >= banner.Bottom,
                "Update banner must fit between menu and games: visible=" + banner.Visible + ", menu=" + menu.Bounds + ", banner=" + banner.Bounds + ", games=" + gameArea.Bounds);
            var close = banner.Controls.OfType<Button>().Single(b => b.Text == "✕");
            close.PerformClick();
            Check(!banner.Visible && File.Exists(Path.Combine(directory, "settings.json")), "Dismissal must hide the banner and save settings");
            settings = manager.LoadSettings();
            SetField(form, "settings", settings);
            Call(form, "CheckLauncherUpdate");
            Check(!banner.Visible, "Reloaded settings must keep the same notification hidden");
            ((LauncherManifest)Field(form, "launcherManifest")).launcher_version = "1.4.0";
            Call(form, "CheckLauncherUpdate");
            Check(banner.Visible, "A newer release must show a fresh notification");
            ((LauncherManifest)Field(form, "launcherManifest")).launcher_download_url = "file:///program.exe";
            Call(form, "CheckLauncherUpdate");
            Check(!banner.Visible, "Invalid download URL must not produce an unusable notification");

            form.WindowState = FormWindowState.Maximized;
            Application.DoEvents();
            var work = Screen.FromHandle(form.Handle).WorkingArea;
            var clientBounds = form.RectangleToScreen(form.ClientRectangle);
            Check(work.Contains(clientBounds), "Maximized content must stay inside the monitor work area");
            form.WindowState = FormWindowState.Normal;
            Application.DoEvents();
            SendMessage(form.Handle, 0x0112, (IntPtr)0xF030, IntPtr.Zero);
            Application.DoEvents();
            work = Screen.FromHandle(form.Handle).WorkingArea;
            clientBounds = form.RectangleToScreen(form.ClientRectangle);
            Check(clientBounds.Top == work.Top, "Native maximize must keep the complete title bar below the monitor work-area top: client=" + clientBounds + ", work=" + work);
            Check(work.Contains(title.RectangleToScreen(title.ClientRectangle)), "Native maximize must keep the entire title bar onscreen");
            foreach (var button in title.Controls.Cast<Control>().Where(control => control.GetType().Name == "Win98Button"))
                Check(work.Contains(button.RectangleToScreen(button.ClientRectangle)), "Maximized caption buttons must be fully visible");
            CheckNonClientRectangle(form, true);
            SendMessage(form.Handle, 0x0112, (IntPtr)0xF120, IntPtr.Zero);
            Application.DoEvents();
            GetWindowRect(form.Handle, out bounds);
            Check(form.PointToScreen(Point.Empty).Y == bounds.Top, "Native top strip must stay absent after restore");

            ((IDisposable)Field(form, "imageCache")).Dispose();
            using (var client = new HttpClient(new ImageHandler()))
            {
                SetField(form, "imageCache", NewCache(client, Path.Combine(directory, "form-previews")));
                using (var picture = new PictureBox())
                {
                    await (Task)Call(form, "LoadImageAsync", picture, "https://example.test/no-handle");
                    Check(!picture.IsHandleCreated && picture.Image != null, "Preview must populate a PictureBox before handle creation");
                }

                SetField(form, "config", new GameConfig
                {
                    games = Enumerable.Range(0, 4).Select(i => new Game
                    {
                        id = "fixture" + i, name = new[] { "R.E.P.O.", "Lethal Company", "PEAK", "Content Warning" }[i],
                        description = "Preview fixture", image_url = "https://example.test/no-handle",
                        mod_pack_version = "1.0.0", steam_app_id = 1,
                        mod_pack_status = i == 0 ? "outdated" : i == 1 ? "broken" : i == 2 ? "unverified" : null,
                        mod_pack_warning = i == 0 ? "May not work after the latest game update." : null
                    }).ToArray()
                });
                SetField(form, "launcherManifest", new LauncherManifest { launcher_version = "1.5.0", launcher_download_url = "https://example.test/release" });
                var originalCulture = Thread.CurrentThread.CurrentUICulture;
                try
                {
                    Thread.CurrentThread.CurrentUICulture = System.Globalization.CultureInfo.GetCultureInfo("ru-RU");
                    Check((string)Call(form, "UpdateText", "DownloadUpdate") == "\u0421\u043a\u0430\u0447\u0430\u0442\u044c", "Russian update resources must be embedded in the EXE");
                    Thread.CurrentThread.CurrentUICulture = System.Globalization.CultureInfo.GetCultureInfo("en-US");
                    Check((string)Call(form, "UpdateText", "DownloadUpdate") == "Download", "English update resources must be embedded in the EXE");
                }
                finally { Thread.CurrentThread.CurrentUICulture = originalCulture; }
                settings.Theme = "Classic";
                form.ClientSize = new Size(940, 650);
                Call(form, "DisplayGames");
                Check(games.Controls.Count == 4, "The catalog must contain only game tiles, without a visible spacer panel");
                CheckScrolling(form, (FlowLayoutPanel)games, gameArea);
                CheckTheme(form, gameArea, false);
                CheckModpackNotices(form, false);
                CaptureWindow(form, "preview-classic.png");
                CaptureMenu(form, "preview-classic-menu.png");
                SendMessage(form.Handle, 0x0112, (IntPtr)0xF030, IntPtr.Zero);
                Application.DoEvents();
                Check(Screen.FromHandle(form.Handle).WorkingArea.Contains(title.RectangleToScreen(title.ClientRectangle)), "Classic title bar must remain fully visible after catalog layout");
                CaptureWindow(form, "preview-classic-maximized.png");
                SendMessage(form.Handle, 0x0112, (IntPtr)0xF120, IntPtr.Zero);
                var themedScrollbar = (Control)gameArea.GetType().GetProperty("ScrollBar").GetValue(gameArea);
                var themedValue = themedScrollbar.GetType().GetProperty("Value");
                themedValue.SetValue(themedScrollbar, 100);
                int beforeThemeChange = (int)themedValue.GetValue(themedScrollbar);
                Call(form, "SwitchTheme", "Dark");
                Application.DoEvents();
                Check((int)themedValue.GetValue(themedScrollbar) == beforeThemeChange, "Switching themes must preserve the scroll position");
                themedValue.SetValue(themedScrollbar, 0);
                Check(games.Controls.Count == 4, "Switching themes must not introduce a spacer panel");
                CheckTheme(form, gameArea, true);
                CheckModpackNotices(form, true);
                CaptureWindow(form, "preview-dark.png");
                CaptureMenu(form, "preview-dark-menu.png");
                SendMessage(form.Handle, 0x0112, (IntPtr)0xF030, IntPtr.Zero);
                Application.DoEvents();
                Check(Screen.FromHandle(form.Handle).WorkingArea.Contains(title.RectangleToScreen(title.ClientRectangle)), "Dark title bar must remain fully visible after catalog layout");
                CaptureWindow(form, "preview-dark-maximized.png");
                SendMessage(form.Handle, 0x0112, (IntPtr)0xF120, IntPtr.Zero);
                ((GameConfig)Field(form, "config")).games[0].mod_pack_status = null;
                Call(form, "DisplayGames");
                Check(!games.Controls[0].Controls.Cast<Control>().Any(control => control.Name == "modpackWarning") && games.Controls[0].Height == 374,
                    "Clearing a modpack status must remove its warning and restore the compact card layout");
            }
            form.Close();
        }
    }

    private static void CheckScrolling(Form form, FlowLayoutPanel games, Control host)
    {
        var scrollbar = (Control)host.GetType().GetProperty("ScrollBar").GetValue(host);
        var valueProperty = scrollbar.GetType().GetProperty("Value");
        Func<int> value = () => (int)valueProperty.GetValue(scrollbar);
        Action<int> setValue = next => valueProperty.SetValue(scrollbar, next);
        Func<int> maximum = () => (int)scrollbar.GetType().GetProperty("Maximum").GetValue(scrollbar);
        Action<string, object> input = (name, args) => scrollbar.GetType().GetMethod(name, PrivateInstance).Invoke(scrollbar, new[] { args });
        Application.DoEvents();
        Check(scrollbar.Visible && maximum() > 0, "Overflowing game cards must show the custom scrollbar");
        Check(games.PointToScreen(new Point(games.ClientSize.Width, 0)).X >= scrollbar.PointToScreen(Point.Empty).X,
            "The native scrollbar must be clipped outside the viewport");
        setValue(0);
        input("OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, scrollbar.Width / 2, scrollbar.Height - scrollbar.Width / 2, 0));
        input("OnMouseUp", new MouseEventArgs(MouseButtons.Left, 1, scrollbar.Width / 2, scrollbar.Height - scrollbar.Width / 2, 0));
        Check(value() > 0 && -games.AutoScrollPosition.Y == value(), "Scrollbar arrow must move the native content and stay synchronized");
        input("OnKeyDown", new KeyEventArgs(Keys.End));
        Check(value() == maximum() && -games.AutoScrollPosition.Y == maximum(), "End must reach the last game row");
        input("OnMouseWheel", new MouseEventArgs(MouseButtons.None, 0, 0, 0, 120));
        if (SystemInformation.MouseWheelScrollLines != 0)
            Check(value() < maximum(), "Mouse wheel over the custom scrollbar must scroll upward");
        setValue(0);
        var thumb = (Rectangle)scrollbar.GetType().GetProperty("Thumb", PrivateInstance).GetValue(scrollbar);
        input("OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, thumb.Width / 2, thumb.Top + thumb.Height / 2, 0));
        input("OnMouseMove", new MouseEventArgs(MouseButtons.Left, 0, thumb.Width / 2, scrollbar.Height, 0));
        input("OnMouseUp", new MouseEventArgs(MouseButtons.Left, 1, thumb.Width / 2, scrollbar.Height, 0));
        Check(value() == maximum() && -games.AutoScrollPosition.Y == maximum(), "Dragging the thumb must reach the bottom without losing capture");
        setValue(0);
        games.GetType().GetMethod("OnMouseWheel", PrivateInstance).Invoke(games, new object[] { new MouseEventArgs(MouseButtons.None, 0, 0, 0, -120) });
        if (SystemInformation.MouseWheelScrollLines != 0)
            Check(value() > 0 && value() == -games.AutoScrollPosition.Y, "Wheel over the game list must update the custom thumb");
        form.ClientSize = new Size(1280, 680);
        Application.DoEvents();
        Check(!scrollbar.Visible && maximum() == 0, "Scrollbar must disappear when all cards fit");
        form.ClientSize = new Size(720, 540);
        Application.DoEvents();
        Check(scrollbar.Visible && maximum() > 0, "Scrollbar must return after narrowing the window");
        input("OnKeyDown", new KeyEventArgs(Keys.End));
        var viewport = games.Parent.RectangleToScreen(games.Parent.ClientRectangle);
        var last = games.Controls[games.Controls.Count - 1];
        Check(last.RectangleToScreen(last.ClientRectangle).Bottom <= viewport.Bottom,
            "The last game's actions must remain reachable after resizing: last=" + last.Bounds + ", viewport=" + viewport + ", scroll=" + games.AutoScrollPosition + ", maximum=" + maximum() + ", display=" + games.DisplayRectangle + ", client=" + games.ClientSize + ", nativeMax=" + games.VerticalScroll.Maximum + ", page=" + games.VerticalScroll.LargeChange + ", padding=" + games.Padding);
        setValue(0);
        games.ScrollControlIntoView(last);
        Application.DoEvents();
        Check(value() == -games.AutoScrollPosition.Y, "Scrolling a focused card into view must keep the custom thumb synchronized");
        form.ClientSize = new Size(940, 650);
        setValue(0);
    }

    private static void CheckTheme(Form form, Control host, bool dark)
    {
        var scrollbar = (Control)host.GetType().GetProperty("ScrollBar").GetValue(host);
        var title = (Control)Field(form, "titleBar");
        var face = dark ? Color.FromArgb(70, 70, 74) : Color.Silver;
        Check(scrollbar.BackColor == face, "Scrollbar must use the active retro palette");
        Check(title.Controls.Cast<Control>().Where(control => control.GetType().Name == "Win98Button").All(button => button.BackColor == face),
            "Caption buttons and scrollbar must use the same palette");
        var banner = (Control)Field(form, "_updateBanner");
        Check(banner.Controls.OfType<Button>().All(button => button.BackColor == face), "Update buttons must follow the current theme");
        Check(((MenuStrip)Field(form, "menuStrip")).Renderer.GetType().Name == "RetroMenuRenderer", "The menu must use the retro renderer in both themes");
        var games = (Control)Field(form, "gamesPanel");
        var play = games.Controls[0].Controls.OfType<Button>().First(button => button.ContextMenuStrip != null);
        Check(play.ContextMenuStrip.Renderer.GetType().Name == "RetroMenuRenderer", "The play menu must follow the same retro theme");
    }

    private static void CheckModpackNotices(Form form, bool dark)
    {
        var games = (Control)Field(form, "gamesPanel");
        var config = (GameConfig)Field(form, "config");
        var tooltips = (ToolTip)Field(form, "modpackWarnings");
        for (int index = 0; index < 3; index++)
        {
            var tile = games.Controls[index];
            var notice = tile.Controls.Cast<Control>().Single(control => control.Name == "modpackWarning");
            Check(notice.Visible && notice.BackColor != tile.BackColor, "A flagged modpack must show a separate compatibility notice in both themes");
            Check(tile.Controls.OfType<Button>().All(button => button.Enabled && button.Top >= notice.Bottom), "Compatibility warnings must not block installation or overlap game actions");
            var title = notice.Controls.Cast<Control>().Single();
            Check(title.Name == "warningTitle" && tooltips.GetToolTip(title) == tooltips.GetToolTip(notice) && tooltips.GetToolTip(title).Length > 0,
                "A compact warning must show only its title and expose the full explanation on hover");
            Check(tile.Height == games.Controls[3].Height && tile.Controls.OfType<Button>().Min(button => button.Top) == 246,
                "Compatibility warnings must preserve the original tile height and action positions");
        }
        var first = games.Controls[0].Controls.Cast<Control>().Single(control => control.Name == "modpackWarning");
        Check(tooltips.GetToolTip(first).Contains(config.games[0].mod_pack_warning), "Custom publisher explanations must appear unchanged in launcher notices");
        Check(!games.Controls[3].Controls.Cast<Control>().Any(control => control.Name == "modpackWarning"), "Unmarked legacy entries must retain their original appearance");
        var broken = games.Controls[1].Controls.Cast<Control>().Single(control => control.Name == "modpackWarning");
        Check(broken.BackColor != first.BackColor, "Known issues must have a stronger visual state than an outdated warning");
    }

    private static void CheckNonClientRectangle(Form form, bool maximized)
    {
        GetWindowRect(form.Handle, out var window);
        IntPtr pointer = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(NativeRect)));
        try
        {
            Marshal.StructureToPtr(window, pointer, false);
            SendMessage(form.Handle, 0x0083, IntPtr.Zero, pointer);
            var client = (NativeRect)Marshal.PtrToStructure(pointer, typeof(NativeRect));
            int expected = maximized ? Screen.FromHandle(form.Handle).WorkingArea.Top : window.Top;
            Check(client.Top == expected, "WM_NCCALCSIZE without preservation data must calculate the custom caption top correctly");
        }
        finally { Marshal.FreeHGlobal(pointer); }
    }

    private static void CaptureMenu(Form form, string filename)
    {
        if (Environment.GetEnvironmentVariable("KEBUZ_CAPTURE_UI") != "1") return;
        var settings = (LauncherSettings)Field(form, "settings");
        Call(form, "UpdateThemeCheckmarks", settings.Theme);
        form.Opacity = 1;
        var menu = (ToolStripMenuItem)((MenuStrip)Field(form, "menuStrip")).Items[1];
        menu.ShowDropDown();
        menu.DropDownItems[0].Select();
        try { CaptureWindow(form, filename); }
        finally { menu.HideDropDown(); }
    }

    private static void CaptureWindow(Form form, string filename)
    {
        // Optional desktop capture: WM_PRINT used by DrawToBitmap can paint a
        // fictitious native caption even when WM_NCCALCSIZE removed it onscreen.
        if (Environment.GetEnvironmentVariable("KEBUZ_CAPTURE_UI") != "1") return;
        form.TopMost = true;
        form.Opacity = 1;
        form.Refresh();
        Application.DoEvents();
        var bounds = Rectangle.Intersect(form.Bounds, Screen.FromHandle(form.Handle).WorkingArea);
        using (var screenshot = new Bitmap(bounds.Width, bounds.Height))
        using (var graphics = Graphics.FromImage(screenshot))
        {
            graphics.CopyFromScreen(bounds.Location, Point.Empty, screenshot.Size);
            screenshot.Save(Path.Combine(directory, filename));
        }
        form.Opacity = 0;
    }

    private sealed class ImageHandler : HttpMessageHandler
    {
        public int Requests, MaximumConcurrency, Delay = 40;
        public bool Fail;
        private int active;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Requests++;
            MaximumConcurrency = Math.Max(MaximumConcurrency, ++active);
            try
            {
                await Task.Delay(Delay, token);
                if (Fail) throw new HttpRequestException("Offline fixture");
                using (var bitmap = new Bitmap(920, 440))
                using (var graphics = Graphics.FromImage(bitmap))
                using (var bytes = new MemoryStream())
                {
                    graphics.Clear(Color.CornflowerBlue);
                    bitmap.Save(bytes, System.Drawing.Imaging.ImageFormat.Png);
                    return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes.ToArray()) };
                }
            }
            finally { active--; }
        }
    }
}
