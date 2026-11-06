using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace DynamicIsland
{
    /// <summary>
    /// Lancement d'applications et de cibles (fichiers, URL, ms-settings:) et actions d'alimentation.
    /// </summary>
    public static class AppLauncher
    {
        [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        [DllImport("user32.dll")] static extern bool IsIconic(IntPtr hWnd);
        [DllImport("user32.dll")] static extern bool LockWorkStation();
        [DllImport("powrprof.dll", SetLastError = true)] static extern bool SetSuspendState(bool hibernate, bool forceCritical, bool disableWakeEvent);

        const int SW_RESTORE = 9;

        /// <summary>Met l'appli au premier plan si elle tourne déjà, sinon la lance via son AUMID.</summary>
        public static void OpenApp(string aumid, string appName)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(appName) && FocusRunning(appName)) return;
                if (string.IsNullOrWhiteSpace(aumid)) return;
                Process.Start("explorer.exe", "shell:AppsFolder\\" + aumid);
            }
            catch (Exception ex) { App.Log(ex); }
        }

        /// <summary>Cherche une fenêtre principale dont le processus ou le titre correspond au nom donné.</summary>
        public static bool FocusRunning(string appName)
        {
            string want = Normalize(appName);
            if (want.Length == 0) return false;
            try
            {
                foreach (var p in Process.GetProcesses())
                {
                    try
                    {
                        IntPtr h = p.MainWindowHandle;
                        if (h == IntPtr.Zero) continue;
                        if (!Matches(want, Normalize(p.ProcessName)) && !Matches(want, Normalize(p.MainWindowTitle))) continue;
                        if (IsIconic(h)) ShowWindow(h, SW_RESTORE);
                        SetForegroundWindow(h);
                        return true;
                    }
                    catch { }
                    finally { p.Dispose(); }
                }
            }
            catch (Exception ex) { App.Log(ex); }
            return false;
        }

        static bool Matches(string a, string b)
        {
            if (a.Length == 0 || b.Length == 0) return false;
            if (a == b) return true;
            if (a.Length >= 4 && b.Contains(a)) return true;
            if (b.Length >= 4 && a.Contains(b)) return true;
            return false;
        }

        static string Normalize(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new StringBuilder(s.Length);
            foreach (char c in s)
                if (char.IsLetterOrDigit(c)) sb.Append(char.ToLowerInvariant(c));
            return sb.ToString();
        }

        /// <summary>Ouvre un fichier, dossier, exe, URL ou URI via le shell.</summary>
        public static void Launch(string target)
        {
            if (string.IsNullOrWhiteSpace(target)) return;
            try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); }
            catch (Exception ex) { App.Log(ex); }
        }

        public static void Lock()
        {
            try { LockWorkStation(); }
            catch (Exception ex) { App.Log(ex); }
        }

        public static void Sleep()
        {
            try { SetSuspendState(false, false, false); }
            catch (Exception ex) { App.Log(ex); }
        }

        public static void Restart() => RunShutdown("/r /t 0");
        public static void Shutdown() => RunShutdown("/s /t 0");
        public static void Logoff() => RunShutdown("/l");

        static void RunShutdown(string args)
        {
            try
            {
                Process.Start(new ProcessStartInfo("shutdown", args)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                });
            }
            catch (Exception ex) { App.Log(ex); }
        }
    }
}
