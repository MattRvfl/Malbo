using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Win32;

namespace DynamicIsland
{
    /// <summary>
    /// Installation « par utilisateur » dans %LocalAppData%\Programs\Malbo (sans droits admin) :
    /// copie des fichiers, raccourcis, entrée dans « Applications installées », désinstallation.
    /// </summary>
    public static class Installer
    {
        const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\Malbo";
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string ExeName = "Malbo.exe";

        public static string InstallDir =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Malbo");

        static string StartMenuLink => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Malbo.lnk");
        static string DesktopLink => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Malbo.lnk");

        static string Norm(string p) => Path.GetFullPath(p).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        public static bool IsInstalled
        {
            get
            {
                try { return string.Equals(Norm(AppContext.BaseDirectory), Norm(InstallDir), StringComparison.OrdinalIgnoreCase); }
                catch { return false; }
            }
        }

        public static void Install()
        {
            try
            {
                if (IsInstalled) return;
                string src = Norm(AppContext.BaseDirectory);
                string dst = InstallDir;
                CopyDir(src, dst);

                string exe = Path.Combine(dst, ExeName);
                CreateShortcut(StartMenuLink, exe, dst);
                CreateShortcut(DesktopLink, exe, dst);

                using (var k = Registry.CurrentUser.CreateSubKey(UninstallKey))
                {
                    k.SetValue("DisplayName", "Malbo");
                    k.SetValue("DisplayIcon", exe);
                    k.SetValue("DisplayVersion", Updater.Current.ToString(3));
                    k.SetValue("Publisher", "Malbo");
                    k.SetValue("InstallLocation", dst);
                    k.SetValue("UninstallString", $"\"{exe}\" --uninstall");
                    k.SetValue("NoModify", 1, RegistryValueKind.DWord);
                    k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                }

                using (var run = Registry.CurrentUser.OpenSubKey(RunKey, true))
                {
                    if (run?.GetValue("Malbo") != null) run.SetValue("Malbo", $"\"{exe}\"");
                }

                Process.Start(new ProcessStartInfo(exe, $"--wait-pid {Environment.ProcessId}")
                {
                    UseShellExecute = false,
                    WorkingDirectory = dst,
                });
                Application.Current?.Shutdown();
            }
            catch (Exception ex)
            {
                App.Log(ex);
                throw;
            }
        }

        public static void Uninstall()
        {
            try { if (App.Settings != null) BannerService.Restore(App.Settings); } catch (Exception ex) { App.Log(ex); }

            try
            {
                using var run = Registry.CurrentUser.OpenSubKey(RunKey, true);
                if (run?.GetValue("Malbo") != null) run.DeleteValue("Malbo", false);
            }
            catch (Exception ex) { App.Log(ex); }

            try { if (File.Exists(StartMenuLink)) File.Delete(StartMenuLink); } catch (Exception ex) { App.Log(ex); }
            try { if (File.Exists(DesktopLink)) File.Delete(DesktopLink); } catch (Exception ex) { App.Log(ex); }
            try { Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, false); } catch (Exception ex) { App.Log(ex); }

            // Ferme les autres instances lancées depuis le dossier d'installation (sinon les fichiers sont verrouillés).
            try
            {
                string dir = Norm(InstallDir);
                foreach (var p in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(ExeName)))
                {
                    try
                    {
                        if (p.Id == Environment.ProcessId) continue;
                        string path = p.MainModule?.FileName;
                        if (path != null && path.StartsWith(dir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) p.Kill();
                    }
                    catch { }
                    finally { p.Dispose(); }
                }
            }
            catch { }

