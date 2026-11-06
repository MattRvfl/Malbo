using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace DynamicIsland
{
    public class WeatherData
    {
        public string City;
        public double Temp, Wind, Humidity;
        public int Code;
        public List<InfoRow> Daily = new();
        /// <summary>Minutes avant la pluie (prochain quart d'heure ≥ 0,1 mm) ; null s'il pleut déjà ou si rien n'est prévu.</summary>
        public int? RainInMinutes;
    }

    public class CalEvent
    {
        public DateTime Start;
        public string Summary;
        public bool AllDay;
    }

    /// <summary>Météo (open-meteo), crypto (CoinGecko), bourse (Yahoo), sport (ESPN), Steam, agenda ICS.</summary>
    public class InfoService
    {
        static readonly HttpClient Http = Create();
        static HttpClient Create()
        {
            var h = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            h.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) DynamicIsland/1.0");
            return h;
        }
        static string U(string s) => Uri.EscapeDataString(s ?? "");
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        static readonly CultureInfo Fr = new("fr-FR");

        static async Task<JsonDocument> Json(string url) => JsonDocument.Parse(await Http.GetStringAsync(url));

        // ---------- Météo ----------
        string _geoCity; double _lat, _lon; string _geoName;

        public static string WeatherIcon(int c) => c switch
        {
            0 => "☀", 1 or 2 => "⛅", 3 => "☁", 45 or 48 => "🌫",
            >= 51 and <= 57 => "🌦", >= 61 and <= 67 => "🌧", >= 71 and <= 77 => "❄",
            >= 80 and <= 82 => "🌧", 85 or 86 => "🌨", >= 95 => "⛈", _ => "🌡"
        };
        public static string WeatherLabel(int c) => c switch
        {
            0 => "Ensoleillé", 1 => "Plutôt dégagé", 2 => "Partiellement nuageux", 3 => "Couvert",
            45 or 48 => "Brouillard", >= 51 and <= 57 => "Bruine", >= 61 and <= 67 => "Pluie",
            >= 71 and <= 77 => "Neige", >= 80 and <= 82 => "Averses", 85 or 86 => "Averses de neige",
            >= 95 => "Orage", _ => "—"
        };

        public async Task<WeatherData> GetWeatherAsync(string city)
        {
            if (_geoCity != city)
            {
                using var g = await Json($"https://geocoding-api.open-meteo.com/v1/search?name={U(city)}&count=1&language=fr");
                var r = g.RootElement.GetProperty("results")[0];
                _lat = r.GetProperty("latitude").GetDouble();
                _lon = r.GetProperty("longitude").GetDouble();
                _geoName = r.GetProperty("name").GetString();
                _geoCity = city;
            }
            using var w = await Json(string.Format(Inv,
                "https://api.open-meteo.com/v1/forecast?latitude={0}&longitude={1}&current=temperature_2m,weather_code,wind_speed_10m,relative_humidity_2m,precipitation&minutely_15=precipitation&forecast_minutely_15=8&daily=weather_code,temperature_2m_max,temperature_2m_min&timezone=auto&forecast_days=5",
                _lat, _lon));
            var cur = w.RootElement.GetProperty("current");
            var d = new WeatherData
            {
                City = _geoName,
                Temp = cur.GetProperty("temperature_2m").GetDouble(),
                Code = cur.GetProperty("weather_code").GetInt32(),
                Wind = cur.GetProperty("wind_speed_10m").GetDouble(),
                Humidity = cur.GetProperty("relative_humidity_2m").GetDouble()
            };
            try { d.RainInMinutes = RainIn(w.RootElement, cur); } catch { d.RainInMinutes = null; }
            var daily = w.RootElement.GetProperty("daily");
            var times = daily.GetProperty("time");
            for (int i = 1; i < times.GetArrayLength(); i++)
            {
                var day = DateTime.Parse(times[i].GetString(), Inv);
                int code = daily.GetProperty("weather_code")[i].GetInt32();
                d.Daily.Add(new InfoRow
                {
                    Left = $"{WeatherIcon(code)}  {Fr.TextInfo.ToTitleCase(day.ToString("dddd", Fr))}",
                    Right = $"{daily.GetProperty("temperature_2m_max")[i].GetDouble():0}° / {daily.GetProperty("temperature_2m_min")[i].GetDouble():0}°",
                    Tone = "sub"
                });
            }
            return d;
        }

        static int? RainIn(JsonElement root, JsonElement cur)
        {
            if (cur.TryGetProperty("precipitation", out var p) && p.ValueKind == JsonValueKind.Number && p.GetDouble() >= 0.1) return null; // il pleut déjà
            if (!root.TryGetProperty("minutely_15", out var m)) return null;
            var times = m.GetProperty("time");
            var prec = m.GetProperty("precipitation");
            int offset = root.TryGetProperty("utc_offset_seconds", out var o) && o.ValueKind == JsonValueKind.Number ? o.GetInt32() : 0;
            var nowLoc = DateTime.UtcNow.AddSeconds(offset);
            for (int i = 0; i < times.GetArrayLength() && i < prec.GetArrayLength(); i++)
            {
                if (prec[i].ValueKind != JsonValueKind.Number || prec[i].GetDouble() < 0.1) continue;
                var t = DateTime.Parse(times[i].GetString(), Inv);
                if (t <= nowLoc) continue;
                return (int)Math.Ceiling((t - nowLoc).TotalMinutes);
            }
            return null;
        }

        // ---------- Crypto ----------
        public async Task<List<InfoRow>> GetCryptoAsync(string ids)
        {
            var list = new List<InfoRow>();
            if (string.IsNullOrWhiteSpace(ids)) return list;
            using var j = await Json($"https://api.coingecko.com/api/v3/simple/price?ids={U(ids.Replace(" ", ""))}&vs_currencies=eur&include_24hr_change=true");
            foreach (var p in j.RootElement.EnumerateObject())
            {
                var price = p.Value.GetProperty("eur").GetDouble();
                var ch = p.Value.TryGetProperty("eur_24h_change", out var c) && c.ValueKind == JsonValueKind.Number ? c.GetDouble() : 0;
                list.Add(new InfoRow
                {
                    Left = Fr.TextInfo.ToTitleCase(p.Name),
                    Sub = price.ToString(price < 10 ? "N4" : "N0", Fr) + " €",
                    Right = (ch >= 0 ? "▲ " : "▼ ") + Math.Abs(ch).ToString("0.0", Fr) + " %",
                    Tone = ch >= 0 ? "up" : "down"
                });
            }
            return list;
        }

        // ---------- Bourse ----------
        public async Task<List<InfoRow>> GetStocksAsync(string symbols)
        {
            var list = new List<InfoRow>();
            foreach (var sym in (symbols ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                try
                {
                    using var j = await Json($"https://query1.finance.yahoo.com/v8/finance/chart/{U(sym)}?interval=1d&range=5d");
                    var meta = j.RootElement.GetProperty("chart").GetProperty("result")[0].GetProperty("meta");
                    var price = meta.GetProperty("regularMarketPrice").GetDouble();
                    var prev = meta.TryGetProperty("chartPreviousClose", out var pc) ? pc.GetDouble() : price;
                    var cur = meta.TryGetProperty("currency", out var cu) ? cu.GetString() : "";
                    var ch = prev != 0 ? (price - prev) / prev * 100 : 0;
                    list.Add(new InfoRow
                    {
                        Left = sym.ToUpperInvariant(),
                        Sub = price.ToString("N2", Fr) + " " + cur,
                        Right = (ch >= 0 ? "▲ " : "▼ ") + Math.Abs(ch).ToString("0.0", Fr) + " %",
                        Tone = ch >= 0 ? "up" : "down"
                    });
                }
                catch { }
            }
            return list;
        }

        // ---------- Sport ----------
        public async Task<(string, List<InfoRow>)> GetSportsAsync(string league)
        {
            var list = new List<InfoRow>();
            using var j = await Json($"https://site.api.espn.com/apis/site/v2/sports/{league}/scoreboard");
            string title = "Sport";
            try { title = j.RootElement.GetProperty("leagues")[0].GetProperty("name").GetString(); } catch { }
            foreach (var ev in j.RootElement.GetProperty("events").EnumerateArray())
            {
                try
                {
                    var comp = ev.GetProperty("competitions")[0];
                    var teams = comp.GetProperty("competitors").EnumerateArray().ToList();
                    var home = teams.FirstOrDefault(t => t.GetProperty("homeAway").GetString() == "home");
                    var away = teams.FirstOrDefault(t => t.GetProperty("homeAway").GetString() == "away");
                    string Name(JsonElement t) => t.GetProperty("team").TryGetProperty("shortDisplayName", out var n) ? n.GetString() : t.GetProperty("team").GetProperty("displayName").GetString();
                    string Score(JsonElement t) => t.TryGetProperty("score", out var s) ? s.GetString() : "";
                    var status = ev.GetProperty("status").GetProperty("type");
                    var state = status.GetProperty("state").GetString(); // pre / in / post
                    var date = DateTime.Parse(ev.GetProperty("date").GetString(), Inv, DateTimeStyles.AdjustToUniversal).ToLocalTime();
                    list.Add(new InfoRow
                    {
                        Left = $"{Name(home)}  –  {Name(away)}",
                        Right = state == "pre" ? date.ToString("ddd HH:mm", Fr) : $"{Score(home)} - {Score(away)}",
                        Sub = state == "in" ? "🔴 " + status.GetProperty("shortDetail").GetString() : state == "post" ? "Terminé" : "",
                        Tone = state == "in" ? "down" : state == "pre" ? "sub" : ""
                    });
                }
                catch { }
            }
            return (title, list);
        }

        // ---------- Steam ----------
        public async Task<List<InfoRow>> GetSteamAsync(string key, string id)
        {
            var list = new List<InfoRow>();
            if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(id)) return list;
            using var f = await Json($"https://api.steampowered.com/ISteamUser/GetFriendList/v1/?key={U(key)}&steamid={U(id)}&relationship=friend");
            var ids = f.RootElement.GetProperty("friendslist").GetProperty("friends").EnumerateArray().Select(x => x.GetProperty("steamid").GetString()).Take(100).ToList();
            if (ids.Count == 0) return list;
            using var p = await Json($"https://api.steampowered.com/ISteamUser/GetPlayerSummaries/v2/?key={U(key)}&steamids={string.Join(",", ids)}");
            foreach (var pl in p.RootElement.GetProperty("response").GetProperty("players").EnumerateArray())
            {
                int st = pl.GetProperty("personastate").GetInt32();
                var game = pl.TryGetProperty("gameextrainfo", out var g) ? g.GetString() : null;
                if (st == 0 && game == null) continue;
                list.Add(new InfoRow
                {
                    Left = pl.GetProperty("personaname").GetString(),
                    Sub = game != null ? "🎮 " + game : st switch { 2 => "Occupé", 3 => "Absent", 4 => "Endormi", _ => "En ligne" },
                    Right = "●",
                    Tone = game != null ? "up" : "accent"
                });
            }
            return list.OrderBy(r => r.Tone == "up" ? 0 : 1).ToList();
        }

        // ---------- Agenda (ICS : Google Agenda "adresse secrète iCal", Outlook "publier le calendrier") ----------
        public async Task<List<CalEvent>> GetEventsAsync(string url)
        {
            var res = new List<CalEvent>();
            if (string.IsNullOrWhiteSpace(url)) return res;
            if (url.StartsWith("webcal://", StringComparison.OrdinalIgnoreCase)) url = "https://" + url.Substring(9);
            var txt = (await Http.GetStringAsync(url)).Replace("\r\n ", "").Replace("\r\n\t", "").Replace("\n ", "");
            var lines = txt.Split('\n').Select(l => l.TrimEnd('\r')).ToArray();
            var now = DateTime.Now;
            CalEvent cur = null; string rrule = null;
            foreach (var line in lines)
            {
                if (line == "BEGIN:VEVENT") { cur = new CalEvent(); rrule = null; continue; }
                if (cur == null) continue;
                if (line == "END:VEVENT")
                {
                    if (cur.Start != default)
                    {
                        var start = rrule != null ? NextOccurrence(cur.Start, rrule, now) : cur.Start;
                        if (start.HasValue && start.Value > now.AddHours(-1) && start.Value < now.AddDays(14))
                            res.Add(new CalEvent { Start = start.Value, Summary = cur.Summary ?? "(sans titre)", AllDay = cur.AllDay });
                    }
                    cur = null; continue;
                }
                int c = line.IndexOf(':');
                if (c < 0) continue;
                var name = line.Substring(0, c);
                var val = line.Substring(c + 1);
                if (name.StartsWith("SUMMARY")) cur.Summary = val.Replace("\\,", ",").Replace("\\n", " ");
                else if (name.StartsWith("RRULE")) rrule = val;
                else if (name.StartsWith("DTSTART"))
                {
                    try
                    {
                        if (val.Length == 8) { cur.Start = DateTime.ParseExact(val, "yyyyMMdd", Inv); cur.AllDay = true; }
                        else
                        {
                            var dt = DateTime.ParseExact(val.TrimEnd('Z'), "yyyyMMdd'T'HHmmss", Inv);
                            if (val.EndsWith("Z")) dt = DateTime.SpecifyKind(dt, DateTimeKind.Utc).ToLocalTime();
                            else
                            {
                                var tz = System.Text.RegularExpressions.Regex.Match(name, "TZID=([^;:]+)");
                                if (tz.Success)
                                {
                                    try { dt = TimeZoneInfo.ConvertTime(dt, TimeZoneInfo.FindSystemTimeZoneById(tz.Groups[1].Value.Trim('"')), TimeZoneInfo.Local); } catch { }
                                }
                            }
                            cur.Start = dt;
                        }
                    }
                    catch { }
                }
            }
            return res.OrderBy(e => e.Start).Take(10).ToList();
        }

        static DateTime? NextOccurrence(DateTime start, string rrule, DateTime now)
        {
            var p = rrule.Split(';').Select(x => x.Split('=')).Where(x => x.Length == 2).ToDictionary(x => x[0], x => x[1]);
            if (!p.TryGetValue("FREQ", out var freq)) return start;
            int interval = p.TryGetValue("INTERVAL", out var iv) && int.TryParse(iv, out var ivv) ? ivv : 1;
            int count = p.TryGetValue("COUNT", out var cs) && int.TryParse(cs, out var cv) ? cv : int.MaxValue;
            DateTime? until = null;
            if (p.TryGetValue("UNTIL", out var us))
            {
                try { until = DateTime.ParseExact(us.TrimEnd('Z').Substring(0, 8), "yyyyMMdd", Inv).AddDays(1); } catch { }
            }
            var d = start;
            for (int i = 0; i < 5000 && i < count; i++)
            {
                if (until.HasValue && d > until.Value) return null;
                if (d > now.AddHours(-1)) return d;
                d = freq switch
                {
                    "DAILY" => d.AddDays(interval),
                    "WEEKLY" => d.AddDays(7 * interval),
                    "MONTHLY" => d.AddMonths(interval),
                    "YEARLY" => d.AddYears(interval),
                    _ => d.AddYears(100)
                };
            }
            return null;
        }
    }
}
