using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace DynamicIsland
{
    public partial class MainWindow
    {
        readonly ObservableCollection<InfoRow> _calcHist = new();
        readonly DispatcherTimer _calcDebounce = new() { Interval = TimeSpan.FromMilliseconds(250) };
        int _calcSeq;
        static ControlTemplate _tileTpl;

        void InitLaunch()
        {
            try
            {
                CalcHistory.ItemsSource = _calcHist;
                _calcDebounce.Tick += async (s, e) => { _calcDebounce.Stop(); await EvalCalc(); };
                RebuildShortcuts();
                BuildPower();
            }
            catch (Exception ex) { App.Log(ex); }

            // Vérification 20 s après le démarrage, puis toutes les 6 h (Malbo reste ouvert des jours entiers).
            _updateTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
            _updateTimer.Tick += async (s, e) =>
            {
                _updateTimer.Interval = TimeSpan.FromHours(6);
                if (!S.AutoUpdate || string.IsNullOrWhiteSpace(S.UpdateRepo)) return;
                try { await CheckUpdateAsync(false); } catch (Exception ex) { App.Log(ex); }
            };
            _updateTimer.Start();
        }

        DispatcherTimer _updateTimer;
        UpdateInfo _pendingUpdate;
        bool _updating;

        // ================== Calcul ==================
        void CalcInput_KeyUp(object sender, KeyEventArgs e)
        {
            _calcDebounce.Stop();
            if (e.Key == Key.Enter) { _ = CalcCommit(); return; }
            _calcDebounce.Start();
        }

        async Task<string> EvalCalc()
        {
            var input = (CalcInput.Text ?? "").Trim();
            int seq = ++_calcSeq;
            string r = null;
            if (input.Length > 0)
            {
                try { r = await Calc.EvaluateAsync(input); } catch (Exception ex) { App.Log(ex); }
            }
            if (seq == _calcSeq) CalcResult.Text = r ?? LaunchHint(input) ?? "";
            return r;
        }

        async Task CalcCommit()
        {
            var input = (CalcInput.Text ?? "").Trim();
            if (input.Length == 0) return;
            var r = await EvalCalc();
            if (LaunchCommit(input, !string.IsNullOrEmpty(r))) return; // minuteur nommé (« 15 min thé ») ou appli à ouvrir
            if (string.IsNullOrEmpty(r)) return;
            if (_calcHist.Count > 0 && _calcHist[0].Left == input && _calcHist[0].Right == r) return;
            _calcHist.Insert(0, new InfoRow { Left = input, Right = r });
            while (_calcHist.Count > 20) _calcHist.RemoveAt(_calcHist.Count - 1);
        }

        void CalcResult_Click(object sender, MouseButtonEventArgs e)
        {
            var t = CalcResult.Text;
            if (string.IsNullOrEmpty(t) || t == "Copié ✔") return;
            try
            {
                _ignoreClip = true;
                Clipboard.SetText(t);
            }
            catch { _ignoreClip = false; return; }
            e.Handled = true;
            if (_mode != Mode.Expanded)
            {
                ShowPeek(new PeekData { Key = "alert", Glyph = "\uE8C8", Title = "Copié", Body = t, Duration = TimeSpan.FromSeconds(1.5), Width = 260, Height = 48 });
                return;
            }
            // île ouverte : les peeks attendraient la fermeture → retour visuel sur place
            CalcResult.Text = "Copié ✔";
            var back = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            back.Tick += (s, a) => { back.Stop(); if (CalcResult.Text == "Copié ✔") CalcResult.Text = t; };
            back.Start();
        }

        // ================== Raccourcis ==================
        static ControlTemplate TileTemplate()
        {
            if (_tileTpl != null) return _tileTpl;
            const string x =
                "<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='Button'>" +
                "<Border x:Name='b' Background='Transparent' CornerRadius='14' Padding='{TemplateBinding Padding}'>" +
                "<ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/></Border>" +
                "<ControlTemplate.Triggers>" +
                "<Trigger Property='IsMouseOver' Value='True'><Setter TargetName='b' Property='Background' Value='{DynamicResource HoverBg}'/></Trigger>" +
                "<Trigger Property='IsPressed' Value='True'><Setter TargetName='b' Property='Opacity' Value='0.6'/></Trigger>" +
                "</ControlTemplate.Triggers></ControlTemplate>";
            _tileTpl = (ControlTemplate)System.Windows.Markup.XamlReader.Parse(x);
            return _tileTpl;
        }

        static bool IsWebUrl(string t) =>
            t != null && (t.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || t.StartsWith("https://", StringComparison.OrdinalIgnoreCase));

        /// <summary>Chemin complet d'un exécutable donné sans dossier (explorer.exe, taskmgr.exe…).</summary>
        static string ResolvePath(string target)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(target)) return null;
                if (File.Exists(target) || Directory.Exists(target)) return target;
                if (Path.IsPathRooted(target) || target.Contains(':')) return null;
                var dirs = new List<string> { Environment.SystemDirectory, Environment.GetFolderPath(Environment.SpecialFolder.Windows) };
                dirs.AddRange((Environment.GetEnvironmentVariable("PATH") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
                foreach (var d in dirs)
                {
                    try
                    {
                        var p = Path.Combine(d, target);
                        if (File.Exists(p)) return p;
                        if (!Path.HasExtension(target) && File.Exists(p + ".exe")) return p + ".exe";
                    }
                    catch { }
                }
            }
            catch { }
            return null;
        }

        void RebuildShortcuts()
        {
            ShortcutHost.Children.Clear();
            S.Shortcuts ??= new List<ShortcutData>();
            foreach (var sc in S.Shortcuts.Where(x => x != null && !string.IsNullOrWhiteSpace(x.Target)).ToList())
                ShortcutHost.Children.Add(MakeShortcut(sc));
        }

        Button MakeShortcut(ShortcutData sc)
        {
            var sp = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            var target = sc.Target.Trim();
            ImageSource icon = null;
            if (!IsWebUrl(target) && !target.StartsWith("ms-settings:", StringComparison.OrdinalIgnoreCase))
                icon = IconHelper.FromFile(ResolvePath(target));

            if (icon != null)
                sp.Children.Add(new Image { Source = icon, Width = 30, Height = 30, HorizontalAlignment = HorizontalAlignment.Center });
            else
            {
                var g = new TextBlock
                {
                    Text = IsWebUrl(target) ? "\uE774" : target.StartsWith("ms-settings:", StringComparison.OrdinalIgnoreCase) ? "\uE713" : "\uE8B7",
                    FontFamily = (FontFamily)FindResource("Icons"), FontSize = 24, Height = 30,
                    HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center
                };
                g.SetResourceReference(TextBlock.ForegroundProperty, "Accent");
                sp.Children.Add(g);
            }
            var name = new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(sc.Name) ? target : sc.Name,
                FontSize = 10.5, MaxWidth = 68, Margin = new Thickness(0, 5, 0, 0),
                TextTrimming = TextTrimming.CharacterEllipsis, TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            name.SetResourceReference(TextBlock.ForegroundProperty, "Fg");
            sp.Children.Add(name);

            var b = new Button
            {
                Template = TileTemplate(), Content = sp, Width = 76, Height = 70, Margin = new Thickness(2),
                Padding = new Thickness(4), Cursor = Cursors.Hand, Focusable = false, ToolTip = $"{sc.Name}\n{target}"
            };
            b.Click += (s, e) => { try { AppLauncher.Launch(target); } catch (Exception ex) { App.Log(ex); } };

            var menu = new ContextMenu();
            var rm = new MenuItem { Header = "Retirer" };
            rm.Click += (s, e) =>
            {
                S.Shortcuts.Remove(sc);
                S.Save();
                RebuildShortcuts();
            };
            menu.Items.Add(rm);
            bool wasPinned = false;
            menu.Opened += (s, e) => { wasPinned = _pinned; _pinned = true; }; // l'île reste ouverte pendant le menu
            menu.Closed += (s, e) => _pinned = wasPinned;
            b.ContextMenu = menu;
            return b;
        }

        void Shortcuts_Drop(object sender, DragEventArgs e)
        {
            var added = new List<ShortcutData>();
            try
            {
                if (e.Data.GetDataPresent(DataFormats.FileDrop))
                {
                    foreach (var f in (string[])e.Data.GetData(DataFormats.FileDrop) ?? Array.Empty<string>())
                    {
                        var n = Path.GetFileNameWithoutExtension(f.TrimEnd('\\'));
                        if (string.IsNullOrEmpty(n)) n = f;
                        added.Add(new ShortcutData { Name = n, Target = f });
                    }
                }
                else
                {
                    var t = (e.Data.GetData(DataFormats.UnicodeText) as string) ?? (e.Data.GetData(DataFormats.Text) as string);
                    t = t?.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0);
                    if (!string.IsNullOrEmpty(t))
                    {
                        if (t.StartsWith("www.", StringComparison.OrdinalIgnoreCase)) t = "https://" + t;
                        string n = t;
                        if (Uri.TryCreate(t, UriKind.Absolute, out var u) && !string.IsNullOrEmpty(u.Host))
                        {
                            n = u.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? u.Host.Substring(4) : u.Host;
                        }
                        added.Add(new ShortcutData { Name = n, Target = t });
                    }
                }
            }
            catch (Exception ex) { App.Log(ex); }

            e.Handled = true;
            if (added.Count == 0) return;
            S.Shortcuts ??= new List<ShortcutData>();
            foreach (var a in added)
                if (!S.Shortcuts.Any(x => string.Equals(x?.Target, a.Target, StringComparison.OrdinalIgnoreCase))) S.Shortcuts.Add(a);
            S.Save();
            RebuildShortcuts();
        }

        // ================== Alimentation ==================
        void BuildPower()
        {
            PowerHost.Children.Clear();
            AddPower("\uE72E", "Verrouiller", false, AppLauncher.Lock);
            AddPower("\uE708", "Veille", true, AppLauncher.Sleep);
            AddPower("\uE777", "Redémarrer", true, AppLauncher.Restart);
            AddPower("\uE7E8", "Éteindre", true, AppLauncher.Shutdown);
            AddPower("\uF3B1", "Déconnexion", true, AppLauncher.Logoff);
        }

        void AddPower(string glyph, string label, bool confirm, Action act)
        {
            var g = new TextBlock { Text = glyph, FontFamily = (FontFamily)FindResource("Icons"), FontSize = 13, Width = 24, VerticalAlignment = VerticalAlignment.Center };
            var t = new TextBlock { Text = label, FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
            var sp = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left };
            sp.Children.Add(g);
            sp.Children.Add(t);
            var b = new Button
            {
                Style = (Style)FindResource("PillBtn"), Content = sp, HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left, Padding = new Thickness(12, 7, 12, 7), Margin = new Thickness(0, 2, 0, 2)
            };
            // le modèle PillBtn centre le contenu : on l'étire à la largeur du bouton pour l'aligner à gauche
            b.SizeChanged += (s, e) => sp.Width = Math.Max(0, b.ActualWidth - b.Padding.Left - b.Padding.Right);

            DispatcherTimer reset = null;
            void Restore()
            {
                t.Text = label;
                if (b.Template?.FindName("b", b) is Border bd) bd.ClearValue(Border.BackgroundProperty);
                b.ClearValue(ForegroundProperty);
            }
            void Run()
            {
                try { act(); } catch (Exception ex) { App.Log(ex); }
            }

            b.Click += (s, e) =>
            {
                e.Handled = true;
                if (!confirm) { Run(); return; }
                if (reset != null && reset.IsEnabled)
                {
                    reset.Stop();
                    Restore();
                    Run();
                    return;
                }
                t.Text = "Confirmer ?";
                if (b.Template?.FindName("b", b) is Border bd) bd.Background = new SolidColorBrush(Color.FromRgb(0xFF, 0x45, 0x3A));
                b.Foreground = Brushes.White;
                if (reset == null)
                {
                    reset = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
                    reset.Tick += (s2, e2) => { reset.Stop(); Restore(); };
                }
                reset.Start();
            };

            PowerHost.Children.Add(b);
        }

        // ================== Mises à jour ==================
        static string V(Version v) => v == null ? "?" : v.Build >= 0 ? v.ToString(3) : v.ToString();

        public async Task<string> CheckUpdateAsync(bool manual)
        {
            var repo = (S.UpdateRepo ?? "").Trim();
            if (repo.Length == 0)
            {
                const string none = "Aucun dépôt GitHub configuré (Réglages › Mises à jour).";
                if (manual) ShowPeek(new PeekData { Key = "alert", Glyph = "\uE946", Title = "Mises à jour", Body = none, Duration = TimeSpan.FromSeconds(4), Width = 420, Height = 76 });
                return none;
            }

            UpdateInfo u;
            try { u = await Updater.CheckAsync(repo, S.BetaChannel); }
            catch (Exception ex)
            {
                App.Log(ex);
                var err = "Vérification impossible : " + ex.Message;
                if (manual) ShowPeek(new PeekData { Key = "alert", Glyph = "\uE783", Title = "Mises à jour", Body = err, Duration = TimeSpan.FromSeconds(5), Width = 420, Height = 76 });
                return err;
            }

            if (u == null)
            {
                var ok = $"Malbo est à jour (version {Updater.CurrentName}).";
                if (manual) ShowPeek(new PeekData { Key = "alert", Glyph = "\uE73E", Title = "Malbo est à jour", Body = $"Version {Updater.CurrentName}", Duration = TimeSpan.FromSeconds(3), Width = 340, Height = 68 });
                return ok;
            }

            // Installation automatique : on attend que l'île soit au repos pour ne pas couper l'utilisateur.
            if (!manual && (S.AutoInstallUpdates || u.Mandatory))
            {
                _pendingUpdate = u;
                TryAutoInstall();
                return $"Version {u.Name} : installation automatique dès que Malbo est au repos.";
            }

            var notes = (u.Notes ?? "").Split('\n').Select(l => l.Trim().TrimStart('#', '-', '*', ' ')).FirstOrDefault(l => l.Length > 0);
            ShowPeek(new PeekData
            {
                Key = "alert", Glyph = "\uE896",
                Title = $"Mise à jour {u.Name} dispo",
                Body = string.IsNullOrEmpty(notes) ? $"Version actuelle : {Updater.CurrentName}" : notes,
                Duration = TimeSpan.FromSeconds(12), Width = 460, Height = 76,
                Actions = new() { ("Installer", () => _ = ApplyUpdateAsync(u)) }
            });
            return $"Version {u.Name} disponible (actuelle : {Updater.CurrentName}).";
        }

        /// <summary>Appelé chaque seconde : lance la mise à jour en attente quand l'île est compacte et inutilisée.</summary>
        void TryAutoInstall()
        {
            var u = _pendingUpdate;
            if (u == null || _updating) return;
            bool idle = _mode == Mode.Compact && !HitZone.IsMouseOver && !_menuOpen && _curPeek == null;
            if (!idle) return;
            _pendingUpdate = null;
            _ = ApplyUpdateAsync(u);
        }

        async Task ApplyUpdateAsync(UpdateInfo u)
        {
            if (_updating) return;
            _updating = true;
            try
            {
                ShowPeek(new PeekData { Key = "alert", Glyph = "\uE896", Title = "Mise à jour en cours…", Body = $"Malbo {u.Name} va redémarrer", Duration = TimeSpan.FromSeconds(4), Width = 360, Height = 68 });
                await Updater.ApplyAsync(u);
            }
            catch (Exception ex)
            {
                App.Log(ex);
                Alert("Mise à jour impossible", ex.Message, "\uE783");
            }
            finally { _updating = false; }
        }
    }
}
