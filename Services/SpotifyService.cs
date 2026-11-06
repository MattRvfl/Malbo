using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace DynamicIsland
{
    public class SpotifyPlayer
    {
        public string TrackId, TrackUri, Repeat;
        public bool Shuffle, IsPlaying;
    }

    /// <summary>Spotify Web API (OAuth PKCE, sans secret client).</summary>
    public class SpotifyService
    {
        const string Redirect = "http://127.0.0.1:8898/callback";
        const int Port = 8898;
        const string Api = "https://api.spotify.com/v1";
        const string TokenUrl = "https://accounts.spotify.com/api/token";
        const string Scopes = "user-library-read user-library-modify user-read-playback-state user-modify-playback-state playlist-read-private playlist-read-collaborative playlist-modify-private playlist-modify-public";

        static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };
        static string U(string s) => Uri.EscapeDataString(s ?? "");

        readonly SemaphoreSlim _tokenLock = new(1, 1);
        string _access;
        DateTime _expires = DateTime.MinValue;
        string _meId;

        static AppSettings S => App.Settings;

        public bool Connected => !string.IsNullOrEmpty(S?.SpotifyClientId) && !string.IsNullOrEmpty(S?.SpotifyRefresh);

        static string B64Url(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        public async Task<string> ConnectAsync()
        {
            try
            {
                var clientId = S.SpotifyClientId?.Trim();
                if (string.IsNullOrEmpty(clientId)) return "Renseigne d'abord le Client ID Spotify.";

                var verifier = B64Url(RandomNumberGenerator.GetBytes(48));
                var challenge = B64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
                var state = B64Url(RandomNumberGenerator.GetBytes(16));

                var url = "https://accounts.spotify.com/authorize?response_type=code" +
                          $"&client_id={U(clientId)}&redirect_uri={U(Redirect)}&scope={U(Scopes)}" +
                          $"&code_challenge_method=S256&code_challenge={U(challenge)}&state={U(state)}";

                var code = await LoopbackAuth.WaitForCodeAsync(Port, url, state, "Spotify");
                if (string.IsNullOrEmpty(code)) return "Connexion à Spotify annulée ou expirée.";

                using var form = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "authorization_code",
                    ["code"] = code,
                    ["redirect_uri"] = Redirect,
                    ["client_id"] = clientId,
                    ["code_verifier"] = verifier
                });
                using var resp = await Http.PostAsync(TokenUrl, form);
                var body = await resp.Content.ReadAsStringAsync();
                if (!resp.IsSuccessStatusCode) return "Spotify a refusé l'échange du code : " + ErrorText(body);

                using var j = JsonDocument.Parse(body);
                if (!ApplyToken(j.RootElement)) return "Réponse de Spotify invalide.";
                if (string.IsNullOrEmpty(S.SpotifyRefresh)) return "Spotify n'a pas fourni de jeton de rafraîchissement.";
                _meId = null;
                return null;
            }
            catch (Exception ex)
            {
                App.Log(ex);
                return "Erreur lors de la connexion à Spotify : " + ex.Message;
            }
        }

        static string ErrorText(string body)
        {
            try
            {
                using var j = JsonDocument.Parse(body);
                var r = j.RootElement;
                if (r.TryGetProperty("error_description", out var d) && d.ValueKind == JsonValueKind.String) return d.GetString();
                if (r.TryGetProperty("error", out var e))
                {
                    if (e.ValueKind == JsonValueKind.String) return e.GetString();
                    if (e.ValueKind == JsonValueKind.Object && e.TryGetProperty("message", out var m)) return m.GetString();
                }
            }
            catch { }
            return body?.Length > 200 ? body.Substring(0, 200) : body;
        }

        bool ApplyToken(JsonElement r)
        {
            if (!r.TryGetProperty("access_token", out var at) || at.ValueKind != JsonValueKind.String) return false;
            _access = at.GetString();
            int exp = r.TryGetProperty("expires_in", out var e) && e.ValueKind == JsonValueKind.Number ? e.GetInt32() : 3600;
            _expires = DateTime.UtcNow.AddSeconds(Math.Max(60, exp - 60));
            if (r.TryGetProperty("refresh_token", out var rt) && rt.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(rt.GetString()))
            {
                if (S.SpotifyRefresh != rt.GetString())
                {
                    S.SpotifyRefresh = rt.GetString();
                    S.Save();
                }
            }
            return true;
        }

        async Task<string> TokenAsync(bool force = false)
        {
            if (!Connected) return null;
            if (!force && _access != null && DateTime.UtcNow < _expires) return _access;
            await _tokenLock.WaitAsync();
            try
            {
                if (!force && _access != null && DateTime.UtcNow < _expires) return _access;
                using var form = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "refresh_token",
                    ["refresh_token"] = S.SpotifyRefresh,
                    ["client_id"] = S.SpotifyClientId.Trim()
                });
                using var resp = await Http.PostAsync(TokenUrl, form);
                var body = await resp.Content.ReadAsStringAsync();
                if (!resp.IsSuccessStatusCode)
                {
                    _access = null;
                    // Jeton révoqué : on oublie le refresh pour forcer une reconnexion
                    if (resp.StatusCode == HttpStatusCode.BadRequest && body.Contains("invalid_grant"))
                    {
                        S.SpotifyRefresh = "";
                        S.Save();
                    }
                    return null;
                }
                using var j = JsonDocument.Parse(body);
                return ApplyToken(j.RootElement) ? _access : null;
            }
            finally { _tokenLock.Release(); }
        }

        /// <summary>Appel authentifié. Retourne (code HTTP, corps) ou (0, null) si impossible.</summary>
        async Task<(int Status, string Body)> CallAsync(HttpMethod method, string path, object json = null)
        {
            for (int attempt = 0; attempt < 2; attempt++)
            {
                var token = await TokenAsync(attempt > 0);
                if (token == null) return (0, null);
                using var req = new HttpRequestMessage(method, Api + path);
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                if (json != null)
                    req.Content = new StringContent(JsonSerializer.Serialize(json), Encoding.UTF8, "application/json");
                else if (method == HttpMethod.Put || method == HttpMethod.Post)
                    req.Content = new StringContent("", Encoding.UTF8, "application/json");
                using var resp = await Http.SendAsync(req);
                if (resp.StatusCode == HttpStatusCode.Unauthorized && attempt == 0) continue;
                var body = await resp.Content.ReadAsStringAsync();
                return ((int)resp.StatusCode, body);
            }
            return (0, null);
        }

        static bool Ok(int status) => status >= 200 && status < 300;

        public async Task<SpotifyPlayer> PlayerAsync()
        {
            try
            {
                var (st, body) = await CallAsync(HttpMethod.Get, "/me/player");
                if (st == 204 || !Ok(st) || string.IsNullOrWhiteSpace(body)) return null;
                using var j = JsonDocument.Parse(body);
                var r = j.RootElement;
                var p = new SpotifyPlayer
                {
                    Shuffle = r.TryGetProperty("shuffle_state", out var sh) && sh.ValueKind == JsonValueKind.True,
                    Repeat = r.TryGetProperty("repeat_state", out var rp) && rp.ValueKind == JsonValueKind.String ? rp.GetString() : "off",
                    IsPlaying = r.TryGetProperty("is_playing", out var ip) && ip.ValueKind == JsonValueKind.True
                };
                if (r.TryGetProperty("item", out var item) && item.ValueKind == JsonValueKind.Object)
                {
                    if (item.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String) p.TrackId = id.GetString();
                    if (item.TryGetProperty("uri", out var uri) && uri.ValueKind == JsonValueKind.String) p.TrackUri = uri.GetString();
                }
                return p;
            }
            catch (Exception ex) { App.Log(ex); return null; }
        }

        public async Task<bool?> IsLikedAsync(string trackId)
        {
            try
            {
                if (string.IsNullOrEmpty(trackId)) return null;
                var (st, body) = await CallAsync(HttpMethod.Get, "/me/tracks/contains?ids=" + U(trackId));
                if (!Ok(st) || string.IsNullOrWhiteSpace(body)) return null;
                using var j = JsonDocument.Parse(body);
                if (j.RootElement.ValueKind == JsonValueKind.Array && j.RootElement.GetArrayLength() > 0)
                    return j.RootElement[0].ValueKind == JsonValueKind.True;
                return null;
            }
            catch (Exception ex) { App.Log(ex); return null; }
        }

        public async Task<bool> SetLikedAsync(string trackId, bool liked)
        {
            try
            {
                if (string.IsNullOrEmpty(trackId)) return false;
                var (st, _) = await CallAsync(liked ? HttpMethod.Put : HttpMethod.Delete, "/me/tracks?ids=" + U(trackId));
                return Ok(st);
            }
            catch (Exception ex) { App.Log(ex); return false; }
        }

        public async Task<bool> SetShuffleAsync(bool on)
        {
            try
            {
                var (st, _) = await CallAsync(HttpMethod.Put, "/me/player/shuffle?state=" + (on ? "true" : "false"));
                return Ok(st);
            }
            catch (Exception ex) { App.Log(ex); return false; }
        }

        public async Task<bool> SetRepeatAsync(string state)
        {
            try
            {
                if (state != "off" && state != "context" && state != "track") state = "off";
                var (st, _) = await CallAsync(HttpMethod.Put, "/me/player/repeat?state=" + state);
                return Ok(st);
            }
            catch (Exception ex) { App.Log(ex); return false; }
        }

        public async Task<List<(string Id, string Name)>> PlaylistsAsync()
        {
            try
            {
                if (_meId == null)
                {
                    var (ms, mb) = await CallAsync(HttpMethod.Get, "/me");
                    if (!Ok(ms) || string.IsNullOrWhiteSpace(mb)) return null;
                    using var mj = JsonDocument.Parse(mb);
                    _meId = mj.RootElement.TryGetProperty("id", out var mid) ? mid.GetString() : null;
                }
                var (st, body) = await CallAsync(HttpMethod.Get, "/me/playlists?limit=50");
                if (!Ok(st) || string.IsNullOrWhiteSpace(body)) return null;
                var list = new List<(string Id, string Name)>();
                using var j = JsonDocument.Parse(body);
                if (!j.RootElement.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array) return list;
                foreach (var pl in items.EnumerateArray())
                {
                    if (pl.ValueKind != JsonValueKind.Object) continue;
                    string owner = null;
                    if (pl.TryGetProperty("owner", out var o) && o.ValueKind == JsonValueKind.Object && o.TryGetProperty("id", out var oid))
                        owner = oid.GetString();
                    bool collab = pl.TryGetProperty("collaborative", out var c) && c.ValueKind == JsonValueKind.True;
                    if (!(collab || (owner != null && owner == _meId))) continue;
                    var id = pl.TryGetProperty("id", out var pid) ? pid.GetString() : null;
                    var name = pl.TryGetProperty("name", out var pn) ? pn.GetString() : "";
                    if (!string.IsNullOrEmpty(id)) list.Add((id, name));
                }
                return list;
            }
            catch (Exception ex) { App.Log(ex); return null; }
        }

        public async Task<bool> AddToPlaylistAsync(string playlistId, string trackUri)
        {
            try
            {
                if (string.IsNullOrEmpty(playlistId) || string.IsNullOrEmpty(trackUri)) return false;
                var (st, _) = await CallAsync(HttpMethod.Post, "/playlists/" + U(playlistId) + "/tracks", new { uris = new[] { trackUri } });
                return Ok(st);
            }
            catch (Exception ex) { App.Log(ex); return false; }
        }
    }
}
