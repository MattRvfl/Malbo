using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Media;

namespace DynamicIsland
{
    public class WindowInfo
    {
        public IntPtr Handle;
        public string Title;
        public string App;
        public ImageSource Icon;
    }

    /// <summary>Fenêtres ouvertes (celles d'Alt+Tab) et bascule vers l'une d'elles.</summary>
    public static class WindowService
    {
        delegate bool EnumProc(IntPtr h, IntPtr l);
        [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc cb, IntPtr l);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll")] static extern bool IsIconic(IntPtr h);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
        [DllImport("user32.dll")] static extern int GetWindowTextLength(IntPtr h);
        [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr h, uint cmd);
        [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h, int cmd);
        [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
        [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr h, int attr, out int v, int size);

        const int GWL_EXSTYLE = -20, WS_EX_TOOLWINDOW = 0x80, WS_EX_APPWINDOW = 0x40000, DWMWA_CLOAKED = 14;
        const uint GW_OWNER = 4;

        public static List<WindowInfo> List(IntPtr self)
        {
            var res = new List<WindowInfo>();
            var sb = new StringBuilder(260);
            try
            {
                EnumWindows((h, l) =>
                {
                    try
                    {
                        if (h == self || !IsWindowVisible(h) || GetWindowTextLength(h) == 0) return true;
                        int ex = Native.GetWindowLong(h, GWL_EXSTYLE);
                        bool tool = (ex & WS_EX_TOOLWINDOW) != 0, app = (ex & WS_EX_APPWINDOW) != 0;
                        if (tool && !app) return true;
                        if (GetWindow(h, GW_OWNER) != IntPtr.Zero && !app) return true;
                        // fenêtres UWP « fantômes » (masquées par DWM)
                        if (DwmGetWindowAttribute(h, DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0 && cloaked != 0) return true;
                        sb.Clear();
                        GetWindowText(h, sb, sb.Capacity);
                        var title = sb.ToString();
                        if (title == "Program Manager") return true;

                        string appName = "", file = null;
                        Native.GetWindowThreadProcessId(h, out uint pid);
                        try
                        {
                            using var p = Process.GetProcessById((int)pid);
                            appName = p.ProcessName;
                            try { file = p.MainModule?.FileName; } catch { }
                        }
                        catch { }
                        res.Add(new WindowInfo { Handle = h, Title = title, App = appName, Icon = IconHelper.FromFile(file) });
                    }
                    catch { }
                    return res.Count < 40;
                }, IntPtr.Zero);
            }
            catch (Exception ex) { App.Log(ex); }
            return res;
        }

        public static void Activate(IntPtr h)
        {
            try
            {
                if (IsIconic(h)) ShowWindow(h, 9); // SW_RESTORE
                SetForegroundWindow(h);
            }
            catch (Exception ex) { App.Log(ex); }
        }
    }
}
