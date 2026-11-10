using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace DynamicIsland
{
    /// <summary>Une activité en direct : progression d'une tâche poussée par un script, une appli, ou un minuteur nommé.</summary>
    public class LiveActivity
    {
        public string Id = "";
        public string Title = "";
        public string Body = "";
        /// <summary>0..1, ou null si la progression est inconnue.</summary>
        public double? Progress;
        public string Glyph = "\uE000";
        public DateTime Updated = DateTime.Now;
        /// <summary>Secondes sans mise à jour avant disparition.</summary>
        public int Ttl = 600;
        /// <summary>Minuteur nommé : fin prévue (la progression est alors calculée par Malbo).</summary>
        public DateTime? EndsAt;
        public DateTime StartedAt = DateTime.Now;
        public bool IsTimer => EndsAt.HasValue;
    }

    /// <summary>
    /// Petite API HTTP locale (127.0.0.1 uniquement) pour afficher des activités dans l'île :
    ///   POST   /activity        {"id":"build","title":"Compilation","body":"étape 3/8","progress":0.4}
    ///   DELETE /activity/build
    ///   POST   /notify          {"title":"Terminé","body":"Rendu prêt"}
    ///   GET    /activities
    /// Chaque requête doit porter le jeton (en-tête X-Malbo-Token ou ?token=) affiché dans les réglages :
    /// sans lui, n'importe quelle page web ouverte dans le navigateur pourrait écrire dans l'île.
    /// </summary>
    public class LiveActivityService
    {
        readonly object _lock = new();
        readonly Dictionary<string, LiveActivity> _acts = new(StringComparer.OrdinalIgnoreCase);
        TcpListener _listener;
        CancellationTokenSource _cts;

        public event Action Changed;
        public event Action<string, string> Notify;
        public event Action<LiveActivity> Finished;
        public int Port { get; private set; }
        public string Error { get; private set; }

        public List<LiveActivity> Snapshot()
        {
            lock (_lock) return _acts.Values.OrderBy(a => a.StartedAt).ToList();
        }

        public void Set(LiveActivity a)
        {
            lock (_lock)
            {
                if (_acts.TryGetValue(a.Id, out var old)) a.StartedAt = old.StartedAt;
                a.Updated = DateTime.Now;
                _acts[a.Id] = a;
            }
            Changed?.Invoke();
        }

        public bool Remove(string id)
        {
            bool ok;
            lock (_lock) ok = _acts.Remove(id ?? "");
            if (ok) Changed?.Invoke();
            return ok;
        }

        /// <summary>Appelé chaque seconde : fait avancer les minuteurs et retire les activités oubliées.</summary>
        public void Tick()
        {
            List<LiveActivity> done = null;
            bool changed = false;
            lock (_lock)
            {
                var now = DateTime.Now;
                foreach (var a in _acts.Values.ToList())
                {
                    if (a.IsTimer)
                    {
                        if (now >= a.EndsAt.Value) { _acts.Remove(a.Id); (done ??= new()).Add(a); changed = true; }
                    }
                    else if ((now - a.Updated).TotalSeconds > a.Ttl) { _acts.Remove(a.Id); changed = true; }
                }
            }
            if (done != null) foreach (var a in done) Finished?.Invoke(a);
            if (changed) Changed?.Invoke();
        }

        public void Start(int port)
        {
            Stop();
            try
            {
                _listener = new TcpListener(IPAddress.Loopback, port);
                _listener.Start(8);
                Port = port;
                Error = null;
                _cts = new CancellationTokenSource();
                var ct = _cts.Token;
                var l = _listener;
                _ = Task.Run(() => AcceptLoop(l, ct));
            }
            catch (Exception ex)
            {
                Error = ex.Message;
                Port = 0;
                _listener = null;
            }
        }

        public void Stop()
        {
            try { _cts?.Cancel(); } catch { }
            try { _listener?.Stop(); } catch { }
            _listener = null; _cts = null; Port = 0;
        }

        async Task AcceptLoop(TcpListener l, CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                TcpClient c;
                try { c = await l.AcceptTcpClientAsync(ct); }
                catch { break; }
                _ = Task.Run(() => Handle(c));
            }
        }

        async Task Handle(TcpClient client)
        {
            using (client)
            {
                try
                {
                    client.ReceiveTimeout = client.SendTimeout = 3000;
                    using var s = client.GetStream();
                    using var timeout = new CancellationTokenSource(4000);
                    var (method, path, headers, body) = await ReadRequest(s, timeout.Token);
                    if (method == null) return;
                    var (code, json) = Route(method, path, headers, body);
                    var payload = Encoding.UTF8.GetBytes(json);
                    var head = $"HTTP/1.1 {code}\r\nContent-Type: application/json; charset=utf-8\r\nContent-Length: {payload.Length}\r\nConnection: close\r\n\r\n";
                    await s.WriteAsync(Encoding.ASCII.GetBytes(head), timeout.Token);
                    await s.WriteAsync(payload, timeout.Token);
                }
                catch { }
            }
        }

        static async Task<(string Method, string Path, Dictionary<string, string> Headers, string Body)> ReadRequest(NetworkStream s, CancellationToken ct)
        {
            var buf = new byte[65536];
            int len = 0, headEnd = -1;
            while (headEnd < 0 && len < buf.Length)
            {
                int n = await s.ReadAsync(buf.AsMemory(len, buf.Length - len), ct);
                if (n <= 0) return default;
                len += n;
                for (int i = 3; i < len; i++)
                    if (buf[i - 3] == '\r' && buf[i - 2] == '\n' && buf[i - 1] == '\r' && buf[i] == '\n') { headEnd = i + 1; break; }
            }
            if (headEnd < 0) return default;
            var lines = Encoding.ASCII.GetString(buf, 0, headEnd).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
            var first = lines[0].Split(' ');
            if (first.Length < 2) return default;
            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var l in lines.Skip(1))
            {
                int c = l.IndexOf(':');
                if (c > 0) headers[l.Substring(0, c).Trim()] = l.Substring(c + 1).Trim();
            }
            int want = headers.TryGetValue("Content-Length", out var cl) && int.TryParse(cl, out var v) ? v : 0;
            if (want < 0 || headEnd + want > buf.Length) return default; // corps trop gros
            while (len < headEnd + want)
            {
                int n = await s.ReadAsync(buf.AsMemory(len, headEnd + want - len), ct);
                if (n <= 0) break;
                len += n;
            }
            return (first[0].ToUpperInvariant(), first[1], headers, Encoding.UTF8.GetString(buf, headEnd, Math.Max(0, Math.Min(want, len - headEnd))));
        }

        (string Code, string Json) Route(string method, string rawPath, Dictionary<string, string> headers, string body)
        {
            // Un navigateur envoie toujours Origin sur une requête entre sites : on refuse, c'est une API pour scripts locaux.
            if (headers.ContainsKey("Origin")) return ("403 Forbidden", "{\"error\":\"origin\"}");

            string path = rawPath, query = "";
            int q = rawPath.IndexOf('?');
            if (q >= 0) { path = rawPath.Substring(0, q); query = rawPath.Substring(q + 1); }
            string token = headers.TryGetValue("X-Malbo-Token", out var t) ? t : null;
            foreach (var kv in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
                if (kv.StartsWith("token=", StringComparison.OrdinalIgnoreCase)) token = Uri.UnescapeDataString(kv.Substring(6));
            var expected = App.Settings?.LiveApiToken ?? "";
            if (expected.Length == 0 || !FixedEquals(token ?? "", expected)) return ("401 Unauthorized", "{\"error\":\"token\"}");

            path = path.TrimEnd('/');
            try
            {
                if (method == "GET" && path == "/activities")
                {
                    var list = Snapshot().Select(a => new { id = a.Id, title = a.Title, body = a.Body, progress = a.Progress });
                    return ("200 OK", JsonSerializer.Serialize(list));
                }
                if (method == "POST" && path == "/activity")
                {
                    using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body);
                    var r = doc.RootElement;
                    var a = new LiveActivity
                    {
                        Id = Cut(Str(r, "id") ?? "default", 64),
                        Title = Cut(Str(r, "title") ?? "Activité", 80),
                        Body = Cut(Str(r, "body") ?? "", 160),
                    };
                    if (r.TryGetProperty("progress", out var p) && p.ValueKind == JsonValueKind.Number)
                    {
                        double v = p.GetDouble();
                        if (v > 1) v /= 100; // accepte 0..1 ou 0..100
                        a.Progress = Math.Clamp(v, 0, 1);
                    }
                    var glyph = Str(r, "glyph");
                    if (!string.IsNullOrEmpty(glyph)) a.Glyph = Cut(glyph, 2);
                    if (r.TryGetProperty("ttl", out var ttl) && ttl.ValueKind == JsonValueKind.Number) a.Ttl = Math.Clamp(ttl.GetInt32(), 5, 86400);
                    bool done = r.TryGetProperty("done", out var d) && d.ValueKind == JsonValueKind.True;
                    if (done)
                    {
                        Remove(a.Id);
                        Finished?.Invoke(a);
                    }
                    else
                    {
                        int count; lock (_lock) count = _acts.Count;
                        if (count >= 12 && !Snapshot().Any(x => x.Id.Equals(a.Id, StringComparison.OrdinalIgnoreCase)))
                            return ("429 Too Many Requests", "{\"error\":\"trop d'activités\"}");
                        Set(a);
                    }
                    return ("200 OK", "{\"ok\":true}");
                }
                if (method == "DELETE" && path.StartsWith("/activity/"))
                {
                    bool ok = Remove(Uri.UnescapeDataString(path.Substring("/activity/".Length)));
                    return ("200 OK", ok ? "{\"ok\":true}" : "{\"ok\":false}");
                }
                if (method == "POST" && path == "/notify")
                {
                    using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body);
                    Notify?.Invoke(Cut(Str(doc.RootElement, "title") ?? "Malbo", 80), Cut(Str(doc.RootElement, "body") ?? "", 200));
                    return ("200 OK", "{\"ok\":true}");
                }
            }
            catch (JsonException) { return ("400 Bad Request", "{\"error\":\"json\"}"); }
            catch (Exception ex) { App.Log(ex); return ("500 Internal Server Error", "{\"error\":\"interne\"}"); }
            return ("404 Not Found", "{\"error\":\"route\"}");
        }

        static bool FixedEquals(string a, string b) =>
            System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));

        static string Str(JsonElement e, string n) =>
            e.ValueKind == JsonValueKind.Object && e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

        static string Cut(string s, int max) => s.Length <= max ? s : s.Substring(0, max);
    }
}
