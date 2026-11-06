using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Security.Principal;
using System.Text;

namespace DynamicIsland
{
    /// <summary>
    /// Compteur d'images par seconde basé sur PresentMon 2.x (chemin dans App.Settings.PresentMonPath).
    /// PresentMon écrit un CSV que l'on relit au fil de l'eau à chaque Poll().
    /// </summary>
    public class FpsService
    {
        Process _proc;
        int _pid;
        int _deniedPid;
        string _csv;
        long _pos;
        int _col = -1;
        bool _headerRead;

        public double Fps { get; private set; }

        public bool Active
        {
            get
            {
                try { return _proc != null && !_proc.HasExited; }
                catch { return _proc != null; }
            }
        }

        static bool IsAdmin()
        {
            try
            {
                using var id = WindowsIdentity.GetCurrent();
                return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch { return false; }
        }

        public void Start(int pid)
        {
            if (pid <= 0) return;
            if (pid == _pid) return;
            if (pid == _deniedPid) return; // UAC déjà refusé pour ce processus : on ne redemande pas
            Stop();

            string exe = App.Settings?.PresentMonPath;
            if (string.IsNullOrWhiteSpace(exe) || !File.Exists(exe)) return;

            _csv = Path.Combine(Path.GetTempPath(), $"malbo_fps_{pid}.csv");
            try { if (File.Exists(_csv)) File.Delete(_csv); } catch { }

            var psi = new ProcessStartInfo(exe,
                $"--process_id {pid} --output_file \"{_csv}\" --terminate_on_proc_exit --stop_existing_session --session_name MalboFps --no_console_stats")
            {
                WindowStyle = ProcessWindowStyle.Hidden,
                CreateNoWindow = true,
                UseShellExecute = false,
            };
            if (!IsAdmin())
            {
                psi.Verb = "runas";
                psi.UseShellExecute = true;
            }

            try
            {
                _proc = Process.Start(psi);
                _pid = pid;
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == 1223) // ERROR_CANCELLED : UAC refusé
            {
                _deniedPid = pid;
                _proc = null;
            }
            catch (Exception ex)
            {
                App.Log(ex);
                _proc = null;
            }
        }

        public void Poll()
        {
            if (_csv == null || !File.Exists(_csv)) return;
            try
            {
                string chunk;
                using (var fs = new FileStream(_csv, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    if (fs.Length < _pos) _pos = 0; // fichier recréé
                    if (fs.Length == _pos) return;
                    fs.Seek(_pos, SeekOrigin.Begin);
                    var buf = new byte[fs.Length - _pos];
                    int read = 0;
                    while (read < buf.Length)
                    {
                        int n = fs.Read(buf, read, buf.Length - read);
                        if (n <= 0) break;
                        read += n;
                    }
                    int last = Array.LastIndexOf(buf, (byte)'\n', read - 1);
                    if (last < 0) return; // pas encore de ligne complète
                    chunk = Encoding.UTF8.GetString(buf, 0, last + 1);
                    _pos += last + 1;
                }

                double sum = 0;
                int count = 0;
                foreach (var raw in chunk.Split('\n'))
                {
                    var line = raw.TrimEnd('\r');
                    if (line.Length == 0) continue;
                    var cells = line.Split(',');
                    if (!_headerRead)
                    {
                        _headerRead = true;
                        _col = FindColumn(cells);
                        continue;
                    }
                    if (_col < 0 || _col >= cells.Length) continue;
                    if (double.TryParse(cells[_col].Trim().Trim('"'), NumberStyles.Float, CultureInfo.InvariantCulture, out double ms) && ms > 0)
                    {
                        sum += ms;
                        count++;
                    }
                }
                if (count > 0 && sum > 0) Fps = 1000.0 * count / sum;
            }
            catch (Exception ex) { App.Log(ex); }
        }

        static int FindColumn(string[] header)
        {
            int frameTime = -1;
            for (int i = 0; i < header.Length; i++)
            {
                var h = header[i].Trim().Trim('"');
                if (h.Equals("MsBetweenPresents", StringComparison.OrdinalIgnoreCase)) return i;
                if (frameTime < 0 && h.Equals("FrameTime", StringComparison.OrdinalIgnoreCase)) frameTime = i;
            }
            return frameTime;
        }

        public void Stop()
        {
            if (_proc != null)
            {
                try { if (!_proc.HasExited) _proc.Kill(); } catch { }
                try { _proc.Dispose(); } catch { }
            }
            _proc = null;
            _pid = 0;
            _pos = 0;
            _col = -1;
            _headerRead = false;
            Fps = 0;
            if (_csv != null)
            {
                try { if (File.Exists(_csv)) File.Delete(_csv); } catch { }
                _csv = null;
            }
        }
    }
}
