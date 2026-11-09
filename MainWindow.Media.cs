using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;

namespace DynamicIsland
{
    public partial class MainWindow
    {
        List<LyricLine> _lines;
        string _lyricKey, _currentLyric, _peekedKey;
        int _lyricIdx = -1;
        bool _seeking, _lyricsSynced, _lyricsManual;
        DateTime _lyricsManualUntil;
        double _lyricsTarget;
        readonly List<TextBlock> _lyricBlocks = new();

        // Spotify (API) : état du morceau courant
        string _spKey, _spTrackId, _spTrackUri, _spRepeat = "off";
        bool _spShuffle;
        bool? _liked;
        static readonly SolidColorBrush LikeRed = Frozen(Color.FromRgb(0xFF, 0x2D, 0x55));

        // Fond flouté
        bool _backdropOn;
        BitmapSource _backdropArt;

        void InitMedia()
        {
            _vol.Init();
            _vol.VolumeChanged += (v, m) => Dispatcher.InvokeAsync(() =>
            {
                if (!S.VolumeOsd) return;
                ShowPeek(new PeekData
                {
                    Key = "osd", Glyph = m ? "\uE74F" : v < 0.01 ? "\uE992" : v < 0.34 ? "\uE993" : v < 0.67 ? "\uE994" : "\uE995",
                    Title = m ? "Muet" : $"Volume  {Math.Round(v * 100)} %", Progress = m ? 0 : v,
                    Duration = TimeSpan.FromSeconds(1.6), Width = 300, Height = 48
                });
            });

            MediaSlider.PreviewMouseDown += (s, e) => _seeking = true;
            MediaSlider.PreviewMouseUp += (s, e) =>
            {
                _seeking = false;
                _media.Seek(TimeSpan.FromSeconds(MediaSlider.Value));
            };

            LyricsViewport.MouseWheel += Lyrics_Wheel;
            LyricsViewport.SizeChanged += (s, e) => { if (_lyricsSynced) ScrollLyrics(); };

            _media.StateChanged += () => Dispatcher.InvokeAsync(OnMediaChanged);
            _ = _media.InitAsync();
        }

        void OnMediaChanged()
        {
            var st = _media.State;
            MediaTitle.Text = st.HasMedia ? st.Title : "Rien en lecture";
            MediaArtist.Text = st.Artist;
            MediaApp.Text = st.HasMedia ? "via " + PrettyApp(st.AppId) : "";
            PlayBtn.Content = st.IsPlaying ? "\uE769" : "\uE768";
            MediaSlider.Maximum = Math.Max(1, st.Duration.TotalSeconds);

            if (st.Art != null)
            {
                var brush = new ImageBrush(st.Art) { Stretch = Stretch.UniformToFill };
                BigArt.Background = brush;
                BigArtGlyph.Visibility = Visibility.Collapsed;
                CompactArt.Background = brush;
                if (S.AccentFromArt) Application.Current.Resources["Accent"] = new SolidColorBrush(AccentFrom(st.Art));
            }
            else
            {
                BigArt.SetResourceReference(Border.BackgroundProperty, "CardBg");
                BigArtGlyph.Visibility = Visibility.Visible;
            }

            if (st.Key != _lyricKey)
            {
                _lyricKey = st.Key;
                LoadLyrics(st);
            }

            if (st.Key != _spKey)
            {
                _spKey = st.Key;
                _spTrackId = _spTrackUri = null;
                _liked = null;
                UpdateLikeUI();
                RefreshSpotify();
            }
            else LikeBtn.Visibility = PlaylistBtn.Visibility = Vis(IsSpotifyApi);
            UpdateShuffleRepeatUI();
            UpdateBackdrop();

            // Petit "peek" quand un nouveau morceau démarre
            if (S.TrackPeek && st.HasMedia && st.IsPlaying && st.Key != _peekedKey && _mode != Mode.Expanded)
            {
                _peekedKey = st.Key;
                ShowPeek(new PeekData
                {
                    Key = "track", Image = st.Art, Glyph = "\uE8D6", Title = st.Title, Body = st.Artist,
                    Duration = TimeSpan.FromSeconds(3), Width = 380, Height = 68,
                    OnClick = () => { Expand(); TabMedia.IsChecked = true; }
                });
            }
            UpdateCompact();
        }

