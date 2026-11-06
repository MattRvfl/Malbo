using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using Windows.Media;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace DynamicIsland
{
    public class MediaState
    {
        public string Title = "", Artist = "", Album = "", AppId = "";
        public BitmapSource Art;
        public bool IsPlaying, HasMedia;
        public bool? Shuffle;
        public int? Repeat;   // 0 aucun, 1 morceau, 2 liste
        public TimeSpan Position, Duration;
        public DateTime PositionStamp = DateTime.Now;
        public string Key => Title + "|" + Artist;

        public TimeSpan CurrentPosition
        {
            get
            {
                var p = IsPlaying ? Position + (DateTime.Now - PositionStamp) : Position;
                if (Duration > TimeSpan.Zero && p > Duration) p = Duration;
                return p < TimeSpan.Zero ? TimeSpan.Zero : p;
            }
        }
    }

    /// <summary>Lit la session média Windows (SMTC) : Spotify, navigateurs, Deezer, VLC…</summary>
    public class MediaService
    {
        GlobalSystemMediaTransportControlsSessionManager _mgr;
        GlobalSystemMediaTransportControlsSession _s;
        string _artKey;
        public MediaState State { get; private set; } = new MediaState();
        public event Action StateChanged;

        public async Task InitAsync()
        {
            try
            {
                _mgr = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
                _mgr.SessionsChanged += (a, b) => Pick();
                _mgr.CurrentSessionChanged += (a, b) => Pick();
                Pick();
            }
            catch (Exception ex) { App.Log(ex); }
        }

        static bool Playing(GlobalSystemMediaTransportControlsSession x)
        {
            try { return x.GetPlaybackInfo().PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing; }
            catch { return false; }
        }

        /// <summary>Choisit la meilleure session : Spotify en lecture > autre en lecture > session courante.</summary>
        public void Pick()
        {
            if (_mgr == null) return;
            try
            {
                var sessions = _mgr.GetSessions().ToList();
                var best = sessions.FirstOrDefault(x => Playing(x) && x.SourceAppUserModelId.IndexOf("spotify", StringComparison.OrdinalIgnoreCase) >= 0)
                           ?? sessions.FirstOrDefault(Playing)
                           ?? (_s != null && sessions.Contains(_s) ? _s : null)
                           ?? _mgr.GetCurrentSession()
                           ?? sessions.FirstOrDefault();
                if (best != _s)
                {
                    Unhook();
                    _s = best;
                    Hook();
                }
                _ = RefreshAsync();
            }
            catch (Exception ex) { App.Log(ex); }
        }

        void Hook()
        {
            if (_s == null) return;
            _s.MediaPropertiesChanged += OnProps;
            _s.PlaybackInfoChanged += OnPlay;
            _s.TimelinePropertiesChanged += OnTime;
        }

        void Unhook()
        {
            if (_s == null) return;
            try
            {
                _s.MediaPropertiesChanged -= OnProps;
                _s.PlaybackInfoChanged -= OnPlay;
                _s.TimelinePropertiesChanged -= OnTime;
            }
            catch { }
        }

        void OnProps(GlobalSystemMediaTransportControlsSession s, MediaPropertiesChangedEventArgs e) => _ = RefreshAsync();
        void OnPlay(GlobalSystemMediaTransportControlsSession s, PlaybackInfoChangedEventArgs e) => _ = RefreshAsync();
        void OnTime(GlobalSystemMediaTransportControlsSession s, TimelinePropertiesChangedEventArgs e) => _ = RefreshAsync();

        public async Task RefreshAsync()
        {
            var s = _s;
            var st = new MediaState();
            if (s != null)
            {
                try
                {
                    var p = await s.TryGetMediaPropertiesAsync();
                    st.Title = p?.Title ?? "";
                    st.Artist = p?.Artist ?? "";
                    st.Album = p?.AlbumTitle ?? "";
                    st.AppId = s.SourceAppUserModelId ?? "";
                    if (st.Key == _artKey && State.Art != null) st.Art = State.Art;
                    else { st.Art = await LoadArt(p?.Thumbnail); _artKey = st.Key; }

                    st.IsPlaying = Playing(s);
                    try
                    {
                        var pi = s.GetPlaybackInfo();
                        st.Shuffle = pi.IsShuffleActive;
                        st.Repeat = pi.AutoRepeatMode switch
                        {
                            MediaPlaybackAutoRepeatMode.None => 0,
                            MediaPlaybackAutoRepeatMode.Track => 1,
                            MediaPlaybackAutoRepeatMode.List => 2,
                            _ => (int?)null
                        };
                    }
                    catch { }
                    var tl = s.GetTimelineProperties();
                    st.Duration = tl.EndTime - tl.StartTime;
                    st.Position = tl.Position;
                    var stamp = tl.LastUpdatedTime.LocalDateTime;
                    st.PositionStamp = stamp.Year < 2000 || stamp > DateTime.Now ? DateTime.Now : stamp;
                    st.HasMedia = !string.IsNullOrWhiteSpace(st.Title);
                }
                catch { }
            }
            if (s != _s) return;
            State = st;
            StateChanged?.Invoke();
        }

        static async Task<BitmapSource> LoadArt(IRandomAccessStreamReference r)
        {
            if (r == null) return null;
            try
            {
                using var ras = await r.OpenReadAsync();
                using var st = ras.AsStreamForRead();
                var ms = new MemoryStream();
                await st.CopyToAsync(ms);
                ms.Position = 0;
                var bi = new BitmapImage();
                bi.BeginInit();
                bi.CacheOption = BitmapCacheOption.OnLoad;
                bi.StreamSource = ms;
                bi.EndInit();
                bi.Freeze();
                return bi;
            }
            catch { return null; }
        }

        public async void TogglePlay() { try { if (_s != null) await _s.TryTogglePlayPauseAsync(); } catch { } }
        public async void Next() { try { if (_s != null) await _s.TrySkipNextAsync(); } catch { } }
        public async void Prev() { try { if (_s != null) await _s.TrySkipPreviousAsync(); } catch { } }
        public async void Seek(TimeSpan t) { try { if (_s != null) await _s.TryChangePlaybackPositionAsync(t.Ticks); } catch { } }

        public async Task<bool> SetShuffle(bool on)
        {
            try { return _s != null && await _s.TryChangeShuffleActiveAsync(on); }
            catch { return false; }
        }

        /// <summary>0 aucun, 1 morceau, 2 liste.</summary>
        public async Task<bool> SetRepeat(int mode)
        {
            try
            {
                if (_s == null) return false;
                var m = mode == 1 ? MediaPlaybackAutoRepeatMode.Track : mode == 2 ? MediaPlaybackAutoRepeatMode.List : MediaPlaybackAutoRepeatMode.None;
                return await _s.TryChangeAutoRepeatModeAsync(m);
            }
            catch { return false; }
        }
    }
}
