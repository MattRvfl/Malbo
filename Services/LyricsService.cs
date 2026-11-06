using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace DynamicIsland
{
    public class LyricLine
    {
        public TimeSpan Time;
        public string Text;
        public bool Synced => Time >= TimeSpan.Zero;
    }

    /// <summary>Paroles synchronisées via lrclib.net (gratuit, sans clé).</summary>
    public class LyricsService
    {
        static readonly HttpClient Http = Create();
        readonly Dictionary<string, List<LyricLine>> _cache = new();

        static HttpClient Create()
        {
            var h = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
            h.DefaultRequestHeaders.UserAgent.ParseAdd("DynamicIslandWindows/1.0");
            return h;
        }

        static string E(string s) => Uri.EscapeDataString(s ?? "");

        public async Task<List<LyricLine>> GetAsync(string title, string artist, string album, TimeSpan dur)
        {
            var key = title + "|" + artist;
            if (_cache.TryGetValue(key, out var c)) return c;
            List<LyricLine> res = null;

            // 1) correspondance exacte
            try
            {
                var url = $"https://lrclib.net/api/get?track_name={E(title)}&artist_name={E(artist)}";
                if (!string.IsNullOrEmpty(album)) url += $"&album_name={E(album)}";
                if (dur.TotalSeconds > 0) url += $"&duration={(int)Math.Round(dur.TotalSeconds)}";
                var r = await Http.GetAsync(url);
                if (r.IsSuccessStatusCode)
                {
                    using var j = JsonDocument.Parse(await r.Content.ReadAsStringAsync());
                    res = FromJson(j.RootElement);
                }
            }
            catch { }

            // 2) recherche par titre nettoyé + premier artiste
            if (res == null)
                res = await Search($"https://lrclib.net/api/search?track_name={E(Clean(title))}&artist_name={E(FirstArtist(artist))}");

            // 3) recherche libre (utile pour YouTube : "Artiste - Titre (Clip officiel)")
            if (res == null)
                res = await Search($"https://lrclib.net/api/search?q={E(Clean(title) + " " + FirstArtist(artist))}");
            if (res == null)
                res = await Search($"https://lrclib.net/api/search?q={E(Clean(title))}");

            res ??= new List<LyricLine>();
            _cache[key] = res;
            return res;
        }

        static async Task<List<LyricLine>> Search(string url)
        {
            try
            {
                var s = await Http.GetStringAsync(url);
                using var j = JsonDocument.Parse(s);
                JsonElement? best = null;
                foreach (var e in j.RootElement.EnumerateArray())
                {
                    if (e.TryGetProperty("syncedLyrics", out var sl) && sl.ValueKind == JsonValueKind.String) { best = e; break; }
                    best ??= e;
                }
                return best != null ? FromJson(best.Value) : null;
            }
            catch { return null; }
        }

        static List<LyricLine> FromJson(JsonElement e)
        {
            if (e.TryGetProperty("syncedLyrics", out var s) && s.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(s.GetString()))
                return ParseLrc(s.GetString());
            if (e.TryGetProperty("plainLyrics", out var p) && p.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(p.GetString()))
                return p.GetString().Split('\n').Select(l => new LyricLine { Time = TimeSpan.FromSeconds(-1), Text = l.Trim() }).ToList();
            return null;
        }

        static readonly Regex Rx = new(@"\[(\d+):(\d+(?:[.:]\d+)?)\]");

        public static List<LyricLine> ParseLrc(string lrc)
        {
            var list = new List<LyricLine>();
            foreach (var raw in lrc.Split('\n'))
            {
                var line = raw.Trim();
                var ms = Rx.Matches(line);
                if (ms.Count == 0) continue;
                var text = Rx.Replace(line, "").Trim();
                foreach (Match m in ms)
                {
                    var sec = double.Parse(m.Groups[2].Value.Replace(':', '.'), CultureInfo.InvariantCulture);
                    list.Add(new LyricLine { Time = TimeSpan.FromMinutes(int.Parse(m.Groups[1].Value)) + TimeSpan.FromSeconds(sec), Text = text });
                }
            }
            return list.OrderBy(l => l.Time).ToList();
        }

        static string Clean(string t)
        {
            t ??= "";
            t = Regex.Replace(t, @"\s*[\(\[][^\)\]]*[\)\]]", "");
            t = Regex.Replace(t, @"\s+-\s+(remaster|live|radio|version|edit|mono|stereo).*$", "", RegexOptions.IgnoreCase);
            return t.Trim();
        }

        static string FirstArtist(string a) =>
            (a ?? "").Split(new[] { ",", "&", " feat", " ft.", " x " }, StringSplitOptions.None)[0].Replace(" - Topic", "").Trim();
    }
}
