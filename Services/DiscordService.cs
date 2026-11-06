using System;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace DynamicIsland
{
    public class VoiceUser : Observable
    {
        string _id, _name, _avatar; bool _speaking, _muted;
        public string Id { get => _id; set { _id = value; Raise(); } }
        public string Name { get => _name; set { _name = value; Raise(); Raise(nameof(Initial)); } }
        public string AvatarUrl { get => _avatar; set { _avatar = value; Raise(); } }
        public string Initial => string.IsNullOrEmpty(_name) ? "?" : _name.Substring(0, 1).ToUpperInvariant();
        public bool Speaking { get => _speaking; set { if (_speaking == value) return; _speaking = value; Raise(); } }
        public bool Muted { get => _muted; set { if (_muted == value) return; _muted = value; Raise(); Raise(nameof(MutedVis)); } }
        public Visibility MutedVis => _muted ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Discord RPC local (named pipe IPC) : salon vocal courant, qui parle, micro coupé.</summary>
    public class DiscordService
    {
        const int OpHandshake = 0, OpFrame = 1, OpClose = 2, OpPing = 3, OpPong = 4;
        const string TokenUrl = "https://discord.com/api/oauth2/token";
        static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };
        static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(120);
        static readonly string[] ChannelEvents = { "VOICE_STATE_CREATE", "VOICE_STATE_UPDATE", "VOICE_STATE_DELETE", "SPEAKING_START", "SPEAKING_STOP" };

        static AppSettings S => App.Settings;

        readonly object _lock = new();
        readonly SemaphoreSlim _writeLock = new(1, 1);
        readonly SemaphoreSlim _refreshLock = new(1, 1);
        readonly ConcurrentDictionary<string, TaskCompletionSource<JsonElement>> _pending = new();
        readonly List<VoiceUser> _users = new();

        NamedPipeClientStream _pipe;
        CancellationTokenSource _cts;
        string _channelId;
        string _selfId;
        int _refreshQueued;
        volatile bool _connected, _busy;

        public bool Connected => _connected;
        public bool Busy => _busy;
        public bool InChannel { get { lock (_lock) return _connected && _channelId != null; } }
        public string ChannelName { get; private set; }
        public bool SelfMuted { get; private set; }
        public List<VoiceUser> Users { get { lock (_lock) return new List<VoiceUser>(_users); } }

        public event Action VoiceChanged;
        public event Action<string, bool> SpeakingChanged;

        // ---------- Connexion ----------
        public async Task<string> ConnectAsync(bool interactive)
        {
            if (_busy) return "Connexion à Discord déjà en cours…";
            if (_connected) return null;
            _busy = true;
            try
            {
                var clientId = S.DiscordClientId?.Trim();
                if (string.IsNullOrEmpty(clientId)) return "Renseigne d'abord le Client ID Discord.";
                if (!interactive && string.IsNullOrEmpty(S.DiscordRefresh)) return "Reconnexion requise : connecte Discord depuis les réglages.";

                var pipe = await OpenPipeAsync();
                if (pipe == null) return "Discord n'est pas lancé (aucun canal IPC trouvé).";

                var cts = new CancellationTokenSource();
                lock (_lock) { _pipe = pipe; _cts = cts; }

                // Handshake + READY
                await WriteFrameAsync(pipe, OpHandshake, new { v = 1, client_id = clientId });
                var ready = await WaitReadyAsync(pipe);
                if (ready != null) { Cleanup(pipe, false); return ready; }

                _ = Task.Run(() => ReadLoopAsync(pipe, cts.Token));

                // Authentification
                string access = null;
                if (!string.IsNullOrEmpty(S.DiscordRefresh))
                {
                    var (tok, err) = await TokenAsync(new Dictionary<string, string>
                    {
                        ["grant_type"] = "refresh_token",
                        ["refresh_token"] = S.DiscordRefresh,
                        ["client_id"] = clientId,
                        ["client_secret"] = S.DiscordSecret ?? ""
                    });
                    access = tok;
                    if (access == null && !interactive)
                    {
                        Cleanup(pipe, false);
                        return "Reconnexion requise : " + err;
                    }
                }
                if (access == null)
                {
                    string code;
                    // Discord refuse désormais le scope générique "rpc" (invalid_scope) : on ne demande que les scopes vocaux.
                    try
                    {
                        code = await AuthorizeAsync(clientId, new[] { "rpc.voice.read", "rpc.voice.write", "identify" });
                    }
                    catch (InvalidOperationException ex) when (ex.Message.Contains("scope"))
                    {
                        code = await AuthorizeAsync(clientId, new[] { "rpc.voice.read", "identify" });
                    }
                    if (string.IsNullOrEmpty(code)) { Cleanup(pipe, false); return "Discord n'a pas renvoyé de code d'autorisation."; }

                    var form = new Dictionary<string, string>
                    {
                        ["grant_type"] = "authorization_code",
                        ["code"] = code,
                        ["client_id"] = clientId,
                        ["client_secret"] = S.DiscordSecret ?? ""
                    };
                    string err = null;
                    if (!string.IsNullOrEmpty(S.DiscordRedirect))
                    {
                        form["redirect_uri"] = S.DiscordRedirect;
                        (access, err) = await TokenAsync(form);
                        form.Remove("redirect_uri");
                    }
                    if (access == null) (access, err) = await TokenAsync(form);
                    if (access == null) { Cleanup(pipe, false); return "Échange du jeton Discord refusé : " + err; }
                }

                var auth = await CommandAsync("AUTHENTICATE", new { access_token = access });
                try { _selfId = auth.GetProperty("user").GetProperty("id").GetString(); } catch { _selfId = null; }

                _connected = true;
                try { await CommandAsync("SUBSCRIBE", new { }, "VOICE_CHANNEL_SELECT"); } catch (Exception ex) { App.Log(ex); }
                await RefreshChannelAsync();
                return null;
            }
            catch (Exception ex)
            {
                App.Log(ex);
                NamedPipeClientStream p; lock (_lock) p = _pipe;
                if (p != null) Cleanup(p, true);
                var msg = ex is TimeoutException ? "Discord n'a pas répondu à temps."
                        : ex.Message.Contains("invalid_scope") ? "Discord réserve la lecture du vocal aux applis qu'il a approuvées. Pas d'inquiétude : Malbo détecte tes appels Discord en local, sans connexion."
                        : ex.Message;
                return "Connexion à Discord impossible : " + msg;
            }
            finally { _busy = false; }
        }

        async Task<string> AuthorizeAsync(string clientId, string[] scopes)
        {
            var data = await CommandAsync("AUTHORIZE", new { client_id = clientId, scopes });
            return data.ValueKind == JsonValueKind.Object && data.TryGetProperty("code", out var c) ? c.GetString() : null;
        }

        async Task<(string Access, string Error)> TokenAsync(Dictionary<string, string> form)
        {
            try
            {
                using var content = new FormUrlEncodedContent(form);
                using var resp = await Http.PostAsync(TokenUrl, content);
                var body = await resp.Content.ReadAsStringAsync();
                using var j = JsonDocument.Parse(body);
                var r = j.RootElement;
                if (!resp.IsSuccessStatusCode || !r.TryGetProperty("access_token", out var at))
                {
                    var err = r.TryGetProperty("error_description", out var d) ? d.GetString()
                            : r.TryGetProperty("error", out var e) ? e.ToString() : ((int)resp.StatusCode).ToString();
                    if (form.TryGetValue("grant_type", out var gt) && gt == "refresh_token" && body.Contains("invalid_grant"))
                    {
                        S.DiscordRefresh = "";
                        S.Save();
                    }
                    return (null, err);
                }
                if (r.TryGetProperty("refresh_token", out var rt) && rt.ValueKind == JsonValueKind.String)
                {
                    S.DiscordRefresh = rt.GetString();
                    S.Save();
                }
                return (at.GetString(), null);
            }
            catch (Exception ex) { App.Log(ex); return (null, ex.Message); }
        }

        static async Task<NamedPipeClientStream> OpenPipeAsync()
        {
            for (int i = 0; i < 10; i++)
            {
                var p = new NamedPipeClientStream(".", "discord-ipc-" + i, PipeDirection.InOut, PipeOptions.Asynchronous);
                try
                {
                    using var t = new CancellationTokenSource(400);
                    await p.ConnectAsync(t.Token);
                    return p;
                }
                catch { try { p.Dispose(); } catch { } }
            }
            return null;
        }

        /// <summary>Attend le READY après le handshake. Retourne null si OK, sinon un message.</summary>
        async Task<string> WaitReadyAsync(NamedPipeClientStream pipe)
        {
            using var t = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            while (true)
            {
                var (op, json) = await ReadFrameAsync(pipe, t.Token);
                if (op == OpPing) { await WriteRawAsync(pipe, OpPong, json); continue; }
                using var doc = JsonDocument.Parse(json);
                var r = doc.RootElement;
                if (op == OpClose)
                {
                    var m = r.TryGetProperty("message", out var mm) ? mm.GetString() : "connexion fermée";
                    return "Discord a refusé la connexion : " + m;
                }
                if (op == OpFrame && r.TryGetProperty("evt", out var evt) && evt.ValueKind == JsonValueKind.String)
                {
                    if (evt.GetString() == "READY") return null;
                    if (evt.GetString() == "ERROR")
                        return "Discord a refusé la connexion : " + ErrorMessage(r);
                }
            }
        }

        static string ErrorMessage(JsonElement r)
        {
            try
            {
                if (r.TryGetProperty("data", out var d) && d.ValueKind == JsonValueKind.Object && d.TryGetProperty("message", out var m))
                    return m.GetString();
            }
            catch { }
            return "erreur inconnue";
        }

        // ---------- Trames ----------
        static async Task<(int Op, byte[] Json)> ReadFrameAsync(Stream s, CancellationToken ct)
        {
            var head = new byte[8];
            await s.ReadExactlyAsync(head, ct);
            int op = BinaryPrimitives.ReadInt32LittleEndian(head.AsSpan(0, 4));
            int len = BinaryPrimitives.ReadInt32LittleEndian(head.AsSpan(4, 4));
            if (len < 0 || len > 16 * 1024 * 1024) throw new IOException("Trame Discord invalide.");
            var body = new byte[len];
            if (len > 0) await s.ReadExactlyAsync(body, ct);
            return (op, body);
        }

        Task WriteFrameAsync(Stream s, int op, object payload) =>
            WriteRawAsync(s, op, Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload)));

        async Task WriteRawAsync(Stream s, int op, byte[] json)
        {
            var buf = new byte[8 + json.Length];
            BinaryPrimitives.WriteInt32LittleEndian(buf.AsSpan(0, 4), op);
            BinaryPrimitives.WriteInt32LittleEndian(buf.AsSpan(4, 4), json.Length);
            json.CopyTo(buf, 8);
            await _writeLock.WaitAsync();
            try
            {
                await s.WriteAsync(buf, 0, buf.Length);
                await s.FlushAsync();
            }
            finally { _writeLock.Release(); }
        }

        async Task<JsonElement> CommandAsync(string cmd, object args, string evt = null)
        {
            NamedPipeClientStream pipe; lock (_lock) pipe = _pipe;
            if (pipe == null) throw new IOException("Discord n'est pas connecté.");
            var nonce = Guid.NewGuid().ToString();
            var tcs = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pending[nonce] = tcs;
            try
            {
                var payload = new Dictionary<string, object> { ["cmd"] = cmd, ["args"] = args, ["nonce"] = nonce };
                if (evt != null) payload["evt"] = evt;
                await WriteFrameAsync(pipe, OpFrame, payload);
                var done = await Task.WhenAny(tcs.Task, Task.Delay(CommandTimeout));
                if (done != tcs.Task) throw new TimeoutException("Délai dépassé pour " + cmd);
                return await tcs.Task;
            }
            finally { _pending.TryRemove(nonce, out _); }
        }

        async Task ReadLoopAsync(NamedPipeClientStream pipe, CancellationToken ct)
        {
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    var (op, json) = await ReadFrameAsync(pipe, ct);
                    if (op == OpPing) { await WriteRawAsync(pipe, OpPong, json); continue; }
                    if (op == OpClose) break;
                    if (op != OpFrame) continue;

                    JsonElement root;
                    using (var doc = JsonDocument.Parse(json)) root = doc.RootElement.Clone();

                    string nonce = root.TryGetProperty("nonce", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() : null;
                    string cmd = root.TryGetProperty("cmd", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString() : null;
                    string evt = root.TryGetProperty("evt", out var e) && e.ValueKind == JsonValueKind.String ? e.GetString() : null;
                    var data = root.TryGetProperty("data", out var d) ? d : default;

                    if (nonce != null && _pending.TryRemove(nonce, out var tcs))
                    {
                        if (evt == "ERROR") tcs.TrySetException(new InvalidOperationException(ErrorMessage(root)));
                        else tcs.TrySetResult(data);
                        continue;
                    }
                    if (cmd == "DISPATCH" && evt != null)
                    {
                        try { HandleDispatch(evt, data); } catch (Exception ex) { App.Log(ex); }
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) when (ex is IOException || ex is ObjectDisposedException || ex is EndOfStreamException) { }
            catch (Exception ex) { App.Log(ex); }
            Cleanup(pipe, true);
        }

        void HandleDispatch(string evt, JsonElement data)
        {
            switch (evt)
            {
                case "VOICE_CHANNEL_SELECT":
                case "VOICE_STATE_CREATE":
                case "VOICE_STATE_UPDATE":
                case "VOICE_STATE_DELETE":
                    QueueRefresh();
                    break;
                case "SPEAKING_START":
                case "SPEAKING_STOP":
                    if (data.ValueKind != JsonValueKind.Object || !data.TryGetProperty("user_id", out var uid)) return;
                    var id = uid.ValueKind == JsonValueKind.String ? uid.GetString() : uid.ToString();
                    bool on = evt == "SPEAKING_START";
                    VoiceUser u; lock (_lock) u = _users.FirstOrDefault(x => x.Id == id);
                    if (u != null) u.Speaking = on;
                    SpeakingChanged?.Invoke(id, on);
                    break;
            }
        }

        void QueueRefresh()
        {
            if (Interlocked.Exchange(ref _refreshQueued, 1) == 1) return;
            _ = Task.Run(async () =>
            {
                await Task.Delay(80);
                Interlocked.Exchange(ref _refreshQueued, 0);
                await RefreshChannelAsync();
            });
        }

        // ---------- Salon vocal ----------
        async Task RefreshChannelAsync()
        {
            if (!_connected) return;
            await _refreshLock.WaitAsync();
            try
            {
                var data = await CommandAsync("GET_SELECTED_VOICE_CHANNEL", new { });
                string old; lock (_lock) old = _channelId;

                if (data.ValueKind != JsonValueKind.Object)
                {
                    if (old != null) await SubscribeChannelAsync("UNSUBSCRIBE", old);
                    lock (_lock) { _channelId = null; _users.Clear(); }
                    ChannelName = null;
                    VoiceChanged?.Invoke();
                    return;
                }

                var id = data.TryGetProperty("id", out var cid) ? cid.GetString() : null;
                if (id != old)
                {
                    if (old != null) await SubscribeChannelAsync("UNSUBSCRIBE", old);
                    if (id != null) await SubscribeChannelAsync("SUBSCRIBE", id);
                    lock (_lock) { _channelId = id; _users.Clear(); }
                }
                ChannelName = data.TryGetProperty("name", out var nm) && nm.ValueKind == JsonValueKind.String ? nm.GetString() : "";

                bool? selfMuted = null;
                var fresh = new List<VoiceUser>();
                if (data.TryGetProperty("voice_states", out var states) && states.ValueKind == JsonValueKind.Array)
                {
                    List<VoiceUser> known; lock (_lock) known = new List<VoiceUser>(_users);
                    foreach (var vs in states.EnumerateArray())
                    {
                        if (!vs.TryGetProperty("user", out var user) || user.ValueKind != JsonValueKind.Object) continue;
                        var uid = Str(user, "id");
                        if (uid == null) continue;
                        var name = Str(vs, "nick");
                        if (string.IsNullOrEmpty(name)) name = Str(user, "global_name");
                        if (string.IsNullOrEmpty(name)) name = Str(user, "username") ?? "?";
                        var avatar = Str(user, "avatar");
                        bool muted = false;
                        if (vs.TryGetProperty("voice_state", out var st) && st.ValueKind == JsonValueKind.Object)
                            muted = Bool(st, "self_mute") || Bool(st, "mute");

                        var u = known.FirstOrDefault(x => x.Id == uid) ?? new VoiceUser { Id = uid };
                        if (u.Name != name) u.Name = name;
                        var url = avatar != null ? $"https://cdn.discordapp.com/avatars/{uid}/{avatar}.png?size=64" : null;
                        if (u.AvatarUrl != url) u.AvatarUrl = url;
                        u.Muted = muted;
                        fresh.Add(u);
                        if (uid == _selfId) selfMuted = muted;
                    }
                }
                lock (_lock) { _users.Clear(); _users.AddRange(fresh); }

                if (selfMuted == null)
                {
                    try
                    {
                        var vset = await CommandAsync("GET_VOICE_SETTINGS", new { });
                        selfMuted = Bool(vset, "mute");
                    }
                    catch { }
                }
                if (selfMuted.HasValue) SelfMuted = selfMuted.Value;
                VoiceChanged?.Invoke();
            }
            catch (Exception ex) when (ex is IOException || ex is ObjectDisposedException) { }
            catch (Exception ex) { App.Log(ex); }
            finally { _refreshLock.Release(); }
        }

        async Task SubscribeChannelAsync(string cmd, string channelId)
        {
            foreach (var evt in ChannelEvents)
            {
                try { await CommandAsync(cmd, new { channel_id = channelId }, evt); }
                catch (Exception ex) when (ex is IOException || ex is ObjectDisposedException) { throw; }
                catch { }
            }
        }

        static string Str(JsonElement e, string name) =>
            e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

        static bool Bool(JsonElement e, string name) =>
            e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

        // ---------- Actions ----------
        public async Task ToggleMuteAsync()
        {
            try
            {
                if (!_connected) return;
                var vs = await CommandAsync("GET_VOICE_SETTINGS", new { });
                bool mute = !Bool(vs, "mute");
                var res = await CommandAsync("SET_VOICE_SETTINGS", new { mute });
                SelfMuted = res.ValueKind == JsonValueKind.Object && res.TryGetProperty("mute", out _) ? Bool(res, "mute") : mute;
                if (_selfId != null)
                {
                    VoiceUser me; lock (_lock) me = _users.FirstOrDefault(x => x.Id == _selfId);
                    if (me != null) me.Muted = SelfMuted;
                }
                VoiceChanged?.Invoke();
            }
            catch (Exception ex) when (ex is IOException || ex is ObjectDisposedException) { }
            catch (Exception ex) { App.Log(ex); }
        }

        public void Disconnect()
        {
            NamedPipeClientStream p; lock (_lock) p = _pipe;
            if (p != null) Cleanup(p, true);
        }

        /// <summary>Ferme le pipe (s'il est toujours le pipe courant), vide l'état et fait échouer les commandes en attente.</summary>
        void Cleanup(NamedPipeClientStream pipe, bool raise)
        {
            CancellationTokenSource cts;
            lock (_lock)
            {
                if (_pipe != pipe) { try { pipe.Dispose(); } catch { } return; }
                cts = _cts;
                _pipe = null; _cts = null;
                _channelId = null;
                _users.Clear();
            }
            bool was = _connected;
            _connected = false;
            ChannelName = null;
            SelfMuted = false;
            try { cts?.Cancel(); } catch { }
            try { pipe.Dispose(); } catch { }
            try { cts?.Dispose(); } catch { }
            foreach (var key in _pending.Keys.ToList())
                if (_pending.TryRemove(key, out var tcs)) tcs.TrySetException(new IOException("Connexion à Discord perdue."));
            if (raise || was)
            {
                try { VoiceChanged?.Invoke(); } catch (Exception ex) { App.Log(ex); }
            }
        }
    }
}