        static string PrettyApp(string id)
        {
            if (string.IsNullOrEmpty(id)) return "Windows";
            var l = id.ToLowerInvariant();
            if (l.Contains("spotify")) return "Spotify";
            if (l.Contains("chrome")) return "Chrome";
            if (l.Contains("msedge") || l.Contains("edge")) return "Edge";
            if (l.Contains("firefox")) return "Firefox";
            if (l.Contains("opera")) return "Opera";
            if (l.Contains("brave")) return "Brave";
            if (l.Contains("deezer")) return "Deezer";
            if (l.Contains("applemusic") || l.Contains("itunes")) return "Apple Music";
            if (l.Contains("vlc")) return "VLC";
            if (l.Contains("zune") || l.Contains("media")) return "Lecteur multimédia";
            var s = id.Split('!').First().Split('\\').Last();
            return s.Replace(".exe", "", StringComparison.OrdinalIgnoreCase);
        }

        // ================== Fond flouté ==================
        void UpdateBackdrop()
        {
            var st = _media.State;
            bool on = _mode == Mode.Expanded && PanelMedia.Visibility == Visibility.Visible && st.HasMedia && st.Art != null;
            if (on && st.Art != _backdropArt) { _backdropArt = st.Art; ArtBackdrop.Source = st.Art; }
            if (on == _backdropOn) return;
            _backdropOn = on;
            var da = new DoubleAnimation(on ? 0.55 : 0, TimeSpan.FromMilliseconds(on ? 450 : 250));
            if (on) ArtBackdrop.Visibility = Visibility.Visible;
            else da.Completed += (s, e) => { if (!_backdropOn) ArtBackdrop.Visibility = Visibility.Collapsed; };
            ArtBackdrop.BeginAnimation(OpacityProperty, da);
        }

        // ================== Aléatoire / répéter / Spotify ==================
        bool IsSpotifyApi => _media.State.HasMedia && PrettyApp(_media.State.AppId) == "Spotify" && _spotify.Connected;

        async void RefreshSpotify()
        {
            bool sp = IsSpotifyApi;
            LikeBtn.Visibility = PlaylistBtn.Visibility = Vis(sp);
            if (!sp) return;
            var key = _media.State.Key;
            try
            {
                await Task.Delay(500); // laisse le temps à l'API de voir le nouveau morceau
                var p = await _spotify.PlayerAsync();
                if (key != _media.State.Key || p == null) return;
                _spTrackId = p.TrackId;
                _spTrackUri = p.TrackUri;
                _spShuffle = p.Shuffle;
                _spRepeat = string.IsNullOrEmpty(p.Repeat) ? "off" : p.Repeat;
                UpdateShuffleRepeatUI();
                _liked = string.IsNullOrEmpty(p.TrackId) ? null : await _spotify.IsLikedAsync(p.TrackId);
                if (key != _media.State.Key) return;
                UpdateLikeUI();
            }
            catch (Exception ex) { App.Log(ex); }
        }

        void UpdateLikeUI()
        {
            bool liked = _liked == true;
            LikeBtn.Content = liked ? "\uEB52" : "\uEB51";
            if (liked) LikeBtn.Foreground = LikeRed;
            else LikeBtn.SetResourceReference(ForegroundProperty, "Fg");
            LikeBtn.ToolTip = liked ? "Retirer des titres likés" : "Ajouter aux titres likés";
        }

        void UpdateShuffleRepeatUI()
        {
            bool shuffle;
            int repeat; // 0 aucun, 1 morceau, 2 liste
            if (IsSpotifyApi)
            {
                shuffle = _spShuffle;
                repeat = _spRepeat == "track" ? 1 : _spRepeat == "context" ? 2 : 0;
            }
            else
            {
                var st = _media.State;
                shuffle = st.Shuffle == true;
                repeat = st.Repeat ?? 0;
            }
            ShuffleBtn.Opacity = shuffle ? 1 : 0.45;
            RepeatBtn.Opacity = repeat != 0 ? 1 : 0.45;
            RepeatBtn.Content = repeat == 1 ? "\uE8ED" : "\uE8EE";
            RepeatBtn.ToolTip = repeat == 1 ? "Répéter le morceau" : repeat == 2 ? "Répéter la liste" : "Répéter";
        }

