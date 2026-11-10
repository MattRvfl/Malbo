using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace DynamicIsland
{
    /// <summary>
    /// Traduction des paroles. Utilise le point d'accès public (non officiel) de Google Traduction :
    /// gratuit et sans clé, mais sans garantie — s'il ne répond pas, les paroles restent simplement non traduites.
    /// Désactivé par défaut : le texte des paroles est envoyé à Google.
    /// </summary>
    public class TranslateService
    {
        static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };
        readonly Dictionary<string, string[]> _cache = new();

        /// <summary>Traduit chaque ligne vers <paramref name="lang"/> (ex. "fr"). Renvoie null en cas d'échec, ou si le texte est déjà dans cette langue.</summary>
        public async Task<string[]> LinesAsync(string key, IReadOnlyList<string> lines, string lang)
        {
            if (lines == null || lines.Count == 0) return null;
            key = lang + "|" + key;
            if (_cache.TryGetValue(key, out var hit)) return hit;
            try
            {
                var res = new string[lines.Count];
                string detected = null;
                // par paquets : l'URL est limitée en taille
                int i = 0;
                while (i < lines.Count)
                {
                    var sb = new StringBuilder();
                    int start = i;
                    while (i < lines.Count && sb.Length + lines[i].Length < 1400)
                    {
                        if (i > start) sb.Append('\n');
                        sb.Append((lines[i] ?? "").Replace('\n', ' '));
                        i++;
                    }
                    if (i == start) { i++; continue; } // ligne démesurée : ignorée
                    var url = "https://translate.googleapis.com/translate_a/single?client=gtx&sl=auto&tl=" + Uri.EscapeDataString(lang)
                            + "&dt=t&q=" + Uri.EscapeDataString(sb.ToString());
                    using var doc = JsonDocument.Parse(await Http.GetStringAsync(url));
                    var root = doc.RootElement;
                    if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() == 0 || root[0].ValueKind != JsonValueKind.Array) return null;
                    if (detected == null && root.GetArrayLength() > 2 && root[2].ValueKind == JsonValueKind.String) detected = root[2].GetString();
                    var full = new StringBuilder();
                    foreach (var seg in root[0].EnumerateArray())
                        if (seg.ValueKind == JsonValueKind.Array && seg.GetArrayLength() > 0 && seg[0].ValueKind == JsonValueKind.String) full.Append(seg[0].GetString());
                    var parts = full.ToString().Split('\n');
                    for (int k = 0; k < parts.Length && start + k < i; k++) res[start + k] = parts[k].Trim();
                }
                if (detected != null && detected.StartsWith(lang, StringComparison.OrdinalIgnoreCase)) res = null; // déjà dans la bonne langue
                if (_cache.Count > 30) _cache.Clear();
                _cache[key] = res;
                return res;
            }
            catch { return null; }
        }
    }
}
