using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace DynamicIsland
{
    public partial class MainWindow
    {
        WeatherData _weather;
        List<CalEvent> _events = new();
        readonly HashSet<string> _reminded = new();
        readonly DispatcherTimer _infoTimer = new() { Interval = TimeSpan.FromMinutes(1) };
        int _infoMin;
        static readonly CultureInfo Fr = new("fr-FR");

        void InitInfo()
        {
            _infoTimer.Tick += (s, e) => { _infoMin++; RefreshInfo(false); };
            _infoTimer.Start();
            RefreshInfo(true);
        }

        void RefreshInfo(bool all)
        {
            if (all || _infoMin % 10 == 0) LoadWeather();
            if (all || _infoMin % 2 == 0) LoadMarkets();
            if (all || _infoMin % 1 == 0) LoadSports();
            if (all || _infoMin % 2 == 0) LoadSteam();
            if (all || _infoMin % 15 == 0) LoadCalendar();
        }

        async void LoadWeather()
        {
            try
            {
                _weather = await _info.GetWeatherAsync(S.City);
                WeatherIcon.Text = InfoService.WeatherIcon(_weather.Code);
                WeatherTemp.Text = $"{_weather.Temp:0}°";
                WeatherDesc.Text = InfoService.WeatherLabel(_weather.Code);
                WeatherCity.Text = $"{_weather.City} · vent {_weather.Wind:0} km/h · humidité {_weather.Humidity:0} %";
                WeatherDaily.ItemsSource = _weather.Daily;
                UpdateCompact();
                CheckRain();
            }
            catch { WeatherDesc.Text = "Météo indisponible"; WeatherCity.Text = $"Ville « {S.City} » introuvable ou hors ligne"; }
        }

        DateTime _lastRainAlert = DateTime.MinValue;

        /// <summary>Alerte « Pluie dans n min » (au plus une fois toutes les 2 h).</summary>
        void CheckRain()
        {
            if (!S.RainAlerts || _weather?.RainInMinutes is not int n || n > 60) return;
            if ((DateTime.Now - _lastRainAlert).TotalHours < 2) return;
            _lastRainAlert = DateTime.Now;
            Alert($"Pluie dans {n} min", _weather.City, "\uE1B4");
        }

        async void LoadMarkets()
        {
            try
            {
                var crypto = await _info.GetCryptoAsync(S.Cryptos);
                var stocks = await _info.GetStocksAsync(S.Stocks);
                MarketList.ItemsSource = crypto.Concat(stocks).ToList();
            }
            catch { }
        }

        async void LoadSports()
        {
            try
            {
                var (title, rows) = await _info.GetSportsAsync(S.SportLeague);
                SportTitle.Text = title;
                SportList.ItemsSource = rows.Count > 0 ? rows : new List<InfoRow> { new() { Left = "Aucun match cette semaine", Tone = "sub" } };
            }
            catch { SportList.ItemsSource = new List<InfoRow> { new() { Left = "Scores indisponibles", Tone = "sub" } }; }
        }

        async void LoadSteam()
        {
            if (string.IsNullOrWhiteSpace(S.SteamApiKey)) { SteamEmpty.Visibility = Visibility.Visible; SteamList.ItemsSource = null; return; }
            try
            {
                var rows = await _info.GetSteamAsync(S.SteamApiKey, S.SteamId);
                SteamList.ItemsSource = rows;
                SteamEmpty.Text = "Aucun ami en ligne";
                SteamEmpty.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            }
            catch { SteamEmpty.Text = "Steam : clé ou SteamID invalide (profil public requis)"; SteamEmpty.Visibility = Visibility.Visible; }
        }

        async void LoadCalendar()
        {
            if (string.IsNullOrWhiteSpace(S.IcsUrl)) { CalEmpty.Visibility = Visibility.Visible; CalList.ItemsSource = null; return; }
            try
            {
                _events = await _info.GetEventsAsync(S.IcsUrl);
                CalList.ItemsSource = _events.Select(ev => new InfoRow
                {
                    Left = ev.Summary,
                    Sub = ev.AllDay ? DayLabel(ev.Start) + " · toute la journée" : DayLabel(ev.Start) + " · " + ev.Start.ToString("HH:mm"),
                    Right = Until(ev.Start),
                    Tone = (ev.Start - DateTime.Now).TotalHours < 1 ? "accent" : "sub"
                }).ToList();
                CalEmpty.Text = "Rien de prévu dans les 14 jours";
                CalEmpty.Visibility = _events.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            }
            catch { CalEmpty.Text = "Impossible de lire l'agenda (URL ICS invalide ?)"; CalEmpty.Visibility = Visibility.Visible; }
        }

        static string DayLabel(DateTime d) =>
            d.Date == DateTime.Today ? "Aujourd'hui" : d.Date == DateTime.Today.AddDays(1) ? "Demain" : Fr.TextInfo.ToTitleCase(d.ToString("ddd d MMM", Fr));

        static string Until(DateTime d)
        {
            var t = d - DateTime.Now;
            if (t.TotalMinutes < 0) return "en cours";
            if (t.TotalMinutes < 60) return $"dans {(int)t.TotalMinutes} min";
            if (t.TotalHours < 24) return $"dans {(int)t.TotalHours} h";
            return $"dans {(int)Math.Ceiling(t.TotalDays)} j";
        }

        void CheckCalendarReminders()
        {
            foreach (var ev in _events)
            {
                if (ev.AllDay) continue;
                var mins = (ev.Start - DateTime.Now).TotalMinutes;
                var key = ev.Summary + ev.Start.Ticks;
                if (mins > 0 && mins <= 10 && _reminded.Add(key))
                    Alert(ev.Summary, $"Commence dans {Math.Ceiling(mins)} min ({ev.Start:HH:mm})", "\uE10A");
            }
        }

        void UpdateClocks()
        {
            var rows = new List<InfoRow> { new() { Left = "Ici", Right = DateTime.Now.ToString("HH:mm:ss"), Sub = TimeZoneInfo.Local.StandardName, Tone = "accent" } };
            foreach (var id in (S.TimeZones ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                try
                {
                    var tz = TimeZoneInfo.FindSystemTimeZoneById(id);
                    var t = TimeZoneInfo.ConvertTime(DateTime.Now, tz);
                    var diff = (t - DateTime.Now).TotalHours;
                    rows.Add(new InfoRow
                    {
                        Left = ZoneName(id),
                        Right = t.ToString("HH:mm"),
                        Sub = (Math.Round(diff) >= 0 ? "+" : "") + Math.Round(diff, 1) + " h" + (t.Date != DateTime.Today ? (t.Date > DateTime.Today ? " · demain" : " · hier") : "")
                    });
                }
                catch { }
            }
            ClockList.ItemsSource = rows;
        }

        static string ZoneName(string id) => id switch
        {
            "Eastern Standard Time" => "New York",
            "Pacific Standard Time" => "Los Angeles",
            "Central Standard Time" => "Chicago",
            "Tokyo Standard Time" => "Tokyo",
            "GMT Standard Time" => "Londres",
            "China Standard Time" => "Pékin",
            "AUS Eastern Standard Time" => "Sydney",
            "Arabian Standard Time" => "Dubaï",
            "Russian Standard Time" => "Moscou",
            "E. South America Standard Time" => "São Paulo",
            "India Standard Time" => "New Delhi",
            "Korea Standard Time" => "Séoul",
            "Morocco Standard Time" => "Casablanca",
            "W. Central Africa Standard Time" => "Lagos / Alger",
            _ => id.Replace(" Standard Time", "")
        };

        // ---------- Plugins ----------
        void InitPlugins()
        {
            var plugins = PluginLoader.Load();
            PluginEmpty.Visibility = plugins.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            foreach (var p in plugins)
            {
                try
                {
                    var card = new Border { Style = (Style)FindResource("Card"), Width = 310 };
                    var dp = new DockPanel();
                    var h = new TextBlock { Text = p.Name, Style = (Style)FindResource("H"), Margin = new Thickness(0, 0, 0, 6) };
                    DockPanel.SetDock(h, Dock.Top);
                    dp.Children.Add(h);
                    dp.Children.Add(p.CreateView());
                    card.Child = dp;
                    PluginHost.Children.Add(card);
                }
                catch (Exception ex) { App.Log(ex); }
            }
        }
    }
}