        async void Shuffle_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (IsSpotifyApi)
                {
                    var p = await _spotify.PlayerAsync();
                    bool cur = p?.Shuffle ?? _spShuffle;
                    if (await _spotify.SetShuffleAsync(!cur)) _spShuffle = !cur;
                    UpdateShuffleRepeatUI();
                }
                else
                {
                    bool cur = _media.State.Shuffle == true;
                    if (await _media.SetShuffle(!cur)) await _media.RefreshAsync();
                }
            }
            catch (Exception ex) { App.Log(ex); }
        }

        async void Repeat_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (IsSpotifyApi)
                {
                    var p = await _spotify.PlayerAsync();
                    var cur = string.IsNullOrEmpty(p?.Repeat) ? _spRepeat : p.Repeat;
                    var next = cur == "off" ? "context" : cur == "context" ? "track" : "off";
                    if (await _spotify.SetRepeatAsync(next)) _spRepeat = next;
                    UpdateShuffleRepeatUI();
                }
                else
                {
                    int cur = _media.State.Repeat ?? 0;
                    int next = cur == 0 ? 2 : cur == 2 ? 1 : 0;
                    if (await _media.SetRepeat(next)) await _media.RefreshAsync();
                }
            }
            catch (Exception ex) { App.Log(ex); }
        }

        async void Like_Click(object sender, RoutedEventArgs e)
        {
            var id = _spTrackId;
            if (string.IsNullOrEmpty(id)) return;
            bool target = _liked != true;
            try
            {
                if (await _spotify.SetLikedAsync(id, target) && id == _spTrackId)
                {
                    _liked = target;
                    UpdateLikeUI();
                }
            }
            catch (Exception ex) { App.Log(ex); }
        }

        async void Playlist_Click(object sender, RoutedEventArgs e)
        {
            var uri = _spTrackUri;
            if (string.IsNullOrEmpty(uri)) return;
            try
            {
                var lists = await _spotify.PlaylistsAsync();
                var m = NewMenu();
                if (lists == null || lists.Count == 0)
                {
                    var none = MenuEntry("Aucune playlist modifiable", null);
                    none.IsEnabled = false;
                    m.Items.Add(none);
                }
                else
                {
                    foreach (var (id, name) in lists)
                    {
                        var pid = id; var pname = name;
                        m.Items.Add(MenuEntry(pname, async () =>
                        {
                            bool ok = false;
                            try { ok = await _spotify.AddToPlaylistAsync(pid, uri); } catch (Exception ex) { App.Log(ex); }
                            ShowPeek(new PeekData
                            {
                                Key = "alert", Glyph = "\uECC8",
                                Title = ok ? $"Ajouté à {pname}" : $"Impossible d'ajouter à {pname}",
                                Body = _media.State.Title,
                                Duration = TimeSpan.FromSeconds(2.5), Width = 360, Height = 68
                            });
                        }));
                    }
                }
                m.PlacementTarget = PlaylistBtn;
                m.Placement = PlacementMode.Bottom;
                m.IsOpen = true;
            }
            catch (Exception ex) { App.Log(ex); }
        }

        // ================== Paroles ==================
        bool LyricsVisible => _mode == Mode.Expanded && PanelMedia.Visibility == Visibility.Visible && Root.Visibility == Visibility.Visible;

        async void LoadLyrics(MediaState st)
        {
            _lines = null; _lyricIdx = -1; _currentLyric = null;
            _lyricsSynced = false; _lyricsManual = false;
            LyricsPanel.Children.Clear();
            _lyricBlocks.Clear();
            SetShift(10, false);
            if (!st.HasMedia) { LyricsStatus.Text = ""; return; }
            LyricsStatus.Text = "Recherche…";
            var key = st.Key;
            var res = await _lyrics.GetAsync(st.Title, st.Artist, st.Album, st.Duration);
            if (key != _lyricKey) return;
            if (res == null || res.Count == 0)
            {
                LyricsStatus.Text = "Introuvables";
                LyricsPanel.Children.Add(new TextBlock { Text = "Pas de paroles trouvées pour ce titre.", Foreground = (Brush)FindResource("SubFg"), FontSize = 12 });
                return;
            }
            _lines = res;
            bool synced = res.Any(l => l.Synced);
            _lyricsSynced = synced;
            LyricsStatus.Text = synced ? "Synchronisées" : "Non synchronisées";
            foreach (var l in res)
            {
                var tb = new TextBlock
                {
                    Text = string.IsNullOrWhiteSpace(l.Text) ? "♪" : l.Text,
                    TextWrapping = TextWrapping.Wrap,
                    FontSize = 17, FontWeight = FontWeights.SemiBold,
                    Margin = new Thickness(0, 5, 0, 5),
                    Cursor = synced ? Cursors.Hand : Cursors.Arrow,
                    Opacity = synced ? 0.3 : 0.9,
                    RenderTransformOrigin = new Point(0, 0.5),
                    RenderTransform = new ScaleTransform(1, 1)
                };
                tb.SetResourceReference(TextBlock.ForegroundProperty, "Fg");
                var line = l;
                tb.MouseLeftButtonUp += (s, e) =>
                {
                    if (!line.Synced) return;
                    _lyricsManual = false;
                    _media.Seek(line.Time);
                };
                LyricsPanel.Children.Add(tb);
                _lyricBlocks.Add(tb);
            }
            if (synced)
            {
                for (int i = 0; i < _lyricBlocks.Count; i++) StyleLine(i, false);
                Dispatcher.BeginInvoke(new Action(ScrollLyrics), System.Windows.Threading.DispatcherPriority.Loaded);
            }
        }

        /// <summary>Opacité, échelle et flou d'une ligne selon sa distance à la ligne courante.</summary>
        void StyleLine(int i, bool anim)
        {
            var tb = _lyricBlocks[i];
            int d = _lyricIdx < 0 ? i + 1 : Math.Abs(i - _lyricIdx);
            double op = d == 0 ? 1 : d == 1 ? 0.55 : d == 2 ? 0.4 : 0.3;
            double blur = d == 0 ? 0 : d == 1 ? 1.5 : d == 2 ? 3 : 4.5;
            double sc = d == 0 ? 1 : 0.94;
            var st = tb.RenderTransform as ScaleTransform;
            if (anim)
            {
                var dur = TimeSpan.FromMilliseconds(300);
                var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
                tb.BeginAnimation(OpacityProperty, new DoubleAnimation(op, dur) { EasingFunction = ease });
                st?.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(sc, dur) { EasingFunction = ease });
                st?.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(sc, dur) { EasingFunction = ease });
            }
            else
            {
                tb.BeginAnimation(OpacityProperty, null);
                tb.Opacity = op;
                if (st != null)
                {
                    st.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                    st.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                    st.ScaleX = st.ScaleY = sc;
                }
            }

            // pas d'effet au-delà de ±6 lignes (performances)
            if (d > 6) { tb.Effect = null; return; }
            var be = tb.Effect as BlurEffect;
            if (!anim)
            {
                if (blur <= 0) { tb.Effect = null; return; }
                if (be == null) tb.Effect = be = new BlurEffect { RenderingBias = RenderingBias.Performance };
                be.BeginAnimation(BlurEffect.RadiusProperty, null);
                be.Radius = blur;
                return;
            }
            if (be == null)
            {
                if (blur <= 0) return;
                // sans effet, la ligne était soit la ligne courante (net), soit au-delà de ±6 (flou max)
                int oldD = _styleOldIdx < 0 ? i + 1 : Math.Abs(i - _styleOldIdx);
                tb.Effect = be = new BlurEffect { Radius = oldD == 0 ? 0 : 4.5, RenderingBias = RenderingBias.Performance };
            }
            var ba = new DoubleAnimation(blur, TimeSpan.FromMilliseconds(300)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            if (blur <= 0)
            {
                int idx = i;
                var eff = be;
                ba.Completed += (s, e) => { if (_lyricIdx == idx && tb.Effect == eff) tb.Effect = null; };
            }
            be.BeginAnimation(BlurEffect.RadiusProperty, ba);
        }

        int _styleOldIdx = -1;

        void RestyleAround(int oldIdx, int newIdx, bool anim)
        {
            _styleOldIdx = oldIdx;
            var set = new HashSet<int>();
            foreach (var c in new[] { oldIdx, newIdx })
                for (int i = Math.Max(0, c - 7); i <= Math.Min(_lyricBlocks.Count - 1, c + 7); i++) set.Add(i);
            foreach (var i in set) StyleLine(i, anim);
        }

        void RestyleAllLyrics()
        {
            if (!_lyricsSynced) return;
            for (int i = 0; i < _lyricBlocks.Count; i++) StyleLine(i, false);
        }

        void MediaTick()
        {
            var st = _media.State;
            if (!st.HasMedia) return;
            var pos = st.CurrentPosition;

            if (_mode == Mode.Expanded && PanelMedia.Visibility == Visibility.Visible)
            {
                if (!_seeking) MediaSlider.Value = Math.Min(MediaSlider.Maximum, pos.TotalSeconds);
                MediaPos.Text = FmtM(pos);
                MediaDur.Text = FmtM(st.Duration);
            }

            if (_lines == null || _lines.Count == 0 || !_lines[0].Synced) return;

            // fin de la pause du suivi automatique après un défilement à la molette
            if (_lyricsManual && DateTime.Now >= _lyricsManualUntil) ScrollLyrics();

            int idx = -1;
            var p = pos + TimeSpan.FromMilliseconds(250);
            for (int i = 0; i < _lines.Count; i++)
            {
                if (_lines[i].Time <= p) idx = i; else break;
            }
            if (idx == _lyricIdx) return;

            int old = _lyricIdx;
            _lyricIdx = idx;
            if (_lyricBlocks.Count == _lines.Count) RestyleAround(old, idx, LyricsVisible);
            _currentLyric = idx >= 0 ? (string.IsNullOrWhiteSpace(_lines[idx].Text) ? "♪ ♪ ♪" : _lines[idx].Text) : null;
            ScrollLyrics();
            UpdateCompact();
        }

        /// <summary>Place la ligne courante à 35 % de la hauteur, avec un ressort.</summary>
        void ScrollLyrics()
        {
            if (!_lyricsSynced || _lyricBlocks.Count == 0) return;
            if (_lyricsManual && DateTime.Now < _lyricsManualUntil) return;
            _lyricsManual = false;
            double vh = LyricsViewport.ActualHeight;
            if (vh <= 0) return;
            try
            {
                var tb = _lyricBlocks[Math.Clamp(_lyricIdx, 0, _lyricBlocks.Count - 1)];
                double y = VisualTreeHelper.GetOffset(tb).Y + tb.ActualHeight / 2;
                SetShift(vh * 0.35 - y, LyricsVisible);
            }
            catch { }
        }

        void SetShift(double y, bool anim)
        {
            _lyricsTarget = y;
            if (anim)
            {
                LyricsShift.BeginAnimation(TranslateTransform.YProperty,
                    new DoubleAnimation(y, TimeSpan.FromMilliseconds(800)) { EasingFunction = new SpringEase { Damping = 9, Frequency = 8 } });
            }
            else
            {
                LyricsShift.BeginAnimation(TranslateTransform.YProperty, null);
                LyricsShift.Y = y;
            }
        }

        void Lyrics_Wheel(object sender, MouseWheelEventArgs e)
        {
            if (_lyricBlocks.Count == 0) return;
            e.Handled = true;
            _lyricsManual = true;
            _lyricsManualUntil = DateTime.Now.AddSeconds(4);
            double vh = LyricsViewport.ActualHeight, ph = LyricsPanel.ActualHeight;
            double max = _lyricsSynced ? vh * 0.35 : 10;
            double min = Math.Min(max, vh * 0.5 - ph);
            _lyricsTarget = Math.Clamp(_lyricsTarget + e.Delta * 0.5, min, max);
            LyricsShift.BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(_lyricsTarget, TimeSpan.FromMilliseconds(220)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        }

        static string FmtM(TimeSpan t) => t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : $"{(int)t.TotalMinutes}:{t.Seconds:00}";

        void Play_Click(object sender, RoutedEventArgs e) => _media.TogglePlay();
        void Next_Click(object sender, RoutedEventArgs e) => _media.Next();
        void Prev_Click(object sender, RoutedEventArgs e) => _media.Prev();

        void RefreshAppVolumes()
        {
            var cur = AppVolList.ItemsSource as List<AppVolumeItem>;
            var list = _vol.GetSessions(onlyIfChanged: cur != null);
            if (list == null) return; // rien n'a changé
            if (cur != null && cur.Select(x => x.Pid).SequenceEqual(list.Select(x => x.Pid))) { list.ForEach(x => x.Dispose()); return; }
            AppVolList.ItemsSource = list;
            cur?.ForEach(x => x.Dispose());
        }

        /// <summary>Couleur dominante (saturée) de la pochette.</summary>
        static Color AccentFrom(BitmapSource src)
        {
            try
            {
                var small = new TransformedBitmap(src, new ScaleTransform(24.0 / src.PixelWidth, 24.0 / src.PixelHeight));
                var fc = new FormatConvertedBitmap(small, PixelFormats.Bgra32, null, 0);
                int w = fc.PixelWidth, h = fc.PixelHeight;
                var px = new byte[w * h * 4];
                fc.CopyPixels(px, w * 4, 0);
                double r = 0, g = 0, b = 0, wt = 0;
                for (int i = 0; i < px.Length; i += 4)
                {
                    double B = px[i], G = px[i + 1], R = px[i + 2];
                    double max = Math.Max(R, Math.Max(G, B)), min = Math.Min(R, Math.Min(G, B));
                    double sat = max == 0 ? 0 : (max - min) / max, bright = max / 255.0;
                    double k = sat * sat * bright + 0.01;
                    r += R * k; g += G * k; b += B * k; wt += k;
                }
                r /= wt; g /= wt; b /= wt;
                double m = Math.Max(r, Math.Max(g, b));
                if (m < 1) return Color.FromRgb(10, 132, 255);
                double f = 235 / m;
                return Color.FromRgb((byte)Math.Min(255, r * f), (byte)Math.Min(255, g * f), (byte)Math.Min(255, b * f));
            }
            catch { return Color.FromRgb(10, 132, 255); }
        }
    }
}