            // Suppression différée du dossier (on garde %AppData%\Malbo, les réglages).
            try
            {
                Process.Start(new ProcessStartInfo("cmd.exe", $"/c timeout /t 2 >nul & rmdir /s /q \"{Norm(InstallDir)}\"")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                });
            }
            catch (Exception ex) { App.Log(ex); }
        }

        static void CopyDir(string src, string dst)
        {
            Directory.CreateDirectory(dst);
            foreach (var f in Directory.GetFiles(src))
                File.Copy(f, Path.Combine(dst, Path.GetFileName(f)), true);
            foreach (var d in Directory.GetDirectories(src))
                CopyDir(d, Path.Combine(dst, Path.GetFileName(d)));
        }

        static void CreateShortcut(string lnkPath, string target, string workDir)
        {
            object shell = null, lnk = null;
            try
            {
                var t = Type.GetTypeFromProgID("WScript.Shell");
                if (t == null) return;
                Directory.CreateDirectory(Path.GetDirectoryName(lnkPath));
                shell = Activator.CreateInstance(t);
                dynamic sh = shell;
                lnk = sh.CreateShortcut(lnkPath);
                dynamic l = lnk;
                l.TargetPath = target;
                l.WorkingDirectory = workDir;
                l.IconLocation = target + ",0";
                l.Description = "Malbo";
                l.Save();
            }
            catch (Exception ex) { App.Log(ex); }
            finally
            {
                if (lnk != null && Marshal.IsComObject(lnk)) Marshal.FinalReleaseComObject(lnk);
                if (shell != null && Marshal.IsComObject(shell)) Marshal.FinalReleaseComObject(shell);
            }
        }
    }

    public class UpdateInfo
    {
        public Version Version;
        public string ZipUrl;
        public string Notes;
    }

    /// <summary>Mises à jour via les releases GitHub (asset .zip de publish.ps1).</summary>
    public static class Updater
    {
        static readonly HttpClient Http = CreateClient();

        static HttpClient CreateClient()
        {
            var h = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
            h.DefaultRequestHeaders.UserAgent.ParseAdd("Malbo");
            return h;
        }

        static Version Three(Version v) => new Version(v.Major, Math.Max(v.Minor, 0), Math.Max(v.Build, 0));

        public static Version Current
        {
            get
            {
                var v = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0);
                return Three(v);
            }
        }

        public static async Task<UpdateInfo> CheckAsync(string repo)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(repo)) return null;
                repo = repo.Trim().Trim('/');
                using var req = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{repo}/releases/latest");
                req.Headers.Accept.ParseAdd("application/vnd.github+json");
                using var resp = await Http.SendAsync(req);
                if (!resp.IsSuccessStatusCode) return null;
                var json = await resp.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                string tag = root.TryGetProperty("tag_name", out var t) ? t.GetString() : null;
                if (string.IsNullOrWhiteSpace(tag)) return null;
                tag = tag.Trim().TrimStart('v', 'V');
                if (!Version.TryParse(tag, out var ver)) return null;
                ver = Three(ver);
                if (ver <= Current) return null;

                string zip = null;
                if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
                {
                    foreach (var a in assets.EnumerateArray())
                    {
                        string name = a.TryGetProperty("name", out var n) ? n.GetString() : null;
                        if (name == null || !name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) continue;
                        zip = a.TryGetProperty("browser_download_url", out var u) ? u.GetString() : null;
                        if (zip != null) break;
                    }
                }
                if (zip == null) return null;

                string notes = root.TryGetProperty("body", out var b) && b.ValueKind == JsonValueKind.String ? b.GetString() : "";
                return new UpdateInfo { Version = ver, ZipUrl = zip, Notes = notes };
            }
            catch { return null; }
        }

        public static async Task ApplyAsync(UpdateInfo u)
        {
            if (u == null || string.IsNullOrEmpty(u.ZipUrl)) return;
            try
            {
                string tmp = Path.GetTempPath();
                string tag = u.Version?.ToString() ?? "new";
                string zipPath = Path.Combine(tmp, $"Malbo-update-{tag}.zip");
                string extractDir = Path.Combine(tmp, $"Malbo-update-{tag}");

                using (var resp = await Http.GetAsync(u.ZipUrl, HttpCompletionOption.ResponseHeadersRead))
                {
                    resp.EnsureSuccessStatusCode();
                    using var fs = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None);
                    await resp.Content.CopyToAsync(fs);
                }

                if (Directory.Exists(extractDir)) Directory.Delete(extractDir, true);
                ZipFile.ExtractToDirectory(zipPath, extractDir, true);
                try { File.Delete(zipPath); } catch { }

                string exe = Directory.GetFiles(extractDir, ExeNameOf(), SearchOption.AllDirectories)
                    .OrderBy(p => p.Length).FirstOrDefault();
                if (exe == null) throw new FileNotFoundException("Malbo.exe introuvable dans la mise à jour.");
                string src = Path.GetDirectoryName(exe).TrimEnd('\\');
                string dst = AppContext.BaseDirectory.TrimEnd('\\');
                int pid = Environment.ProcessId;

                string bat = Path.Combine(tmp, "malbo_update.bat");
                var sb = new StringBuilder();
                sb.Append("@echo off\r\n");
                sb.Append("chcp 65001 >nul\r\n");
                sb.Append(":wait\r\n");
                sb.Append($"tasklist /fi \"PID eq {pid}\" 2>nul | find \"{pid}\" >nul\r\n");
                sb.Append("if not errorlevel 1 (\r\n  ping -n 2 127.0.0.1 >nul\r\n  goto wait\r\n)\r\n");
                sb.Append($"xcopy \"{src}\\*\" \"{dst}\" /e /y /q /i >nul\r\n");
                sb.Append($"start \"\" \"{dst}\\{ExeNameOf()}\"\r\n");
                sb.Append($"rmdir /s /q \"{extractDir}\" >nul 2>&1\r\n");
                sb.Append("(goto) 2>nul & del \"%~f0\"\r\n");
                File.WriteAllText(bat, sb.ToString(), new UTF8Encoding(false));

                Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"{bat}\"")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                });

                var app = Application.Current;
                if (app != null) app.Dispatcher.Invoke(() => app.Shutdown());
            }
            catch (Exception ex)
            {
                App.Log(ex);
                throw;
            }
        }

        static string ExeNameOf() => "Malbo.exe";
    }
}
