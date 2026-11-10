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
                // Version « fichier unique » : seul l'exe compte (ne jamais copier le dossier où il traîne, ex. Téléchargements).
                if (File.Exists(Path.Combine(src, "Malbo.dll"))) CopyDir(src, dst);
                else
                {
                    Directory.CreateDirectory(dst);
                    File.Copy(Environment.ProcessPath, Path.Combine(dst, ExeName), true);
                }

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
        public string ExeUrl;
        public string Notes;
        /// <summary>Empreinte SHA-256 attendue du fichier (manifeste). Vide = non vérifiée.</summary>
        public string Sha256;
        /// <summary>Le manifeste impose la mise à jour (correctif important).</summary>
        public bool Mandatory;
        /// <summary>« manifest » ou « release ».</summary>
        public string Source;
        /// <summary>Suffixe de pré-version (« alpha », « beta.2 »…), vide pour une version finale.</summary>
        public string Pre = "";
        public string Name => Version == null ? "?" : Version.ToString(3) + (string.IsNullOrEmpty(Pre) ? "" : "-" + Pre);
    }

    /// <summary>
    /// Mises à jour depuis le dépôt GitHub :
    /// 1. manifeste <c>update.json</c> à la racine du dépôt (version, url, sha256, notes) — généré par publish.ps1 ;
    /// 2. à défaut, la dernière release GitHub (asset Malbo.exe, ou un .zip).
    /// Le fichier téléchargé est vérifié (SHA-256 du manifeste + en-tête d'exécutable) avant remplacement.
    /// </summary>
    public static class Updater
    {
        public const string ManifestFile = "update.json";
        static readonly HttpClient Http = CreateClient();

        static HttpClient CreateClient()
        {
            var h = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
            h.DefaultRequestHeaders.UserAgent.ParseAdd("Malbo-Updater");
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

        static string NormRepo(string repo)
        {
            repo = (repo ?? "").Trim().Trim('/');
            const string gh = "https://github.com/";
            if (repo.StartsWith(gh, StringComparison.OrdinalIgnoreCase)) repo = repo.Substring(gh.Length);
            if (repo.EndsWith(".git", StringComparison.OrdinalIgnoreCase)) repo = repo.Substring(0, repo.Length - 4);
            var parts = repo.Split('/');
            return parts.Length >= 2 ? parts[0] + "/" + parts[1] : null;
        }

        /// <summary>Suffixe de pré-version de l'exe en cours (« alpha », « beta »…), vide pour une version finale.</summary>
        public static string CurrentPre
        {
            get
            {
                try
                {
                    var info = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "";
                    return TryVersion(info, out _, out var pre) ? pre : "";
                }
                catch { return ""; }
            }
        }

        public static string CurrentName => Current.ToString(3) + (CurrentPre.Length > 0 ? "-" + CurrentPre : "");

        static bool TryVersion(string s, out Version v) => TryVersion(s, out v, out _);

        /// <summary>Lit « v1.3.0-alpha+abc » → (1.3.0, "alpha").</summary>
        static bool TryVersion(string s, out Version v, out string pre)
        {
            v = null; pre = "";
            if (string.IsNullOrWhiteSpace(s)) return false;
            s = s.Trim().TrimStart('v', 'V');
            int plus = s.IndexOf('+');
            if (plus >= 0) s = s.Substring(0, plus);
            int dash = s.IndexOf('-');
            if (dash > 0) { pre = s.Substring(dash + 1).Trim().ToLowerInvariant(); s = s.Substring(0, dash); }
            if (!Version.TryParse(s, out var p)) return false;
            v = Three(p);
            return true;
        }

        /// <summary>Compare deux versions. À numéro égal : alpha &lt; beta &lt; rc &lt; version finale (sans suffixe).</summary>
        static int Cmp(Version v1, string p1, Version v2, string p2)
        {
            int c = v1.CompareTo(v2);
            if (c != 0) return c;
            p1 ??= ""; p2 ??= "";
            if (p1.Length == 0 || p2.Length == 0) return p2.Length.CompareTo(p1.Length) == 0 ? 0 : (p1.Length == 0 ? 1 : -1);
            return Math.Sign(string.CompareOrdinal(p1, p2));
        }

        static bool IsNewer(Version v, string pre) => Cmp(v, pre, Current, CurrentPre) > 0;

        /// <summary>
        /// Renvoie la mise à jour disponible, ou null si Malbo est à jour. Lève une exception si le dépôt est injoignable.
        /// Avec <paramref name="beta"/>, les pré-versions (alpha, bêta) publiées sur GitHub sont aussi proposées.
        /// </summary>
        public static async Task<UpdateInfo> CheckAsync(string repo, bool beta = false)
        {
            repo = NormRepo(repo);
            if (repo == null) throw new ArgumentException("Dépôt GitHub invalide (format attendu : propriétaire/nom).");

            var fromManifest = await CheckManifestAsync(repo);
            if (!beta)
            {
                if (fromManifest.Found) return fromManifest.Info;
                return await CheckReleaseAsync(repo, false);
            }
            // Canal bêta : on garde la plus récente entre le manifeste (version finale) et les pré-versions GitHub.
            UpdateInfo pre = null;
            try { pre = await CheckReleaseAsync(repo, true); }
            catch { if (fromManifest.Info == null) throw; }
            var st = fromManifest.Info;
            if (pre == null) return st;
            if (st == null) return pre;
            return Cmp(st.Version, st.Pre, pre.Version, pre.Pre) >= 0 ? st : pre;
        }

        static async Task<(bool Found, UpdateInfo Info)> CheckManifestAsync(string repo)
        {
            try
            {
                // HEAD = branche par défaut du dépôt ; le paramètre évite un manifeste périmé en cache.
                var url = $"https://raw.githubusercontent.com/{repo}/HEAD/{ManifestFile}?t={DateTime.UtcNow.Ticks / TimeSpan.TicksPerMinute}";
                using var resp = await Http.GetAsync(url);
                if (!resp.IsSuccessStatusCode) return (false, null);
                using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
                var r = doc.RootElement;
                string Str(string n) => r.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

                if (!TryVersion(Str("version"), out var ver, out var pre)) return (false, null);
                if (!IsNewer(ver, pre)) return (true, null);

                string dl = Str("url");
                if (string.IsNullOrWhiteSpace(dl) || !dl.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return (false, null);
                bool mandatory = r.TryGetProperty("mandatory", out var m) && m.ValueKind == JsonValueKind.True;
                var info = new UpdateInfo
                {
                    Version = ver, Pre = pre, Notes = Str("notes") ?? "", Sha256 = Str("sha256"), Mandatory = mandatory, Source = "manifest"
                };
                if (dl.Split('?')[0].EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) info.ZipUrl = dl; else info.ExeUrl = dl;
                return (true, info);
            }
            catch (Exception ex) when (ex is JsonException || ex is HttpRequestException || ex is TaskCanceledException)
            {
                return (false, null); // pas de manifeste lisible : on tente les releases
            }
        }

        static async Task<UpdateInfo> CheckReleaseAsync(string repo, bool includePre)
        {
            // /releases/latest ignore les pré-versions ; la liste complète les contient (la plus récente en premier).
            var api = includePre ? $"https://api.github.com/repos/{repo}/releases?per_page=15" : $"https://api.github.com/repos/{repo}/releases/latest";
            using var req = new HttpRequestMessage(HttpMethod.Get, api);
            req.Headers.Accept.ParseAdd("application/vnd.github+json");
            using var resp = await Http.SendAsync(req);
            if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return null; // aucune release publiée
            if ((int)resp.StatusCode == 403) throw new HttpRequestException("limite de l'API GitHub atteinte, réessaie plus tard.");
            resp.EnsureSuccessStatusCode();
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());

            UpdateInfo best = null;
            var items = doc.RootElement.ValueKind == JsonValueKind.Array ? doc.RootElement.EnumerateArray().ToList() : new System.Collections.Generic.List<JsonElement> { doc.RootElement };
            foreach (var root in items)
            {
                if (root.TryGetProperty("draft", out var dr) && dr.ValueKind == JsonValueKind.True) continue;
                string tag = root.TryGetProperty("tag_name", out var t) ? t.GetString() : null;
                if (!TryVersion(tag, out var ver, out var pre) || !IsNewer(ver, pre)) continue;
                if (best != null && Cmp(ver, pre, best.Version, best.Pre) <= 0) continue;

                string zip = null, exe = null, sha = null;
                if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
                {
                    foreach (var a in assets.EnumerateArray())
                    {
                        string name = a.TryGetProperty("name", out var n) ? n.GetString() : null;
                        string url = a.TryGetProperty("browser_download_url", out var u) ? u.GetString() : null;
                        if (name == null || url == null) continue;
                        if (exe == null && name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                        {
                            exe = url;
                            // GitHub publie l'empreinte de chaque fichier joint (« sha256:… ») : on la vérifie aussi.
                            if (a.TryGetProperty("digest", out var dg) && dg.ValueKind == JsonValueKind.String && dg.GetString().StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
                                sha = dg.GetString().Substring(7);
                        }
                        else if (zip == null && name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) zip = url;
                    }
                }
                if (zip == null && exe == null) continue;
                string notes = root.TryGetProperty("body", out var b) && b.ValueKind == JsonValueKind.String ? b.GetString() : "";
                best = new UpdateInfo { Version = ver, Pre = pre, ZipUrl = exe == null ? zip : null, ExeUrl = exe, Notes = notes, Sha256 = exe != null ? sha : null, Source = "release" };
            }
            return best;
        }

        /// <summary>Télécharge dans un fichier temporaire en calculant le SHA-256 au passage, puis vérifie l'empreinte attendue.</summary>
        static async Task DownloadVerifiedAsync(string url, string path, string expectedSha)
        {
            string part = path + ".part";
            using (var resp = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead))
            {
                resp.EnsureSuccessStatusCode();
                using var src = await resp.Content.ReadAsStreamAsync();
                using var fs = new FileStream(part, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);
                using var sha = System.Security.Cryptography.IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA256);
                var buf = new byte[81920];
                int n;
                long total = 0;
                while ((n = await src.ReadAsync(buf, 0, buf.Length)) > 0)
                {
                    sha.AppendData(buf, 0, n);
                    await fs.WriteAsync(buf, 0, n);
                    total += n;
                }
                if (total < 1024) throw new InvalidDataException("Fichier de mise à jour vide ou incomplet.");
                var got = Convert.ToHexString(sha.GetHashAndReset());
                if (!string.IsNullOrWhiteSpace(expectedSha) && !string.Equals(got, expectedSha.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    fs.Close();
                    try { File.Delete(part); } catch { }
                    throw new InvalidDataException("Empreinte SHA-256 incorrecte : téléchargement corrompu, mise à jour annulée.");
                }
            }
            if (File.Exists(path)) File.Delete(path);
            File.Move(part, path);
        }

        static void EnsureExe(string path)
        {
            using var f = File.OpenRead(path);
            if (f.ReadByte() != 'M' || f.ReadByte() != 'Z') throw new InvalidDataException("Le fichier téléchargé n'est pas un exécutable Windows.");
        }

        public static async Task ApplyAsync(UpdateInfo u)
        {
            if (u == null || (string.IsNullOrEmpty(u.ZipUrl) && string.IsNullOrEmpty(u.ExeUrl))) return;
            if (!string.IsNullOrEmpty(u.ExeUrl)) { await ApplyExeAsync(u); return; }
            try
            {
                string tmp = Path.GetTempPath();
                string tag = u.Version?.ToString() ?? "new";
                string zipPath = Path.Combine(tmp, $"Malbo-update-{tag}.zip");
                string extractDir = Path.Combine(tmp, $"Malbo-update-{tag}");

                await DownloadVerifiedAsync(u.ZipUrl, zipPath, u.Sha256);

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

        /// <summary>Mise à jour « fichier unique » : télécharge le nouvel exe et remplace l'actuel une fois Malbo fermé.</summary>
        static async Task ApplyExeAsync(UpdateInfo u)
        {
            try
            {
                string tmp = Path.GetTempPath();
                string newExe = Path.Combine(tmp, $"Malbo-update-{u.Version}.exe");
                await DownloadVerifiedAsync(u.ExeUrl, newExe, u.Sha256);
                EnsureExe(newExe);

                string target = Environment.ProcessPath;
                int pid = Environment.ProcessId;
                string bat = Path.Combine(tmp, "malbo_update.bat");
                var sb = new StringBuilder();
                sb.Append("@echo off\r\n");
                sb.Append("chcp 65001 >nul\r\n");
                sb.Append(":wait\r\n");
                sb.Append($"tasklist /fi \"PID eq {pid}\" 2>nul | find \"{pid}\" >nul\r\n");
                sb.Append("if not errorlevel 1 (\r\n  ping -n 2 127.0.0.1 >nul\r\n  goto wait\r\n)\r\n");
                // Le gardien peut encore tenir l'exe quelques instants : on réessaie la copie.
                sb.Append("set n=0\r\n:copy\r\n");
                sb.Append($"copy /y \"{newExe}\" \"{target}\" >nul 2>&1\r\n");
                sb.Append("if errorlevel 1 (\r\n  set /a n+=1\r\n  if %n% lss 15 (\r\n    ping -n 2 127.0.0.1 >nul\r\n    goto copy\r\n  )\r\n)\r\n");
                sb.Append($"start \"\" \"{target}\"\r\n");
                sb.Append($"del \"{newExe}\" >nul 2>&1\r\n");
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
