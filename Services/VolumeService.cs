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
            // Appelé toutes les 5 s : sans ce Dispose, chaque appel laissait fuiter un objet COM MMDevice.
            if (d.ID == _devId) { d.Dispose(); return; }
            if (_dev != null)
            {
                try { _dev.AudioEndpointVolume.OnVolumeNotification -= OnVol; } catch { }
                ClearAppSessions();
                try { _dev.Dispose(); } catch { }
            }
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
                    ClearAppSessions();
                    var mgr = _dev.AudioSessionManager;
                    mgr.RefreshSessions();
                    var ss = mgr.Sessions;
                    for (int i = 0; i < ss.Count; i++)
                    {
                        AudioSessionControl s = null;
                        try
                        {
                            s = ss[i];
                            if (!s.IsSystemSoundsSession && ProcessName(s.GetProcessID).IndexOf(processName, StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                _appSessions.Add(s);
                                s = null;
                            }
                        }
                        catch { }
                        finally { s?.Dispose(); } // sessions non retenues : on libère l'objet COM tout de suite
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

        void ClearAppSessions()
        {
            foreach (var s in _appSessions) { try { s.Dispose(); } catch { } }
            _appSessions.Clear();
        }

        // Nom de processus mis en cache : Process.GetProcessById coûte cher et chaque objet Process doit être libéré.
        readonly Dictionary<uint, string> _pnames = new();

        string ProcessName(uint pid)
        {
            if (_pnames.TryGetValue(pid, out var n)) return n;
            try { using var p = Process.GetProcessById((int)pid); n = p.ProcessName; } catch { n = ""; }
            if (_pnames.Count > 256) _pnames.Clear();
            _pnames[pid] = n;
            return n;
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
                        _micCheck = DateTime.Now;
                        var m = _en.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);
                        if (_mic != null && _mic.ID == m.ID) m.Dispose();
                        else { var old = _mic; _mic = m; try { old?.Dispose(); } catch { } }
                    }
                    return _mic.AudioMeterInformation.MasterPeakValue;
                }
                catch { return 0; }
            }
        }

        string _sessionSig;

        /// <summary>
        /// Volume par appli. Avec <paramref name="onlyIfChanged"/>, renvoie null si les sessions audio n'ont pas changé
        /// depuis le dernier appel (évite de recréer icônes et objets COM toutes les 3 s).
        /// </summary>
        public List<AppVolumeItem> GetSessions(bool onlyIfChanged = false)
        {
            var list = new List<AppVolumeItem>();
            var all = new List<AudioSessionControl>();
            try
            {
                if (_dev == null) return list;
                var mgr = _dev.AudioSessionManager;
                mgr.RefreshSessions();
                var ss = mgr.Sessions;
                var sig = new System.Text.StringBuilder();
                for (int i = 0; i < ss.Count; i++)
                {
                    try
                    {
                        var s = ss[i];
                        all.Add(s);
                        if (s.State != NAudio.CoreAudioApi.Interfaces.AudioSessionState.AudioSessionStateExpired)
                            sig.Append(s.GetProcessID).Append(',');
                    }
                    catch { }
                }
                var sigStr = sig.ToString();
                if (onlyIfChanged && sigStr == _sessionSig) return null;
                _sessionSig = sigStr;

                foreach (var s in all)
                {
                    try
                    {
                        if (s.State == NAudio.CoreAudioApi.Interfaces.AudioSessionState.AudioSessionStateExpired) continue;
                        uint pid = s.GetProcessID;
                        string name = s.DisplayName;
                        System.Windows.Media.ImageSource icon = null;
                        if (s.IsSystemSoundsSession) name = "Sons système";
                        else
                        {
                            try
                            {
                                using var p = Process.GetProcessById((int)pid);
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
                    catch { }
                }
            }
            catch { }
            finally
            {
                // libère les sessions qui ne sont pas gardées par la liste
                foreach (var s in all)
                    if (!list.Any(x => x.Owns(s))) { try { s.Dispose(); } catch { } }
            }
            return list;
        }

        // ---------- Volume général, micro global, choix de la sortie ----------
        /// <summary>Change le volume général de <paramref name="delta"/> (ex. ±0.02 par cran de molette).</summary>
        public void Nudge(float delta)
        {
            try
            {
                var v = _dev?.AudioEndpointVolume;
                if (v == null) return;
                v.MasterVolumeLevelScalar = Math.Clamp(v.MasterVolumeLevelScalar + delta, 0f, 1f);
            }
            catch { }
        }

        MMDevice CaptureDevice()
        {
            _ = MicPeak; // (re)charge le micro par défaut
            return _mic;
        }

        /// <summary>Micro par défaut coupé pour toutes les applis (réunions Teams, Zoom, Meet, Discord…).</summary>
        public bool MicMuted
        {
            get { try { return CaptureDevice()?.AudioEndpointVolume.Mute ?? false; } catch { return false; } }
        }

        /// <summary>Bascule le micro. Renvoie le nouvel état (true = coupé), ou null s'il n'y a pas de micro.</summary>
        public bool? ToggleMicMute()
        {
            try
            {
                var m = CaptureDevice();
                if (m == null) return null;
                bool next = !m.AudioEndpointVolume.Mute;
                m.AudioEndpointVolume.Mute = next;
                return next;
            }
            catch (Exception ex) { App.Log(ex); return null; }
        }

        public List<(string Id, string Name, bool IsDefault)> Outputs()
        {
            var list = new List<(string, string, bool)>();
            try
            {
                if (_en == null) return list;
                var col = _en.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
                foreach (var d in col)
                {
                    try { list.Add((d.ID, d.FriendlyName, d.ID == _devId)); } catch { }
                    finally { d.Dispose(); }
                }
            }
            catch (Exception ex) { App.Log(ex); }
            return list;
        }

        /// <summary>Change la sortie audio par défaut de Windows (même effet que le menu son de la barre des tâches).</summary>
        public bool SetDefaultOutput(string id)
        {
            object o = null;
            try
            {
                o = new PolicyConfigClient();
                var pc = (IPolicyConfig)o;
                for (int role = 0; role < 3; role++) // console, multimédia, communications
                {
                    int hr = pc.SetDefaultEndpoint(id, role);
                    if (hr != 0) return false;
                }
                Attach();
                return true;
            }
            catch (Exception ex) { App.Log(ex); return false; }
            finally { if (o != null) System.Runtime.InteropServices.Marshal.FinalReleaseComObject(o); }
        }

        // Interface système non documentée mais stable depuis Windows 7 (utilisée par tous les sélecteurs de sortie audio).
        [System.Runtime.InteropServices.ComImport, System.Runtime.InteropServices.Guid("870af99c-171d-4f9e-af0d-e63df40c2bc9")]
        class PolicyConfigClient { }

        [System.Runtime.InteropServices.ComImport, System.Runtime.InteropServices.Guid("f8679f50-850a-41cf-9c72-430f290290c8"),
         System.Runtime.InteropServices.InterfaceType(System.Runtime.InteropServices.ComInterfaceType.InterfaceIsIUnknown)]
        interface IPolicyConfig
        {
            [System.Runtime.InteropServices.PreserveSig] int Unused1();
            [System.Runtime.InteropServices.PreserveSig] int Unused2();
            [System.Runtime.InteropServices.PreserveSig] int Unused3();
            [System.Runtime.InteropServices.PreserveSig] int Unused4();
            [System.Runtime.InteropServices.PreserveSig] int Unused5();
            [System.Runtime.InteropServices.PreserveSig] int Unused6();
            [System.Runtime.InteropServices.PreserveSig] int Unused7();
            [System.Runtime.InteropServices.PreserveSig] int Unused8();
            [System.Runtime.InteropServices.PreserveSig] int Unused9();
            [System.Runtime.InteropServices.PreserveSig] int Unused10();
            [System.Runtime.InteropServices.PreserveSig]
            int SetDefaultEndpoint([System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPWStr)] string deviceId, int role);
        }
    }
}
