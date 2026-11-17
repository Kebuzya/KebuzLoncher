using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Resources;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace KebuzLoncher
{
    public partial class Form1 : AeroForm
    {
        private MenuStrip menuStrip;
        private ToolStripMenuItem viewMenu;
        private ToolStripMenuItem themeDarkItem;
        private ToolStripMenuItem themeClassicItem;
        private FlowLayoutPanel gamesPanel;
        private RetroScrollHost gamesHost;
        private StatusStrip statusStrip;
        private ToolStripStatusLabel statusLabel;
        private ToolStripProgressBar progressBar;

        private GameConfig config;
        private LauncherManifest launcherManifest;
        private LauncherFeeds feeds;
        private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        private LauncherSettings settings;
        private SafeModManager modManager;
        private SettingsManager settingsManager;

        private ThumbnailCache imageCache;
        private static readonly HttpClient httpClient = CreateSafeHttpClient();
        private static readonly ResourceManager russianResources = new ResourceManager("KebuzLoncher.Properties.Resources.ru", typeof(Form1).Assembly);

        private readonly List<(Label badge, Game game)> updateBadges = new List<(Label, Game)>();
        private readonly List<(Label versionLabel, Game game)> versionLabels = new List<(Label, Game)>();
        private readonly ToolTip modpackWarnings = new ToolTip { AutoPopDelay = 20000, InitialDelay = 300, ReshowDelay = 100 };

        private Panel mainPanel;
        private Win98TitleBar titleBar;
        private Panel _updateBanner;
        private Label _updateBannerLabel;

        public Form1()
        {
            InitializeComponent();
            InitializeManagers();
            imageCache = new ThumbnailCache(httpClient, Path.Combine(settingsManager.GetCacheDir(), "thumbnails"));
            feeds = new LauncherFeeds(httpClient, settingsManager.GetCacheDir());

            this.DoubleBuffered = true;
            this.MinimumSize = new Size(720, 540);

            try
            {
                this.Icon = System.Drawing.Icon.ExtractAssociatedIcon(
                    System.Reflection.Assembly.GetExecutingAssembly().Location);
            }
            catch { }

            titleBar = Win98TitleBar.AttachToForm(this);

            mainPanel = new Panel();
            mainPanel.Dock = DockStyle.Fill;
            mainPanel.Padding = Padding.Empty;
            mainPanel.BackColor = Color.FromArgb(45, 45, 48);

            this.Controls.Remove(menuStrip);
            this.Controls.Remove(gamesHost);
            this.Controls.Remove(statusStrip);

            mainPanel.Controls.Add(menuStrip);
            mainPanel.Controls.Add(gamesHost);
            mainPanel.Controls.Add(statusStrip);

            this.Controls.Add(mainPanel);
            mainPanel.BringToFront();

            menuStrip.Dock = DockStyle.Top;
            menuStrip.Padding = new Padding(0, 0, 0, 0);

            CreateUpdateBanner();
            mainPanel.Controls.SetChildIndex(gamesHost, 0);
            mainPanel.Controls.SetChildIndex(_updateBanner, 1);
            mainPanel.Controls.SetChildIndex(menuStrip, mainPanel.Controls.Count - 1);
            ThemeManager.Apply(this, settings.Theme);
            ApplyUpdateBannerTheme();

            this.Shown += async (s, e) =>
            {
                await Task.WhenAll(LoadGamesList(), RefreshLauncherUpdateAsync());
            };
        }

        private static HttpClient CreateSafeHttpClient()
        {
            System.Net.ServicePointManager.DefaultConnectionLimit = Math.Max(8, System.Net.ServicePointManager.DefaultConnectionLimit);
            var client = new HttpClient();
            client.DefaultRequestHeaders.Add("User-Agent", "KebuzGameLauncher/" + LauncherVersion.Display);
            client.Timeout = TimeSpan.FromSeconds(30);
            return client;
        }

        private void InitializeComponent()
        {
            this.Text = "Kebuz Launcher " + LauncherVersion.Display;
            this.Size = new Size(940, 760);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.BackColor = Color.FromArgb(45, 45, 48);
            this.ForeColor = Color.White;
            this.FormClosed += Form1_FormClosed;

            menuStrip = new MenuStrip();
            menuStrip.Name = "menuStrip";

            var toolsMenu = new ToolStripMenuItem("Инструменты");
            var openBackupsItem = new ToolStripMenuItem("Открыть папку бекапов");
            openBackupsItem.Click += (s, e) =>
                Process.Start("explorer.exe", settingsManager.GetBackupsDir());

            var openSettingsItem = new ToolStripMenuItem("Открыть папку настроек");
            openSettingsItem.Click += (s, e) =>
                Process.Start("explorer.exe",
                    LauncherData.Root);

            var manageBackupsItem = new ToolStripMenuItem("Управление бекапами...");
            manageBackupsItem.Click += (s, e) =>
                new BackupsForm(settingsManager.GetBackupsDir()).ShowDialog(this);

            var clearCacheItem = new ToolStripMenuItem("Очистить кэш изображений");
            clearCacheItem.Click += (s, e) => ClearImageCache();

            var refreshItem = new ToolStripMenuItem("Обновить список игр");
            refreshItem.Click += async (s, e) =>
            {
                refreshItem.Enabled = false;
                try
                {
                    await Task.WhenAll(LoadGamesList(), RefreshLauncherUpdateAsync());
                }
                finally { refreshItem.Enabled = true; }
            };

            toolsMenu.DropDownItems.AddRange(new ToolStripItem[] {
            openBackupsItem, openSettingsItem, new ToolStripSeparator(),
            manageBackupsItem, new ToolStripSeparator(),
            clearCacheItem, refreshItem
        });

            viewMenu = new ToolStripMenuItem("Вид");
            themeDarkItem = new ToolStripMenuItem("Тёмная тема");
            themeClassicItem = new ToolStripMenuItem("Классическая тема");
            themeDarkItem.Click += (s, e) => SwitchTheme("Dark");
            themeClassicItem.Click += (s, e) => SwitchTheme("Classic");
            viewMenu.DropDownItems.Add(themeDarkItem);
            viewMenu.DropDownItems.Add(themeClassicItem);

            menuStrip.Items.Add(toolsMenu);
            menuStrip.Items.Add(viewMenu);
            var legalItem = new ToolStripMenuItem(UpdateText("LegalNoticesTitle"));
            legalItem.Click += (s, e) =>
            {
                using (var dialog = LegalNotices.Create(settings.Theme)) dialog.ShowDialog(this);
            };
            menuStrip.Items.Add(legalItem);
            menuStrip.Dock = DockStyle.Top;

            // Панель игр
            gamesPanel = new FlowLayoutPanel();
            gamesPanel.Name = "gamesPanel";
            gamesPanel.Dock = DockStyle.Fill;
            gamesPanel.AutoScroll = true;
            gamesPanel.BackColor = Color.FromArgb(37, 37, 38);
            gamesPanel.Padding = new Padding(20, 20, 20, 80);
            gamesPanel.WrapContents = true;
            gamesHost = new RetroScrollHost(gamesPanel);
            gamesHost.Dock = DockStyle.Fill;
            gamesHost.ScrollBar.AccessibleName = UpdateText("GameListScrollBar");

            // Строка статуса
            statusStrip = new StatusStrip();
            statusStrip.BackColor = Color.FromArgb(0, 122, 204);
            statusStrip.ForeColor = Color.White;

            statusLabel = new ToolStripStatusLabel();
            statusLabel.Text = "Загружаем список игр...";
            statusStrip.Items.Add(statusLabel);

            progressBar = new ToolStripProgressBar();
            progressBar.Visible = false;
            progressBar.Style = ProgressBarStyle.Blocks;
            progressBar.Minimum = 0;
            progressBar.Maximum = 100;
            progressBar.Width = 180;
            statusStrip.Items.Add(progressBar);
        }

        private void ClearImageCache()
        {
            try
            {
                foreach (Control tile in gamesPanel.Controls)
                {
                    if (tile is Panel tilePanel)
                    {
                        foreach (Control ctrl in tilePanel.Controls)
                        {
                            if (ctrl is Panel container)
                            {
                                foreach (Control inner in container.Controls)
                                {
                                    if (inner is PictureBox pb && pb.Image != null)
                                    {
                                        pb.Image = null;
                                    }
                                }
                            }
                        }
                    }
                }

                imageCache.Dispose();
                string thumbnails = Path.Combine(settingsManager.GetCacheDir(), "thumbnails");
                // Only previews; keep the offline game catalog.
                try
                {
                    if (Directory.Exists(thumbnails)) Directory.Delete(thumbnails, true);
                }
                finally { imageCache = new ThumbnailCache(httpClient, thumbnails); }
                CreateGameTiles();
                CheckForUpdates();

                UpdateStatus("Кэш изображений очищен");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при очистке кэша: {ex.Message}", "Ошибка");
            }
        }

        private void SwitchTheme(string themeName)
        {
            settings.Theme = themeName;
            settingsManager.SaveSettings(settings);
            ThemeManager.Apply(this, themeName);
            titleBar.UpdateTheme(themeName);
            ApplyUpdateBannerTheme();
            UpdateThemeCheckmarks(themeName);
        }

        private void UpdateThemeCheckmarks(string themeName)
        {
            themeDarkItem.Checked = themeName == "Dark";
            themeClassicItem.Checked = themeName == "Classic";
        }

        private void InitializeManagers()
        {
            modManager = new SafeModManager();
            settingsManager = new SettingsManager();
            settings = settingsManager.LoadSettings();
            UpdateThemeCheckmarks(settings.Theme);
        }

        private async Task<GameConfig> LoadCachedConfig()
        {
            return await feeds.LoadCatalogAsync(lifetime.Token);
        }

        private async Task<GameConfig> ReadCachedConfig()
        {
            return await feeds.ReadCachedCatalogAsync(lifetime.Token);
        }

        private async Task RefreshLauncherUpdateAsync()
        {
            try
            {
                var manifest = await feeds.LoadManifestAsync(lifetime.Token);
                if (IsDisposed || lifetime.IsCancellationRequested) return;
                launcherManifest = manifest;
                CheckLauncherUpdate();
            }
            catch (OperationCanceledException) { }
        }

        private bool TryLoadLocalConfig()
        {
            try
            {
                if (File.Exists("test_config.json"))
                {
                    string localJson = File.ReadAllText("test_config.json", Encoding.UTF8);
                    config = JsonConvert.DeserializeObject<GameConfig>(localJson);

                    if (config?.games?.Length > 0)
                    {
                        CreateGameTiles();
                        CheckForUpdates();
                        CheckLauncherUpdate();
                        UpdateStatus($"Загружено локально: {config.games.Length} игр");
                        return true;
                    }
                }
            }
            catch { }
            return false;
        }

        private async Task LoadGamesList()
        {
            try
            {
                if (config == null)
                {
                    var cached = await ReadCachedConfig();
                    if (IsDisposed || lifetime.IsCancellationRequested) return;
                    if (cached?.games?.Length > 0)
                    {
                        config = cached;
                        DisplayGames();
                    }
                }
                UpdateStatus("Загрузка списка игр...");
                var loaded = await LoadCachedConfig();
                if (IsDisposed || lifetime.IsCancellationRequested) return;

                if (loaded?.games?.Length > 0)
                {
                    config = loaded;
                    DisplayGames();
                    return;
                }

                throw new Exception("Конфигурация игр пуста");
            }
            catch (Exception ex)
            {
                if (IsDisposed || lifetime.IsCancellationRequested) return;
                if (config?.games?.Length > 0)
                {
                    UpdateStatus("Сеть недоступна. Показан сохранённый список игр.");
                    return;
                }
                if (TryLoadLocalConfig())
                    return;

                MessageBox.Show($"Ошибка загрузки: {ex.Message}\n\nБудут показаны тестовые данные",
                    "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                CreateTestGame();
            }
        }

        private void DisplayGames()
        {
            CreateGameTiles();
            CheckForUpdates();
            CheckLauncherUpdate();
            UpdateStatus($"Загружено игр: {config.games.Length}");
        }

        private void ApplyUpdateBannerTheme()
        {
            bool dark = settings.Theme == "Dark";
            _updateBanner.BackColor = dark ? Color.FromArgb(50, 60, 70) : SystemColors.Info;
            _updateBannerLabel.ForeColor = dark ? Color.Gainsboro : SystemColors.InfoText;
        }

        private void CreateUpdateBanner()
        {
            _updateBanner = new Panel();
            _updateBanner.Dock = DockStyle.Top;
            _updateBanner.Height = 28;
            _updateBanner.BackColor = SystemColors.Info;
            _updateBanner.Visible = false;
            _updateBanner.Paint += (s, e) =>
                RetroButton.DrawRetro3D(e.Graphics,
                    new Rectangle(0, 0, _updateBanner.Width - 1, _updateBanner.Height - 1),
                    raised: false, baseColor: _updateBanner.BackColor);

            _updateBannerLabel = new Label();
            _updateBannerLabel.ForeColor = SystemColors.InfoText;
            _updateBannerLabel.Font = new Font("MS Sans Serif", 8f, FontStyle.Bold);
            _updateBannerLabel.TextAlign = ContentAlignment.MiddleLeft;
            _updateBannerLabel.Dock = DockStyle.Fill;
            _updateBannerLabel.Padding = new Padding(6, 0, 0, 0);
            _updateBannerLabel.BackColor = Color.Transparent;

            var btnDownload = new RetroButton();
            btnDownload.Text = UpdateText("DownloadUpdate");
            btnDownload.Size = new Size(70, 20);
            btnDownload.Dock = DockStyle.Right;
            btnDownload.BackColor = SystemColors.Control;
            btnDownload.ForeColor = SystemColors.ControlText;
            btnDownload.Font = new Font("MS Sans Serif", 8f);
            btnDownload.Click += (s, e) =>
            {
                if (!TryGetLauncherDownloadUri(out var uri)) return;
                try { Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true }); }
                catch (Exception ex) { UpdateStatus(string.Format(UpdateText("OpenUpdateFailed"), ex.Message)); }
            };

            var btnClose = new RetroButton();
            btnClose.Text = "✕";
            btnClose.Size = new Size(22, 20);
            btnClose.Dock = DockStyle.Right;
            btnClose.BackColor = SystemColors.Control;
            btnClose.ForeColor = SystemColors.ControlText;
            btnClose.Font = new Font("MS Sans Serif", 8f);
            btnClose.Click += (s, e) =>
            {
                if (LauncherVersion.TryParse(launcherManifest?.launcher_version, out var version))
                {
                    settings.DismissedLauncherVersion = version.ToString(4);
                    settingsManager.SaveSettings(settings);
                }
                _updateBanner.Visible = false;
            };

            _updateBanner.Controls.Add(_updateBannerLabel);
            _updateBanner.Controls.Add(btnDownload);
            _updateBanner.Controls.Add(btnClose);

            mainPanel.Controls.Add(_updateBanner);
            _updateBanner.BringToFront();
            menuStrip.BringToFront();
        }

        private void CheckLauncherUpdate()
        {
            _updateBanner.Visible = false;
            if (!LauncherVersion.ShouldNotify(launcherManifest?.launcher_version, settings.DismissedLauncherVersion) ||
                !TryGetLauncherDownloadUri(out _)) return;
            _updateBannerLabel.Text = string.Format(UpdateText("LauncherUpdateAvailable"), launcherManifest.launcher_version);
            _updateBanner.Visible = true;
            mainPanel.Controls.SetChildIndex(_updateBanner, 1);
            mainPanel.Controls.SetChildIndex(menuStrip, mainPanel.Controls.Count - 1);
        }

        private bool TryGetLauncherDownloadUri(out Uri uri)
        {
            return Uri.TryCreate(launcherManifest?.launcher_download_url, UriKind.Absolute, out uri) &&
                uri.Scheme == Uri.UriSchemeHttps;
        }

        private static string UpdateText(string key) =>
            (CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ru" ? russianResources : Properties.Resources.ResourceManager)
                .GetString(key, CultureInfo.InvariantCulture);

        private void CreateGameTiles()
        {
            if (gamesPanel.InvokeRequired)
            {
                gamesPanel.Invoke(new Action(CreateGameTiles));
                return;
            }

            while (gamesPanel.Controls.Count > 0)
                gamesPanel.Controls[0].Dispose();
            modpackWarnings.RemoveAll();
            updateBadges.Clear();
            versionLabels.Clear();

            if (config?.games != null)
            {
                foreach (var game in config.games)
                    CreateGameTile(game);
            }

            ThemeManager.Apply(this, settings.Theme);
            titleBar.UpdateTheme(settings.Theme);
            ApplyUpdateBannerTheme();
        }

        private void CreateGameTile(Game game)
        {
            const int W = 250;
            const int IW = 230;
            const int LP = 10;

            var tilePanel = new Panel();
            tilePanel.Size = new Size(W, 350);
            tilePanel.BackColor = Color.FromArgb(62, 62, 66);
            tilePanel.BorderStyle = BorderStyle.None;
            tilePanel.Margin = new Padding(8);

            tilePanel.Paint += (sender, e) =>
            {
                var p = (Panel)sender;
                RetroButton.DrawRetro3D(
                    e.Graphics,
                    new Rectangle(0, 0, p.Width - 1, p.Height - 1),
                    raised: true,
                    baseColor: p.BackColor);
            };

            var picContainer = new Panel();
            picContainer.Size = new Size(IW, 110);
            picContainer.Location = new Point(LP, 8);
            picContainer.BackColor = Color.Black;

            var pictureBox = new PictureBox();
            pictureBox.Dock = DockStyle.Fill;
            pictureBox.SizeMode = PictureBoxSizeMode.Zoom;
            pictureBox.BackColor = Color.Black;
            _ = LoadImageAsync(pictureBox, game.image_url);

            var badge = new Label();
            badge.Text = "🆕 Обновление!";
            badge.BackColor = Color.OrangeRed;
            badge.ForeColor = Color.White;
            badge.Font = new Font("Segoe UI", 8f, FontStyle.Bold);
            badge.AutoSize = true;
            badge.Location = new Point(4, 4);
            badge.Padding = new Padding(2);
            badge.Visible = false;

            picContainer.Controls.Add(pictureBox);
            picContainer.Controls.Add(badge);
            badge.BringToFront();
            updateBadges.Add((badge, game));

            var nameLabel = new Label();
            nameLabel.Text = game.name;
            nameLabel.Font = new Font("Segoe UI", 11, FontStyle.Bold);
            nameLabel.ForeColor = Color.White;
            nameLabel.Location = new Point(LP, 124);
            nameLabel.Size = new Size(IW, 24);
            nameLabel.TextAlign = ContentAlignment.MiddleCenter;

            string installedVer = settings.InstalledVersions.TryGetValue(game.id, out string iv) ? iv : null;
            string verLine1 = $"Последняя: {game.mod_pack_version ?? "не указана"}";
            string verLine2 = installedVer != null
                ? $"Установлено: {installedVer}"
                : "Установлено: неизвестно";
            var versionLabel = new Label();
            versionLabel.Text = $"{verLine1}\n{verLine2}";
            versionLabel.Font = new Font("Segoe UI", 8);
            versionLabel.ForeColor = Color.LightGray;
            versionLabel.Location = new Point(LP, 157);
            versionLabel.Size = new Size(IW, 28);
            versionLabel.TextAlign = ContentAlignment.MiddleCenter;
            versionLabels.Add((versionLabel, game));

            var descLabel = new Label();
            descLabel.Text = game.description ?? "Описание отсутствует";
            descLabel.Font = new Font("Segoe UI", 9);
            descLabel.ForeColor = Color.LightGray;
            descLabel.Location = new Point(LP, 185);
            descLabel.Size = new Size(IW, 60);
            descLabel.TextAlign = ContentAlignment.TopCenter;

            Control[] pathControls;
            if (game.steam_app_id > 0)
            {
                // Кнопка "Играть" с выбором режима
                var playBtn = CreateTileButton("▶ Играть", Color.FromArgb(34, 100, 34),
                    new Point(LP, 246), new Size(IW, 28));

                var playMenu = new ContextMenuStrip();
                playBtn.ContextMenuStrip = playMenu;
                playMenu.Items.Add("▶ Играть с модами").Click +=
                    (s, e) => LaunchGame(game, withMods: true);
                playMenu.Items.Add("▶ Играть без модов").Click +=
                    (s, e) => LaunchGame(game, withMods: false);

                playBtn.Click += (s, e) =>
                {
                    var btn = (Control)s;
                    playMenu.Show(btn, new Point(0, btn.Height));
                };

                var modsBtn = CreateTileButton("🔧 Установить моды", Color.FromArgb(83, 83, 87),
                    new Point(LP, 278), new Size(IW, 28));
                modsBtn.Click += async (s, e) => await InstallMods(game);

                int halfW = (IW - 4) / 2;
                var steamBtn = CreateTileButton("🔍 Steam", Color.FromArgb(59, 89, 120),
                    new Point(LP, 310), new Size(halfW, 26));
                steamBtn.Click += (s, e) => DetectGamePathViaSteam(game);

                var manualBtn = CreateTileButton("📁 Вручную", Color.FromArgb(100, 100, 100),
                    new Point(LP + halfW + 4, 310), new Size(halfW, 26));
                manualBtn.Click += (s, e) => ChangeGamePath(game);

                pathControls = new Control[] { steamBtn, manualBtn };

                var infoBtn = CreateTileButton("ℹ️ Информация", Color.FromArgb(70, 70, 74),
                    new Point(LP, 340), new Size(IW, 26));
                infoBtn.Click += (s, e) => ShowGameInfo(game);

                tilePanel.Size = new Size(W, 374);
                tilePanel.Controls.AddRange(new Control[] {
                    picContainer, nameLabel, versionLabel, descLabel,
                    playBtn, modsBtn, infoBtn
                });
                tilePanel.Controls.AddRange(pathControls);
            }
            else
            {
                var modsBtn = CreateTileButton("🔧 Установить моды", Color.FromArgb(83, 83, 87),
                    new Point(LP, 246), new Size(IW, 28));
                modsBtn.Click += async (s, e) => await InstallMods(game);

                var changePathBtn = CreateTileButton("📁 Сменить папку", Color.FromArgb(100, 100, 100),
                    new Point(LP, 278), new Size(IW, 26));
                changePathBtn.Click += (s, e) => ChangeGamePath(game);
                pathControls = new Control[] { changePathBtn };

                var infoBtn = CreateTileButton("ℹ️ Информация", Color.FromArgb(70, 70, 74),
                    new Point(LP, 308), new Size(IW, 26));
                infoBtn.Click += (s, e) => ShowGameInfo(game);

                tilePanel.Controls.AddRange(new Control[] {
                    picContainer, nameLabel, versionLabel, descLabel, modsBtn, infoBtn
                });
                tilePanel.Controls.AddRange(pathControls);
            }

            if (ModpackNotice.HasWarning(game))
            {
                descLabel.Height = 30;
                descLabel.AutoEllipsis = true;
                var warning = ModpackNotice.CreatePanel(game, IW);
                warning.Location = new Point(LP, 220);
                string text = ModpackNotice.FullText(game);
                modpackWarnings.SetToolTip(warning, text);
                foreach (Control child in warning.Controls) modpackWarnings.SetToolTip(child, text);
                tilePanel.Controls.Add(warning);
            }
            gamesPanel.Controls.Add(tilePanel);
        }

        private RetroButton CreateTileButton(string text, Color backColor, Point location, Size size)
        {
            return new RetroButton
            {
                Text = text,
                BackColor = backColor,
                ForeColor = Color.White,
                Location = location,
                Size = size
            };
        }

        private void CheckForUpdates()
        {
            if (config?.games == null) return;

            foreach (var (badge, game) in updateBadges)
            {
                if (string.IsNullOrEmpty(game.mod_pack_version)) continue;
                if (!settings.GamePaths.TryGetValue(game.id, out string gamePath)) continue;
                if (!Directory.Exists(gamePath)) continue;

                string installed = GetCurrentModVersion(game.id, gamePath);
                if (installed != game.mod_pack_version)
                {
                    if (badge.IsHandleCreated)
                        badge.Invoke(new Action(() => badge.Visible = true));
                    else
                        badge.Visible = true;
                }
            }
        }

        private void ShowGameInfo(Game game)
        {
            string message = $"Игра: {game.name}\n" +
                             $"Версия модпака: {game.mod_pack_version ?? "не указана"}\n\n";
            if (ModpackNotice.HasWarning(game)) message += ModpackNotice.FullText(game) + Environment.NewLine + Environment.NewLine;

            if (string.IsNullOrEmpty(game.mod_pack_url))
                message += "Для этой игры модпак не доступен.\n\n";
            else
                message += "Доступен модпак для установки.\n\n";

            message += "Вы можете приобрести игру в официальных магазинах:\n" +
                       "• Steam\n• Epic Games Store\n• Microsoft Store";

            MessageBox.Show(message, "Информация о игре",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void LaunchGame(Game game, bool withMods)
        {
            if (game.steam_app_id <= 0) return;

            if (!withMods)
            {
                if (!settings.GamePaths.TryGetValue(game.id, out string gamePath)
                    || !Directory.Exists(gamePath))
                {
                    MessageBox.Show(
                        "Укажите путь к папке игры, чтобы запустить без модов.",
                        "Путь не указан", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                string hook    = Path.Combine(gamePath, "winhttp.dll");
                string hookOff = hook + ".disabled";
                bool renamed   = false;

                if (File.Exists(hook))
                {
                    try { File.Move(hook, hookOff); renamed = true; }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Не удалось отключить моды:\n{ex.Message}", "Ошибка");
                        return;
                    }
                }

                System.Diagnostics.Process.Start($"steam://run/{game.steam_app_id}");

                if (renamed)
                {
                    // Восстанавливаем через 10 секунд — игровой экзешник к тому моменту уже запущен
                    Task.Delay(10000).ContinueWith(_ =>
                    {
                        try
                        {
                            if (File.Exists(hookOff) && !File.Exists(hook))
                                File.Move(hookOff, hook);
                        }
                        catch { }
                    });
                    UpdateStatus("Запуск без модов... Моды будут включены через 10 сек.");
                }
            }
            else
            {
                System.Diagnostics.Process.Start($"steam://run/{game.steam_app_id}");
            }
        }

        private async Task InstallMods(Game game)
        {
            try
            {
                if (string.IsNullOrEmpty(game.mod_pack_url))
                {
                    MessageBox.Show($"Для игры {game.name} нет доступного модпака.",
                        "Информация", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                string gamePath = settingsManager.GetOrSelectGamePath(game, settings);
                if (string.IsNullOrEmpty(gamePath)) return;

                string installedVersion = GetCurrentModVersion(game.id, gamePath);
                if (installedVersion == game.mod_pack_version)
                {
                    var ask = MessageBox.Show(
                        $"Текущая версия модов совпадает с устанавливаемой ({game.mod_pack_version}).\nПродолжить установку?",
                        "Проверка версии",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Question);

                    if (ask == DialogResult.No) return;
                }



                await modManager.InstallOrPatch(
                    game,
                    gamePath,
                    installedVersion,
                    status => UpdateStatus(status),
                    visible => SetProgressVisible(visible),
                    progress => UpdateProgress(progress)
                );

                if (!string.IsNullOrEmpty(game.mod_pack_version))
                    SaveModVersion(game.id, gamePath, game.mod_pack_version);

                foreach (var (badge, g) in updateBadges)
                {
                    if (g.id == game.id)
                        badge.Invoke(new Action(() => badge.Visible = false));
                }

                string newVerText = $"Последняя: {game.mod_pack_version}\nУстановлено: {game.mod_pack_version}";
                foreach (var (lbl, g) in versionLabels)
                {
                    if (g.id == game.id)
                        lbl.Invoke(new Action(() => lbl.Text = newVerText));
                }

                settingsManager.SaveSettings(settings);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при установке модов: {ex.Message}",
                    "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
                UpdateStatus("Ошибка установки модов");
            }
        }

        private string GetCurrentModVersion(string gameId, string gamePath)
        {
            if (!string.IsNullOrEmpty(gameId) &&
                settings.InstalledVersions.TryGetValue(gameId, out string saved) &&
                !string.IsNullOrEmpty(saved))
                return saved;

            string versionFile = Path.Combine(gamePath, "launcher_mod_version.txt");
            try
            {
                if (File.Exists(versionFile))
                {
                    string ver = File.ReadAllText(versionFile, Encoding.UTF8).Trim();
                    if (!string.IsNullOrEmpty(ver))
                    {
                        if (!string.IsNullOrEmpty(gameId))
                        {
                            settings.InstalledVersions[gameId] = ver;
                            settingsManager.SaveSettings(settings);
                        }
                        return ver;
                    }
                }
            }
            catch { }

            return null;
        }

        private void SaveModVersion(string gameId, string gamePath, string version)
        {
            if (!string.IsNullOrEmpty(gameId))
            {
                settings.InstalledVersions[gameId] = version;
                settingsManager.SaveSettings(settings);
            }
            try { File.WriteAllText(Path.Combine(gamePath, "launcher_mod_version.txt"), version, Encoding.UTF8); }
            catch { }
        }

        private void ChangeGamePath(Game game)
        {
            string newPath = settingsManager.SelectGameFolder(game, settings);
            if (!string.IsNullOrEmpty(newPath))
            {
                settings.GamePaths[game.id] = newPath;
                settingsManager.SaveSettings(settings);
                MessageBox.Show($"Папка для {game.name} изменена на:\n{newPath}",
                    "Успех", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private void DetectGamePathViaSteam(Game game)
        {
            UpdateStatus("Поиск игры в Steam...");
            string found = SteamDetector.FindGamePath(game.steam_app_id, game.executable);
            UpdateStatus("Готово");

            if (found == null)
            {
                var ask = MessageBox.Show(
                    $"Игра {game.name} не найдена автоматически.\nВыбрать папку вручную?",
                    "Не найдено",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Information);

                if (ask == DialogResult.Yes)
                    ChangeGamePath(game);
                return;
            }

            var result = MessageBox.Show(
                $"Найдена игра:\n{found}\n\nИспользовать этот путь?",
                "Обнаружена игра",
                MessageBoxButtons.YesNoCancel,
                MessageBoxIcon.Question);

            if (result == DialogResult.Yes)
            {
                settings.GamePaths[game.id] = found;
                settingsManager.SaveSettings(settings);
                MessageBox.Show($"Папка для {game.name} сохранена:\n{found}",
                    "Успех", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else if (result == DialogResult.No)
            {
                ChangeGamePath(game);
            }
        }

        private async Task LoadImageAsync(PictureBox pictureBox, string imageUrl)
        {
            var cache = imageCache;
            var image = await cache.GetAsync(imageUrl);
            // Continuation runs on the UI thread. PictureBox.Image can be set before
            // handle creation; checking IsHandleCreated used to lose cached previews.
            if (!IsDisposed && !pictureBox.IsDisposed && ReferenceEquals(cache, imageCache))
            {
                pictureBox.Image = image;
                if (image == null) pictureBox.BackColor = Color.DarkGray;
            }
        }

        private void UpdateStatus(string status)
        {
            if (statusStrip.InvokeRequired)
            {
                statusStrip.Invoke(new Action<string>(UpdateStatus), status);
                return;
            }
            statusLabel.Text = status;
        }

        private void SetProgressVisible(bool visible)
        {
            if (statusStrip.InvokeRequired)
            {
                statusStrip.Invoke(new Action<bool>(SetProgressVisible), visible);
                return;
            }
            progressBar.Visible = visible;
            if (!visible) progressBar.Value = 0;
        }

        private void UpdateProgress(int percent)
        {
            if (statusStrip.InvokeRequired)
            {
                statusStrip.Invoke(new Action<int>(UpdateProgress), percent);
                return;
            }
            progressBar.Value = Math.Max(0, Math.Min(100, percent));
        }

        private void CreateTestGame()
        {
            var testGame = new Game
            {
                id = "test",
                name = "Тестовая игра",
                description = "Для демонстрации лаунчера",
                image_url = "https://via.placeholder.com/240x180/333/666?text=Test+Game",
                mod_pack_version = "1.0.0"
            };
            CreateGameTile(testGame);
            UpdateStatus("Используется тестовая игра");
        }

        private void Form1_FormClosed(object sender, FormClosedEventArgs e)
        {
            lifetime.Cancel();
            modpackWarnings.Dispose();
            imageCache?.Dispose();
        }
    }
}
