using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Management;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Win32;
using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;
using Windows.Devices.Enumeration.Pnp;

namespace DynamicIsland
{
    public class SysStats
    {
        public double Cpu, Ram, Gpu = -1, Temp = -1, Down, Up;
        public ulong RamUsed, RamTotal;
        public bool HasBattery, Charging;
        public int Battery = -1, BatteryMinutes = -1;
        public string Wifi = "—";
        public int WifiSignal = -1;
        public TimeSpan Uptime;
        public bool Mic, Cam;
        public string MicApp = "", CamApp = "";
        public List<InfoRow> Disks = new();
        public List<InfoRow> Bluetooth = new();
    }

    /// <summary>CPU, RAM, GPU, réseau, batterie, Wi-Fi, disques, Bluetooth, micro/caméra, luminosité.</summary>
    public class SystemService
    {
        public SysStats Latest { get; private set; } = new SysStats();
        public event Action<int> BrightnessChanged;
        /// <summary>Vrai quand le panneau Système est affiché : GPU échantillonné, Wi-Fi/disques/température toutes les 10 s (sinon 60 s).</summary>
        public bool Detailed { get; set; }
        DateTime _slowRead = DateTime.MinValue;
        bool _wasDetailed;

        long _pi, _pk, _pu, _rx, _tx;
        DateTime _netT;
        List<PerformanceCounter> _gpu;
        DateTime _gpuRefresh;
        bool _tempOk = true, _brightOk = true, _gpuOk = true;
        int _lastBright = -1, _n;
        ManagementObjectSearcher _brightQ;
        List<InfoRow> _bt = new();
        const string BatteryKey = "{104EA319-6EE2-4701-BD47-8DDBF425BBE5} 2";

        public void Start() => Task.Run(Loop);

        async Task Loop()
        {
            while (true)
            {
                try { Sample(); } catch (Exception ex) { App.Log(ex); }
                await Task.Delay(1000);
            }
        }

        void Sample()
        {
            var prev = Latest;
            var s = new SysStats();

            if (Native.GetSystemTimes(out var i, out var k, out var u))
            {
                long di = i - _pi, tot = (k - _pk) + (u - _pu);
                if (_pk != 0 && tot > 0) s.Cpu = Math.Clamp((tot - di) * 100.0 / tot, 0, 100);
                _pi = i; _pk = k; _pu = u;
            }

            var m = new Native.MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<Native.MEMORYSTATUSEX>() };
            if (Native.GlobalMemoryStatusEx(ref m))
            {
                s.RamTotal = m.ullTotalPhys;
                s.RamUsed = m.ullTotalPhys - m.ullAvailPhys;
                s.Ram = m.dwMemoryLoad;
            }

