using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;

namespace DynamicIsland
{
    public class NotifData
    {
        public uint Id;
        public string App = "", Title = "", Body = "";
        public string Aumid = "";
        public DateTime Time;
        public BitmapSource Icon;
    }

    /// <summary>
    /// Intercepte les notifications Windows (Discord, Telegram, WhatsApp, Messenger, Slack, Teams, mails…)
    /// via UserNotificationListener. Nécessite l'autorisation "Accès aux notifications".
    /// </summary>
    public class NotificationService
    {
        UserNotificationListener _l;
        readonly HashSet<uint> _seen = new();
        readonly Dictionary<string, BitmapSource> _icons = new();
        public event Action<NotifData> Received;
        public string Status { get; private set; } = "Initialisation…";
        public bool Ok { get; private set; }

        public async Task InitAsync()
        {
            try
            {
                _l = UserNotificationListener.Current;
                var a = await _l.RequestAccessAsync();
                if (a != UserNotificationListenerAccessStatus.Allowed)
                {
                    Status = "Accès refusé. Active « Accès aux notifications » dans Paramètres › Confidentialité › Notifications.";
                    return;
                }
                foreach (var n in await _l.GetNotificationsAsync(NotificationKinds.Toast)) _seen.Add(n.Id);
                Ok = true;
                Status = "";
                _ = Loop();
            }
            catch (Exception ex)
            {
                Status = "Écoute des notifications indisponible : " + ex.Message;
                App.Log(ex);
            }
        }

        async Task Loop()
        {
            while (true)
            {
                await Task.Delay(1000);
                try
                {
                    var list = await _l.GetNotificationsAsync(NotificationKinds.Toast);
                    var ids = new HashSet<uint>();
                    foreach (var n in list)
                    {
                        ids.Add(n.Id);
                        if (!_seen.Add(n.Id)) continue;
                        var d = await Convert(n);
                        if (d != null) Received?.Invoke(d);
                    }
                    _seen.IntersectWith(ids);
                }
                catch { }
            }
        }

        async Task<NotifData> Convert(UserNotification n)
        {
            try
            {
                var d = new NotifData { Id = n.Id, Time = n.CreationTime.LocalDateTime };
                try { d.App = n.AppInfo?.DisplayInfo?.DisplayName ?? "Application"; } catch { d.App = "Application"; }
                try { d.Aumid = n.AppInfo?.AppUserModelId ?? ""; } catch { d.Aumid = ""; }
                var binding = n.Notification?.Visual?.GetBinding(KnownNotificationBindings.ToastGeneric);
                if (binding != null)
                {
                    var texts = binding.GetTextElements().Select(t => t.Text).Where(t => !string.IsNullOrWhiteSpace(t)).ToList();
                    d.Title = texts.FirstOrDefault() ?? d.App;
                    d.Body = string.Join("\n", texts.Skip(1));
                }
                d.Icon = await Icon(n, d.App);
                return d;
            }
            catch { return null; }
        }

        async Task<BitmapSource> Icon(UserNotification n, string app)
        {
            if (_icons.TryGetValue(app, out var c)) return c;
            BitmapSource bmp = null;
            try
            {
                var r = n.AppInfo.DisplayInfo.GetLogo(new Windows.Foundation.Size(64, 64));
                using var ras = await r.OpenReadAsync();
                using var st = ras.AsStreamForRead();
                using var ms = new MemoryStream();
                await st.CopyToAsync(ms);
                ms.Position = 0;
                var bi = new BitmapImage();
                bi.BeginInit(); bi.CacheOption = BitmapCacheOption.OnLoad; bi.DecodePixelWidth = 64; bi.StreamSource = ms; bi.EndInit();
                bi.Freeze();
                bmp = bi;
            }
            catch { }
            if (bmp == null)
            {
                try { bmp = await Task.Run(() => IconFromProcess(app)); } catch { }
            }
            _icons[app] = bmp;
            return bmp;
        }

        static string Norm(string s) => new string((s ?? "").ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

        /// <summary>Repli : icône de l'exe d'un processus en cours dont le nom correspond à l'appli (« Google Chrome » → chrome.exe).</summary>
        static BitmapSource IconFromProcess(string app)
        {
            var a = Norm(app);
            if (a.Length < 4) return null;
            var procs = System.Diagnostics.Process.GetProcesses();
            try
            {
                var matches = procs.Where(p =>
                {
                    try
                    {
                        var n = Norm(p.ProcessName);
                        if (n.Length < 4) return false;
                        return a.Contains(n) || n.Contains(a);
                    }
                    catch { return false; }
                })
                .OrderByDescending(p => { try { return p.MainWindowHandle != IntPtr.Zero; } catch { return false; } })
                .ThenBy(p => { try { return Math.Abs(Norm(p.ProcessName).Length - a.Length); } catch { return int.MaxValue; } });
                foreach (var p in matches)
                {
                    try
                    {
                        var file = p.MainModule?.FileName;
                        if (string.IsNullOrEmpty(file)) continue;
                        if (IconHelper.FromFile(file) is BitmapSource b) return b;
                    }
                    catch { }
                }
            }
            finally { foreach (var p in procs) { try { p.Dispose(); } catch { } } }
            return null;
        }

        public void Remove(uint id) { try { _l?.RemoveNotification(id); } catch { } }
        public void ClearAll() { try { _l?.ClearNotifications(); } catch { } }
    }
}
