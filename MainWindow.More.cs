using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Windows.Devices.Radios;

namespace DynamicIsland
{
    /// <summary>Onglet « Plus » (1.3) : fenêtres ouvertes, activités en direct, sortie audio, bascules, notes, historique, profils.</summary>
    public partial class MainWindow
    {
        readonly LiveActivityService _live = new();
        List<LiveActivity> _liveList = new();
        WrapPanel _toggleHost, _profileHost;
        StackPanel _winHost, _liveHost, _audioHost, _histHost;
        static ControlTemplate _rowTpl;

        /// <summary>Ligne cliquable pleine largeur, contenu aligné à gauche (fenêtres, sorties audio, historique).</summary>
        static ControlTemplate RowTemplate()
        {
            if (_rowTpl != null) return _rowTpl;
            const string x =
                "<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='Button'>" +
                "<Border x:Name='b' Background='Transparent' CornerRadius='10' Padding='{TemplateBinding Padding}'>" +
                "<ContentPresenter HorizontalAlignment='Stretch' VerticalAlignment='Center'/></Border>" +
                "<ControlTemplate.Triggers>" +
                "<Trigger Property='IsMouseOver' Value='True'><Setter TargetName='b' Property='Background' Value='{DynamicResource HoverBg}'/></Trigger>" +
                "<Trigger Property='IsPressed' Value='True'><Setter TargetName='b' Property='Opacity' Value='0.6'/></Trigger>" +
                "</ControlTemplate.Triggers></ControlTemplate>";
            _rowTpl = (ControlTemplate)System.Windows.Markup.XamlReader.Parse(x);
            return _rowTpl;
        }
        TextBlock _liveHint;
        TextBox _notesBox;
        readonly DispatcherTimer _notesSave = new() { Interval = TimeSpan.FromSeconds(1.2) };
        string _winSig;

        // ================== Construction ==================
        void InitMore()
        {
            try
            {
                var left = new StackPanel();
                var right = new StackPanel();
                Grid.SetColumn(right, 1);
                MoreGrid.Children.Add(left);
                MoreGrid.Children.Add(right);

                // --- Fenêtres ouvertes ---
                _winHost = new StackPanel();
                left.Children.Add(MoreCard("Fenêtres ouvertes", "Clique pour basculer sur une fenêtre.", _winHost));

                // --- Activités en direct ---
                _liveHost = new StackPanel();
                _liveHint = MoreSub("");
                var liveBox = new StackPanel();
                liveBox.Children.Add(_liveHost);
                liveBox.Children.Add(_liveHint);
                left.Children.Add(MoreCard("Activités en direct", null, liveBox));

                // --- Historique d'écoute ---
                _histHost = new StackPanel();
                left.Children.Add(MoreCard("Derniers titres écoutés", "Clique un titre pour copier son nom.", _histHost));

                // --- Bascules et outils ---
                _toggleHost = new WrapPanel();
                right.Children.Add(MoreCard("Actions rapides", null, _toggleHost));

                // --- Sortie audio ---
                _audioHost = new StackPanel();
                right.Children.Add(MoreCard("Sortie audio", null, _audioHost));

                // --- Profils ---
                _profileHost = new WrapPanel();
                right.Children.Add(MoreCard("Profils", "Clic : appliquer · clic droit : enregistrer l'état actuel dans le profil.", _profileHost));

                // --- Notes rapides ---
                _notesBox = new TextBox
                {
                    Style = (Style)FindResource("Input"), AcceptsReturn = true, TextWrapping = TextWrapping.Wrap,
                    MinHeight = 90, MaxHeight = 180, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    VerticalContentAlignment = VerticalAlignment.Top, Text = S.Notes ?? ""
                };
                _notesBox.TextChanged += (s, e) => { _notesSave.Stop(); _notesSave.Start(); };
                _notesSave.Tick += (s, e) => { _notesSave.Stop(); S.Notes = _notesBox.Text; S.Save(); };
                right.Children.Add(MoreCard("Notes rapides", "Enregistrées automatiquement.", _notesBox));

                // --- Astuces : tout ce qui ne se voit pas (gestes, raccourcis) ---
                left.Children.Add(MoreCard("Astuces", null, TipsPanel()));

                BuildToggles();
                BuildProfiles();
                RefreshHistory();
            }
            catch (Exception ex) { App.Log(ex); }

            _live.Changed += () => Dispatcher.InvokeAsync(OnLiveChanged);
            _live.Notify += (t, b) => Dispatcher.InvokeAsync(() =>
                ShowPeek(new PeekData { Key = "alert", Glyph = "\uE000", Title = t, Body = b, Duration = TimeSpan.FromSeconds(5), Width = 420, Height = 76 }));
            _live.Finished += a => Dispatcher.InvokeAsync(() =>
            {
                if (a.IsTimer) Alert(a.Title, "Minuteur terminé", "\uE492");
                else Alert(a.Title, string.IsNullOrEmpty(a.Body) ? "Terminé ✔" : a.Body, "\uE182");
            });
            ApplyLiveApi();
        }

