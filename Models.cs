using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DynamicIsland
{
    public class Observable : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        protected void Raise([CallerMemberName] string n = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }

    public class TodoItem : Observable
    {
        string _text; bool _done;
        public string Text { get => _text; set { _text = value; Raise(); } }
        public bool Done { get => _done; set { _done = value; Raise(); } }
    }

    public class NotifItem
    {
        public uint Id { get; set; }
        public string App { get; set; }
        public string Aumid { get; set; }
        public string Title { get; set; }
        public string Body { get; set; }
        public DateTime Time { get; set; }
        /// <summary>Nombre de notifications regroupées dans cette carte (même appli, même conversation).</summary>
        public int Count { get; set; } = 1;
        public System.Collections.Generic.List<uint> Ids { get; set; } = new();
        public string Header => $"{App} · {Time:HH:mm}" + (Count > 1 ? $" · {Count} messages" : "");
        public ImageSource Icon { get; set; }
    }

    public class ClipItem
    {
        public string Text { get; set; }
        /// <summary>Miniature affichée dans l'historique (quelques Ko au lieu de l'image pleine résolution).</summary>
        public BitmapSource Image { get; set; }
        /// <summary>Image d'origine compressée en PNG : décodée seulement quand on la recopie / l'enregistre.</summary>
        public byte[] Png { get; set; }
        public int PixelWidth { get; set; }
        public int PixelHeight { get; set; }

        public BitmapSource LoadFull()
        {
            if (Png == null) return Image;
            using var ms = new System.IO.MemoryStream(Png);
            var bi = new BitmapImage();
            bi.BeginInit(); bi.CacheOption = BitmapCacheOption.OnLoad; bi.StreamSource = ms; bi.EndInit();
            bi.Freeze();
            return bi;
        }
        public DateTime Time { get; set; }
        public string Preview => Image != null ? "Image" : (Text.Length > 160 ? Text.Substring(0, 160) + "…" : Text).Replace("\r", "").Trim();
        public string TimeText => Time.ToString("HH:mm");
        public bool Pinned { get; set; }
        public string Footer => (Pinned ? "Épinglé · " : "") + TimeText;
        public Visibility ImageVis => Image != null ? Visibility.Visible : Visibility.Collapsed;
    }

    public class ShelfItem
    {
        public string Path { get; set; }
        public string Name => System.IO.Path.GetFileName(Path.TrimEnd('\\'));
        public ImageSource Icon { get; set; }
    }

    public class InfoRow
    {
        public string Left { get; set; }
        public string Right { get; set; }
        public string Sub { get; set; }
        public string Tone { get; set; } = "";
        public Visibility SubVis => string.IsNullOrEmpty(Sub) ? Visibility.Collapsed : Visibility.Visible;
    }

    public class AppVolumeItem : Observable, IDisposable
    {
        readonly NAudio.CoreAudioApi.AudioSessionControl _s;
        public bool Owns(NAudio.CoreAudioApi.AudioSessionControl s) => ReferenceEquals(_s, s);
        public void Dispose() { try { _s?.Dispose(); } catch { } }
        public uint Pid { get; }
        public string Name { get; }
        public ImageSource Icon { get; }
        public AppVolumeItem(NAudio.CoreAudioApi.AudioSessionControl s, uint pid, string name, ImageSource icon)
        { _s = s; Pid = pid; Name = name; Icon = icon; }
        public float Volume
        {
            get { try { return _s.SimpleAudioVolume.Volume; } catch { return 0; } }
            set { try { _s.SimpleAudioVolume.Volume = value; } catch { } Raise(); }
        }
    }

    public static class IconHelper
    {
        // Cache par exécutable : une icône par appli au lieu d'une nouvelle à chaque rafraîchissement.
        static readonly System.Collections.Concurrent.ConcurrentDictionary<string, ImageSource> Cache = new(StringComparer.OrdinalIgnoreCase);

        public static ImageSource FromFile(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path)) return null;
                if (System.IO.Directory.Exists(path)) path = Environment.GetFolderPath(Environment.SpecialFolder.Windows) + "\\explorer.exe";
                bool cacheable = path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);
                if (cacheable && Cache.TryGetValue(path, out var hit)) return hit;
                using var ico = System.Drawing.Icon.ExtractAssociatedIcon(path);
                if (ico == null) return null;
                var src = Imaging.CreateBitmapSourceFromHIcon(ico.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                src.Freeze();
                if (cacheable) { if (Cache.Count > 128) Cache.Clear(); Cache[path] = src; }
                return src;
            }
            catch { return null; }
        }
    }
}
