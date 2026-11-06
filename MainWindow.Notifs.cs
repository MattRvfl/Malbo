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
            _notifs.CollectionChanged += (s, e) => NotifEmpty.Visibility = _notifs.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
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

            _notifs.Insert(0, new NotifItem { Id = d.Id, App = d.App, Aumid = d.Aumid, Title = d.Title, Body = d.Body, Time = d.Time, Icon = d.Icon });
            while (_notifs.Count > 60) _notifs.RemoveAt(_notifs.Count - 1);
            TabNotif.Tag = $"Notifs ({_notifs.Count})";

            if (Quiet) return;
            string aumid = d.Aumid, app = d.App;
            ShowPeek(new PeekData
            {
                Key = "notif", Image = d.Icon, Glyph = "\uEA8F",
                Title = string.IsNullOrEmpty(d.Title) ? d.App : $"{d.Title}",
                Body = string.IsNullOrEmpty(d.Body) ? d.App : $"{d.App} · {d.Body.Replace("\n", " ")}",
                Duration = TimeSpan.FromSeconds(5), Width = 440, Height = 76,
                OnClick = () =>
                {
                    try { AppLauncher.OpenApp(aumid, app); } catch (Exception ex) { App.Log(ex); }
                    if (_mode == Mode.Peek) EndPeek();
                }
            });
        }

        /// <summary>Ne pas déranger, ou Pomodoro en phase de travail avec le mode Focus.</summary>
        bool Quiet => S.DoNotDisturb || (S.FocusDnd && _tm == TMode.Pomo && _tRun && _pomoWork);

        void NotifDismiss_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.Tag is NotifItem n) RemoveNotif(n);
        }

        void RemoveNotif(NotifItem n)
        {
            _notifs.Remove(n);
            _notif.Remove(n.Id);
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
                VoiceMuteBtn.Visibility = inCh ? Visibility.Visible : Visibility.Collapsed;
                if (!inCh && DiscordCallLocal)
                {
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
                    VoiceMuteBtn.Content = _discord.SelfMuted ? "\uEC54" : "\uE720";
                    VoiceMuteBtn.ToolTip = _discord.SelfMuted ? "Réactiver le micro" : "Couper le micro";
                    if (!_wasInChannel)
                        ShowPeek(new PeekData { Key = "alert", Glyph = "\uE7F6", Title = $"Connecté au salon {name}", Body = "Discord", Duration = TimeSpan.FromSeconds(3), Width = 360, Height = 68 });
                }
                else { _voiceSig = null; VoiceList.ItemsSource = null; }
                _wasInChannel = inCh;
                UpdateCompact();
            }
            catch (Exception ex) { App.Log(ex); }
        }

        async void VoiceMute_Click(object sender, RoutedEventArgs e)
        {
            try { await _discord.ToggleMuteAsync(); } catch (Exception ex) { App.Log(ex); }
            UpdateVoiceUI();
        }

        // ---------- Détection locale d'un appel Discord (sans RPC) ----------
        // Discord réserve la lecture du vocal (rpc.voice.read) aux applis approuvées : on déduit l'appel
        // du micro utilisé par Discord, et la parole des niveaux audio (sortie Discord = les autres, micro = toi).
        bool _dcCall;
        long _dcOutT, _dcMicT;
        bool _dcLastOthers, _dcLastMe;

        bool DiscordCallLocal => _dcCall && !_discord.InChannel;
        bool OthersSpeaking => Environment.TickCount64 - _dcOutT < 600;
        bool MeSpeaking => Environment.TickCount64 - _dcMicT < 600;

        void LocalVoiceTick()
        {
            var s = _sys.Latest;
            bool call = s.Mic && (s.MicApp ?? "").IndexOf("discord", StringComparison.OrdinalIgnoreCase) >= 0;
            if (call != _dcCall)
            {
                _dcCall = call;
                if (call && !_discord.InChannel)
                    ShowPeek(new PeekData { Key = "alert", Glyph = "", Title = "Appel Discord", Body = "En cours", Duration = TimeSpan.FromSeconds(2.5), Width = 320, Height = 68 });
                UpdateVoiceUI();
            }
            if (!DiscordCallLocal) return;
            var now = Environment.TickCount64;
            if (_vol.AppPeak("discord") > 0.015f) _dcOutT = now;
            if (_vol.MicPeak > 0.04f) _dcMicT = now;
            if (OthersSpeaking != _dcLastOthers || MeSpeaking != _dcLastMe)
            {
                _dcLastOthers = OthersSpeaking; _dcLastMe = MeSpeaking;
                UpdateCompact();
                if (VoiceCard.Visibility == Visibility.Visible) VoiceChannel.Text = LocalVoiceLabel();
            }
        }

        string LocalVoiceLabel() =>
            OthersSpeaking && MeSpeaking ? "\U0001F399 Toi + quelqu'un"
            : OthersSpeaking ? "\U0001F399 Quelqu'un parle"
            : MeSpeaking ? "\U0001F399 Tu parles"
            : "Appel Discord en cours";

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
