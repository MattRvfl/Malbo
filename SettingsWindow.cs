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
        // Page de la catégorie en cours de construction (chaque Section() en ouvre une nouvelle).
        StackPanel _p = new() { Margin = new Thickness(22, 10, 22, 10) };
        readonly List<(string Title, StackPanel Page, RadioButton Nav, System.Text.StringBuilder Words)> _pages = new();
        readonly StackPanel _nav = new();
        readonly ScrollViewer _host = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Brush _fg = Brushes.White;
        readonly List<Action> _commit = new();
        public event Action Saved;
        public Func<Task<string>> ConnectSpotify, ConnectDiscord, CheckUpdate;
        public Action Install;

        public SettingsWindow(AppSettings s)
        {
            _s = s;
            Title = "Réglages · Malbo";
            Icon = new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/Assets/malbo.ico"));
            Width = 780; Height = 640; MinWidth = 640; MinHeight = 420;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            // Même thème que l'île (clair / sombre / auto) : on reprend ses couleurs.
            SetResourceReference(BackgroundProperty, "IslandBg");
            SetResourceReference(ForegroundProperty, "Fg");
            _fg = Application.Current.Resources["Fg"] as Brush ?? Brushes.White;
            FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");

            var root = new DockPanel();
            var bar = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(16, 8, 16, 14) };
            DockPanel.SetDock(bar, Dock.Bottom);
            bar.Children.Add(Btn("Annuler", Close));
            bar.Children.Add(Btn("Enregistrer", Save));
            root.Children.Add(bar);

            // Colonne de gauche : recherche + catégories
            var side = new DockPanel { Width = 190, Margin = new Thickness(14, 14, 0, 0) };
            DockPanel.SetDock(side, Dock.Left);
            var search = new TextBox { Style = (Style)Application.Current.FindResource("Input"), Margin = new Thickness(0, 0, 0, 8), ToolTip = "Rechercher un réglage" };
            var hint = new TextBlock { Text = "Rechercher…", IsHitTestVisible = false, Margin = new Thickness(10, 7, 0, 0), FontSize = 12, Opacity = 0.5 };
            var searchBox = new Grid();
            searchBox.Children.Add(search);
            searchBox.Children.Add(hint);
            DockPanel.SetDock(searchBox, Dock.Top);
            side.Children.Add(searchBox);
            side.Children.Add(new ScrollViewer { Content = _nav, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
            search.TextChanged += (o, e) =>
            {
                hint.Visibility = search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
                Filter(search.Text);
            };
            root.Children.Add(side);
            root.Children.Add(_host);
            Content = root;
            PreviewKeyDown += (o, e) => { if (e.Key == System.Windows.Input.Key.Escape) Close(); };

            Section("Apparence");
            Combo("Position", new[] { "Centre", "Gauche", "Droite" }, () => s.Position, v => s.Position = v);
            var screens = System.Windows.Forms.Screen.AllScreens.Select((sc, i) => $"Écran {i + 1}{(sc.Primary ? " (principal)" : "")} · {sc.Bounds.Width}×{sc.Bounds.Height}").ToArray();
            ComboIdx("Écran", screens, () => s.ScreenIndex, v => s.ScreenIndex = v);
            Combo("Thème", new[] { "Sombre", "Clair", "Auto" }, () => s.Theme, v => s.Theme = v);
            Slide("Opacité", 0.4, 1, () => s.Opacity, v => s.Opacity = v);
            Text("Décalage vers le bas (px)", () => s.TopOffset.ToString(), v => { if (int.TryParse(v, out var n) && n >= 0 && n <= 200) s.TopOffset = n; });
            Note("Descend l'île pour qu'elle ne recouvre pas les onglets du navigateur ou la barre de titre des fenêtres (essaie 30 à 40).");
            Slide("Taille", 0.8, 1.5, () => s.Scale, v => s.Scale = v);
            Check("L'île suit l'écran où se trouve la souris", () => s.FollowMouseScreen, v => s.FollowMouseScreen = v);
            Check("Couleur d'accent selon la pochette", () => s.AccentFromArt, v => s.AccentFromArt = v);
            Text("Couleur d'accent perso (#RRGGBB)", () => s.AccentColor, v => { if (System.Text.RegularExpressions.Regex.IsMatch(v, "^#?[0-9a-fA-F]{6}$")) s.AccentColor = v.StartsWith("#") ? v : "#" + v; });
            Note("Utilisée quand « selon la pochette » est décoché.");
            Check("Molette sur l'île compacte = volume · clic molette = lecture/pause", () => s.WheelVolume, v => s.WheelVolume = v);
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

            Section("Onglets");
            Text("Onglets principaux, dans l'ordre", () => s.MainTabs, v => s.MainTabs = string.IsNullOrWhiteSpace(v) ? MainWindow.DefaultMainTabs : v);
            Note("Ceux-ci sont dans la barre ; les autres passent derrière le bouton « … ». Noms : Média, Notifs, Outils, Système, Lancer, Fichiers, Infos, Bureau, Plugins.");
            Note("Onglets activés :");
            Check("Média", () => s.ShowMedia, v => s.ShowMedia = v);
            Check("Notifications", () => s.ShowNotif, v => s.ShowNotif = v);
            Check("Outils", () => s.ShowTools, v => s.ShowTools = v);
            Check("Fichiers", () => s.ShowFiles, v => s.ShowFiles = v);
            Check("Système", () => s.ShowSystem, v => s.ShowSystem = v);
            Check("Infos", () => s.ShowInfo, v => s.ShowInfo = v);
            Check("Bureau (fenêtres, activités, sortie audio, notes, astuces…)", () => s.ShowMore, v => s.ShowMore = v);
            Check("Plugins", () => s.ShowPlugins, v => s.ShowPlugins = v);

            Section("Notifications");
            Check("Masquer les bannières Windows (Malbo les affiche à la place)", () => s.HideWindowsBanners, v => s.HideWindowsBanners = v);
            Note("Les notifs restent dans le centre de notifications Windows. Les bannières reviennent quand tu quittes Malbo.");
            Check("Ne pas déranger", () => s.DoNotDisturb, v => s.DoNotDisturb = v);
            Text("Applis ignorées (séparées par des virgules)", () => s.BlockedApps, v => s.BlockedApps = v);
            Check("Regrouper les messages d'une même conversation", () => s.GroupNotifs, v => s.GroupNotifs = v);
            Text("Heures calmes (ex. 23:00-07:00)", () => s.QuietHours, v => s.QuietHours = v);
            Text("Mots-clés prioritaires (virgules)", () => s.PriorityKeywords, v => s.PriorityKeywords = v);
            Note("Une notification qui contient un mot-clé prioritaire (nom d'un contact, « urgent »…) s'affiche même en Ne pas déranger, pendant un appel ou les heures calmes.");
            Text("Rappel plus tard (minutes)", () => s.SnoozeMinutes.ToString(), v => { if (int.TryParse(v, out var n) && n > 0 && n <= 720) s.SnoozeMinutes = n; });
            Check("Pendant un appel (Teams, Zoom, Meet, Discord…) : notifications en pause", () => s.MeetingDnd, v => s.MeetingDnd = v);
            Check("Alertes système (disque plein, surchauffe, batterie Bluetooth faible)", () => s.SystemAlerts, v => s.SystemAlerts = v);
            Note("Windows doit autoriser l'accès : Paramètres › Confidentialité et sécurité › Notifications.");
            Link("Ouvrir les paramètres de notifications", "ms-settings:privacy-notifications");

            Section("Spotify");
            var spId = Text("Client ID", () => s.SpotifyClientId, v => s.SpotifyClientId = v);
            Note("Crée une appli sur developer.spotify.com (Dashboard › Create app) et ajoute la Redirect URI http://127.0.0.1:8898/callback, puis colle son Client ID ici.");
            Link("Ouvrir le tableau de bord Spotify", "https://developer.spotify.com/dashboard");
            var spSt = Status();
            ConnectButton("Connecter Spotify", () => ConnectSpotify, spSt, () => { s.SpotifyClientId = spId.Text.Trim(); });

            Section("Paroles");
            Check("Traduire les paroles", () => s.TranslateLyrics, v => s.TranslateLyrics = v);
            Text("Langue de traduction (fr, en, es…)", () => s.TranslateTo, v => { if (v.Length >= 2 && v.Length <= 5) s.TranslateTo = v.ToLowerInvariant(); });
            Note("Passe par le service public de Google Traduction : les paroles du titre en cours lui sont envoyées. Désactivé par défaut.");

            Section("Activités en direct");
            Check("API locale pour tes scripts (127.0.0.1 uniquement)", () => s.LiveApi, v => s.LiveApi = v);
            Text("Port", () => s.LiveApiPort.ToString(), v => { if (int.TryParse(v, out var n) && n >= 1024 && n <= 65535) s.LiveApiPort = n; });
            Text("Jeton (à envoyer dans X-Malbo-Token)", () => s.LiveApiToken, v => { if (v.Length >= 8) s.LiveApiToken = v; });
            Note("Exemple PowerShell : Invoke-RestMethod http://127.0.0.1:" + s.LiveApiPort + "/activity -Method Post -Headers @{'X-Malbo-Token'='" + s.LiveApiToken + "'} -ContentType 'application/json' -Body '{\"id\":\"build\",\"title\":\"Compilation\",\"progress\":0.4}'");
            Note("Champs : id, title, body, progress (0 à 1), done (true = terminé), ttl (secondes). DELETE /activity/<id> retire l'activité · POST /notify {title, body} affiche un message.");

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
            Check("Canal bêta : recevoir aussi les pré-versions (alpha, bêta)", () => s.BetaChannel, v => s.BetaChannel = v);
            Note($"Malbo lit le manifeste {Updater.ManifestFile} du dépôt (sinon la dernière release). Le fichier téléchargé est vérifié par SHA-256 avant d'être installé. Dépôt officiel : {AppSettings.DefaultRepo}.");
            try { Note($"Version installée : {Updater.CurrentName}"); } catch { }
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

            Section("Sauvegarde");
            Note("Exporte tous tes réglages, tâches, alarmes, notes et raccourcis dans un fichier. Les connexions Spotify/Discord sont liées à ce compte Windows : sur un autre PC il faudra les refaire.");
            var bkSt = Status();
            var exp = Btn("Exporter les réglages…", () =>
            {
                try
                {
                    var d = new Microsoft.Win32.SaveFileDialog { FileName = $"Malbo-reglages-{DateTime.Now:yyyyMMdd}.json", Filter = "Réglages Malbo (*.json)|*.json" };
                    if (d.ShowDialog(this) != true) return;
                    foreach (var c in _commit) c();
                    s.ExportTo(d.FileName);
                    bkSt.Text = "Exporté ✔ " + d.FileName;
                }
                catch (Exception ex) { bkSt.Text = ex.Message; }
            });
            var imp = Btn("Importer…", () =>
            {
                try
                {
                    var d = new Microsoft.Win32.OpenFileDialog { Filter = "Réglages Malbo (*.json)|*.json" };
                    if (d.ShowDialog(this) != true) return;
                    var loaded = AppSettings.ImportFrom(d.FileName);
                    if (MessageBox.Show(this, "Remplacer tous les réglages actuels par ceux du fichier ? Malbo va redémarrer.", "Importer les réglages", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
                    loaded.Save();
                    Process.Start(new ProcessStartInfo(Environment.ProcessPath, $"--wait-pid {Environment.ProcessId}") { UseShellExecute = false });
                    Application.Current.Shutdown();
                }
                catch (Exception ex) { bkSt.Text = "Import impossible : " + ex.Message; }
            });
            var bkRow = new StackPanel { Orientation = Orientation.Horizontal };
            bkRow.Children.Add(exp); bkRow.Children.Add(imp);
            _p.Children.Add(bkRow);
            _p.Children.Add(bkSt);

            Section("Avancé");
            Note("Raccourcis : Ctrl+Alt+I ouvre/épingle l'île · Ctrl+Alt+H masque/affiche · Ctrl+Alt+Espace lecture/pause · Ctrl+Alt+M coupe le micro partout.");
            var pl = Btn("Ouvrir le dossier des plugins", () =>
            {
                Directory.CreateDirectory(AppSettings.PluginDir);
                Process.Start("explorer.exe", AppSettings.PluginDir);
            });
            pl.HorizontalAlignment = HorizontalAlignment.Left;
            _p.Children.Add(pl);

            if (_pages.Count > 0) _pages[0].Nav.IsChecked = true;
        }

        static string Fold(string t) => new string((t ?? "").Normalize(System.Text.NormalizationForm.FormD)
            .Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark).ToArray()).ToLowerInvariant();

        /// <summary>Recherche : ne garde que les catégories qui contiennent le texte tapé, et ouvre la première.</summary>
        void Filter(string query)
        {
            var q = Fold(query).Trim();
            RadioButton first = null;
            bool currentVisible = false;
            foreach (var pg in _pages)
            {
                bool hit = q.Length == 0 || Fold(pg.Title + " " + pg.Words).Contains(q);
                pg.Nav.Visibility = hit ? Visibility.Visible : Visibility.Collapsed;
                if (hit && first == null) first = pg.Nav;
                if (hit && pg.Nav.IsChecked == true) currentVisible = true;
            }
            if (!currentVisible && first != null) first.IsChecked = true;
        }

        /// <summary>Mots trouvables par la recherche pour la catégorie en cours.</summary>
        void Index(string text)
        {
            if (_pages.Count > 0 && !string.IsNullOrEmpty(text)) _pages[_pages.Count - 1].Words.Append(' ').Append(text);
        }

        Button Btn(string text, Action a)
        {
            var b = new Button { Content = text, Style = (Style)Application.Current.FindResource("PillBtn"), Padding = new Thickness(16, 7, 16, 7), Margin = new Thickness(4) };
            b.Click += (s, e) => a();
            return b;
        }

        /// <summary>Ouvre une nouvelle catégorie : une page à droite + une entrée dans le menu de gauche.</summary>
        void Section(string t)
        {
            var page = new StackPanel { Margin = new Thickness(22, 14, 22, 14) };
            var title = new TextBlock { Text = t, FontSize = 20, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 12) };
            page.Children.Add(title);
            var nav = new RadioButton
            {
                Content = t, GroupName = "settingsNav", Style = (Style)Application.Current.FindResource("SegBtn"),
                HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 1, 0, 1), FontSize = 12.5
            };
            nav.Checked += (o, e) => { _host.Content = page; _host.ScrollToTop(); };
            _nav.Children.Add(nav);
            _pages.Add((t, page, nav, new System.Text.StringBuilder()));
            _p = page;
        }

        void Note(string t)
        {
            Index(t);
            var tb = new TextBlock { Text = t, FontSize = 11.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 8) };
            tb.SetResourceReference(TextBlock.ForegroundProperty, "SubFg");
            _p.Children.Add(tb);
        }

        void Link(string t, string url)
        {
            var tb = new TextBlock { Text = t + " ↗", FontSize = 12, Foreground = new SolidColorBrush(Color.FromRgb(100, 170, 255)), Cursor = System.Windows.Input.Cursors.Hand, Margin = new Thickness(0, 0, 0, 6) };
            tb.MouseLeftButtonUp += (s, e) => { try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { } };
            _p.Children.Add(tb);
        }

        Grid Row(string label, UIElement ctl)
        {
            Index(label);
            var g = new Grid { Margin = new Thickness(0, 4, 0, 4) };
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
            Index(label);
            var c = new CheckBox { Content = label, IsChecked = get(), Margin = new Thickness(0, 5, 0, 5), FontSize = 12.5 };
            c.SetResourceReference(ForegroundProperty, "Fg");
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

        /// <summary>Choix parmi quelques valeurs : boutons segmentés (au style de l'île) plutôt qu'une liste déroulante Windows.</summary>
        void Combo(string label, string[] items, Func<string> get, Action<string> set, bool editable = false)
        {
            if (editable)
            {
                // valeur libre : champ de texte, les valeurs connues sont rappelées dessous
                Text(label, get, v => { if (!string.IsNullOrWhiteSpace(v)) set(v); });
                Note("Valeurs courantes : " + string.Join(" · ", items));
                return;
            }
            int idx = Array.IndexOf(items, get());
            ComboIdx(label, items, () => idx < 0 ? 0 : idx, i => { if (i >= 0 && i < items.Length) set(items[i]); });
        }

        void ComboIdx(string label, string[] items, Func<int> get, Action<int> set)
        {
            var wrap = new WrapPanel();
            var group = "seg" + Guid.NewGuid().ToString("N");
            int sel = Math.Clamp(get(), 0, Math.Max(0, items.Length - 1));
            var buttons = new List<RadioButton>();
            for (int i = 0; i < items.Length; i++)
            {
                var rb = new RadioButton { Content = items[i], GroupName = group, IsChecked = i == sel, Style = (Style)Application.Current.FindResource("SegBtn"), Margin = new Thickness(0, 2, 4, 2) };
                buttons.Add(rb);
                wrap.Children.Add(rb);
                Index(items[i]);
            }
            Row(label, wrap);
            _commit.Add(() => set(Math.Max(0, buttons.FindIndex(b => b.IsChecked == true))));
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
