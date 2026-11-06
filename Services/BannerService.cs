using System;
using Microsoft.Win32;

namespace DynamicIsland
{
    /// <summary>
    /// Masque les bannières Windows (en bas à droite) app par app via la clé ShowBanner.
    /// Les notifications restent dans le centre de notifications, donc Malbo continue de les recevoir.
    /// Les valeurs d'origine sont sauvegardées et restaurées quand on quitte Malbo ou qu'on désactive l'option.
    /// </summary>
    public static class BannerService
    {
        const string Root = @"Software\Microsoft\Windows\CurrentVersion\Notifications\Settings";

        public static void Apply(AppSettings s)
        {
            bool changed = false;
            try
            {
                using var root = Registry.CurrentUser.OpenSubKey(Root, true);
                if (root == null) return;
                foreach (var name in root.GetSubKeyNames())
                {
                    if (name.Contains("Malbo", StringComparison.OrdinalIgnoreCase)) continue;
                    using var k = root.OpenSubKey(name, true);
                    if (k == null) continue;
                    var cur = k.GetValue("ShowBanner");
                    if (cur is int v && v == 0) continue; // déjà masqué (par nous ou par l'utilisateur)
                    if (!s.BannerBackup.ContainsKey(name)) s.BannerBackup[name] = cur is int i ? i : -1;
                    k.SetValue("ShowBanner", 0, RegistryValueKind.DWord);
                    changed = true;
                }
            }
            catch (Exception ex) { App.Log(ex); }
            if (changed) s.Save();
        }

        public static void Restore(AppSettings s)
        {
            if (s.BannerBackup.Count == 0) return;
            try
            {
                using var root = Registry.CurrentUser.OpenSubKey(Root, true);
                if (root != null)
                {
                    foreach (var kv in s.BannerBackup)
                    {
                        using var k = root.OpenSubKey(kv.Key, true);
                        if (k == null) continue;
                        if (kv.Value < 0) k.DeleteValue("ShowBanner", false);
                        else k.SetValue("ShowBanner", kv.Value, RegistryValueKind.DWord);
                    }
                }
            }
            catch (Exception ex) { App.Log(ex); }
            s.BannerBackup.Clear();
            s.Save();
        }
    }
}