        static readonly (string Icon, string Text)[] Tips =
        {
            (Ico.Keyboard, "Ctrl+Alt+I ouvre et épingle l'île · Ctrl+Alt+H la masque"),
            (Ico.Play, "Ctrl+Alt+Espace ou clic molette sur l'île : lecture / pause"),
            (Ico.SpeakerHigh, "Molette sur l'île fermée : volume"),
            (Ico.MicrophoneSlash, "Ctrl+Alt+M coupe le micro pour toutes les applis"),
            (Ico.X, "Glisse un aperçu vers le haut pour le fermer · Échap referme l'île"),
            (Ico.Bell, "Glisse une notification sur le côté pour la supprimer"),
            (Ico.AppWindow, "Ctrl+Tab ou Ctrl+1…9 : changer d'onglet · Tab : parcourir les boutons"),
            (Ico.Timer, "Dans Lancer : « 15 min thé » crée un minuteur, un nom d'appli l'ouvre"),
            (Ico.Folder, "Glisse un fichier sur l'île pour le garder sous la main"),
            (Ico.Gear, "Clic droit sur l'île : menu rapide"),
        };

        StackPanel TipsPanel()
        {
            var sp = new StackPanel();
            foreach (var (icon, text) in Tips)
            {
                var row = new DockPanel { Margin = new Thickness(0, 3, 0, 3) };
                var g = new TextBlock { Text = icon, FontFamily = (FontFamily)FindResource("Icons"), FontSize = 13, Width = 22, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 1, 0, 0) };
                g.SetResourceReference(TextBlock.ForegroundProperty, "Accent");
                DockPanel.SetDock(g, Dock.Left);
                row.Children.Add(g);
                var t = new TextBlock { Text = text, FontSize = 11.5, TextWrapping = TextWrapping.Wrap };
                t.SetResourceReference(TextBlock.ForegroundProperty, "Fg");
                row.Children.Add(t);
                sp.Children.Add(row);
            }
            return sp;
        }

        /// <summary>Au tout premier lancement : un aperçu qui mène aux astuces (gestes et raccourcis ne se devinent pas).</summary>
        void FirstRunTip()
        {
            if (S.TipsShown) return;
            S.TipsShown = true;
            S.Save();
            var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
            t.Tick += (s, e) =>
            {
                t.Stop();
                ShowPeek(new PeekData
                {
                    Key = "alert", Glyph = Ico.Lightbulb, Title = "Bienvenue dans Malbo", Body = "Survole l'île pour l'ouvrir. Gestes et raccourcis : voir les astuces.",
                    Duration = TimeSpan.FromSeconds(10), Width = 500, Height = 76,
                    Actions = new() { ("Astuces", () => { Expand(); TabMore.IsChecked = true; SetPinned(true); }) }
                });
            };
            t.Start();
        }

        /// <summary>Vérifie que la police d'icônes embarquée est bien chargée ; sinon on le note dans error.log (les icônes seraient des carrés).</summary>
        void CheckIconFont()
        {
            try
            {
                var fams = Fonts.GetFontFamilies(new Uri("pack://application:,,,/Assets/Fonts/")).Select(f => f.Source).ToList();
                // WPF nomme la famille de Phosphor-Bold.ttf « Phosphor » (le suffixe de poids est retiré).
                if (!fams.Any(f => f.EndsWith("#Phosphor")) || !fams.Any(f => f.EndsWith("#Phosphor-Fill")))
                    App.Log(new InvalidOperationException("Police d'icônes Phosphor introuvable dans les ressources. Familles vues : " + string.Join(", ", fams)));
            }
            catch (Exception ex) { App.Log(ex); }
        }

