using System;
using System.Runtime.InteropServices;

namespace DynamicIsland
{
    static class Native
    {
        [DllImport("kernel32.dll")] public static extern bool GetSystemTimes(out long idle, out long kernel, out long user);

        [StructLayout(LayoutKind.Sequential)]
        public struct MEMORYSTATUSEX
        {
            public uint dwLength, dwMemoryLoad;
            public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual;
        }
        [DllImport("kernel32.dll")] public static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX m);
        [DllImport("kernel32.dll")] public static extern IntPtr GetCurrentProcess();
        [DllImport("psapi.dll")] public static extern bool EmptyWorkingSet(IntPtr process);

        /// <summary>
        /// Rend au système la mémoire physique inutilisée (GC compacté + working set vidé). Les pages réellement
        /// utilisées reviennent d'elles-mêmes : c'est sans danger, mais à n'appeler que quand Malbo est au repos.
        /// </summary>
        public static void TrimMemory(bool collect = true)
        {
            try
            {
                if (collect)
                {
                    System.Runtime.GCSettings.LargeObjectHeapCompactionMode = System.Runtime.GCLargeObjectHeapCompactionMode.CompactOnce;
                    GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, true, true);
                    GC.WaitForPendingFinalizers();
                }
                EmptyWorkingSet(GetCurrentProcess());
            }
            catch { }
        }

        [DllImport("shell32.dll")] public static extern int SHQueryUserNotificationState(out int state);
        [DllImport("user32.dll")] public static extern bool AddClipboardFormatListener(IntPtr h);
        [DllImport("user32.dll")] public static extern bool RemoveClipboardFormatListener(IntPtr h);
        [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr h, int id, uint mod, uint vk);
        [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr h, int id);
        [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr h, int i);
        [DllImport("user32.dll")] public static extern int SetWindowLong(IntPtr h, int i, int v);

        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr h, System.Text.StringBuilder s, int n);
        [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
        [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
        [DllImport("user32.dll")] public static extern IntPtr MonitorFromWindow(IntPtr h, uint flags);
        [DllImport("user32.dll")] public static extern bool GetMonitorInfo(IntPtr m, ref MONITORINFO mi);
        [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] public struct MONITORINFO { public int cbSize; public RECT rcMonitor, rcWork; public uint dwFlags; }

        static readonly string[] ShellClasses = { "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "Windows.UI.Core.CoreWindow", "ForegroundStaging", "XamlExplorerHostIslandWindow" };
        static readonly string[] WallpaperApps = { "wallpaper32", "wallpaper64", "wallpaperui", "lively", "livelywpf", "rainmeter", "explorer" };

        /// <summary>Vrai si la fenêtre au premier plan (hors bureau / fond animé) couvre tout l'écran de l'île.</summary>
        public static bool ForegroundIsFullscreen(IntPtr self) => ForegroundFullscreenWindow(self) != IntPtr.Zero;

        /// <summary>Fenêtre au premier plan (hors bureau / fond animé) qui couvre tout l'écran de l'île, sinon IntPtr.Zero.</summary>
        public static IntPtr ForegroundFullscreenWindow(IntPtr self)
        {
            var h = GetForegroundWindow();
            if (h == IntPtr.Zero || h == self) return IntPtr.Zero;
            var cls = new System.Text.StringBuilder(256);
            GetClassName(h, cls, 256);
            if (Array.IndexOf(ShellClasses, cls.ToString()) >= 0) return IntPtr.Zero;
            GetWindowThreadProcessId(h, out uint pid);
            try
            {
                var name = System.Diagnostics.Process.GetProcessById((int)pid).ProcessName.ToLowerInvariant();
                if (Array.IndexOf(WallpaperApps, name) >= 0) return IntPtr.Zero;
            }
            catch { }
            var mon = MonitorFromWindow(h, 2);
            if (mon != MonitorFromWindow(self, 2)) return IntPtr.Zero;
            var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            if (!GetMonitorInfo(mon, ref mi) || !GetWindowRect(h, out var r)) return IntPtr.Zero;
            bool full = r.Left <= mi.rcMonitor.Left && r.Top <= mi.rcMonitor.Top && r.Right >= mi.rcMonitor.Right && r.Bottom >= mi.rcMonitor.Bottom;
            return full ? h : IntPtr.Zero;
        }

        /// <summary>Identifiant du processus propriétaire d'une fenêtre (0 si inconnu).</summary>
        public static uint WindowPid(IntPtr h)
        {
            if (h == IntPtr.Zero) return 0;
            GetWindowThreadProcessId(h, out uint pid);
            return pid;
        }

        public const int GWL_EXSTYLE = -20, WS_EX_TOOLWINDOW = 0x80;
        public const int WM_CLIPBOARDUPDATE = 0x031D, WM_HOTKEY = 0x0312;
        public const uint MOD_ALT = 1, MOD_CONTROL = 2;
    }
}
