using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using Microsoft.Win32;

namespace DynamicIsland
{
    public class PeekData
    {
        public string Key = "";
        public ImageSource Image;
        public string Glyph = "\uEA8F";
        public string Title, Body;
        public double? Progress;
        public TimeSpan Duration = TimeSpan.FromSeconds(4);
        public double Width = 420, Height = 76;
        public List<(string label, Action act)> Actions;
        public Action OnClick;
    }

    public partial class MainWindow : Window
    {
        enum Mode { Compact, Peek, Expanded }
        enum Act { None, Media, Timer, Voice, Download }
        Mode _mode = Mode.Compact;
        bool _pinned, _userHidden, _fsHidden, _ready, _menuOpen;
        PeekData _curPeek;
        readonly Queue<PeekData> _peeks = new();
        double _targetW, _targetH;

        readonly DispatcherTimer _fast = new() { Interval = TimeSpan.FromMilliseconds(40) };
        readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromMilliseconds(250) };
        readonly DispatcherTimer _hover = new() { Interval = TimeSpan.FromMilliseconds(180) };
        readonly DispatcherTimer _leave = new() { Interval = TimeSpan.FromMilliseconds(450) };
        readonly DispatcherTimer _peekT = new();
        int _tickCount;
        IntPtr _hwnd;
        System.Windows.Forms.NotifyIcon _tray;
        Dictionary<RadioButton, FrameworkElement> _tabs;

        static AppSettings S => App.Settings;

        readonly MediaService _media = new();
        readonly LyricsService _lyrics = new();
        readonly NotificationService _notif = new();
        readonly SystemService _sys = new();
        readonly VolumeService _vol = new();
        readonly InfoService _info = new();
        readonly SpotifyService _spotify = new();
        readonly DiscordService _discord = new();
        readonly FpsService _fps = new();
        bool _fpsMode;

        const double ExpW = 680, ExpH = 430, CompactH = 36;

        static readonly SolidColorBrush Green = Frozen(Color.FromRgb(0x30, 0xD1, 0x58));
        static SolidColorBrush Frozen(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }

        public MainWindow()
        {
            InitializeComponent();
            _tabs = new()
            {
                [TabMedia] = PanelMedia, [TabNotif] = PanelNotif, [TabTools] = PanelTools, [TabFiles] = PanelFiles,
                [TabSystem] = PanelSystem, [TabInfo] = PanelInfo, [TabLaunch] = PanelLaunch, [TabPlugins] = PanelPlugins
            };

            // Tous les événements souris passent par la zone de survol élargie
            HitZone.MouseEnter += (s, e) => { _leave.Stop(); if (_mode == Mode.Compact) _hover.Start(); };
            HitZone.MouseLeave += (s, e) => { _hover.Stop(); if (_mode == Mode.Expanded && !_pinned && !_menuOpen) _leave.Start(); };
            HitZone.MouseLeftButtonDown += HitZone_Down;
            HitZone.MouseMove += HitZone_Move;
            HitZone.MouseLeftButtonUp += Island_Click;
            HitZone.MouseRightButtonUp += HitZone_RightClick;
            Island.SizeChanged += (s, e) => UpdateClip();
            Island.DragEnter += (s, e) => { if (_mode != Mode.Expanded) { Expand(); TabFiles.IsChecked = true; } };
            Island.Drop += (s, e) =>
            {
                // un dépôt sur la carte Raccourcis (onglet Lancer) ne va pas sur l'étagère
                if (_mode == Mode.Expanded && PanelLaunch.Visibility == Visibility.Visible) return;
                Island_Drop(s, e);
            };
            Bubble.MouseLeftButtonUp += Bubble_Click;

            _hover.Tick += (s, e) => { _hover.Stop(); if (_mode == Mode.Compact && HitZone.IsMouseOver) Expand(); };
            _leave.Tick += (s, e) => { _leave.Stop(); if (!HitZone.IsMouseOver && !_pinned && !_menuOpen && _mode == Mode.Expanded) Collapse(); };
            _peekT.Tick += (s, e) =>
            {
                if (HitZone.IsMouseOver && _curPeek?.Key != "osd") return; // reste affiché tant que la souris est dessus
                _peekT.Stop();
                EndPeek();
            };
            _fast.Tick += (s, e) => FastTick();
            _tick.Tick += (s, e) => SlowTick();

            Loaded += OnLoaded;
            Closed += (s, e) =>
            {
                if (_tray != null) _tray.Visible = false;
                try { _fps.Stop(); } catch { }
            };
        }