        /// <summary>Démarre ou arrête l'API locale selon les réglages.</summary>
        void ApplyLiveApi()
        {
            try
            {
                int port = Math.Clamp(S.LiveApiPort, 1024, 65535);
                if (!S.LiveApi) _live.Stop();
                else if (_live.Port != port) _live.Start(port);
                RefreshLive();
            }
            catch (Exception ex) { App.Log(ex); }
        }

        Border MoreCard(string title, string sub, UIElement content)
        {
            var sp = new StackPanel();
            var h = new TextBlock { Text = title, Style = (Style)FindResource("H") };
            sp.Children.Add(h);
            if (!string.IsNullOrEmpty(sub)) { var t = MoreSub(sub); t.Margin = new Thickness(0, 2, 0, 6); sp.Children.Add(t); }
            else h.Margin = new Thickness(0, 0, 0, 6);
            sp.Children.Add(content);
            return new Border { Style = (Style)FindResource("Card"), Child = sp };
        }

        TextBlock MoreSub(string text) => new() { Text = text, Style = (Style)FindResource("Sub"), TextWrapping = TextWrapping.Wrap };

        Button MorePill(string text, Action act, string tip = null)
        {
            var b = new Button { Content = text, Style = (Style)FindResource("PillBtn"), Margin = new Thickness(2), ToolTip = tip };
            b.Click += (s, e) => { try { act(); } catch (Exception ex) { App.Log(ex); } };
            return b;
        }

