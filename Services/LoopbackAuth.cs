using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DynamicIsland
{
    /// <summary>Petit serveur HTTP local (TcpListener sur 127.0.0.1) qui récupère le code OAuth renvoyé par le navigateur.</summary>
    public static class LoopbackAuth
    {
        static CancellationTokenSource _current;

        public static async Task<string> WaitForCodeAsync(int port, string authorizeUrl, string expectedState, string serviceName)
        {
            TcpListener listener = null;
            // Une nouvelle tentative annule l'attente précédente (sinon le port reste occupé 3 min)
            var cts = new CancellationTokenSource(TimeSpan.FromMinutes(3));
            var previous = Interlocked.Exchange(ref _current, cts);
            try { previous?.Cancel(); } catch { }
            try
            {
                for (int attempt = 0; ; attempt++)
                {
                    try
                    {
                        listener = new TcpListener(IPAddress.Loopback, port);
                        listener.Start();
                        break;
                    }
                    catch (SocketException) when (attempt < 15)
                    {
                        listener = null;
                        await Task.Delay(200);
                    }
                }
                try { Process.Start(new ProcessStartInfo(authorizeUrl) { UseShellExecute = true }); }
                catch (Exception ex) { App.Log(ex); }

                while (!cts.IsCancellationRequested)
                {
                    TcpClient client;
                    try { client = await listener.AcceptTcpClientAsync(cts.Token); }
                    catch (OperationCanceledException) { return null; }

                    using (client)
                    {
                        client.ReceiveTimeout = 5000;
                        client.SendTimeout = 5000;
                        var stream = client.GetStream();
                        string requestLine;
                        try
                        {
                            using var reader = new StreamReader(stream, Encoding.ASCII, false, 4096, leaveOpen: true);
                            requestLine = await reader.ReadLineAsync();
                            // Consomme les en-têtes
                            string line;
                            while (!string.IsNullOrEmpty(line = await reader.ReadLineAsync())) { }
                        }
                        catch { continue; }
                        if (string.IsNullOrEmpty(requestLine)) continue;

                        var parts = requestLine.Split(' ');
                        var target = parts.Length >= 2 ? parts[1] : "";
                        var path = target;
                        var query = "";
                        int q = target.IndexOf('?');
                        if (q >= 0) { path = target.Substring(0, q); query = target.Substring(q + 1); }

                        if (!parts[0].Equals("GET", StringComparison.OrdinalIgnoreCase) || !path.Equals("/callback", StringComparison.OrdinalIgnoreCase))
                        {
                            await Respond(stream, "404 Not Found", "Introuvable");
                            continue;
                        }

                        var p = ParseQuery(query);
                        p.TryGetValue("code", out var code);
                        p.TryGetValue("state", out var state);
                        p.TryGetValue("error", out var error);

                        if (!string.IsNullOrEmpty(error))
                        {
                            await Respond(stream, "200 OK", Page("Connexion refusée", $"{serviceName} a renvoyé une erreur : {WebUtility.HtmlEncode(error)}. Tu peux fermer cet onglet et réessayer depuis Malbo.", false));
                            return null;
                        }
                        if (state != expectedState)
                        {
                            await Respond(stream, "200 OK", Page("Échec de la connexion", "La vérification de sécurité a échoué (state invalide). Réessaie depuis Malbo.", false));
                            return null;
                        }
                        if (string.IsNullOrEmpty(code))
                        {
                            await Respond(stream, "200 OK", Page("Échec de la connexion", "Aucun code d'autorisation reçu. Réessaie depuis Malbo.", false));
                            return null;
                        }
                        await Respond(stream, "200 OK", Page("Connexion réussie", $"Malbo est connecté à {WebUtility.HtmlEncode(serviceName)}, tu peux fermer cet onglet.", true));
                        return code;
                    }
                }
                return null;
            }
            catch (Exception ex) { App.Log(ex); return null; }
            finally
            {
                try { listener?.Stop(); } catch { }
                Interlocked.CompareExchange(ref _current, null, cts);
                cts.Dispose();
            }
        }

        static Dictionary<string, string> ParseQuery(string query)
        {
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in (query ?? "").Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                int e = kv.IndexOf('=');
                var k = e >= 0 ? kv.Substring(0, e) : kv;
                var v = e >= 0 ? kv.Substring(e + 1) : "";
                try { d[Uri.UnescapeDataString(k.Replace('+', ' '))] = Uri.UnescapeDataString(v.Replace('+', ' ')); } catch { }
            }
            return d;
        }

        static string Page(string title, string message, bool ok) =>
            "<!doctype html><html lang=\"fr\"><head><meta charset=\"utf-8\"><title>Malbo</title><style>" +
            "html,body{margin:0;height:100%;background:#0b0b0d;color:#f2f2f2;font-family:'Segoe UI',system-ui,sans-serif}" +
            "body{display:flex;align-items:center;justify-content:center}" +
            ".c{background:#16161a;border:1px solid #26262c;border-radius:22px;padding:36px 44px;max-width:440px;text-align:center}" +
            ".d{width:14px;height:14px;border-radius:50%;margin:0 auto 18px;background:" + (ok ? "#30d158" : "#ff453a") + "}" +
            "h1{font-size:20px;margin:0 0 10px;font-weight:600}p{margin:0;color:#a1a1aa;font-size:14px;line-height:1.5}" +
            "</style></head><body><div class=\"c\"><div class=\"d\"></div><h1>" + WebUtility.HtmlEncode(title) + "</h1><p>" + message + "</p></div></body></html>";

        static async Task Respond(Stream stream, string status, string body)
        {
            try
            {
                var bytes = Encoding.UTF8.GetBytes(body);
                var head = Encoding.ASCII.GetBytes($"HTTP/1.1 {status}\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {bytes.Length}\r\nConnection: close\r\nCache-Control: no-store\r\n\r\n");
                await stream.WriteAsync(head, 0, head.Length);
                await stream.WriteAsync(bytes, 0, bytes.Length);
                await stream.FlushAsync();
            }
            catch { }
        }
    }
}
