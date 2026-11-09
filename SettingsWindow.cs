using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace DynamicIsland
{
    /// <summary>Fenêtre de réglages construite en code.</summary>
    public class SettingsWindow : Window
    {
        readonly AppSettings _s;
        readonly StackPanel _p = new() { Margin = new Thickness(22, 10, 22, 10) };
        readonly List<Action> _commit = new();
        public event Action Saved;
        public Func<Task<string>> ConnectSpotify, ConnectDiscord, CheckUpdate;
        public Action Install;

        public SettingsWindow(AppSettings s)
        {
            _s = s;
            Title = "Réglages · Malbo";
            Icon = new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/Assets/malbo.ico"));
            Width = 540; Height = 760;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = new SolidColorBrush(Color.FromRgb(0x16, 0x16, 0x18));
            Foreground = Brushes.White;
            FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");

            var root = new DockPanel();
            var bar = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(16) };
            DockPanel.SetDock(bar, Dock.Bottom);
            bar.Children.Add(Btn("Annuler", Close));
            bar.Children.Add(Btn("Enregistrer", Save));
            root.Children.Add(bar);
            root.Children.Add(new ScrollViewer { Content = _p, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
            Content = root;

            Section("Apparence");
            Combo("Position", new[] { "Centre", "Gauche", "Droite" }, () => s.Position, v => s.Position = v);
            var screens = System.Windows.Forms.Screen.AllScreens.Select((sc, i) => $"Écran {i + 1}{(sc.Primary ? " (principal)" : "")} · {sc.Bounds.Width}×{sc.Bounds.Height}").ToArray();
            ComboIdx("Écran", screens, () => s.ScreenIndex, v => s.ScreenIndex = v);
            Combo("Thème", new[] { "Sombre", "Clair", "Auto" }, () => s.Theme, v => s.Theme = v);
            Slide("Opacité", 0.4, 1, () => s.Opacity, v => s.Opacity = v);
            Slide("Taille", 0.8, 1.5, () => s.Scale, v => s.Scale = v);
            Check("Couleur d'accent selon la pochette", () => s.AccentFromArt, v => s.AccentFromArt = v);
            Check("Paroles dans l'île compacte", () => s.LyricsInCompact, v => s.LyricsInCompact = v);
            Check("Afficher le morceau quand il change", () => s.TrackPeek, v => s.TrackPeek = v);
            Check("OSD volume", () => s.VolumeOsd, v => s.VolumeOsd = v);
            Check("OSD luminosité (portables)", () => s.BrightnessOsd, v => s.BrightnessOsd = v);
            Check("Masquer en plein écran (jeux, vidéos)", () => s.HideInFullscreen, v => s.HideInFullscreen = v);
            Check("Lancer au démarrage de Windows", () => s.StartWithWindows, v => s.StartWithWindows = v);

            Section("Île");
            Text("Délai de survol (ms)", () => s.HoverDelay.ToString(), v => { if (int.TryParse(v, out var n) && n >= 0 && n <= 5000) s.HoverDelay = n; });
            Text("Zone de survol (px)", () => s.HoverZone.ToString(), v => { if (int.TryParse(v, out var n) && n >= 0 && n <= 200) s.HoverZone = n; });
            Check("Onglet Lancer visible", () => s.ShowLaunch, v => s.ShowLaunch = v);

            Section("Onglets visibles");
            Check("Média", () => s.ShowMedia, v => s.ShowMedia = v);
            Check("Notifications", () => s.ShowNotif, v => s.ShowNotif = v);
            Check("Outils", () => s.ShowTools, v => s.ShowTools = v);
            Check("Fichiers", () => s.ShowFiles, v => s.ShowFiles = v);
            Check("Système", () => s.ShowSystem, v => s.ShowSystem = v);
            Check("Infos", () => s.ShowInfo, v => s.ShowInfo = v);
            Check("Plugins", () => s.ShowPlugins, v => s.ShowPlugins = v);

            Section("Notifications");
            Check("Masquer les bannières Windows (Malbo les affiche à la place)", () => s.HideWindowsBanners, v => s.HideWindowsBanners = v);
            Note("Les notifs restent dans le centre de notifications Windows. Les bannières reviennent quand tu quittes Malbo.");
            Check("Ne pas déranger", () => s.DoNotDisturb, v => s.DoNotDisturb = v);
            Text("Applis ignorées (séparées par des virgules)", () => s.BlockedApps, v => s.BlockedApps = v);
            Note("Windows doit autoriser l'accès : Paramètres › Confidentialité et sécurité › Notifications.");
            Link("Ouvrir les paramètres de notifications", "ms-settings:privacy-notifications");

            Section("Spotify");
            var spId = Text("Client ID", () => s.SpotifyClientId, v => s.SpotifyClientId = v);
            Note("Crée une appli sur developer.spotify.com (Dashboard › Create app) et ajoute la Redirect URI http://127.0.0.1:8898/callback, puis colle son Client ID ici.");
            Link("Ouvrir le tableau de bord Spotify", "https://developer.spotify.com/dashboard");
            var spSt = Status();
            ConnectButton("Connecter Spotify", () => ConnectSpotify, spSt, () => { s.SpotifyClientId = spId.Text.Trim(); });

            Section("Discord");
            var dcId = Text("Client ID", () => s.DiscordClientId, v => s.DiscordClientId = v);
            var dcSecret = Text("Client Secret", () => s.DiscordSecret, v => s.DiscordSecret = v);
            var dcRedirect = Text("Redirect URI", () => s.DiscordRedirect, v => s.DiscordRedirect = v);
            Note("Crée une appli sur discord.com/developers avec TON compte (Discord n'autorise la lecture du vocal qu'au propriétaire de l'appli et à ses testeurs). "
               + "Client ID = « Application ID » ; dans OAuth2 : copie le Client Secret et ajoute la redirection http://127.0.0.1. "
               + "L'appli Discord de bureau doit être ouverte : une fenêtre d'autorisation s'y affichera.");
            Link("Ouvrir le portail développeur Discord", "https://discord.com/developers/applications");
            var dcSt = Status();
            ConnectButton("Connecter Discord", () => ConnectDiscord, dcSt, () =>
            {
                s.DiscordClientId = dcId.Text.Trim();
                s.DiscordSecret = dcSecret.Text.Trim();
                s.DiscordRedirect = string.IsNullOrWhiteSpace(dcRedirect.Text) ? "http://127.0.0.1" : dcRedirect.Text.Trim();
            });

            Section("Infos");
            Text("Ville (météo)", () => s.City, v => s.City = v);
            Check("Alertes pluie (dans l'heure)", () => s.RainAlerts, v => s.RainAlerts = v);
            Text("URL ICS de l'agenda", () => s.IcsUrl, v => s.IcsUrl = v);
            Note("Google Agenda : Paramètres de l'agenda › « Adresse secrète au format iCal ». Outlook : Calendrier › Partager › Publier › lien ICS.");
            Text("Cryptos (ids CoinGecko)", () => s.Cryptos, v => s.Cryptos = v);
            Text("Actions (symboles Yahoo)", () => s.Stocks, v => s.Stocks = v);
            var leagues = new[] { "soccer/fra.1", "soccer/eng.1", "soccer/esp.1", "soccer/ita.1", "soccer/ger.1", "soccer/uefa.champions", "basketball/nba", "football/nfl", "hockey/nhl", "baseball/mlb" };
            Combo("Ligue sportive", leagues, () => s.SportLeague, v => s.SportLeague = v, editable: true);
            Text("Clé API Steam", () => s.SteamApiKey, v => s.SteamApiKey = v);
            Text("SteamID64", () => s.SteamId, v => s.SteamId = v);
            Link("Obtenir une clé API Steam", "https://steamcommunity.com/dev/apikey");
            Text("Fuseaux horaires (IDs Windows)", () => s.TimeZones, v => s.TimeZones = v);

            Section("Outils");
            Text("Pomodoro : travail (min)", () => s.PomodoroWork.ToString(), v => { if (int.TryParse(v, out var n) && n > 0) s.PomodoroWork = n; });
            Text("Pomodoro : pause (min)", () => s.PomodoroBreak.ToString(), v => { if (int.TryParse(v, out var n) && n > 0) s.PomodoroBreak = n; });
            Check("Focus : le Pomodoro coupe les notifications pendant le travail", () => s.FocusDnd, v => s.FocusDnd = v);

            Section("Jeu");
            Check("Compteur FPS en jeu", () => s.FpsCounter, v => s.FpsCounter = v);
            Text("Chemin de PresentMon (.exe)", () => s.PresentMonPath, v => s.PresentMonPath = v);
            Link("Télécharger PresentMon", "https://github.com/GameTechDev/PresentMon/releases");
            Note("PresentMon a besoin des droits administrateur : Windows les demandera au lancement d'un jeu, sauf si Malbo tourne déjà en administrateur.");

            Section("Mises à jour");
            var repo = Text("Dépôt GitHub (owner/nom)", () => s.UpdateRepo, v => s.UpdateRepo = v);
            Check("Rechercher les mises à jour (au démarrage puis toutes les 6 h)", () => s.AutoUpdate, v => s.AutoUpdate = v);
            Check("Installer automatiquement quand l'île est au repos", () => s.AutoInstallUpdates, v => s.AutoInstallUpdates = v);
            Note($"Malbo lit le manifeste {Updater.ManifestFile} du dépôt (sinon la dernière release). Le fichier téléchargé est vérifié par SHA-256 avant d'être installé. Dépôt officiel : {AppSettings.DefaultRepo}.");
            try { Note($"Version installée : {Updater.Current}"); } catch { }
            var upSt = Status();
            ConnectButton("Vérifier maintenant", () => CheckUpdate, upSt, () => { s.UpdateRepo = string.IsNullOrWhiteSpace(repo.Text) ? AppSettings.DefaultRepo : repo.Text.Trim(); }, ok: null);
            bool installed = true;
            try { installed = Installer.IsInstalled; } catch { }
            if (!installed)
            {
                var instSt = new TextBlock { FontSize = 11.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 6), Foreground = Brushes.Gray };
                var inst = Btn("Installer Malbo sur ce PC", () => { });
                inst.HorizontalAlignment = HorizontalAlignment.Left;
                inst.Click += (o, e) =>
                {
                    try
                    {
                        if (Install == null) { instSt.Text = "Indisponible"; return; }
                        Install();
                        instSt.Text = "Malbo est installé ✔";
                        inst.IsEnabled = false;
                    }
                    catch (Exception ex) { instSt.Text = ex.Message; instSt.Foreground = new SolidColorBrush(Color.FromRgb(255, 69, 58)); }
                };
                _p.Children.Add(inst);
                _p.Children.Add(instSt);
            }

            Section("Avancé");
            Note("Raccourcis : Ctrl+Alt+I ouvre/épingle l'île · Ctrl+Alt+H masque/affiche · Ctrl+Alt+Espace lecture/pause.");
            var pl = Btn("Ouvrir le dossier des plugins", () =>
            {
                Directory.CreateDirectory(AppSettings.PluginDir);
                Process.Start("explorer.exe", AppSettings.PluginDir);
            });
            pl.HorizontalAlignment = HorizontalAlignment.Left;
            _p.Children.Add(pl);
        }

        Button Btn(string text, Action a)
        {
            var b = new Button { Content = text, Style = (Style)Application.Current.FindResource("PillBtn"), Padding = new Thickness(16, 7, 16, 7), Margin = new Thickness(4) };
            b.Click += (s, e) => a();
            return b;
        }

        void Section(string t) => _p.Children.Add(new TextBlock
        {
            Text = t, FontSize = 15, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 18, 0, 6),
            Foreground = new SolidColorBrush(Color.FromRgb(10, 132, 255))
        });

        void Note(string t) => _p.Children.Add(new TextBlock { Text = t, FontSize = 11, TextWrapping = TextWrapping.Wrap, Foreground = Brushes.Gray, Margin = new Thickness(0, 2, 0, 6) });

        void Link(string t, string url)
        {
            var tb = new TextBlock { Text = t + " ↗", FontSize = 12, Foreground = new SolidColorBrush(Color.FromRgb(100, 170, 255)), Cursor = System.Windows.Input.Cursors.Hand, Margin = new Thickness(0, 0, 0, 6) };
            tb.MouseLeftButtonUp += (s, e) => { try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { } };
            _p.Children.Add(tb);
        }

        Grid Row(string label, UIElement ctl)
        {
            var g = new Grid { Margin = new Thickness(0, 3, 0, 3) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, FontSize = 12.5, TextWrapping = TextWrapping.Wrap });
            Grid.SetColumn(ctl, 1);
            g.Children.Add(ctl);
            _p.Children.Add(g);
            return g;
        }

        void Check(string label, Func<bool> get, Action<bool> set)
        {
            var c = new CheckBox { Content = label, IsChecked = get(), Margin = new Thickness(0, 4, 0, 4), Foreground = Brushes.White };
            _p.Children.Add(c);
            _commit.Add(() => set(c.IsChecked == true));
        }

        TextBox Text(string label, Func<string> get, Action<string> set)
        {
            var t = new TextBox { Text = get() ?? "", Style = (Style)Application.Current.FindResource("Input") };
            Row(label, t);
            _commit.Add(() => set(t.Text.Trim()));
            return t;
        }

        TextBlock Status() => new() { FontSize = 11.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 6), Foreground = Brushes.Gray };

        /// <summary>Bouton qui enregistre d'abord les valeurs saisies (pre), appelle l'action puis affiche le résultat.
        /// ok = texte affiché quand l'action renvoie null ; ok == null → le message renvoyé est toujours affiché tel quel.</summary>
        void ConnectButton(string label, Func<Func<Task<string>>> action, TextBlock status, Action pre, string ok = "Connecté ✔")
        {
            Button b = null;
            b = Btn(label, async () =>
            {
                var f = action();
                if (f == null) { status.Text = "Indisponible"; return; }
                try { pre?.Invoke(); _s.Save(); } catch { }
                b.IsEnabled = false;
                status.Foreground = Brushes.Gray;
                status.Text = ok != null ? "Connexion… valide l'autorisation dans la fenêtre qui s'ouvre." : "Vérification…";
                try
                {
                    var r = await f();
                    if (ok == null) status.Text = r ?? "";
                    else
                    {
                        status.Text = r == null ? ok : r;
                        status.Foreground = r == null ? new SolidColorBrush(Color.FromRgb(48, 209, 88)) : new SolidColorBrush(Color.FromRgb(255, 69, 58));
                    }
                }
                catch (Exception ex)
                {
                    status.Text = ex.Message;
                    status.Foreground = new SolidColorBrush(Color.FromRgb(255, 69, 58));
                }
                finally { b.IsEnabled = true; }
            });
            b.HorizontalAlignment = HorizontalAlignment.Left;
            _p.Children.Add(b);
            _p.Children.Add(status);
        }

        void Combo(string label, string[] items, Func<string> get, Action<string> set, bool editable = false)
        {
            var c = new ComboBox { ItemsSource = items, IsEditable = editable, Text = get(), SelectedItem = items.Contains(get()) ? get() : null };
            Row(label, c);
            _commit.Add(() => { var v = editable ? c.Text : c.SelectedItem as string; if (!string.IsNullOrWhiteSpace(v)) set(v); });
        }

        void ComboIdx(string label, string[] items, Func<int> get, Action<int> set)
        {
            var c = new ComboBox { ItemsSource = items, SelectedIndex = Math.Clamp(get(), 0, items.Length - 1) };
            Row(label, c);
            _commit.Add(() => set(Math.Max(0, c.SelectedIndex)));
        }

        void Slide(string label, double min, double max, Func<double> get, Action<double> set)
        {
            var sp = new DockPanel();
            var val = new TextBlock { Width = 44, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center, FontSize = 12 };
            DockPanel.SetDock(val, Dock.Right);
            var sl = new Slider { Minimum = min, Maximum = max, Value = get(), IsMoveToPointEnabled = true, VerticalAlignment = VerticalAlignment.Center };
            sl.ValueChanged += (s, e) => val.Text = $"{sl.Value * 100:0} %";
            val.Text = $"{sl.Value * 100:0} %";
            sp.Children.Add(val);
            sp.Children.Add(sl);
            Row(label, sp);
            _commit.Add(() => set(Math.Round(sl.Value, 2)));
        }

        void Save()
        {
            foreach (var c in _commit) c();
            _s.Save();
            Saved?.Invoke();
            Close();
        }
    }
}