        /// <summary>Retour visuel qui marche île ouverte (les aperçus attendraient sa fermeture).</summary>
        void Flash(Button b, string text)
        {
            var old = b.Content;
            b.Content = text;
            var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.6) };
            t.Tick += (s, e) => { t.Stop(); if (Equals(b.Content, text)) b.Content = old; };
            t.Start();
        }

        void OnMoreShown()
        {
            RefreshWindows(true);
            RefreshLive();
            RefreshAudioOutputs();
            RefreshHistory();
            _ = RefreshRadios();
        }

        // ================== Fenêtres ouvertes ==================
        void RefreshWindows(bool force = false)
        {
            if (_winHost == null) return;
            var list = WindowService.List(_hwnd);
            var sig = string.Join("|", list.Select(w => w.Handle + ":" + w.Title));
            if (!force && sig == _winSig) return;
            _winSig = sig;
            _winHost.Children.Clear();
            foreach (var w in list)
            {
                var row = new DockPanel();
                if (w.Icon != null)
                {
                    var img = new Image { Source = w.Icon, Width = 18, Height = 18, Margin = new Thickness(0, 0, 7, 0) };
                    DockPanel.SetDock(img, Dock.Left);
                    row.Children.Add(img);
                }
                var t = new TextBlock { Text = w.Title, FontSize = 11.5, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
                t.SetResourceReference(TextBlock.ForegroundProperty, "Fg");
                row.Children.Add(t);
                var handle = w.Handle;
                var b = new Button
                {
                    Template = RowTemplate(), Content = row, Padding = new Thickness(8, 6, 8, 6), Margin = new Thickness(0, 1, 0, 1),
                    Cursor = Cursors.Hand, Focusable = false, ToolTip = $"{w.Title}\n{w.App}"
                };
                b.Click += (s, e) =>
                {
                    WindowService.Activate(handle);
                    if (!_pinned) Collapse();
                };
                _winHost.Children.Add(b);
            }
            if (list.Count == 0) _winHost.Children.Add(MoreSub("Aucune fenêtre ouverte."));
        }

        // ================== Activités en direct ==================
        void OnLiveChanged()
        {
            _liveList = _live.Snapshot();
            if (_mode == Mode.Expanded && PanelMore.Visibility == Visibility.Visible) RefreshLive();
            UpdateCompact();
        }

        static string LiveText(LiveActivity a)
        {
            if (a.IsTimer) return $"{a.Title} · {ShortTime(a.EndsAt.Value - DateTime.Now)}";
            if (a.Progress.HasValue) return $"{a.Title} · {a.Progress.Value * 100:0} %";
            return string.IsNullOrEmpty(a.Body) ? a.Title : $"{a.Title} · {a.Body}";
        }

        static double LiveProgress(LiveActivity a)
        {
            if (a.IsTimer)
            {
                double total = (a.EndsAt.Value - a.StartedAt).TotalSeconds;
                return total <= 0 ? 1 : Math.Clamp((DateTime.Now - a.StartedAt).TotalSeconds / total, 0, 1);
            }
            return a.Progress ?? 0;
        }

        void RefreshLive()
        {
            if (_liveHost == null) return;
            _liveHost.Children.Clear();
            foreach (var a in _liveList)
            {
                var g = new Grid { Margin = new Thickness(0, 2, 0, 6) };
                g.ColumnDefinitions.Add(new ColumnDefinition());
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var sp = new StackPanel();
                var t = new TextBlock { Text = LiveText(a), FontSize = 12, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
                t.SetResourceReference(TextBlock.ForegroundProperty, "Fg");
                sp.Children.Add(t);
                if (!a.IsTimer && a.Progress.HasValue && !string.IsNullOrEmpty(a.Body)) sp.Children.Add(MoreSub(a.Body));
                if (a.IsTimer || a.Progress.HasValue)
                    sp.Children.Add(new ProgressBar { Style = (Style)FindResource("Thin"), Maximum = 1, Value = LiveProgress(a), Margin = new Thickness(0, 4, 0, 0) });
                g.Children.Add(sp);
                var id = a.Id;
                var x = new Button { Style = (Style)FindResource("IconBtn"), Content = "\uE4F6", FontSize = 10, Width = 24, Height = 24, ToolTip = "Retirer", VerticalAlignment = VerticalAlignment.Top };
                x.Click += (s, e) => _live.Remove(id);
                Grid.SetColumn(x, 1);
                g.Children.Add(x);
                _liveHost.Children.Add(g);
            }
            _liveHint.Text = !S.LiveApi ? "API locale désactivée dans les réglages. Les minuteurs nommés (onglet Lancer : « 15 min thé ») s'affichent quand même ici."
                : _live.Port == 0 ? "API locale indisponible : " + (_live.Error ?? "port occupé") + ". Change le port dans les réglages."
                : _liveList.Count == 0 ? $"Rien en cours. Tes scripts peuvent afficher une progression ici : POST http://127.0.0.1:{_live.Port}/activity (jeton dans Réglages › Activités en direct). Ou tape « 15 min thé » dans l'onglet Lancer."
                : $"API locale : http://127.0.0.1:{_live.Port}";
        }

        // ================== Minuteurs nommés + recherche d'applis (onglet Lancer) ==================
        static readonly Regex TimerCmd = new(@"^(\d+(?:[.,]\d+)?)\s*(secondes?|sec|s|minutes?|min|mn|m|heures?|h)\s*(.*)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        static readonly string[] ConvWords = { "en ", "in ", "to ", "vers ", "=", "->" };

        /// <summary>« 15 min thé » → (15 minutes, "thé"). Renvoie false pour une conversion (« 10 min en s »).</summary>
        static bool ParseTimerCommand(string input, out TimeSpan dur, out string name)
        {
            dur = default; name = null;
            var m = TimerCmd.Match((input ?? "").Trim());
            if (!m.Success) return false;
            var rest = m.Groups[3].Value.Trim();
            if (ConvWords.Any(w => (rest + " ").StartsWith(w, StringComparison.OrdinalIgnoreCase))) return false;
            if (!double.TryParse(m.Groups[1].Value.Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var n) || n <= 0) return false;
            var u = m.Groups[2].Value.ToLowerInvariant();
            // « 10 m » seul reste une longueur (mètres) : il faut un nom pour en faire un minuteur
            if (u == "m" && rest.Length == 0) return false;
            dur = u.StartsWith("s") ? TimeSpan.FromSeconds(n) : u.StartsWith("h") ? TimeSpan.FromHours(n) : TimeSpan.FromMinutes(n);
            if (dur.TotalSeconds < 1 || dur.TotalHours > 24) return false;
            name = rest.Length == 0 ? "Minuteur" : char.ToUpper(rest[0]) + rest.Substring(1);
            return true;
        }

        void StartNamedTimer(TimeSpan dur, string name)
        {
            _live.Set(new LiveActivity
            {
                Id = "timer:" + Guid.NewGuid().ToString("N").Substring(0, 8), Title = name, Glyph = "\uE492",
                StartedAt = DateTime.Now, EndsAt = DateTime.Now + dur
            });
        }

        List<(string Name, string Path)> _apps;

        /// <summary>Raccourcis du menu Démarrer (indexés au premier besoin).</summary>
        List<(string Name, string Path)> StartMenuApps()
        {
            if (_apps != null) return _apps;
            var list = new List<(string, string)>();
            foreach (var root in new[] { Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), Environment.GetFolderPath(Environment.SpecialFolder.Programs) })
            {
                try
                {
                    if (!Directory.Exists(root)) continue;
                    foreach (var f in Directory.EnumerateFiles(root, "*.lnk", SearchOption.AllDirectories))
                    {
                        var n = Path.GetFileNameWithoutExtension(f);
                        if (n.Contains("désinstall", StringComparison.OrdinalIgnoreCase) || n.Contains("uninstall", StringComparison.OrdinalIgnoreCase)) continue;
                        list.Add((n, f));
                    }
                }
                catch { }
            }
            return _apps = list.GroupBy(a => a.Item1, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).OrderBy(a => a.Item1.Length).ToList();
        }

        (string Name, string Path)? FindApp(string input)
        {
            input = (input ?? "").Trim();
            if (input.Length < 2) return null;
            var apps = StartMenuApps();
            var hit = apps.FirstOrDefault(a => a.Name.StartsWith(input, StringComparison.OrdinalIgnoreCase));
            if (hit.Name == null) hit = apps.FirstOrDefault(a => a.Name.Contains(input, StringComparison.OrdinalIgnoreCase));
            return hit.Name == null ? null : hit;
        }

        /// <summary>Indice affiché sous le champ quand ce n'est pas un calcul : minuteur nommé ou appli à ouvrir.</summary>
        string LaunchHint(string input)
        {
            if (ParseTimerCommand(input, out var d, out var n)) return $"↵ Minuteur {FmtT(d)} · {n}";
            var app = FindApp(input);
            return app.HasValue ? "↵ Ouvrir " + app.Value.Name : null;
        }

        /// <summary>Entrée dans le champ Lancer. Renvoie true si la commande a été prise en charge (minuteur ou appli).</summary>
        bool LaunchCommit(string input, bool calcFound)
        {
            if (ParseTimerCommand(input, out var d, out var n))
            {
                StartNamedTimer(d, n);
                CalcInput.Clear();
                CalcResult.Text = $"Minuteur « {n} » lancé ✔";
                return true;
            }
            if (calcFound) return false;
            var app = FindApp(input);
            if (!app.HasValue) return false;
            try { AppLauncher.Launch(app.Value.Path); } catch (Exception ex) { App.Log(ex); }
            CalcInput.Clear();
            CalcResult.Text = "";
            if (!_pinned) Collapse();
            return true;
        }

        // ================== Actions rapides ==================
        Button _micBtn, _wifiBtn, _btBtn;

        void BuildToggles()
        {
            _toggleHost.Children.Clear();
            _micBtn = MorePill("Micro", ToggleMicGlobal, "Couper / réactiver le micro pour toutes les applis (Ctrl+Alt+M)");
            _wifiBtn = MorePill("Wi-Fi", () => _ = ToggleRadio(RadioKind.WiFi, _wifiBtn, "Wi-Fi"));
            _btBtn = MorePill("Bluetooth", () => _ = ToggleRadio(RadioKind.Bluetooth, _btBtn, "Bluetooth"));
            _toggleHost.Children.Add(_micBtn);
            _toggleHost.Children.Add(_wifiBtn);
            _toggleHost.Children.Add(_btBtn);
            _toggleHost.Children.Add(MorePill("Pipette / règle", StartPicker, "Clic : copie la couleur d'un pixel · glisser : mesure en pixels"));
            Button clip = null;
            clip = MorePill("Clip de jeu", () => { SendGameClip(); Flash(clip, "Demandé ✔"); }, "Enregistre les 30 dernières secondes via la Xbox Game Bar (Win+Alt+G). L'enregistrement en arrière-plan doit être activé dans Windows.");
            _toggleHost.Children.Add(clip);
            _toggleHost.Children.Add(MorePill("Mode nuit", () => AppLauncher.Launch("ms-settings:nightlight"), "Ouvre le réglage de l'éclairage nocturne"));
            _toggleHost.Children.Add(MorePill("Économie d'énergie", () => AppLauncher.Launch("ms-settings:batterysaver"), "Ouvre le réglage de l'économiseur de batterie"));
            UpdateMicBtn();
        }

        void UpdateMicBtn()
        {
            if (_micBtn == null) return;
            bool muted = _vol.MicMuted;
            _micBtn.Content = muted ? "Micro coupé" : "Micro actif";
            if (muted) _micBtn.Foreground = Frozen(Color.FromRgb(0xFF, 0x45, 0x3A));
            else _micBtn.SetResourceReference(ForegroundProperty, "Fg");
        }

        /// <summary>Coupe / réactive le micro par défaut pour toutes les applis.</summary>
        void ToggleMicGlobal()
        {
            var r = _vol.ToggleMicMute();
            UpdateMicBtn();
            if (r == null) { ShowPeek(new PeekData { Key = "alert", Glyph = "\uE326", Title = "Aucun micro détecté", Duration = TimeSpan.FromSeconds(2), Width = 280, Height = 48 }); return; }
            ShowPeek(new PeekData { Key = "osd", Glyph = r.Value ? "\uE328" : "\uE326", Title = r.Value ? "Micro coupé" : "Micro réactivé", Duration = TimeSpan.FromSeconds(1.6), Width = 260, Height = 48 });
            UpdateVoiceUI();
        }

        async Task RefreshRadios()
        {
            try
            {
                var w = await RadioService.StateAsync(RadioKind.WiFi);
                var b = await RadioService.StateAsync(RadioKind.Bluetooth);
                if (_wifiBtn != null) { _wifiBtn.Content = w == null ? "Wi-Fi —" : w.Value ? "Wi-Fi activé" : "Wi-Fi coupé"; _wifiBtn.IsEnabled = w != null; }
                if (_btBtn != null) { _btBtn.Content = b == null ? "Bluetooth —" : b.Value ? "Bluetooth activé" : "Bluetooth coupé"; _btBtn.IsEnabled = b != null; }
            }
            catch (Exception ex) { App.Log(ex); }
        }

        async Task ToggleRadio(RadioKind kind, Button b, string label)
        {
            try
            {
                b.IsEnabled = false;
                var on = await RadioService.ToggleAsync(kind);
                b.Content = on == null ? label + " —" : on.Value ? label + " activé" : label + " coupé";
                b.IsEnabled = on != null;
            }
            catch (Exception ex)
            {
                App.Log(ex);
                b.IsEnabled = true;
                b.ToolTip = ex.Message;
                Flash(b, "Refusé par Windows");
            }
        }

        void StartPicker()
        {
            if (!_pinned) Collapse();
            // laisse l'île se replier avant de figer l'écran
            var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(450) };
            t.Tick += (s, e) =>
            {
                t.Stop();
                try
                {
                    var p = new ScreenPicker(VisualTreeHelper.GetDpi(this));
                    p.Picked += (title, value) =>
                    {
                        try { _ignoreClip = true; Clipboard.SetText(value); } catch { _ignoreClip = false; }
                        Dispatcher.BeginInvoke(new Action(() =>
                            ShowPeek(new PeekData { Key = "alert", Glyph = "\uE568", Title = title, Body = value, Duration = TimeSpan.FromSeconds(3), Width = 320, Height = 68 })), DispatcherPriority.Background);
                    };
                    p.Show();
                }
                catch (Exception ex) { App.Log(ex); }
            };
            t.Start();
        }

        [StructLayout(LayoutKind.Sequential)]
        struct INPUT { public uint type; public KEYBDINPUT ki; public ulong pad; }
        [StructLayout(LayoutKind.Sequential)]
        struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr dwExtraInfo; }
        [DllImport("user32.dll", SetLastError = true)] static extern uint SendInput(uint n, INPUT[] inputs, int size);

        /// <summary>Win+Alt+G : « enregistrer ce qui vient de se passer » de la Xbox Game Bar.</summary>
        static void SendGameClip()
        {
            try
            {
                ushort[] keys = { 0x5B /*Win*/, 0x12 /*Alt*/, 0x47 /*G*/ };
                var inputs = new INPUT[6];
                for (int i = 0; i < 3; i++) inputs[i] = new INPUT { type = 1, ki = new KEYBDINPUT { wVk = keys[i] } };
                for (int i = 0; i < 3; i++) inputs[3 + i] = new INPUT { type = 1, ki = new KEYBDINPUT { wVk = keys[2 - i], dwFlags = 2 /*KEYUP*/ } };
                SendInput(6, inputs, Marshal.SizeOf<INPUT>());
            }
            catch (Exception ex) { App.Log(ex); }
        }

        // ================== Sortie audio ==================
        void RefreshAudioOutputs()
        {
            if (_audioHost == null) return;
            _audioHost.Children.Clear();
            var outs = _vol.Outputs();
            foreach (var (id, name, isDefault) in outs)
            {
                var t = new TextBlock { Text = (isDefault ? "● " : "○ ") + name, FontSize = 11.5, TextTrimming = TextTrimming.CharacterEllipsis };
                if (isDefault) t.SetResourceReference(TextBlock.ForegroundProperty, "Accent");
                else t.SetResourceReference(TextBlock.ForegroundProperty, "Fg");
                var b = new Button
                {
                    Template = RowTemplate(), Content = t, Padding = new Thickness(8, 6, 8, 6), Cursor = Cursors.Hand, Focusable = false, ToolTip = name
                };
                var devId = id;
                b.Click += (s, e) =>
                {
                    if (!_vol.SetDefaultOutput(devId)) b.ToolTip = "Windows a refusé le changement de sortie.";
                    RefreshAudioOutputs();
                };
                _audioHost.Children.Add(b);
            }
            if (outs.Count == 0) _audioHost.Children.Add(MoreSub("Aucune sortie audio active."));
        }

        // ================== Historique d'écoute ==================
        string _histKey;

        void RecordHistory(MediaState st)
        {
            if (!st.HasMedia || !st.IsPlaying || string.IsNullOrWhiteSpace(st.Title) || st.Key == _histKey) return;
            _histKey = st.Key;
            S.History ??= new();
            var last = S.History.FirstOrDefault();
            if (last != null && last.Title == st.Title && last.Artist == st.Artist) return;
            S.History.Insert(0, new TrackEntry { Title = st.Title, Artist = st.Artist, App = PrettyApp(st.AppId), Time = DateTime.Now });
            while (S.History.Count > 50) S.History.RemoveAt(S.History.Count - 1);
            S.Save();
            if (_mode == Mode.Expanded && PanelMore.Visibility == Visibility.Visible) RefreshHistory();
        }

        void RefreshHistory()
        {
            if (_histHost == null) return;
            _histHost.Children.Clear();
            foreach (var h in (S.History ?? new()).Take(15))
            {
                var sp = new StackPanel();
                var t = new TextBlock { Text = h.Title, FontSize = 11.5, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
                t.SetResourceReference(TextBlock.ForegroundProperty, "Fg");
                sp.Children.Add(t);
                var sub = MoreSub($"{h.Artist} · {h.App} · {(h.Time.Date == DateTime.Today ? h.Time.ToString("HH:mm") : h.Time.ToString("dd/MM HH:mm"))}");
                sub.TextWrapping = TextWrapping.NoWrap; sub.TextTrimming = TextTrimming.CharacterEllipsis;
                sp.Children.Add(sub);
                var text = string.IsNullOrWhiteSpace(h.Artist) ? h.Title : $"{h.Title} - {h.Artist}";
                var b = new Button
                {
                    Template = RowTemplate(), Content = sp, Padding = new Thickness(8, 5, 8, 5), Cursor = Cursors.Hand, Focusable = false,
                    ToolTip = "Copier « " + text + " »"
                };
                b.Click += (s, e) =>
                {
                    try { _ignoreClip = true; Clipboard.SetText(text); t.Text = "Copié ✔"; } catch { _ignoreClip = false; }
                    var back = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
                    back.Tick += (s2, e2) => { back.Stop(); t.Text = h.Title; };
                    back.Start();
                };
                _histHost.Children.Add(b);
            }
            if (_histHost.Children.Count == 0) _histHost.Children.Add(MoreSub("Rien pour l'instant : les titres joués apparaîtront ici."));
        }

        // ================== Profils ==================
        void BuildProfiles()
        {
            if (_profileHost == null) return;
            _profileHost.Children.Clear();
            foreach (var p in S.Profiles ?? new())
            {
                var prof = p;
                var b = MorePill(p.Name, () => ApplyProfile(prof));
                if (string.Equals(S.ActiveProfile, p.Name, StringComparison.OrdinalIgnoreCase)) b.SetResourceReference(ForegroundProperty, "Accent");
                b.MouseRightButtonUp += (s, e) =>
                {
                    e.Handled = true;
                    CaptureProfile(prof);
                    S.ActiveProfile = prof.Name;
                    S.Save();
                    BuildProfiles();
                    var nb = _profileHost.Children.OfType<Button>().FirstOrDefault(x => Equals(x.Content, prof.Name));
                    if (nb != null) Flash(nb, "Enregistré ✔");
                };
                _profileHost.Children.Add(b);
            }
        }

        void CaptureProfile(ProfileData p)
        {
            p.DoNotDisturb = S.DoNotDisturb; p.Theme = S.Theme; p.FpsCounter = S.FpsCounter; p.HideInFullscreen = S.HideInFullscreen;
            p.ShowMedia = S.ShowMedia; p.ShowNotif = S.ShowNotif; p.ShowTools = S.ShowTools; p.ShowFiles = S.ShowFiles;
            p.ShowSystem = S.ShowSystem; p.ShowInfo = S.ShowInfo; p.ShowLaunch = S.ShowLaunch;
        }

        void ApplyProfile(ProfileData p)
        {
            S.DoNotDisturb = p.DoNotDisturb; S.Theme = p.Theme ?? "Sombre"; S.FpsCounter = p.FpsCounter; S.HideInFullscreen = p.HideInFullscreen;
            S.ShowMedia = p.ShowMedia; S.ShowNotif = p.ShowNotif; S.ShowTools = p.ShowTools; S.ShowFiles = p.ShowFiles;
            S.ShowSystem = p.ShowSystem; S.ShowInfo = p.ShowInfo; S.ShowLaunch = p.ShowLaunch;
            S.ActiveProfile = p.Name;
            S.Save();
            ApplySettings();
            BuildProfiles();
        }

        // ================== Suivi de l'écran de la souris ==================
        int _mouseScreen = -1;

        /// <summary>Index de l'écran où afficher l'île : celui de la souris si l'option est active, sinon celui des réglages.</summary>
        int TargetScreen()
        {
            var screens = System.Windows.Forms.Screen.AllScreens;
            if (S.FollowMouseScreen && _mouseScreen >= 0 && _mouseScreen < screens.Length) return _mouseScreen;
            return Math.Clamp(S.ScreenIndex, 0, screens.Length - 1);
        }

        void FollowMouseTick()
        {
            if (!S.FollowMouseScreen) { _mouseScreen = -1; return; }
            if (_mode != Mode.Compact || HitZone.IsMouseOver) return; // ne bouge jamais pendant qu'on s'en sert
            try
            {
                var screens = System.Windows.Forms.Screen.AllScreens;
                var cur = System.Windows.Forms.Screen.FromPoint(System.Windows.Forms.Cursor.Position);
                int idx = Array.FindIndex(screens, s => s.DeviceName == cur.DeviceName);
                if (idx < 0 || idx == _mouseScreen) return;
                bool first = _mouseScreen < 0;
                _mouseScreen = idx;
                if (!first || idx != Math.Clamp(S.ScreenIndex, 0, screens.Length - 1)) PositionWindow();
            }
            catch { }
        }

        // ================== Gestes ==================
        void HitZone_Wheel(object sender, MouseWheelEventArgs e)
        {
            if (!S.WheelVolume || _mode == Mode.Expanded) return; // île ouverte : la molette fait défiler les listes
            _vol.Nudge(e.Delta > 0 ? 0.02f : -0.02f);
            e.Handled = true;
        }

        void HitZone_MiddleClick(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Middle) return;
            _media.TogglePlay();
            e.Handled = true;
        }
    }
}
