using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace DynamicIsland
{
    /// <summary>
    /// Barre d'onglets (onglets principaux + menu « autres »), hauteur de l'île selon l'onglet,
    /// navigation au clavier, île qui reste ouverte pendant la saisie, contraste de la couleur d'accent.
    /// </summary>
    public partial class MainWindow
    {
        public const string DefaultMainTabs = "Média, Notifs, Outils, Système, Lancer";

        /// <summary>Tous les onglets, dans leur ordre naturel, avec leur réglage « visible ».</summary>
        List<(RadioButton Tab, Func<bool> Enabled)> TabCatalog() => new()
        {
            (TabMedia, () => S.ShowMedia), (TabNotif, () => S.ShowNotif), (TabTools, () => S.ShowTools), (TabSystem, () => S.ShowSystem),
            (TabLaunch, () => S.ShowLaunch), (TabFiles, () => S.ShowFiles), (TabInfo, () => S.ShowInfo), (TabMore, () => S.ShowMore),
            (TabPlugins, () => S.ShowPlugins)
        };

        static string TabName(RadioButton t) => t.Tag as string ?? "";

        static string Fold(string s) => new string((s ?? "").Normalize(System.Text.NormalizationForm.FormD)
            .Where(c => char.IsLetterOrDigit(c) && System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark).ToArray()).ToLowerInvariant();

        /// <summary>Onglets principaux (dans l'ordre choisi dans les réglages), puis les autres.</summary>
        (List<RadioButton> Main, List<RadioButton> Other) SplitTabs()
        {
            var all = TabCatalog().Where(t => t.Enabled()).Select(t => t.Tab).ToList();
            var wanted = (string.IsNullOrWhiteSpace(S.MainTabs) ? DefaultMainTabs : S.MainTabs)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(Fold).ToList();
            var main = new List<RadioButton>();
            foreach (var w in wanted)
            {
                var t = all.FirstOrDefault(x => Fold(TabName(x)) == w || (w.Length >= 3 && Fold(TabName(x)).StartsWith(w)));
                if (t != null && !main.Contains(t)) main.Add(t);
            }
            if (main.Count == 0) main.AddRange(all.Take(5)); // réglage vide ou illisible : on ne laisse jamais la barre vide
            return (main, all.Where(t => !main.Contains(t)).ToList());
        }

        /// <summary>Range la barre : onglets principaux, puis l'onglet « autre » actuellement ouvert (s'il y en a un), puis le bouton « … ».</summary>
        void LayoutTabs()
        {
            if (TabBar == null) return;
            var (main, other) = SplitTabs();
            var current = _tabs.Keys.FirstOrDefault(t => t.IsChecked == true);
            if (current != null && !main.Contains(current) && !other.Contains(current))
            {
                // l'onglet ouvert vient d'être désactivé dans les réglages
                current = main.FirstOrDefault();
                if (current != null) current.IsChecked = true;
            }
            TabBar.Children.Clear();
            foreach (var t in main) { t.Visibility = Visibility.Visible; TabBar.Children.Add(t); }
            foreach (var t in _tabs.Keys.Where(t => !main.Contains(t)))
            {
                t.Visibility = t == current && other.Contains(t) ? Visibility.Visible : Visibility.Collapsed;
                TabBar.Children.Add(t);
            }
            MoreTabsBtn.Visibility = other.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            MoreTabsBtn.ToolTip = other.Count > 0 ? "Autres onglets : " + string.Join(", ", other.Select(TabName)) : null;
            TabBar.Children.Add(MoreTabsBtn);
        }

        void MoreTabs_Click(object sender, RoutedEventArgs e)
        {
            var (_, other) = SplitTabs();
            var m = NewMenu();
            foreach (var t in other)
            {
                var tab = t;
                var mi = MenuEntry(TabName(t), () => tab.IsChecked = true);
                mi.IsCheckable = true;
                mi.IsChecked = t.IsChecked == true;
                m.Items.Add(mi);
            }
            m.Items.Add(MenuSep());
            m.Items.Add(MenuEntry("Choisir les onglets principaux…", OpenSettings));
            m.PlacementTarget = MoreTabsBtn;
            m.Placement = PlacementMode.Bottom;
            m.IsOpen = true;
        }

        /// <summary>Onglets accessibles, dans l'ordre de la barre puis du menu (pour Ctrl+Tab et Ctrl+1…9).</summary>
        List<RadioButton> TabCycle()
        {
            var (main, other) = SplitTabs();
            return main.Concat(other).ToList();
        }

        // ================== Hauteur de l'île selon l'onglet ==================
        /// <summary>
        /// L'île ouverte n'a plus une taille unique : un onglet peu rempli (notifications, plugins, fichiers) prend moins de place.
        /// Les onglets à mise en page fixe gardent la hauteur complète.
        /// </summary>
        double TabHeight()
        {
            if (PanelNotif.Visibility == Visibility.Visible)
            {
                double h = 150 + Math.Min(_notifs.Count, 4) * 76;
                if (_notifs.Count == 0) h = 190;
                if (VoiceCard.Visibility == Visibility.Visible) h += 84;
                if (NotifStatus.Visibility == Visibility.Visible) h += 34;
                return Math.Clamp(h, 190, ExpH);
            }
            if (PanelPlugins.Visibility == Visibility.Visible) return PluginHost.Children.Count == 0 ? 200 : ExpH;
            if (PanelFiles.Visibility == Visibility.Visible) return _shelf.Count <= 3 && _clips.Count <= 3 ? 330 : ExpH;
            return ExpH;
        }

        void ApplyTabHeight()
        {
            if (_mode != Mode.Expanded) return;
            double h = TabHeight();
            ExpandedLayer.Height = h - 24;
            AnimateSize(ExpW, h);
        }

        // ================== Clavier ==================
        /// <summary>Vrai tant qu'on tape dans un champ de l'île : elle ne doit pas se replier sous les doigts.</summary>
        bool Typing => IsActive && Keyboard.FocusedElement is TextBoxBase tb && tb.IsDescendantOf(Island);

        void InitKeyboard()
        {
            PreviewKeyDown += Window_PreviewKeyDown;
            // on clique ailleurs, ou le champ perd le focus alors que la souris est déjà partie : l'île peut se refermer
            Deactivated += (s, e) => { if (_mode == Mode.Expanded && !_pinned && !_menuOpen && !HitZone.IsMouseOver) _leave.Start(); };
            Island.LostKeyboardFocus += (s, e) => { if (_mode == Mode.Expanded && !_pinned && !_menuOpen && !HitZone.IsMouseOver) _leave.Start(); };
        }

        void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (_mode != Mode.Expanded) return;
            bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
            if (e.Key == Key.Escape)
            {
                SetPinned(false);
                Collapse();
                e.Handled = true;
                return;
            }
            if (!ctrl) return;
            var tabs = TabCycle();
            if (tabs.Count == 0) return;
            int cur = tabs.FindIndex(t => t.IsChecked == true);
            if (e.Key == Key.Tab)
            {
                bool back = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
                tabs[((cur < 0 ? 0 : cur) + (back ? tabs.Count - 1 : 1)) % tabs.Count].IsChecked = true;
                e.Handled = true;
            }
            else if (e.Key >= Key.D1 && e.Key <= Key.D9)
            {
                int i = e.Key - Key.D1;
                if (i < tabs.Count) { tabs[i].IsChecked = true; e.Handled = true; }
            }
        }

        // ================== Contraste de la couleur d'accent ==================
        static double Luminance(Color c)
        {
            static double Ch(byte v) { double x = v / 255.0; return x <= 0.03928 ? x / 12.92 : Math.Pow((x + 0.055) / 1.055, 2.4); }
            return 0.2126 * Ch(c.R) + 0.7152 * Ch(c.G) + 0.0722 * Ch(c.B);
        }

        static double Contrast(Color a, Color b)
        {
            double l1 = Luminance(a), l2 = Luminance(b);
            return (Math.Max(l1, l2) + 0.05) / (Math.Min(l1, l2) + 0.05);
        }

        /// <summary>
        /// Garantit que la couleur d'accent reste lisible sur le fond de l'île (contraste ≥ 3:1, le minimum pour des icônes et du texte gras) :
        /// on l'éclaircit sur fond sombre, on la fonce sur fond clair, en gardant sa teinte.
        /// </summary>
        static Color ReadableAccent(Color accent)
        {
            Color bg = Colors.Black;
            if (Application.Current.Resources["CardBg"] is SolidColorBrush cb) bg = cb.Color;
            bool darkBg = Luminance(bg) < 0.5;
            var c = accent;
            for (int i = 0; i < 12 && Contrast(c, bg) < 3.0; i++)
            {
                c = darkBg
                    ? Color.FromRgb((byte)(c.R + (255 - c.R) * 0.18), (byte)(c.G + (255 - c.G) * 0.18), (byte)(c.B + (255 - c.B) * 0.18))
                    : Color.FromRgb((byte)(c.R * 0.82), (byte)(c.G * 0.82), (byte)(c.B * 0.82));
            }
            return c;
        }

        void SetAccent(Color c)
        {
            var b = new SolidColorBrush(ReadableAccent(c));
            b.Freeze();
            Application.Current.Resources["Accent"] = b;
        }
    }
}
