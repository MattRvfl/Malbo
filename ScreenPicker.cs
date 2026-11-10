using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace DynamicIsland
{
    /// <summary>
    /// Pipette + règle : fige l'écran où se trouve la souris, un clic copie la couleur du pixel (#RRGGBB),
    /// un glisser mesure la distance en pixels. Échap ou clic droit pour annuler.
    /// </summary>
    public class ScreenPicker : Window
    {
        [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr h);

        readonly System.Drawing.Bitmap _shot;
        readonly double _sx, _sy; // pixels écran par unité WPF
        readonly Line _line = new() { Stroke = Brushes.White, StrokeThickness = 1.5, Visibility = Visibility.Collapsed };
        readonly Border _tag;
        readonly TextBlock _tagText = new() { Foreground = Brushes.White, FontSize = 12, FontWeight = FontWeights.SemiBold };
        readonly Border _swatch = new() { Width = 16, Height = 16, CornerRadius = new CornerRadius(4), Margin = new Thickness(0, 0, 6, 0), BorderBrush = Brushes.White, BorderThickness = new Thickness(1) };
        Point? _start;

        /// <summary>(titre, détail copié) quand l'utilisateur a choisi ; jamais appelé s'il annule.</summary>
        public event Action<string, string> Picked;

        public ScreenPicker(DpiScale dpi)
        {
            var sc = System.Windows.Forms.Screen.FromPoint(System.Windows.Forms.Cursor.Position);
            var b = sc.Bounds;
            _shot = new System.Drawing.Bitmap(b.Width, b.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var g = System.Drawing.Graphics.FromImage(_shot))
                g.CopyFromScreen(b.Left, b.Top, 0, 0, b.Size);

            _sx = dpi.DpiScaleX; _sy = dpi.DpiScaleY;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            Topmost = true;
            Left = b.Left / _sx; Top = b.Top / _sy;
            Width = b.Width / _sx; Height = b.Height / _sy;
            Cursor = Cursors.Cross;
            Background = Brushes.Black;

            BitmapSource src;
            var hb = _shot.GetHbitmap();
            try { src = Imaging.CreateBitmapSourceFromHBitmap(hb, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions()); src.Freeze(); }
            finally { DeleteObject(hb); }

            var tagRow = new StackPanel { Orientation = Orientation.Horizontal };
            tagRow.Children.Add(_swatch);
            tagRow.Children.Add(_tagText);
            _tag = new Border { Background = new SolidColorBrush(Color.FromArgb(0xE6, 0, 0, 0)), CornerRadius = new CornerRadius(8), Padding = new Thickness(8, 5, 8, 5), Child = tagRow, IsHitTestVisible = false };

            var canvas = new Canvas { IsHitTestVisible = false };
            canvas.Children.Add(_line);
            canvas.Children.Add(_tag);
            var hint = new TextBlock
            {
                Text = "Clic : copier la couleur · Glisser : mesurer · Échap : annuler",
                Foreground = Brushes.White, FontSize = 12, Padding = new Thickness(10, 6, 10, 6),
                Background = new SolidColorBrush(Color.FromArgb(0xC8, 0, 0, 0)),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 60), IsHitTestVisible = false
            };
            var root = new Grid();
            root.Children.Add(new Image { Source = src, Stretch = Stretch.Fill });
            root.Children.Add(canvas);
            root.Children.Add(hint);
            Content = root;

            MouseMove += (s, e) => Update(e.GetPosition(this));
            MouseLeftButtonDown += (s, e) => { _start = e.GetPosition(this); CaptureMouse(); };
            MouseLeftButtonUp += (s, e) => Finish(e.GetPosition(this));
            MouseRightButtonUp += (s, e) => Close();
            KeyDown += (s, e) => { if (e.Key == Key.Escape) Close(); };
            Loaded += (s, e) => { Activate(); Focus(); Update(Mouse.GetPosition(this)); };
            Closed += (s, e) => _shot.Dispose();
            Deactivated += (s, e) => { try { if (IsVisible) Close(); } catch { } };
        }

        System.Drawing.Color PixelAt(Point p)
        {
            int x = Math.Clamp((int)(p.X * _sx), 0, _shot.Width - 1), y = Math.Clamp((int)(p.Y * _sy), 0, _shot.Height - 1);
            return _shot.GetPixel(x, y);
        }

        static string Hex(System.Drawing.Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

        (double Dx, double Dy, double Dist) Measure(Point a, Point b)
        {
            double dx = Math.Abs(b.X - a.X) * _sx, dy = Math.Abs(b.Y - a.Y) * _sy;
            return (dx, dy, Math.Sqrt(dx * dx + dy * dy));
        }

        void Update(Point p)
        {
            if (_start.HasValue && Mouse.LeftButton == MouseButtonState.Pressed)
            {
                var a = _start.Value;
                _line.X1 = a.X; _line.Y1 = a.Y; _line.X2 = p.X; _line.Y2 = p.Y;
                _line.Visibility = Visibility.Visible;
                var m = Measure(a, p);
                _swatch.Visibility = Visibility.Collapsed;
                _tagText.Text = $"{m.Dist:0} px   ({m.Dx:0} × {m.Dy:0})";
            }
            else
            {
                var c = PixelAt(p);
                _swatch.Visibility = Visibility.Visible;
                _swatch.Background = new SolidColorBrush(Color.FromRgb(c.R, c.G, c.B));
                _tagText.Text = $"{Hex(c)}   rgb({c.R}, {c.G}, {c.B})";
            }
            double x = p.X + 18, y = p.Y + 18;
            if (x > Width - 240) x = p.X - 230;
            if (y > Height - 50) y = p.Y - 44;
            Canvas.SetLeft(_tag, x);
            Canvas.SetTop(_tag, y);
        }

        void Finish(Point p)
        {
            if (IsMouseCaptured) ReleaseMouseCapture();
            var a = _start ?? p;
            _start = null;
            var m = Measure(a, p);
            string title, value;
            if (m.Dist < 4) { var c = PixelAt(p); title = "Couleur copiée"; value = Hex(c); }
            else { title = "Mesure copiée"; value = $"{m.Dist:0} px ({m.Dx:0} × {m.Dy:0})"; }
            try { Picked?.Invoke(title, value); } catch (Exception ex) { App.Log(ex); }
            Close();
        }
    }
}
