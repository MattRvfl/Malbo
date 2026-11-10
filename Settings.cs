using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

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

    public class TrackEntry
    {
        public string Title { get; set; }
        public string Artist { get; set; }
        public string App { get; set; }
        public DateTime Time { get; set; }
    }

    /// <summary>Profil : un jeu de réglages appliqué d'un clic (Travail, Jeu, Nuit…).</summary>
    public class ProfileData
    {
        public string Name { get; set; }
        public bool DoNotDisturb { get; set; }
        public string Theme { get; set; } = "Sombre";
        public bool FpsCounter { get; set; }
        public bool HideInFullscreen { get; set; } = true;
        public bool ShowMedia { get; set; } = true;
        public bool ShowNotif { get; set; } = true;
        public bool ShowTools { get; set; } = true;
        public bool ShowFiles { get; set; } = true;
        public bool ShowSystem { get; set; } = true;
        public bool ShowInfo { get; set; } = true;
        public bool ShowLaunch { get; set; } = true;
    }

    /// <summary>
    /// Chiffre les jetons (Discord, Spotify, Steam, API locale) dans settings.json avec DPAPI :
    /// seul le compte Windows courant peut les relire. Les anciennes valeurs en clair restent lisibles.
    /// </summary>
    public class SecretConverter : JsonConverter<string>
    {
        const string Prefix = "dpapi:";

        public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var v = reader.GetString() ?? "";
            if (!v.StartsWith(Prefix, StringComparison.Ordinal)) return v;
            try
            {
                var raw = System.Security.Cryptography.ProtectedData.Unprotect(Convert.FromBase64String(v.Substring(Prefix.Length)), null,
                    System.Security.Cryptography.DataProtectionScope.CurrentUser);
                return System.Text.Encoding.UTF8.GetString(raw);
            }
            catch { return ""; } // autre compte / autre PC : le jeton est perdu, il faudra se reconnecter
        }

        public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
        {
            if (string.IsNullOrEmpty(value)) { writer.WriteStringValue(""); return; }
            try
            {
                var enc = System.Security.Cryptography.ProtectedData.Protect(System.Text.Encoding.UTF8.GetBytes(value), null,
                    System.Security.Cryptography.DataProtectionScope.CurrentUser);
                writer.WriteStringValue(Prefix + Convert.ToBase64String(enc));
            }
            catch { writer.WriteStringValue(value); }
        }
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
        [JsonConverter(typeof(SecretConverter))] public string SteamApiKey { get; set; } = "";
        public string SteamId { get; set; } = "";
        public string TimeZones { get; set; } = "Eastern Standard Time,Pacific Standard Time,Tokyo Standard Time";

        // Outils
        public int PomodoroWork { get; set; } = 25;
        public int PomodoroBreak { get; set; } = 5;
        public bool FocusDnd { get; set; } = true;
        public bool RainAlerts { get; set; } = true;

        // Spotify / Discord
        public string SpotifyClientId { get; set; } = "";
        [JsonConverter(typeof(SecretConverter))] public string SpotifyRefresh { get; set; } = "";
        public string DiscordClientId { get; set; } = "";
        [JsonConverter(typeof(SecretConverter))] public string DiscordSecret { get; set; } = "";
        public string DiscordRedirect { get; set; } = "http://127.0.0.1";
        [JsonConverter(typeof(SecretConverter))] public string DiscordRefresh { get; set; } = "";

        // Jeu
        public bool FpsCounter { get; set; } = false;
        public string PresentMonPath { get; set; } = "";

        // Mises à jour (manifeste update.json du dépôt, sinon dernière release GitHub)
        public const string DefaultRepo = "MattRvfl/Malbo";
        public string UpdateRepo { get; set; } = DefaultRepo;
        public bool AutoUpdate { get; set; } = true;
        /// <summary>Installe seule la mise à jour quand l'île est au repos (sinon : bouton « Installer »).</summary>
        public bool AutoInstallUpdates { get; set; } = true;

        /// <summary>Recevoir aussi les pré-versions (alpha, bêta).</summary>
        public bool BetaChannel { get; set; } = false;

        // Apparence (1.3)
        /// <summary>Couleur d'accent personnalisée (#RRGGBB), utilisée quand la couleur de la pochette est désactivée.</summary>
        public string AccentColor { get; set; } = "#0A84FF";
        /// <summary>L'île suit l'écran où se trouve la souris.</summary>
        public bool FollowMouseScreen { get; set; } = false;
        public bool WheelVolume { get; set; } = true;
        /// <summary>Onglets affichés directement dans la barre, dans l'ordre ; les autres passent derrière le bouton « … ».</summary>
        public string MainTabs { get; set; } = "Média, Notifs, Outils, Système, Lancer";
        /// <summary>Décalage vertical de l'île (px) pour ne pas recouvrir les onglets du navigateur ou une barre de titre.</summary>
        public int TopOffset { get; set; } = 0;
        public bool TipsShown { get; set; } = false;

        // Notifications (1.3)
        public bool GroupNotifs { get; set; } = true;
        /// <summary>Heures calmes « 23:00-07:00 » : pas d'aperçu pendant cette plage (vide = désactivé).</summary>
        public string QuietHours { get; set; } = "";
        /// <summary>Mots-clés (séparés par des virgules) qui passent malgré Ne pas déranger.</summary>
        public string PriorityKeywords { get; set; } = "";
        public int SnoozeMinutes { get; set; } = 30;

        // Réunions / appels (1.3)
        public bool MeetingDnd { get; set; } = true;

        // Alertes système (1.3)
        public bool SystemAlerts { get; set; } = true;

        // Paroles (1.3)
        public bool TranslateLyrics { get; set; } = false;
        public string TranslateTo { get; set; } = "fr";

        // Activités en direct : API locale (1.3)
        public bool LiveApi { get; set; } = true;
        public int LiveApiPort { get; set; } = 8765;
        [JsonConverter(typeof(SecretConverter))] public string LiveApiToken { get; set; } = "";

        // Données (1.3)
        public string Notes { get; set; } = "";
        public List<string> PinnedClips { get; set; } = new();
        public List<TrackEntry> History { get; set; } = new();
        public string ActiveProfile { get; set; } = "Normal";
        public List<ProfileData> Profiles { get; set; } = new();
        public bool ShowMore { get; set; } = true;

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
                {
                    var st = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
                    // anciens réglages sans dépôt : on branche les mises à jour sur le dépôt officiel
                    if (string.IsNullOrWhiteSpace(st.UpdateRepo)) st.UpdateRepo = DefaultRepo;
                    st.Normalize();
                    return st;
                }
            }
            catch { }
            var fresh = new AppSettings();
            fresh.Normalize();
            return fresh;
        }

        /// <summary>Valeurs par défaut qui ne peuvent pas être de simples initialiseurs (jeton aléatoire, profils).</summary>
        public void Normalize()
        {
            if (string.IsNullOrEmpty(LiveApiToken))
                LiveApiToken = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(12)).ToLowerInvariant();
            Profiles ??= new();
            if (Profiles.Count == 0)
            {
                Profiles.Add(new ProfileData { Name = "Normal" });
                Profiles.Add(new ProfileData { Name = "Travail", DoNotDisturb = true });
                Profiles.Add(new ProfileData { Name = "Jeu", DoNotDisturb = true, FpsCounter = true, HideInFullscreen = false });
                Profiles.Add(new ProfileData { Name = "Nuit", DoNotDisturb = true, Theme = "Sombre" });
            }
            PinnedClips ??= new();
            History ??= new();
            Notes ??= "";
        }

        /// <summary>Copie de sauvegarde. Les jetons y restent chiffrés pour ce compte Windows : sur un autre PC, il faudra reconnecter Spotify/Discord.</summary>
        public void ExportTo(string path) => File.WriteAllText(path, JsonSerializer.Serialize(this, JsonOpts));

        public static AppSettings ImportFrom(string path)
        {
            var st = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path)) ?? throw new InvalidDataException("Fichier de réglages illisible.");
            if (string.IsNullOrWhiteSpace(st.UpdateRepo)) st.UpdateRepo = DefaultRepo;
            st.Normalize();
            return st;
        }

        static readonly object SaveLock = new();
        static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

        /// <summary>Écriture atomique (fichier temporaire puis remplacement) et protégée : Discord/Spotify sauvegardent depuis d'autres threads.</summary>
        public void Save()
        {
            try
            {
                lock (SaveLock)
                {
                    Directory.CreateDirectory(Dir);
                    var tmp = FilePath + ".tmp";
                    File.WriteAllText(tmp, JsonSerializer.Serialize(this, JsonOpts));
                    File.Move(tmp, FilePath, true);
                }
            }
            catch (Exception ex) { App.Log(ex); }
        }
    }
}
