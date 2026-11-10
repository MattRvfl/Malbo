using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace DynamicIsland
{
    public partial class MainWindow
    {
        readonly ObservableCollection<ShelfItem> _shelf = new();
        readonly ObservableCollection<ClipItem> _clips = new();
        bool _ignoreClip;
        FileSystemWatcher _dlWatcher;
        readonly HashSet<string> _activeDl = new(StringComparer.OrdinalIgnoreCase);
        static readonly string[] TempExt = { ".crdownload", ".part", ".partial", ".download", ".opdownload" };

        void InitFiles()
        {
            ShelfList.ItemsSource = _shelf;
            ClipList.ItemsSource = _clips;
            // épingles : seuls les textes épinglés survivent au redémarrage (jamais tout l'historique : il peut contenir des mots de passe)
            foreach (var t in (S.PinnedClips ?? new()).Where(t => !string.IsNullOrEmpty(t)))
                _clips.Add(new ClipItem { Text = t, Time = DateTime.Now, Pinned = true });
            foreach (var p in S.Shelf.Where(p => File.Exists(p) || Directory.Exists(p)))
                _shelf.Add(new ShelfItem { Path = p, Icon = IconHelper.FromFile(p) });
            InitDownloads();
        }

        // ---------- Étagère (drag & drop) ----------
        void Island_Drop(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
            var files = (string[])e.Data.GetData(DataFormats.FileDrop);
            int added = 0;
            foreach (var f in files)
            {
                if (_shelf.Any(s => s.Path.Equals(f, StringComparison.OrdinalIgnoreCase))) continue;
                _shelf.Add(new ShelfItem { Path = f, Icon = IconHelper.FromFile(f) });
                added++;
            }
            SaveShelf();
            if (added > 0 && _mode != Mode.Expanded)
                ShowPeek(new PeekData { Key = "alert", Glyph = "", Title = "Étagère", Body = $"{added} élément(s) ajouté(s)", Duration = TimeSpan.FromSeconds(2.5), Width = 320, Height = 68 });
        }

        Point _dragStart;
        void ShelfItem_MouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed) { _dragStart = e.GetPosition(this); return; }
            var p = e.GetPosition(this);
            if (Math.Abs(p.X - _dragStart.X) < 6 && Math.Abs(p.Y - _dragStart.Y) < 6) return;
            if ((sender as FrameworkElement)?.Tag is ShelfItem it)
            {
                _pinned = true; // évite la fermeture pendant le glisser
                try { DragDrop.DoDragDrop((DependencyObject)sender, new DataObject(DataFormats.FileDrop, new[] { it.Path }), DragDropEffects.Copy | DragDropEffects.Move); }
                catch { }
                SetPinned(false);
            }
        }

        void ShelfOpen_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.Tag is ShelfItem it)
                try { Process.Start(new ProcessStartInfo(it.Path) { UseShellExecute = true }); } catch { }
        }

        void ShelfRemove_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.Tag is ShelfItem it) { _shelf.Remove(it); SaveShelf(); }
        }

        void ShelfClear_Click(object sender, RoutedEventArgs e) { _shelf.Clear(); SaveShelf(); }

        void SaveShelf() { S.Shelf = _shelf.Select(s => s.Path).ToList(); S.Save(); }

        // ---------- Presse-papiers + captures d'écran ----------
        void OnClipboardChanged()
        {
            if (_ignoreClip) { _ignoreClip = false; return; }
            Dispatcher.BeginInvoke(new Action(ReadClipboard), DispatcherPriority.Background);
        }

        void ReadClipboard()
        {
            for (int attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    if (Clipboard.ContainsText())
                    {
                        var t = Clipboard.GetText();
                        if (string.IsNullOrWhiteSpace(t) || t.Length > 1_000_000 || (_clips.Count > PinnedCount() && _clips[PinnedCount()].Text == t)) return;
                        if (_clips.Any(c => c.Pinned && c.Text == t)) return; // déjà épinglé
                        _clips.Insert(PinnedCount(), new ClipItem { Text = t, Time = DateTime.Now });
                    }
                    else if (Clipboard.ContainsImage())
                    {
                        var clip = GetClipboardImage();
                        if (clip == null) return;
                        _clips.Insert(PinnedCount(), clip);
                        // pas plus de 8 images gardées (même compressées, une capture 4K pèse plusieurs Mo)
                        int imgs = 0;
                        for (int i = 0; i < _clips.Count; i++)
                            if (_clips[i].Png != null && ++imgs > 8) { _clips.RemoveAt(i); i--; }
                        ShowScreenshotPeek(clip);
                    }
                    while (_clips.Count > 30 + PinnedCount() && !_clips[_clips.Count - 1].Pinned) _clips.RemoveAt(_clips.Count - 1);
                    return;
                }
                catch { System.Threading.Thread.Sleep(60); }
            }
        }

        /// <summary>
        /// Lit l'image du presse-papiers, la garde compressée (PNG) et n'en décode qu'une miniature :
        /// une capture 4K décodée pèse ~33 Mo, 30 dans l'historique pouvaient dépasser 1 Go de RAM.
        /// </summary>
        static ClipItem GetClipboardImage()
        {
            // Passe par WinForms : l'alpha des captures est correct (WPF les rend parfois noires/transparentes)
            using var img = System.Windows.Forms.Clipboard.GetImage();
            if (img == null) return null;
            byte[] png;
            using (var ms = new MemoryStream())
            {
                img.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                png = ms.ToArray();
            }
            BitmapImage thumb;
            using (var ms = new MemoryStream(png))
            {
                thumb = new BitmapImage();
                thumb.BeginInit(); thumb.CacheOption = BitmapCacheOption.OnLoad;
                if (img.Width > 480) thumb.DecodePixelWidth = 480;
                thumb.StreamSource = ms; thumb.EndInit();
                thumb.Freeze();
            }
            return new ClipItem { Text = "", Image = thumb, Png = png, PixelWidth = img.Width, PixelHeight = img.Height, Time = DateTime.Now };
        }

        void ShowScreenshotPeek(ClipItem clip)
        {
            ShowPeek(new PeekData
            {
                Key = "alert", Image = clip.Image, Title = "Capture copiée", Body = $"{clip.PixelWidth} × {clip.PixelHeight}",
                Duration = TimeSpan.FromSeconds(6), Width = 500, Height = 76,
                Actions = new() { ("Texte", () => _ = CopyTextFromImage(clip)), ("Enregistrer", () => SaveImage(clip)) }
            });
        }

        void SaveImage(ClipItem clip)
        {
            try
            {
                var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Screenshots");
                Directory.CreateDirectory(dir);
                var path = Path.Combine(dir, $"Capture_{DateTime.Now:yyyyMMdd_HHmmss}.png");
                if (clip.Png != null) File.WriteAllBytes(path, clip.Png); // déjà en PNG : pas de ré-encodage
                else
                {
                    using var fs = File.Create(path);
                    var enc = new PngBitmapEncoder();
                    enc.Frames.Add(BitmapFrame.Create(clip.Image));
                    enc.Save(fs);
                }
                _shelf.Insert(0, new ShelfItem { Path = path, Icon = IconHelper.FromFile(path) });
                SaveShelf();
                Dispatcher.BeginInvoke(new Action(() => ShowPeek(new PeekData
                {
                    Key = "alert", Glyph = "", Title = "Capture enregistrée", Body = path, Duration = TimeSpan.FromSeconds(4), Width = 440, Height = 76,
                    Actions = new() { ("Ouvrir", () => { try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); } catch { } }) }
                })), DispatcherPriority.Background);
            }
            catch (Exception ex) { App.Log(ex); }
        }

        void Clip_Click(object sender, MouseButtonEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is not ClipItem c) return;
            try
            {
                _ignoreClip = true;
                if (c.Image != null) Clipboard.SetImage(c.LoadFull()); else Clipboard.SetText(c.Text);
                if (!c.Pinned) { _clips.Remove(c); _clips.Insert(PinnedCount(), c); }
            }
            catch { _ignoreClip = false; }
        }

        void ClipClear_Click(object sender, RoutedEventArgs e)
        {
            // « Effacer » garde les épingles
            for (int i = _clips.Count - 1; i >= 0; i--) if (!_clips[i].Pinned) _clips.RemoveAt(i);
        }

        int PinnedCount() => _clips.Count(c => c.Pinned);

        void SavePinned()
        {
            S.PinnedClips = _clips.Where(c => c.Pinned && c.Png == null && !string.IsNullOrEmpty(c.Text)).Select(c => c.Text).ToList();
            S.Save();
        }

        void ClipSearch_Changed(object sender, TextChangedEventArgs e)
        {
            var q = (ClipSearch.Text ?? "").Trim();
            var view = System.Windows.Data.CollectionViewSource.GetDefaultView(_clips);
            view.Filter = q.Length == 0 ? null : o => o is ClipItem c && (c.Text ?? "").Contains(q, StringComparison.OrdinalIgnoreCase);
        }

        void Clip_RightClick(object sender, MouseButtonEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is not ClipItem c) return;
            e.Handled = true;
            var m = NewMenu();
            m.Items.Add(MenuEntry(c.Pinned ? "Désépingler" : "Épingler", () =>
            {
                int i = _clips.IndexOf(c);
                if (i < 0) return;
                _clips.RemoveAt(i);
                c.Pinned = !c.Pinned;
                _clips.Insert(c.Pinned ? 0 : PinnedCount(), c);
                SavePinned();
            }));
            if (c.Png != null)
            {
                m.Items.Add(MenuEntry("Copier le texte de l'image", () => _ = CopyTextFromImage(c)));
                m.Items.Add(MenuEntry("Enregistrer l'image", () => SaveImage(c)));
            }
            m.Items.Add(MenuSep());
            m.Items.Add(MenuEntry("Supprimer", () => { _clips.Remove(c); if (c.Pinned) SavePinned(); }));
            m.PlacementTarget = sender as UIElement;
            m.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
            m.IsOpen = true;
        }

        /// <summary>Lit le texte d'une capture (OCR intégré à Windows, hors ligne) et le copie.</summary>
        async Task CopyTextFromImage(ClipItem clip)
        {
            try
            {
                var text = (await OcrService.ReadAsync(clip.Png) ?? "").Trim();
                if (text.Length == 0) { Alert("Aucun texte trouvé", "L'image ne contient pas de texte lisible.", "\uE8C8"); return; }
                try { _ignoreClip = true; Clipboard.SetText(text); } catch { _ignoreClip = false; }
                if (!_clips.Any(c => c.Text == text)) _clips.Insert(PinnedCount(), new ClipItem { Text = text, Time = DateTime.Now });
                ShowPeek(new PeekData { Key = "alert", Glyph = "\uE8C8", Title = "Texte copié", Body = text.Replace("\r", "").Replace("\n", " "), Duration = TimeSpan.FromSeconds(4), Width = 440, Height = 76 });
            }
            catch (Exception ex)
            {
                App.Log(ex);
                Alert("Lecture du texte impossible", ex.Message, "\uE783");
            }
        }

        // ---------- Téléchargements ----------
        void InitDownloads()
        {
            try
            {
                var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
                if (!Directory.Exists(dir)) return;
                _dlWatcher = new FileSystemWatcher(dir) { IncludeSubdirectories = false, EnableRaisingEvents = true };
                _dlWatcher.Created += (s, e) => Dispatcher.InvokeAsync(() => { if (IsTemp(e.FullPath)) _activeDl.Add(e.FullPath); });
                _dlWatcher.Renamed += (s, e) => Dispatcher.InvokeAsync(() =>
                {
                    bool wasTemp = IsTemp(e.OldFullPath), isTemp = IsTemp(e.FullPath);
                    _activeDl.Remove(e.OldFullPath);
                    if (isTemp) _activeDl.Add(e.FullPath);
                    else if (wasTemp) DownloadDone(e.FullPath);
                });
                _dlWatcher.Deleted += (s, e) => Dispatcher.InvokeAsync(() =>
                {
                    if (!_activeDl.Remove(e.FullPath)) return;
                    var final = StripTemp(e.FullPath);
                    if (File.Exists(final)) DownloadDone(final); // Firefox : .part supprimé, fichier final créé
                });
            }
            catch (Exception ex) { App.Log(ex); }
        }

        static bool IsTemp(string p) => TempExt.Any(x => p.EndsWith(x, StringComparison.OrdinalIgnoreCase));
        static string StripTemp(string p) { foreach (var x in TempExt) if (p.EndsWith(x, StringComparison.OrdinalIgnoreCase)) return p[..^x.Length]; return p; }

        void DownloadDone(string path)
        {
            if (Path.GetFileName(path).StartsWith("Unconfirmed", StringComparison.OrdinalIgnoreCase)) return;
            ShowPeek(new PeekData
            {
                Key = "alert", Image = IconHelper.FromFile(path), Glyph = "", Title = "Téléchargement terminé", Body = Path.GetFileName(path),
                Duration = TimeSpan.FromSeconds(5), Width = 440, Height = 76,
                Actions = new()
                {
                    ("Ouvrir", () => { try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); } catch { } }),
                    ("Dossier", () => { try { Process.Start("explorer.exe", $"/select,\"{path}\""); } catch { } })
                }
            });
        }

        List<InfoRow> DownloadRows()
        {
            var rows = new List<InfoRow>();
            foreach (var p in _activeDl.ToList())
            {
                try
                {
                    var fi = new FileInfo(p);
                    if (!fi.Exists) { _activeDl.Remove(p); continue; }
                    rows.Add(new InfoRow { Left = Path.GetFileName(StripTemp(p)), Right = $"{fi.Length / 1048576.0:0.0} Mo", Sub = "En cours…", Tone = "accent" });
                }
                catch { }
            }
            return rows;
        }
    }
}
