using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace DynamicIsland
{
    public partial class App : Application
    {
        public static AppSettings Settings;
        static Mutex _mutex;

        protected override void OnStartup(StartupEventArgs e)
        {
            var args = e.Args ?? Array.Empty<string>();

            // Gardien : attend la fin de l'instance principale, puis restaure les bannières si elle n'a pas été relancée.
            if (HasArg(args, "--watchdog"))
            {
                try
                {
                    // Le gardien ne fait qu'attendre : on lui retire presque toute sa RAM (quelques Mo au lieu de ~40).
                    Native.TrimMemory();
                    WaitPid(ArgInt(args, "--watchdog"), Timeout.Infinite);
                    Thread.Sleep(1500);
                    if (Mutex.TryOpenExisting("Malbo_SingleInstance", out var other))
                    {
                        other.Dispose(); // une autre instance tourne (redémarrage ou mise à jour) : rien à faire
                    }
                    else
                    {
                        Settings = AppSettings.Load();
                        BannerService.Restore(Settings);
                    }
                }
                catch (Exception ex) { Log(ex); }
                Shutdown();
                return;
            }

            if (HasArg(args, "--uninstall"))
            {
                try
                {
                    Settings = AppSettings.Load();
                    Installer.Uninstall();
                }
                catch (Exception ex) { Log(ex); }
                Shutdown();
                return;
            }

            if (HasArg(args, "--wait-pid"))
                WaitPid(ArgInt(args, "--wait-pid"), 15000);

            _mutex = new Mutex(true, "Malbo_SingleInstance", out bool created);
            if (!created) { Shutdown(); return; }
            StartWatchdog();
            base.OnStartup(e);
            System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
            Settings = AppSettings.Load();
            DispatcherUnhandledException += (s, a) => { Log(a.Exception); a.Handled = true; };
            TaskScheduler.UnobservedTaskException += (s, a) => { Log(a.Exception); a.SetObserved(); };
            AppDomain.CurrentDomain.UnhandledException += (s, a) => Log(a.ExceptionObject as Exception);
            new MainWindow().Show();
        }

        static bool HasArg(string[] args, string name)
        {
            foreach (var a in args) if (string.Equals(a, name, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        static int ArgInt(string[] args, string name)
        {
            for (int i = 0; i < args.Length - 1; i++)
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase) && int.TryParse(args[i + 1], out int v)) return v;
            return 0;
        }

        static void WaitPid(int pid, int timeoutMs)
        {
            if (pid <= 0 || pid == Environment.ProcessId) return;
            try
            {
                using var p = Process.GetProcessById(pid);
                p.WaitForExit(timeoutMs);
            }
            catch { } // processus déjà terminé
        }

        static void StartWatchdog()
        {
            try
            {
                string exe = Environment.ProcessPath;
                if (string.IsNullOrEmpty(exe)) return;
                Process.Start(new ProcessStartInfo(exe, $"--watchdog {Environment.ProcessId}")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                });
            }
            catch (Exception ex) { Log(ex); }
        }

        public static void Log(Exception ex)
        {
            try
            {
                Directory.CreateDirectory(AppSettings.Dir);
                File.AppendAllText(Path.Combine(AppSettings.Dir, "error.log"), $"[{DateTime.Now}] {ex}\n\n");
            }
            catch { }
        }
    }
}
