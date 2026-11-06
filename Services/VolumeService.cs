using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using NAudio.CoreAudioApi;

namespace DynamicIsland
{
    /// <summary>Volume général (OSD), niveau audio pour le visualiseur, volume par application.</summary>
    public class VolumeService
    {
        MMDeviceEnumerator _en;
        MMDevice _dev;
        string _devId;
        DateTime _ready;
        public event Action<float, bool> VolumeChanged;

        public void Init()
        {
            try { _en = new MMDeviceEnumerator(); Attach(); }
            catch (Exception ex) { App.Log(ex); }
        }

        void Attach()
        {
            var d = _en.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            if (d.ID == _devId) return;
            if (_dev != null) { try { _dev.AudioEndpointVolume.OnVolumeNotification -= OnVol; } catch { } }
            _dev = d;
            _devId = d.ID;
            _dev.AudioEndpointVolume.OnVolumeNotification += OnVol;
            _ready = DateTime.Now.AddSeconds(1.5);
        }

        void OnVol(AudioVolumeNotificationData d)
        {
            if (DateTime.Now < _ready) return;
            VolumeChanged?.Invoke(d.MasterVolume, d.Muted);
        }

        public void CheckDevice() { try { if (_en != null) Attach(); } catch { } }

        public float Peak
        {
            get { try { return _dev?.AudioMeterInformation.MasterPeakValue ?? 0; } catch { return 0; } }
        }

        // ---------- Niveaux par appli et micro (détection locale du vocal Discord) ----------
        MMDevice _mic;
        DateTime _micCheck, _appCheck;
        string _appName;
        readonly List<AudioSessionControl> _appSessions = new();

        /// <summary>Niveau sonore (0..1) sortant des sessions audio d'une appli (ex. "discord").</summary>
        public float AppPeak(string processName)
        {
            try
            {
                if (_dev == null) return 0;
                if (_appName != processName || (DateTime.Now - _appCheck).TotalSeconds > 3)
                {
                    _appName = processName;
                    _appCheck = DateTime.Now;
                    _appSessions.Clear();
                    var mgr = _dev.AudioSessionManager;
                    mgr.RefreshSessions();
                    var ss = mgr.Sessions;
                    for (int i = 0; i < ss.Count; i++)
                    {
                        try
                        {
                            var s = ss[i];
                            if (s.IsSystemSoundsSession) continue;
                            var pn = Process.GetProcessById((int)s.GetProcessID).ProcessName;
                            if (pn.IndexOf(processName, StringComparison.OrdinalIgnoreCase) >= 0) _appSessions.Add(s);
                        }
                        catch { }
                    }
                }
                float max = 0;
                foreach (var s in _appSessions)
                {
                    try { max = Math.Max(max, s.AudioMeterInformation.MasterPeakValue); } catch { }
                }
                return max;
            }
            catch { return 0; }
        }

        /// <summary>Niveau du micro par défaut (communications). Non nul seulement si une appli capture le micro.</summary>
        public float MicPeak
        {
            get
            {
                try
                {
                    if (_en == null) return 0;
                    if (_mic == null || (DateTime.Now - _micCheck).TotalSeconds > 10)
                    {
                        _mic = _en.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);
                        _micCheck = DateTime.Now;
                    }
                    return _mic.AudioMeterInformation.MasterPeakValue;
                }
                catch { return 0; }
            }
        }

        public List<AppVolumeItem> GetSessions()
        {
            var list = new List<AppVolumeItem>();
            try
            {
                var mgr = _dev.AudioSessionManager;
                mgr.RefreshSessions();
                var ss = mgr.Sessions;
                for (int i = 0; i < ss.Count; i++)
                {
                    var s = ss[i];
                    if (s.State == NAudio.CoreAudioApi.Interfaces.AudioSessionState.AudioSessionStateExpired) continue;
                    uint pid = s.GetProcessID;
                    string name = s.DisplayName;
                    System.Windows.Media.ImageSource icon = null;
                    if (s.IsSystemSoundsSession) name = "Sons système";
                    else
                    {
                        try
                        {
                            var p = Process.GetProcessById((int)pid);
                            string file = null;
                            try { file = p.MainModule?.FileName; } catch { }
                            var desc = file != null ? FileVersionInfo.GetVersionInfo(file).FileDescription : null;
                            name = !string.IsNullOrWhiteSpace(desc) && desc.Length < 30 ? desc : CultureInfo.CurrentCulture.TextInfo.ToTitleCase(p.ProcessName);
                            icon = IconHelper.FromFile(file);
                        }
                        catch { }
                    }
                    if (string.IsNullOrWhiteSpace(name) || list.Any(x => x.Name == name)) continue;
                    list.Add(new AppVolumeItem(s, pid, name, icon));
                }
            }
            catch { }
            return list;
        }
    }
}
