using System;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace DynamicIsland
{
    /// <summary>Reconnaissance de texte dans une image, avec le moteur intégré à Windows (hors ligne).</summary>
    public static class OcrService
    {
        /// <summary>Renvoie le texte trouvé (lignes séparées), ou une chaîne vide. Lève une exception si aucune langue OCR n'est installée.</summary>
        public static async Task<string> ReadAsync(byte[] png)
        {
            if (png == null || png.Length == 0) return "";
            var engine = OcrEngine.TryCreateFromUserProfileLanguages()
                         ?? OcrEngine.AvailableRecognizerLanguages.Select(OcrEngine.TryCreateFromLanguage).FirstOrDefault(e => e != null);
            if (engine == null) throw new InvalidOperationException("Aucune langue de reconnaissance de texte installée dans Windows.");

            using var ms = new InMemoryRandomAccessStream();
            await ms.WriteAsync(png.AsBuffer());
            ms.Seek(0);
            var dec = await BitmapDecoder.CreateAsync(ms);
            // Le moteur refuse les images plus grandes que MaxImageDimension : on réduit si besoin.
            uint w = dec.PixelWidth, h = dec.PixelHeight, max = OcrEngine.MaxImageDimension;
            var tr = new BitmapTransform();
            if (w > max || h > max)
            {
                double k = Math.Min((double)max / w, (double)max / h);
                tr.ScaledWidth = (uint)Math.Max(1, w * k);
                tr.ScaledHeight = (uint)Math.Max(1, h * k);
                tr.InterpolationMode = BitmapInterpolationMode.Fant;
            }
            using var bmp = await dec.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, tr,
                ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage);
            var res = await engine.RecognizeAsync(bmp);
            return string.Join(Environment.NewLine, res.Lines.Select(l => l.Text));
        }
    }
}