            long rx = 0, tx = 0;
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up || ni.NetworkInterfaceType == NetworkInterfaceType.Loopback || ni.NetworkInterfaceType == NetworkInterfaceType.Tunnel) continue;
                try { var st = ni.GetIPStatistics(); rx += st.BytesReceived; tx += st.BytesSent; } catch { }
            }
            var now = DateTime.UtcNow;
            if (_rx > 0)
            {
                var dt = Math.Max(0.2, (now - _netT).TotalSeconds);
                s.Down = Math.Max(0, (rx - _rx) / 1024.0 / dt);
                s.Up = Math.Max(0, (tx - _tx) / 1024.0 / dt);
            }
            _rx = rx; _tx = tx; _netT = now;

            bool detailed = Detailed;
            s.Gpu = detailed ? SampleGpu(prev.Gpu) : -1;

            var ps = System.Windows.Forms.SystemInformation.PowerStatus;
            s.HasBattery = !ps.BatteryChargeStatus.HasFlag(System.Windows.Forms.BatteryChargeStatus.NoSystemBattery)
                           && ps.BatteryChargeStatus != System.Windows.Forms.BatteryChargeStatus.Unknown;
            s.Battery = (int)Math.Round(ps.BatteryLifePercent * 100);
            s.Charging = ps.PowerLineStatus == System.Windows.Forms.PowerLineStatus.Online;
            s.BatteryMinutes = ps.BatteryLifeRemaining > 0 ? ps.BatteryLifeRemaining / 60 : -1;

            s.Uptime = TimeSpan.FromMilliseconds(Environment.TickCount64);
            (s.Mic, s.MicApp) = Cap("microphone");
            (s.Cam, s.CamApp) = Cap("webcam");

            var slowNow = DateTime.UtcNow;
            bool slow = (slowNow - _slowRead).TotalSeconds >= (detailed ? 10 : 60) || (detailed && !_wasDetailed);
            _wasDetailed = detailed;
            if (slow)
            {
                _slowRead = slowNow;
                (s.Wifi, s.WifiSignal) = ReadWifi();
                s.Disks = ReadDisks();
                s.Temp = ReadTemp();
            }
            else
            {
                s.Wifi = prev.Wifi; s.WifiSignal = prev.WifiSignal; s.Disks = prev.Disks; s.Temp = prev.Temp;
            }

            if (_n % 15 == 0) _ = Task.Run(BluetoothAsync);
            s.Bluetooth = _bt;

            ReadBrightness();
            Latest = s;
            _n++;
        }

        double SampleGpu(double prev)
        {
            if (!_gpuOk) return -1;
            try
            {
                if (_gpu == null || (DateTime.Now - _gpuRefresh).TotalSeconds > 20)
                {
                    var names = new PerformanceCounterCategory("GPU Engine").GetInstanceNames().Where(n => n.Contains("engtype_3D")).ToArray();
                    _gpu?.ForEach(c => c.Dispose());
                    _gpu = names.Select(n => new PerformanceCounter("GPU Engine", "Utilization Percentage", n, true)).ToList();
                    foreach (var c in _gpu) { try { c.NextValue(); } catch { } }
                    _gpuRefresh = DateTime.Now;
                    return prev;
                }
                double sum = 0;
                foreach (var c in _gpu) { try { sum += c.NextValue(); } catch { } }
                return Math.Min(100, sum);
            }
            catch { _gpuOk = false; return -1; }
        }

        double ReadTemp()
        {
            if (!_tempOk) return -1;
            try
            {
                using var q = new ManagementObjectSearcher(@"root\WMI", "SELECT CurrentTemperature FROM MSAcpi_ThermalZoneTemperature");
                using var res = q.Get();
                foreach (ManagementObject o in res)
                    using (o) return Convert.ToDouble(o["CurrentTemperature"]) / 10.0 - 273.15;
            }
            catch { }
            _tempOk = false;
            return -1;
        }

        void ReadBrightness()
        {
            if (!_brightOk) return;
            try
            {
                // Requête WMI préparée une seule fois (elle tourne chaque seconde), résultats libérés à chaque appel.
                _brightQ ??= new ManagementObjectSearcher(@"root\WMI", "SELECT CurrentBrightness FROM WmiMonitorBrightness");
                using var res = _brightQ.Get();
                foreach (ManagementObject o in res)
                using (o)
                {
                    int b = Convert.ToInt32(o["CurrentBrightness"]);
                    if (_lastBright >= 0 && b != _lastBright) BrightnessChanged?.Invoke(b);
                    _lastBright = b;
                    return;
                }
            }
            catch { }
            try { _brightQ?.Dispose(); } catch { }
            _brightQ = null;
            _brightOk = false; // écran de bureau : pas de luminosité WMI
        }

        static (string, int) ReadWifi()
        {
            try
            {
                var oem = Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage);
                var psi = new ProcessStartInfo("netsh", "wlan show interfaces")
                { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true, StandardOutputEncoding = oem };
                using var p = Process.Start(psi);
                var o = p.StandardOutput.ReadToEnd();
                p.WaitForExit(3000);
                var ssid = Regex.Match(o, @"^\s*SSID\s*:\s*(.+?)\s*$", RegexOptions.Multiline);
                var sig = Regex.Match(o, @"^\s*Signal\s*:\s*(\d+)", RegexOptions.Multiline);
                if (ssid.Success) return (ssid.Groups[1].Value, sig.Success ? int.Parse(sig.Groups[1].Value) : -1);
            }
            catch { }
            bool eth = NetworkInterface.GetAllNetworkInterfaces().Any(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType == NetworkInterfaceType.Ethernet);
            return (eth ? "Ethernet" : "Déconnecté", -1);
        }

        static List<InfoRow> ReadDisks()
        {
            var l = new List<InfoRow>();
            foreach (var d in DriveInfo.GetDrives())
            {
                try
                {
                    if (!d.IsReady || d.DriveType != DriveType.Fixed) continue;
                    var used = 100.0 * (d.TotalSize - d.TotalFreeSpace) / d.TotalSize;
                    l.Add(new InfoRow
                    {
                        Left = $"{d.Name.TrimEnd('\\')} {d.VolumeLabel}",
                        Right = $"{d.TotalFreeSpace / 1073741824.0:0} Go libres",
                        Sub = $"{used:0} % utilisé sur {d.TotalSize / 1073741824.0:0} Go",
                        Tone = used > 90 ? "down" : ""
                    });
                }
                catch { }
            }
            return l;
        }

        async Task BluetoothAsync()
        {
            try
            {
                var names = new HashSet<string>();
                foreach (var sel in new[] {
                    BluetoothDevice.GetDeviceSelectorFromConnectionStatus(BluetoothConnectionStatus.Connected),
                    BluetoothLEDevice.GetDeviceSelectorFromConnectionStatus(BluetoothConnectionStatus.Connected) })
                {
                    foreach (var d in await DeviceInformation.FindAllAsync(sel))
                        if (!string.IsNullOrWhiteSpace(d.Name)) names.Add(d.Name);
                }

                var bat = new Dictionary<string, int>();
                try
                {
                    var objs = await PnpObject.FindAllAsync(PnpObjectType.Device, new[] { BatteryKey, "System.ItemNameDisplay" }, "");
                    foreach (var o in objs)
                    {
                        if (o.Properties.TryGetValue(BatteryKey, out var v) && v != null && o.Properties.TryGetValue("System.ItemNameDisplay", out var n) && n is string nm)
                            bat[nm] = Convert.ToInt32(v);
                    }
                }
                catch { }

                _bt = names.Select(n =>
                {
                    var b = bat.FirstOrDefault(kv => kv.Key.Contains(n, StringComparison.OrdinalIgnoreCase) || n.Contains(kv.Key, StringComparison.OrdinalIgnoreCase));
                    return new InfoRow { Left = n, Right = b.Key != null ? $"{b.Value} %" : "Connecté", Tone = b.Key != null && b.Value <= 20 ? "down" : "accent" };
                }).ToList();
            }
            catch { }
        }

        static (bool, string) Cap(string cap)
        {
            try
            {
                using var k = Registry.CurrentUser.OpenSubKey($@"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\{cap}");
                return k == null ? (false, "") : Scan(k);
            }
            catch { return (false, ""); }
        }

        static (bool, string) Scan(RegistryKey k)
        {
            foreach (var n in k.GetSubKeyNames())
            {
                using var sk = k.OpenSubKey(n);
                if (sk == null) continue;
                if (n == "NonPackaged") { var r = Scan(sk); if (r.Item1) return r; continue; }
                if (sk.GetValue("LastUsedTimeStop") is long stop && stop == 0 && sk.GetValue("LastUsedTimeStart") is long start && start > 0)
                {
                    var name = n.Contains('#') ? Path.GetFileNameWithoutExtension(n.Split('#').Last()) : n.Split('_')[0];
                    if (name.Contains('.')) name = name.Split('.').Last();
                    return (true, name);
                }
            }
            return (false, "");
        }
    }
}
