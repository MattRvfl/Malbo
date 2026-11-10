using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace DynamicIsland
{
    public partial class MainWindow
    {
        readonly ObservableCollection<NotifItem> _notifs = new();

        void InitNotifs()
        {
            NotifList.ItemsSource = _notifs;
            _notifs.CollectionChanged += (s, e) =>
            {
                NotifEmpty.Visibility = _notifs.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
                if (PanelNotif.Visibility == Visibility.Visible) ApplyTabHeight(); // l'île suit le nombre de notifications
            };
            _notif.Received += d => Dispatcher.InvokeAsync(() => OnNotif(d));
            _ = InitNotifAsync();
        }

        async Task InitNotifAsync()
        {
            await _notif.InitAsync();
            NotifStatus.Text = _notif.Status;
            NotifStatus.Visibility = string.IsNullOrEmpty(_notif.Status) ? Visibility.Collapsed : Visibility.Visible;
            try { System.IO.File.WriteAllText(System.IO.Path.Combine(AppSettings.Dir, "status.txt"), $"[{DateTime.Now}] notifications: {(_notif.Ok ? "OK" : _notif.Status)}"); } catch { }
            Application.Current.Exit += (s, e) => BannerService.Restore(S);
            UpdateBanners();
        }

        /// <summary>Coupe les bannières Windows seulement si Malbo reçoit bien les notifications.</summary>
        void UpdateBanners()
        {
            if (S.HideWindowsBanners && _notif.Ok) BannerService.Apply(S);
            else BannerService.Restore(S);
        }

        void OnNotif(NotifData d)
        {
            var blocked = (S.BlockedApps ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (blocked.Any(b => d.App.Contains(b, StringComparison.OrdinalIgnoreCase))) return;
            if (d.App.Contains("Malbo", StringComparison.OrdinalIgnoreCase)) return;

            var item = new NotifItem { Id = d.Id, App = d.App, Aumid = d.Aumid, Title = d.Title, Body = d.Body, Time = d.Time, Icon = d.Icon };
            item.Ids.Add(d.Id);
            // Regroupement : même appli + même titre (= même conversation) → une seule carte avec un compteur.
            if (S.GroupNotifs)
            {
                var same = _notifs.FirstOrDefault(n => n.App == d.App && n.Title == d.Title && !string.IsNullOrEmpty(d.Title));
                if (same != null)
                {
                    item.Count = same.Count + 1;
                    item.Ids.AddRange(same.Ids);
                    var prev = (same.Body ?? "").Split('\n').Take(3);
                    item.Body = string.Join("\n", new[] { d.Body }.Concat(prev).Where(x => !string.IsNullOrWhiteSpace(x)));
                    _notifs.Remove(same);
                }
            }
            _notifs.Insert(0, item);
            while (_notifs.Count > 60) _notifs.RemoveAt(_notifs.Count - 1);
            TabNotif.Tag = $"Notifs ({_notifs.Count})";

            if (Quiet && !IsPriority(d)) return;
            string aumid = d.Aumid, app = d.App;
            ShowPeek(new PeekData
            {
                Key = "notif", Group = d.App ?? "", Image = d.Icon, Glyph = "\uE0CE",
                Title = (string.IsNullOrEmpty(d.Title) ? d.App : d.Title) + (item.Count > 1 ? $"  ({item.Count})" : ""),
                Body = string.IsNullOrEmpty(d.Body) ? d.App : $"{d.App} · {d.Body.Replace("\n", " ")}",
                Duration = TimeSpan.FromSeconds(5), Width = 440, Height = 76,
                OnClick = () =>
                {
                    try { AppLauncher.OpenApp(aumid, app); } catch (Exception ex) { App.Log(ex); }
                    if (_mode == Mode.Peek) EndPeek();
                }
            });
        }

        /// <summary>Ne pas déranger, Pomodoro en phase de travail (Focus), appel en cours, ou heures calmes.</summary>
        bool Quiet => S.DoNotDisturb || (S.FocusDnd && _tm == TMode.Pomo && _tRun && _pomoWork)
                      || (S.MeetingDnd && (_dcCall || _discord.InChannel)) || InQuietHours();

        /// <summary>Heures calmes « 23:00-07:00 » (la plage peut passer minuit).</summary>
        bool InQuietHours()
        {
            var q = (S.QuietHours ?? "").Trim();
            if (q.Length == 0) return false;
            var parts = q.Replace('h', ':').Split('-', StringSplitOptions.TrimEntries);
            if (parts.Length != 2 || !TimeSpan.TryParse(parts[0].TrimEnd(':'), out var a) || !TimeSpan.TryParse(parts[1].TrimEnd(':'), out var b)) return false;
            var now = DateTime.Now.TimeOfDay;
            return a <= b ? now >= a && now < b : now >= a || now < b;
        }

        /// <summary>Un mot-clé prioritaire (réglages) fait passer la notification malgré Ne pas déranger.</summary>
        bool IsPriority(NotifData d)
        {
            var keys = (S.PriorityKeywords ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (keys.Length == 0) return false;
            var text = $"{d.App} {d.Title} {d.Body}";
            return keys.Any(k => text.Contains(k, StringComparison.OrdinalIgnoreCase));
        }

        // ---------- Rappel plus tard ----------
        void NotifSnooze_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.Tag is not NotifItem n) return;
            e.Handled = true;
            _notifs.Remove(n);
            TabNotif.Tag = _notifs.Count > 0 ? $"Notifs ({_notifs.Count})" : "Notifs";
            int min = Math.Clamp(S.SnoozeMinutes, 1, 720);
            var t = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMinutes(min) };
            t.Tick += (s, a) =>
            {
                t.Stop();
                n.Time = DateTime.Now;
                _notifs.Insert(0, n);
                TabNotif.Tag = $"Notifs ({_notifs.Count})";
                ShowPeek(new PeekData
                {
                    Key = "notif", Image = n.Icon as System.Windows.Media.Imaging.BitmapSource, Glyph = "\uE19A",
                    Title = "Rappel · " + (string.IsNullOrEmpty(n.Title) ? n.App : n.Title),
                    Body = string.IsNullOrEmpty(n.Body) ? n.App : $"{n.App} · {n.Body.Replace("\n", " ")}",
                    Duration = TimeSpan.FromSeconds(6), Width = 440, Height = 76,
                    OnClick = () => { try { AppLauncher.OpenApp(n.Aumid, n.App); } catch (Exception ex) { App.Log(ex); } if (_mode == Mode.Peek) EndPeek(); }
                });
            };
            t.Start();
        }

        void NotifDismiss_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.Tag is NotifItem n) RemoveNotif(n);
        }

        void RemoveNotif(NotifItem n)
        {
            _notifs.Remove(n);
            if (n.Ids != null && n.Ids.Count > 0) foreach (var id in n.Ids) _notif.Remove(id);
            else _notif.Remove(n.Id);
            TabNotif.Tag = _notifs.Count > 0 ? $"Notifs ({_notifs.Count})" : "Notifs";
        }

        // ---------- Glisser une carte pour la supprimer ----------
        FrameworkElement _swipeEl;
        Point _swipeStart;
        bool _swipeMoved;

        static TranslateTransform SwipeT(FrameworkElement el)
        {
            if (el.RenderTransform is TranslateTransform t && !t.IsFrozen) return t;
            var nt = new TranslateTransform();
            el.RenderTransform = nt;
            return nt;
        }

        void NotifCard_Down(object sender, MouseButtonEventArgs e)
        {
            if (sender is not FrameworkElement el || el.Tag is not NotifItem) return;
            var t = SwipeT(el);
            t.BeginAnimation(TranslateTransform.XProperty, null);
            el.BeginAnimation(OpacityProperty, null);
            t.X = 0; el.Opacity = 1;
            _swipeEl = el;
            _swipeStart = e.GetPosition(this);
            _swipeMoved = false;
            el.CaptureMouse();
            e.Handled = true;
        }

        void NotifCard_Move(object sender, MouseEventArgs e)
        {
            if (_swipeEl == null || sender != _swipeEl) return;
            if (e.LeftButton != MouseButtonState.Pressed) { EndSwipe(false); return; }
            var dx = e.GetPosition(this).X - _swipeStart.X;
            if (Math.Abs(dx) > 5) _swipeMoved = true;
            if (!_swipeMoved) return;
            SwipeT(_swipeEl).X = dx;
            _swipeEl.Opacity = Math.Max(0.25, 1 - Math.Abs(dx) / 220);
        }

        void NotifCard_Up(object sender, MouseButtonEventArgs e)
        {
            if (_swipeEl == null || sender != _swipeEl) return;
            e.Handled = true;
            EndSwipe(true);
        }

        void EndSwipe(bool released)
        {
            var el = _swipeEl;
            _swipeEl = null;
            if (el == null) return;
            if (el.IsMouseCaptured) el.ReleaseMouseCapture();
            var n = el.Tag as NotifItem;
            var t = SwipeT(el);
            double dx = t.X;

            if (released && !_swipeMoved)
            {
                if (n != null) { try { AppLauncher.OpenApp(n.Aumid, n.App); } catch (Exception ex) { App.Log(ex); } }
                return;
            }

            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            if (released && Math.Abs(dx) > 90 && n != null)
            {
                double to = Math.Sign(dx) * (Math.Max(el.ActualWidth, 300) + 40);
                var a = new DoubleAnimation(dx, to, TimeSpan.FromMilliseconds(200)) { EasingFunction = ease };
                a.Completed += (s, e) => RemoveNotif(n);
                el.BeginAnimation(OpacityProperty, new DoubleAnimation(el.Opacity, 0, TimeSpan.FromMilliseconds(200)));
                t.BeginAnimation(TranslateTransform.XProperty, a);
            }
            else
            {
                t.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(dx, 0, TimeSpan.FromMilliseconds(260)) { EasingFunction = new BackEase { Amplitude = 0.3, EasingMode = EasingMode.EaseOut } });
                el.BeginAnimation(OpacityProperty, new DoubleAnimation(el.Opacity, 1, TimeSpan.FromMilliseconds(200)));
            }
        }

        // ---------- Vocal Discord ----------
        bool _wasInChannel;
        string _voiceSig;

        void InitVoice()
        {
            _discord.VoiceChanged += () => Dispatcher.InvokeAsync(UpdateVoiceUI);
            _discord.SpeakingChanged += (id, speaking) => Dispatcher.InvokeAsync(UpdateVoiceUI);
            if (!string.IsNullOrEmpty(S.DiscordRefresh) && !string.IsNullOrEmpty(S.DiscordClientId)) _ = VoiceConnectSilent();
        }

        async Task VoiceConnectSilent()
        {
            try { await _discord.ConnectAsync(false); } catch (Exception ex) { App.Log(ex); }
        }

        void VoiceReconnectTick()
        {
            if (_discord.Connected || _discord.Busy) return;
            if (string.IsNullOrEmpty(S.DiscordRefresh) || string.IsNullOrEmpty(S.DiscordClientId)) return;
            _ = VoiceConnectSilent();
        }

        void UpdateVoiceUI()
        {
            try
            {
                bool inCh = _discord.InChannel;
                VoiceCard.Visibility = inCh || DiscordCallLocal ? Visibility.Visible : Visibility.Collapsed;
                VoiceMuteBtn.Visibility = inCh || DiscordCallLocal ? Visibility.Visible : Visibility.Collapsed;
                if (!inCh && DiscordCallLocal)
                {
                    // appel détecté en local : le bouton coupe le micro pour toutes les applis
                    bool mm = _vol.MicMuted;
                    VoiceMuteBtn.FontFamily = (FontFamily)FindResource("Icons");
                    VoiceMuteBtn.Content = mm ? "\uE328" : "\uE326";
                    VoiceMuteBtn.ToolTip = mm ? "Réactiver le micro (Ctrl+Alt+M)" : "Couper le micro partout (Ctrl+Alt+M)";
                    VoiceChannel.Text = LocalVoiceLabel();
                    VoiceList.ItemsSource = null;
                    _voiceSig = null;
                }
                else if (inCh)
                {
                    var name = string.IsNullOrEmpty(_discord.ChannelName) ? "Salon vocal" : _discord.ChannelName;
                    VoiceChannel.Text = name;
                    var users = _discord.Users ?? new List<VoiceUser>();
                    var sig = string.Join("|", users.Select(u => $"{u.Id}:{u.Name}:{u.Speaking}:{u.Muted}"));
                    if (sig != _voiceSig) { _voiceSig = sig; VoiceList.ItemsSource = users; }
                    VoiceMuteBtn.FontFamily = (FontFamily)FindResource("Icons");
                    VoiceMuteBtn.Content = _discord.SelfMuted ? "\uE328" : "\uE326";
                    VoiceMuteBtn.ToolTip = _discord.SelfMuted ? "Réactiver le micro" : "Couper le micro";
                    if (!_wasInChannel)
                        ShowPeek(new PeekData { Key = "alert", Glyph = "\uE2A6", Title = $"Connecté au salon {name}", Body = "Discord", Duration = TimeSpan.FromSeconds(3), Width = 360, Height = 68 });
                }
                else { _voiceSig = null; VoiceList.ItemsSource = null; }
                _wasInChannel = inCh;
                UpdateCompact();
            }
            catch (Exception ex) { App.Log(ex); }
        }

        async void VoiceMute_Click(object sender, RoutedEventArgs e)
        {
            if (!_discord.InChannel) { ToggleMicGlobal(); return; }
            try { await _discord.ToggleMuteAsync(); } catch (Exception ex) { App.Log(ex); }
            UpdateVoiceUI();
        }

        // ---------- Détection locale d'un appel (Discord, Teams, Zoom, Meet…), sans connexion ----------
        // On déduit l'appel de l'appli qui tient le micro, et la parole des niveaux audio
        // (son sortant de l'appli = les autres, micro = toi). Discord garde en plus son lecteur vocal RPC s'il est connecté.
        bool _dcCall;
        long _dcOutT, _dcMicT;
        bool _dcLastOthers, _dcLastMe;
        string _callApp = "", _callProc = "";
        DateTime _callStart;

        // nom du processus qui tient le micro → nom affiché
        static readonly (string Key, string Name)[] CallApps =
        {
            ("discord", "Discord"), ("teams", "Teams"), ("zoom", "Zoom"), ("skype", "Skype"), ("slack", "Slack"), ("webex", "Webex"),
            ("whatsapp", "WhatsApp"), ("telegram", "Telegram"), ("signal", "Signal"), ("facetime", "FaceTime"),
            ("chrome", "Appel (Chrome)"), ("msedge", "Appel (Edge)"), ("firefox", "Appel (Firefox)"), ("brave", "Appel (Brave)"), ("opera", "Appel (Opera)")
        };

        bool DiscordCallLocal => _dcCall && !_discord.InChannel;
        bool OthersSpeaking => Environment.TickCount64 - _dcOutT < 600;
        bool MeSpeaking => Environment.TickCount64 - _dcMicT < 600;

        void LocalVoiceTick()
        {
            var s = _sys.Latest;
            string mic = s.Mic ? (s.MicApp ?? "") : "";
            var hit = mic.Length == 0 ? default : CallApps.FirstOrDefault(a => mic.Contains(a.Key, StringComparison.OrdinalIgnoreCase));
            bool call = hit.Key != null;
            if (call != _dcCall || (call && hit.Name != _callApp))
            {
                _dcCall = call;
                if (call)
                {
                    _callApp = hit.Name; _callProc = hit.Key; _callStart = DateTime.Now;
                    if (!_discord.InChannel)
                        ShowPeek(new PeekData { Key = "alert", Glyph = "\uE3BA", Title = _callApp, Body = "Appel en cours" + (S.MeetingDnd ? " · notifications en pause" : ""), Duration = TimeSpan.FromSeconds(2.5), Width = 340, Height = 68 });
                }
                UpdateVoiceUI();
            }
            if (!DiscordCallLocal) return;
            var now = Environment.TickCount64;
            if (_vol.AppPeak(_callProc) > 0.015f) _dcOutT = now;
            if (_vol.MicPeak > 0.04f && !_vol.MicMuted) _dcMicT = now;
            if (OthersSpeaking != _dcLastOthers || MeSpeaking != _dcLastMe)
            {
                _dcLastOthers = OthersSpeaking; _dcLastMe = MeSpeaking;
                UpdateCompact();
                if (VoiceCard.Visibility == Visibility.Visible) VoiceChannel.Text = LocalVoiceLabel();
            }
        }

        string CallClock()
        {
            var d = DateTime.Now - _callStart;
            return d.TotalHours >= 1 ? d.ToString(@"h\:mm\:ss") : d.ToString(@"m\:ss");
        }

        string LocalVoiceLabel()
        {
            string who = _vol.MicMuted ? "Micro coupé"
                : OthersSpeaking && MeSpeaking ? "Toi + quelqu'un"
                : OthersSpeaking ? "Quelqu'un parle"
                : MeSpeaking ? "Tu parles" : null;
            return who == null ? $"{_callApp} · {CallClock()}" : $"{_callApp} · {CallClock()} · {who}";
        }

        bool AnySpeaking
        {
            get
            {
                try
                {
                    if (DiscordCallLocal) return OthersSpeaking || MeSpeaking;
                    return _discord.InChannel && (_discord.Users?.Any(u => u.Speaking) ?? false);
                }
                catch { return false; }
            }
        }

        string VoiceCompactText()
        {
            try
            {
                if (DiscordCallLocal) return LocalVoiceLabel();
                var users = _discord.Users ?? new List<VoiceUser>();
                var speaking = users.Where(u => u.Speaking).Select(u => u.Name).ToList();
                if (speaking.Count > 0) return "\U0001F399 " + string.Join(", ", speaking);
                return $"{_discord.ChannelName} · {users.Count}";
            }
            catch { return ""; }
        }

        void NotifClear_Click(object sender, RoutedEventArgs e)
        {
            _notifs.Clear();
            _notif.ClearAll();
            TabNotif.Tag = "Notifs";
        }

        void Dnd_Click(object sender, RoutedEventArgs e)
        {
            S.DoNotDisturb = DndCheck.IsChecked == true;
            S.Save();
        }
    }
}
