using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace DynamicIsland
{
    public class AlarmData
    {
        public string Time { get; set; }
        public string Label { get; set; }
    }

    public class ShortcutData
    {
        public string Name { get; set; }
        public string Target { get; set; }
    }

    public class AppSettings
    {
        // Île
        public int HoverDelay { get; set; } = 180;
        public int HoverZone { get; set; } = 12;

        // Apparence
        public string Position { get; set; } = "Centre";
        public int ScreenIndex { get; set; } = 0;
        public string Theme { get; set; } = "Sombre";
        public double Opacity { get; set; } = 1;
        public double Scale { get; set; } = 1;
        public bool AccentFromArt { get; set; } = true;
        public bool LyricsInCompact { get; set; } = true;
        public bool HideInFullscreen { get; set; } = true;
        public bool VolumeOsd { get; set; } = true;
        public bool BrightnessOsd { get; set; } = true;
        public bool TrackPeek { get; set; } = true;
        public bool StartWithWindows { get; set; } = false;

        // Notifications
        public bool DoNotDisturb { get; set; } = false;
        public string BlockedApps { get; set; } = "";
        public bool HideWindowsBanners { get; set; } = true;
        public Dictionary<string, int> BannerBackup { get; set; } = new();

        // Infos
        public string City { get; set; } = "Paris";
        public string IcsUrl { get; set; } = "";
        public string Cryptos { get; set; } = "bitcoin,ethereum,solana";
        public string Stocks { get; set; } = "AAPL,MSFT,NVDA";
        public string SportLeague { get; set; } = "soccer/fra.1";
        public string SteamApiKey { get; set; } = "";
        public string SteamId { get; set; } = "";
        public string TimeZones { get; set; } = "Eastern Standard Time,Pacific Standard Time,Tokyo Standard Time";

        // Outils
        public int PomodoroWork { get; set; } = 25;
        public int PomodoroBreak { get; set; } = 5;
        public bool FocusDnd { get; set; } = true;
        public bool RainAlerts { get; set; } = true;

        // Spotify / Discord
        public string SpotifyClientId { get; set; } = "";
        public string SpotifyRefresh { get; set; } = "";
        public string DiscordClientId { get; set; } = "";
        public string DiscordSecret { get; set; } = "";
        public string DiscordRedirect { get; set; } = "http://127.0.0.1";
        public string DiscordRefresh { get; set; } = "";

        // Jeu
        public bool FpsCounter { get; set; } = false;
        public string PresentMonPath { get; set; } = "";

        // Mises à jour
        public string UpdateRepo { get; set; } = "";
        public bool AutoUpdate { get; set; } = true;

        // Onglets
        public bool ShowMedia { get; set; } = true;
        public bool ShowNotif { get; set; } = true;
        public bool ShowTools { get; set; } = true;
        public bool ShowFiles { get; set; } = true;
        public bool ShowSystem { get; set; } = true;
        public bool ShowInfo { get; set; } = true;
        public bool ShowPlugins { get; set; } = true;
        public bool ShowLaunch { get; set; } = true;

        // Données
        public List<TodoItem> Todos { get; set; } = new();
        public List<AlarmData> Alarms { get; set; } = new();
        public List<string> Shelf { get; set; } = new();
        public List<ShortcutData> Shortcuts { get; set; } = new()
        {
            new ShortcutData { Name = "Explorateur", Target = "explorer.exe" },
            new ShortcutData { Name = "Paramètres", Target = "ms-settings:" },
            new ShortcutData { Name = "Gestionnaire des tâches", Target = "taskmgr.exe" },
            new ShortcutData { Name = "YouTube", Target = "https://youtube.com" }
        };

        public static string Dir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Malbo");
        static string OldDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DynamicIsland");
        public static string FilePath => Path.Combine(Dir, "settings.json");
        public static string PluginDir => Path.Combine(Dir, "plugins");

        public static AppSettings Load()
        {
            try
            {
                var old = Path.Combine(OldDir, "settings.json");
                if (!File.Exists(FilePath) && File.Exists(old)) { Directory.CreateDirectory(Dir); File.Copy(old, FilePath); }
                if (File.Exists(FilePath))
                    return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
            }
            catch { }
            return new AppSettings();
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Dir);
                File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch { }
        }
    }
}