        // ================== Démarrage ==================
        void OnLoaded(object sender, RoutedEventArgs e)
        {
            _ready = true;
            ApplySettings();
            InitMedia();
            InitNotifs();
            InitVoice();
            InitTools();
            InitFiles();
            InitSystem();
            InitInfo();
            InitLaunch();
            InitPlugins();
            InitTray();
            _fast.Start();
            _tick.Start();
            UpdateCompact(true);
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            _hwnd = new WindowInteropHelper(this).Handle;
            Native.SetWindowLong(_hwnd, Native.GWL_EXSTYLE, Native.GetWindowLong(_hwnd, Native.GWL_EXSTYLE) | Native.WS_EX_TOOLWINDOW);
            HwndSource.FromHwnd(_hwnd).AddHook(WndProc);
            Native.AddClipboardFormatListener(_hwnd);
            Native.RegisterHotKey(_hwnd, 1, Native.MOD_CONTROL | Native.MOD_ALT, 0x49); // Ctrl+Alt+I : ouvrir/épingler
            Native.RegisterHotKey(_hwnd, 2, Native.MOD_CONTROL | Native.MOD_ALT, 0x48); // Ctrl+Alt+H : masquer/afficher
            Native.RegisterHotKey(_hwnd, 3, Native.MOD_CONTROL | Native.MOD_ALT, 0x20); // Ctrl+Alt+Espace : play/pause
        }

        IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == Native.WM_CLIPBOARDUPDATE) OnClipboardChanged();
            else if (msg == Native.WM_HOTKEY)
            {
                switch (wParam.ToInt32())
                {
                    case 1:
                        if (_mode == Mode.Expanded && _pinned) { SetPinned(false); Collapse(); }
                        else { Expand(); SetPinned(true); Activate(); }
                        break;
                    case 2: _userHidden = !_userHidden; UpdateVisibility(); break;
                    case 3: _media.TogglePlay(); break;
                }
                handled = true;
            }
            return IntPtr.Zero;
        }

        void InitTray()
        {
            System.Drawing.Icon icon;
            using (var st = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/malbo.ico")).Stream)
                icon = new System.Drawing.Icon(st, new System.Drawing.Size(32, 32));
            var menu = new System.Windows.Forms.ContextMenuStrip();
            menu.Items.Add("Afficher / masquer (Ctrl+Alt+H)", null, (s, e) => { _userHidden = !_userHidden; UpdateVisibility(); });
            menu.Items.Add("Ouvrir l'île (Ctrl+Alt+I)", null, (s, e) => { _userHidden = false; UpdateVisibility(); Expand(); SetPinned(true); });
            menu.Items.Add("Réglages", null, (s, e) => OpenSettings());
            menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
            if (!Installer.IsInstalled)
                menu.Items.Add("Installer Malbo sur ce PC", null, (s, e) => { try { Installer.Install(); } catch (Exception ex) { App.Log(ex); } });
            menu.Items.Add("Rechercher une mise à jour", null, (s, e) => { _ = CheckUpdateAsync(true); });
            menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
            menu.Items.Add("Quitter", null, (s, e) => Quit());
            _tray = new System.Windows.Forms.NotifyIcon { Icon = icon, Text = "Malbo", Visible = true, ContextMenuStrip = menu };
            _tray.DoubleClick += (s, e) => OpenSettings();
        }

        void Quit()
        {
            if (_tray != null) _tray.Visible = false;
            Application.Current.Shutdown();
        }

        // ================== Réglages / apparence ==================
        public void ApplySettings()
        {
            ApplyTheme();
            ScaleT.ScaleX = ScaleT.ScaleY = S.Scale;
            double zone = Math.Clamp(S.HoverZone, 0, 60);
            HitZone.Padding = new Thickness(zone, 8, zone, zone);
            _hover.Interval = TimeSpan.FromMilliseconds(Math.Clamp(S.HoverDelay, 0, 3000));
            Width = Math.Max(720, ExpW + 2 * zone + 60) * S.Scale;
            Height = Math.Max(560, ExpH + 8 + zone + 40) * S.Scale;
            Island.Opacity = S.Opacity;
            PositionWindow();
            TabMedia.Visibility = Vis(S.ShowMedia);
            TabNotif.Visibility = Vis(S.ShowNotif);
            TabTools.Visibility = Vis(S.ShowTools);
            TabFiles.Visibility = Vis(S.ShowFiles);
            TabSystem.Visibility = Vis(S.ShowSystem);
            TabInfo.Visibility = Vis(S.ShowInfo);
            TabLaunch.Visibility = Vis(S.ShowLaunch);
            TabPlugins.Visibility = Vis(S.ShowPlugins);
            var first = _tabs.Keys.FirstOrDefault(t => t.Visibility == Visibility.Visible);
            if (_tabs.Keys.FirstOrDefault(t => t.IsChecked == true)?.Visibility != Visibility.Visible && first != null) first.IsChecked = true;
            DndCheck.IsChecked = S.DoNotDisturb;
            SetStartup(S.StartWithWindows);
            if (!S.AccentFromArt) Application.Current.Resources["Accent"] = new SolidColorBrush(Color.FromRgb(10, 132, 255));
        }

        static Visibility Vis(bool b) => b ? Visibility.Visible : Visibility.Collapsed;

        void ApplyTheme()
        {
            bool light = S.Theme == "Clair";
            if (S.Theme == "Auto")
            {
                try { light = (int)(Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize")?.GetValue("AppsUseLightTheme") ?? 0) == 1; }
                catch { }
            }
            var r = Application.Current.Resources;
            r["IslandBg"] = B(light ? "#F5F5F7" : "#000000");
            r["CardBg"] = B(light ? "#FFFFFF" : "#1C1C1E");
            r["CardHover"] = B(light ? "#E8E8ED" : "#2C2C2E");
            r["HoverBg"] = B(light ? "#18000000" : "#22FFFFFF");
            r["Fg"] = B(light ? "#000000" : "#FFFFFF");
            r["SubFg"] = B(light ? "#6C6C70" : "#8E8E93");
        }

        static SolidColorBrush B(string hex) => new((Color)ColorConverter.ConvertFromString(hex));

        void PositionWindow()
        {
            var screens = System.Windows.Forms.Screen.AllScreens;
            var sc = screens[Math.Clamp(S.ScreenIndex, 0, screens.Length - 1)];
            var b = sc.Bounds;
            var dpi = VisualTreeHelper.GetDpi(this);
            double left = b.Left / dpi.DpiScaleX, top = b.Top / dpi.DpiScaleY, sw = b.Width / dpi.DpiScaleX;
            switch (S.Position)
            {
                case "Gauche":
                    Left = left + 8; Root.HorizontalAlignment = HorizontalAlignment.Left; break;
                case "Droite":
                    Left = left + sw - Width - 8; Root.HorizontalAlignment = HorizontalAlignment.Right; break;
                default:
                    Left = left + (sw - Width) / 2; Root.HorizontalAlignment = HorizontalAlignment.Center; break;
            }
            Top = top;
        }

        static void SetStartup(bool on)
        {
            try
            {
                using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
                if (on) k.SetValue("Malbo", $"\"{Environment.ProcessPath}\"");
                else if (k.GetValue("Malbo") != null) k.DeleteValue("Malbo");
                if (k.GetValue("DynamicIsland") != null) k.DeleteValue("DynamicIsland"); // ancien nom
            }
            catch { }
        }

        void OpenSettings()
        {
            var w = new SettingsWindow(S);
            w.ConnectSpotify = _spotify.ConnectAsync;
            w.ConnectDiscord = () => _discord.ConnectAsync(true);
            w.CheckUpdate = () => CheckUpdateAsync(true);
            w.Install = Installer.Install;
            w.Saved += () => { ApplySettings(); RefreshInfo(true); UpdateBanners(); };
            w.Show();
            w.Activate();
        }

        void Settings_Click(object sender, RoutedEventArgs e) => OpenSettings();

        void Pin_Click(object sender, RoutedEventArgs e) => SetPinned(!_pinned);

        void SetPinned(bool p)
        {
            _pinned = p;
            PinBtn.Content = p ? "\uE77A" : "\uE718";
            PinBtn.Foreground = p ? (Brush)FindResource("Accent") : (Brush)FindResource("Fg");
        }

        void UpdateVisibility()
        {
            Root.Visibility = _userHidden || _fsHidden ? Visibility.Hidden : Visibility.Visible;
            UpdateFastTimer();
        }

        // ================== Menus contextuels ==================
        ContextMenu NewMenu()
        {
            var m = new ContextMenu { Style = (Style)FindResource("DarkMenu") };
            m.Opened += (s, e) => { _menuOpen = true; _leave.Stop(); };
            m.Closed += (s, e) =>
            {
                _menuOpen = false;
                if (_mode == Mode.Expanded && !_pinned && !HitZone.IsMouseOver) _leave.Start();
            };
            return m;
        }

        MenuItem MenuEntry(string header, Action act)
        {
            var mi = new MenuItem { Header = header, Style = (Style)FindResource("DarkItem") };
            if (act != null) mi.Click += (s, e) => act();
            return mi;
        }

        Separator MenuSep() => new Separator { Style = (Style)FindResource("DarkSep") };

        void HitZone_RightClick(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            var m = NewMenu();
            m.Items.Add(MenuEntry("Ouvrir l'île", () => { _userHidden = false; UpdateVisibility(); Expand(); }));
            m.Items.Add(MenuEntry("Lecture / pause", () => _media.TogglePlay()));
            var dnd = MenuEntry("Ne pas déranger", null);
            dnd.IsCheckable = true;
            dnd.IsChecked = S.DoNotDisturb;
            dnd.Click += (s, a) =>
            {
                S.DoNotDisturb = !S.DoNotDisturb;
                S.Save();
                DndCheck.IsChecked = S.DoNotDisturb;
            };
            m.Items.Add(dnd);
            m.Items.Add(MenuEntry("Réglages", OpenSettings));
            m.Items.Add(MenuSep());
            m.Items.Add(MenuEntry("Masquer (Ctrl+Alt+H)", () => { _userHidden = true; UpdateVisibility(); }));
            m.Items.Add(MenuEntry("Quitter", Quit));
            m.PlacementTarget = HitZone;
            m.Placement = PlacementMode.MousePoint;
            m.IsOpen = true;
        }

        // ================== États & animations ==================
        Point _downPt;
        bool _peekDown, _peekSwiped;

        void HitZone_Down(object sender, MouseButtonEventArgs e)
        {
            _peekSwiped = false;
            _peekDown = _mode == Mode.Peek;
            if (!_peekDown) return;
            _downPt = e.GetPosition(this);
            HitZone.CaptureMouse();
        }

        void HitZone_Move(object sender, MouseEventArgs e)
        {
            if (!_peekDown || _mode != Mode.Peek || e.LeftButton != MouseButtonState.Pressed) return;
            if (_downPt.Y - e.GetPosition(this).Y > 18)
            {
                // glisser vers le haut : ferme le peek, sans déclencher de clic
                _peekDown = false;
                _peekSwiped = true;
                HitZone.ReleaseMouseCapture();
                _peekT.Stop();
                EndPeek();
            }
        }

        void Island_Click(object sender, MouseButtonEventArgs e)
        {
            bool wasDown = _peekDown;
            _peekDown = false;
            if (HitZone.IsMouseCaptured) HitZone.ReleaseMouseCapture();
            if (_peekSwiped) { _peekSwiped = false; return; }
            if (_mode == Mode.Peek)
            {
                if (!wasDown) return;
                var a = _curPeek?.OnClick;
                _peekT.Stop();
                if (a != null) a(); else EndPeek();
            }
            else if (_mode == Mode.Compact) Expand();
        }

        void AnimateSize(double w, double h)
        {
            if (Math.Abs(w - _targetW) < 0.5 && Math.Abs(h - _targetH) < 0.5) return;
            bool grow = h > _targetH + 1 || (Math.Abs(h - _targetH) <= 1 && w > _targetW);
            _targetW = w; _targetH = h;
            IEasingFunction ease = grow ? new SpringEase() : new SpringEase { Damping = 9 };
            var d = TimeSpan.FromMilliseconds(grow ? 520 : 420);
            Island.BeginAnimation(WidthProperty, new DoubleAnimation(w, d) { EasingFunction = ease });
            Island.BeginAnimation(HeightProperty, new DoubleAnimation(h, d) { EasingFunction = ease });
        }

        void UpdateClip()
        {
            double w = Island.ActualWidth, h = Island.ActualHeight;
            if (w <= 0 || h <= 0) return;
            double r = Math.Min(h / 2, 30);
            Island.CornerRadius = new CornerRadius(r);
            Inner.Clip = new RectangleGeometry(new Rect(0, 0, w, h), r, r);
        }

        void ShowLayer(UIElement target)
        {
            foreach (var l in new UIElement[] { CompactLayer, PeekLayer, ExpandedLayer })
            {
                if (l == target)
                {
                    l.Visibility = Visibility.Visible;
                    var begin = TimeSpan.FromMilliseconds(70);
                    var dur = TimeSpan.FromMilliseconds(280);
                    var blur = new BlurEffect { Radius = 12, RenderingBias = RenderingBias.Performance };
                    l.Effect = blur;
                    var ba = new DoubleAnimation(12, 0, dur) { BeginTime = begin, EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
                    var layer = l;
                    ba.Completed += (s, e) => { if (layer.Effect == blur) layer.Effect = null; };
                    blur.BeginAnimation(BlurEffect.RadiusProperty, ba);
                    l.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, dur) { BeginTime = begin });
                }
                else
                {
                    l.BeginAnimation(OpacityProperty, null);
                    l.Effect = null;
                    l.Visibility = Visibility.Collapsed;
                }
            }
            if (target != CompactLayer) SetBubble(false);
            UpdateBackdrop();
            UpdateFastTimer();
        }

        void Expand()
        {
            if (_mode == Mode.Expanded) return;
            _peekT.Stop();
            if (_curPeek != null && _curPeek.Key != "osd") _peeks.Clear();
            _curPeek = null;
            _mode = Mode.Expanded;
            AnimateSize(ExpW, ExpH);
            ShowLayer(ExpandedLayer);
            OnTabShown();
        }

        void Collapse()
        {
            _mode = Mode.Compact;
            ShowLayer(CompactLayer);
            UpdateCompact(true);
            if (_peeks.Count > 0) Dispatcher.BeginInvoke(new Action(NextPeek), DispatcherPriority.Background);
        }

        public void ShowPeek(PeekData p)
        {
            if (_mode == Mode.Expanded)
            {
                if (p.Key == "osd" || p.Key == "notif" || p.Key == "track") return;
                _peeks.Enqueue(p);
                return;
            }
            if (_mode == Mode.Peek)
            {
                if (_curPeek != null && _curPeek.Key == p.Key && (p.Key == "osd" || p.Key == "track"))
                {
                    RenderPeek(p); _curPeek = p; _peekT.Stop(); _peekT.Interval = p.Duration; _peekT.Start();
                    return;
                }
                if (_curPeek?.Key == "osd" || _curPeek?.Key == "track") { _peeks.Enqueue(p); _peekT.Stop(); EndPeek(); return; }
                if (p.Key == "osd") return;
                _peeks.Enqueue(p);
                return;
            }
            DisplayPeek(p);
        }

        void DisplayPeek(PeekData p)
        {
            _curPeek = p;
            _mode = Mode.Peek;
            RenderPeek(p);
            AnimateSize(p.Width, p.Height);
            ShowLayer(PeekLayer);
            _peekT.Stop();
            _peekT.Interval = p.Duration;
            _peekT.Start();
        }

        void NextPeek()
        {
            if (_mode != Mode.Compact || _peeks.Count == 0) return;
            DisplayPeek(_peeks.Dequeue());
        }

        void EndPeek()
        {
            _curPeek = null;
            if (_peeks.Count > 0) { DisplayPeek(_peeks.Dequeue()); return; }
            Collapse();
        }

        void RenderPeek(PeekData p)
        {
            PeekTitle.Text = p.Title ?? "";
            PeekBody.Text = p.Body ?? "";
            PeekBody.Visibility = string.IsNullOrEmpty(p.Body) ? Visibility.Collapsed : Visibility.Visible;
            bool small = p.Height < 60;
            PeekIconBox.Width = PeekIconBox.Height = small ? 26 : 44;
            PeekGlyph.FontSize = small ? 16 : 22;
            PeekTitle.FontSize = small ? 12.5 : 13.5;
            if (p.Image != null)
            {
                PeekImg.Background = new ImageBrush(p.Image) { Stretch = Stretch.UniformToFill };
                PeekImg.Visibility = Visibility.Visible;
                PeekGlyph.Visibility = Visibility.Collapsed;
            }
            else
            {
                PeekImg.Visibility = Visibility.Collapsed;
                PeekGlyph.Visibility = Visibility.Visible;
                PeekGlyph.Text = p.Glyph;
            }
            PeekProgress.Visibility = p.Progress.HasValue ? Visibility.Visible : Visibility.Collapsed;
            if (p.Progress.HasValue) PeekProgress.Value = Math.Clamp(p.Progress.Value, 0, 1);
            PeekActions.Children.Clear();
            if (p.Actions != null)
            {
                foreach (var (label, act) in p.Actions)
                {
                    var b = new Button { Content = label, Style = (Style)FindResource("PillBtn") };
                    b.Click += (s, e) => { e.Handled = true; _peekT.Stop(); act(); if (_mode == Mode.Peek) EndPeek(); };
                    PeekActions.Children.Add(b);
                }
            }
        }

        // ================== Compact ==================
        string _lastCompactText;
        Act _bubbleAct = Act.None;
        bool _bubbleShown;

        /// <summary>Activités en cours, par priorité : média > minuteur > vocal Discord > téléchargement.</summary>
        List<Act> Activities()
        {
            var l = new List<Act>(4);
            if (_media.State.HasMedia) l.Add(Act.Media);
            if (_tRun) l.Add(Act.Timer);
            if (_discord.InChannel || DiscordCallLocal) l.Add(Act.Voice);
            if (_activeDl.Count > 0) l.Add(Act.Download);
            return l;
        }

        string TimerText() => _tm == TMode.Chrono ? FmtT(_chrono) : FmtT(_tLeft);

        string DownloadName()
        {
            var p = _activeDl.FirstOrDefault();
            return p == null ? "" : Path.GetFileName(StripTemp(p));
        }

        void SetCompactIcon(string glyph, Brush brush)
        {
            CompactArt.Visibility = Visibility.Collapsed;
            CompactIcon.Visibility = Visibility.Visible;
            CompactIcon.Text = glyph;
            if (brush != null) CompactIcon.Foreground = brush;
            else CompactIcon.SetResourceReference(TextBlock.ForegroundProperty, "SubFg");
        }

        void UpdateCompact(bool force = false)
        {
            if (_mode != Mode.Compact && !force) return;
            var st = _media.State;
            var acts = Activities();
            var main = acts.Count > 0 ? acts[0] : Act.None;
            var second = acts.Count > 1 ? acts[1] : Act.None;
            if (_fpsMode)
            {
                second = acts.FirstOrDefault(a => a != Act.Media);
                main = Act.None;
            }
            bool lyric = main == Act.Media && st.IsPlaying && S.LyricsInCompact && !string.IsNullOrEmpty(_currentLyric);
            string text;
            double w;

            if (_fpsMode)
            {
                SetCompactIcon("\uE7FC", (Brush)FindResource("Accent"));
                Bars.Visibility = Visibility.Collapsed;
                text = $"{_fps.Fps:0} FPS";
                w = 130;
            }
            else switch (main)
            {
                case Act.Media:
                    CompactIcon.SetResourceReference(TextBlock.ForegroundProperty, "SubFg");
                    CompactIcon.Visibility = st.Art == null ? Visibility.Visible : Visibility.Collapsed;
                    CompactIcon.Text = "\uE8D6";
                    CompactArt.Visibility = st.Art != null ? Visibility.Visible : Visibility.Collapsed;
                    text = lyric ? _currentLyric : (st.IsPlaying ? st.Title : $"{st.Title} — en pause");
                    Bars.Visibility = st.IsPlaying ? Visibility.Visible : Visibility.Collapsed;
                    w = lyric ? 420 : 330;
                    break;
                case Act.Timer:
                    SetCompactIcon("\uE916", (Brush)FindResource("Accent"));
                    Bars.Visibility = Visibility.Collapsed;
                    text = $"{TimerLabel} · {TimerText()}";
                    w = 230;
                    break;
                case Act.Voice:
                    SetCompactIcon("\uE767", AnySpeaking ? Green : null);
                    Bars.Visibility = Visibility.Collapsed;
                    text = VoiceCompactText();
                    w = 280;
                    break;
                case Act.Download:
                    SetCompactIcon("\uE896", (Brush)FindResource("Accent"));
                    Bars.Visibility = Visibility.Collapsed;
                    text = "Téléchargement · " + DownloadName();
                    w = 300;
                    break;
                default:
                    SetCompactIcon("\uE823", null);
                    Bars.Visibility = Visibility.Collapsed;
                    text = DateTime.Now.ToString("HH:mm") + (_weather != null ? $"   {InfoService.WeatherIcon(_weather.Code)} {_weather.Temp:0}°" : "");
                    w = 200;
                    break;
            }

            if (text != _lastCompactText)
            {
                _lastCompactText = text;
                CompactText.Text = text;
                if (lyric) CompactText.BeginAnimation(OpacityProperty, new DoubleAnimation(0.2, 1, TimeSpan.FromMilliseconds(300)));
            }

            CompactRight.Text = ""; // le minuteur est désormais une activité (île ou bulle)
            var sys = _sys.Latest;
            MicDot.Visibility = Vis(sys.Mic);
            CamDot.Visibility = Vis(sys.Cam);
            MicDot.ToolTip = "Micro utilisé par " + sys.MicApp;
            CamDot.ToolTip = "Caméra utilisée par " + sys.CamApp;

            if (_mode != Mode.Compact) { SetBubble(false); return; }
            UpdateBubble(second);
            if (sys.Mic) w += 12;
            if (sys.Cam) w += 12;
            AnimateSize(w, CompactH);
            UpdateFastTimer();
        }

        // ================== Bulle (2e activité) ==================
        static readonly double RingLen = Math.PI * (30 - 2.5) / 2.5; // circonférence en unités d'épaisseur

        static string ShortTime(TimeSpan t)
        {
            if (t < TimeSpan.Zero) t = TimeSpan.Zero;
            if (t.TotalMinutes >= 100) return $"{(int)t.TotalHours}h";
            return $"{(int)t.TotalMinutes}:{t.Seconds:00}";
        }

        void UpdateBubble(Act a)
        {
            if (a != Act.None)
            {
                _bubbleAct = a;
                BubbleRing.Visibility = Visibility.Hidden;
                BubbleTrack.Visibility = Visibility.Hidden;
                BubbleText.Visibility = Visibility.Collapsed;
                BubbleGlyph.Visibility = Visibility.Visible;
                switch (a)
                {
                    case Act.Timer:
                        BubbleGlyph.Visibility = Visibility.Collapsed;
                        BubbleText.Visibility = Visibility.Visible;
                        BubbleText.Text = ShortTime(_tm == TMode.Chrono ? _chrono : _tLeft);
                        double p = _tm == TMode.Chrono
                            ? (_chrono.TotalSeconds % 60) / 60
                            : (_tTotal.TotalSeconds > 0 ? 1 - _tLeft.TotalSeconds / _tTotal.TotalSeconds : 0);
                        p = Math.Clamp(p, 0, 1);
                        BubbleTrack.Visibility = Visibility.Visible;
                        BubbleRing.Visibility = p > 0.001 ? Visibility.Visible : Visibility.Hidden;
                        BubbleRing.StrokeDashArray = new DoubleCollection { p * RingLen, RingLen * 2 };
                        BubbleGlyph.ToolTip = null;
                        Bubble.ToolTip = TimerLabel;
                        break;
                    case Act.Voice:
                        BubbleGlyph.Text = "\uE767";
                        if (AnySpeaking) BubbleGlyph.Foreground = Green;
                        else BubbleGlyph.SetResourceReference(TextBlock.ForegroundProperty, "Fg");
                        Bubble.ToolTip = VoiceCompactText();
                        break;
                    case Act.Download:
                        BubbleGlyph.Text = "\uE896";
                        BubbleGlyph.SetResourceReference(TextBlock.ForegroundProperty, "Accent");
                        Bubble.ToolTip = "Téléchargement · " + DownloadName();
                        break;
                }
            }
            SetBubble(a != Act.None && _mode == Mode.Compact);
        }

        void SetBubble(bool show)
        {
            if (show == _bubbleShown) return;
            _bubbleShown = show;
            if (!show) _bubbleAct = Act.None;
            var d = TimeSpan.FromMilliseconds(show ? 560 : 220);
            IEasingFunction ease = show ? new SpringEase() : new CubicEase { EasingMode = EasingMode.EaseOut };
            double zone = Math.Clamp(S.HoverZone, 0, 60);
            Bubble.BeginAnimation(WidthProperty, new DoubleAnimation(show ? 36 : 0, d) { EasingFunction = ease });
            Bubble.BeginAnimation(MarginProperty, new ThicknessAnimation(show ? new Thickness(6 - zone, 8, 0, 0) : new Thickness(0, 8, 0, 0), d) { EasingFunction = ease });
            Bubble.BeginAnimation(OpacityProperty, new DoubleAnimation(show ? 1 : 0, TimeSpan.FromMilliseconds(show ? 220 : 180)));
        }

        void Bubble_Click(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            var a = _bubbleAct;
            if (a == Act.None) return;
            Expand();
            switch (a)
            {
                case Act.Timer: TabTools.IsChecked = true; break;
                case Act.Voice: TabNotif.IsChecked = true; break;
                case Act.Download: TabSystem.IsChecked = true; break;
            }
            if (!_pinned) _leave.Start();
        }

        // ================== Ticks ==================
        readonly Random _rnd = new();
        readonly double[] _barH = new double[5];

        /// <summary>40 ms seulement quand c'est utile (barres visibles, onglet Média ouvert), arrêt complet si l'île est masquée.</summary>
        void UpdateFastTimer()
        {
            if (!_ready) return;
            if (Root.Visibility != Visibility.Visible && !_fpsMode) { if (_fast.IsEnabled) _fast.Stop(); return; }
            bool quick = (_mode == Mode.Compact && Bars.Visibility == Visibility.Visible)
                         || (_mode == Mode.Expanded && PanelMedia.Visibility == Visibility.Visible);
            var iv = TimeSpan.FromMilliseconds(quick ? 40 : 200);
            if (_fast.Interval != iv) _fast.Interval = iv;
            if (!_fast.IsEnabled) _fast.Start();
        }

        void FastTick()
        {
            try { LocalVoiceTick(); } catch (Exception ex) { App.Log(ex); }
            if (Bars.Visibility == Visibility.Visible && _mode == Mode.Compact && Root.Visibility == Visibility.Visible)
            {
                var peak = Math.Min(1, _vol.Peak * 1.6);
                var bars = new[] { Bar0, Bar1, Bar2, Bar3, Bar4 };
                double[] weights = { 0.6, 0.95, 1.0, 0.8, 0.55 };
                for (int i = 0; i < 5; i++)
                {
                    double target = 3 + peak * 15 * weights[i] * (0.55 + _rnd.NextDouble() * 0.45);
                    _barH[i] += (target - _barH[i]) * 0.45;
                    bars[i].Height = Math.Max(3, _barH[i]);
                }
            }
            MediaTick();
        }

        void SlowTick()
        {
            _tickCount++;
            TimerTick();
            UpdateFastTimer();
            if (_tickCount % 4 != 0) return;

            // --- chaque seconde ---
            _sys.Detailed = _mode == Mode.Expanded && PanelSystem.Visibility == Visibility.Visible;

            // plein écran (jeux, vidéos) + compteur FPS
            var h = Native.ForegroundFullscreenWindow(_hwnd);
            bool wasFps = _fpsMode;
            if (h != IntPtr.Zero && S.FpsCounter && !string.IsNullOrEmpty(S.PresentMonPath) && File.Exists(S.PresentMonPath))
            {
                try
                {
                    _fps.Start((int)Native.WindowPid(h));
                    _fps.Poll();
                    _fpsMode = true;
                }
                catch (Exception ex) { App.Log(ex); _fpsMode = false; }
            }
            else
            {
                if (_fpsMode) { try { _fps.Stop(); } catch { } }
                _fpsMode = false;
            }
            bool fs = h != IntPtr.Zero && S.HideInFullscreen && !_fpsMode;
            if (fs != _fsHidden) { _fsHidden = fs; UpdateVisibility(); }
            if (wasFps != _fpsMode) { _lastCompactText = null; UpdateFastTimer(); }

            HeaderClock.Text = DateTime.Now.ToString("ddd d MMM · HH:mm", new System.Globalization.CultureInfo("fr-FR"));
            UpdateCompact();
            CheckAlarms();
            CheckCalendarReminders();
            SystemSecond();
            if (_mode == Mode.Expanded)
            {
                if (PanelSystem.Visibility == Visibility.Visible) UpdateSystemUI();
                if (PanelInfo.Visibility == Visibility.Visible) UpdateClocks();
            }
            if (_tickCount % 12 == 0) _media.Pick();       // toutes les 3 s : réévalue la meilleure session
            if (_tickCount % 20 == 0) _vol.CheckDevice();  // toutes les 5 s : périphérique audio par défaut
            if (_tickCount % 60 == 0 && S.HideWindowsBanners && _notif.Ok) BannerService.Apply(S); // nouvelles applis
            if (_tickCount % 120 == 0) VoiceReconnectTick(); // toutes les 30 s : reconnexion Discord
            if (_mode == Mode.Expanded && PanelMedia.Visibility == Visibility.Visible && _tickCount % 12 == 0) RefreshAppVolumes();
        }

        // ================== Onglets ==================
        void Tab_Checked(object sender, RoutedEventArgs e)
        {
            if (!_ready || _tabs == null) return;
            foreach (var kv in _tabs) kv.Value.Visibility = kv.Key == sender ? Visibility.Visible : Visibility.Collapsed;
            OnTabShown();
        }

        void OnTabShown()
        {
            if (PanelMedia.Visibility == Visibility.Visible)
            {
                RefreshAppVolumes();
                if (_spTrackId == null && IsSpotifyApi) RefreshSpotify();
                Dispatcher.BeginInvoke(new Action(() => { RestyleAllLyrics(); ScrollLyrics(); }), DispatcherPriority.Loaded);
            }
            if (PanelSystem.Visibility == Visibility.Visible) UpdateSystemUI();
            if (PanelInfo.Visibility == Visibility.Visible) UpdateClocks();
            UpdateBackdrop();
            UpdateFastTimer();
        }
    }
}
