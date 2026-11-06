using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;

namespace DynamicIsland
{
    /// <summary>
    /// Interface des plugins. Crée une bibliothèque de classes WPF qui référence Malbo.dll,
    /// implémente cette interface et dépose la DLL dans %AppData%\Malbo\plugins.
    /// </summary>
    public interface IIslandPlugin
    {
        string Name { get; }
        string Glyph { get; }          // glyphe Segoe Fluent Icons, ex. ""
        UIElement CreateView();
    }

    public static class PluginLoader
    {
        public static List<IIslandPlugin> Load()
        {
            var list = new List<IIslandPlugin>();
            try
            {
                Directory.CreateDirectory(AppSettings.PluginDir);
                foreach (var dll in Directory.GetFiles(AppSettings.PluginDir, "*.dll"))
                {
                    try
                    {
                        var asm = Assembly.LoadFrom(dll);
                        foreach (var t in asm.GetTypes().Where(t => typeof(IIslandPlugin).IsAssignableFrom(t) && !t.IsAbstract && !t.IsInterface))
                            list.Add((IIslandPlugin)Activator.CreateInstance(t));
                    }
                    catch (Exception ex) { App.Log(ex); }
                }
            }
            catch { }
            return list;
        }
    }
}
