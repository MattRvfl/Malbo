using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace DynamicIsland
{
    /// <summary>
    /// Calculatrice de l'onglet « Lancer » : expressions mathématiques, conversions d'unités,
    /// devises (open.er-api.com) et pourcentages (« 15 % de 80 »).
    /// </summary>
    public static class Calc
    {
        static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");
        static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(6) };
        static readonly Dictionary<string, (DateTime At, Dictionary<string, double> Rates)> RateCache = new();
        static readonly object CacheLock = new();

        const string Sep = @"(?:en|to|in|->|=)";
        static readonly Regex ConvRx = new(@"^\s*([-\d.,]+)\s*(\S+?)\s+" + Sep + @"\s+(\S+)\s*$", RegexOptions.IgnoreCase);
        static readonly Regex ConvSymRx = new(@"^\s*([€$£¥])\s*([-\d.,]+)\s+" + Sep + @"\s+(\S+)\s*$", RegexOptions.IgnoreCase);
        static readonly Regex PercentRx = new(@"^\s*([-\d.,]+)\s*%\s*(?:de|of|d')\s*(.+)$", RegexOptions.IgnoreCase);

        // Unités : catégorie + facteur vers l'unité de base (m, kg, l, m/s, octet, seconde).
        static readonly Dictionary<string, (string Cat, double F)> Units = new(StringComparer.OrdinalIgnoreCase)
        {
            // longueur
            ["m"] = ("len", 1), ["km"] = ("len", 1000), ["cm"] = ("len", 0.01), ["mm"] = ("len", 0.001),
            ["mi"] = ("len", 1609.344), ["mile"] = ("len", 1609.344), ["miles"] = ("len", 1609.344),
            ["ft"] = ("len", 0.3048), ["pied"] = ("len", 0.3048), ["pieds"] = ("len", 0.3048),
            ["in"] = ("len", 0.0254), ["pouce"] = ("len", 0.0254), ["pouces"] = ("len", 0.0254),
            ["yd"] = ("len", 0.9144),
            // masse
            ["kg"] = ("mass", 1), ["g"] = ("mass", 0.001), ["mg"] = ("mass", 1e-6), ["t"] = ("mass", 1000),
            ["lb"] = ("mass", 0.45359237), ["lbs"] = ("mass", 0.45359237), ["livre"] = ("mass", 0.45359237), ["livres"] = ("mass", 0.45359237),
            ["oz"] = ("mass", 0.028349523125),
            // volume
            ["l"] = ("vol", 1), ["ml"] = ("vol", 0.001), ["cl"] = ("vol", 0.01), ["dl"] = ("vol", 0.1),
            ["gal"] = ("vol", 3.785411784), ["m3"] = ("vol", 1000), ["m³"] = ("vol", 1000),
            // vitesse
            ["km/h"] = ("speed", 1 / 3.6), ["kmh"] = ("speed", 1 / 3.6), ["mph"] = ("speed", 0.44704),
            ["m/s"] = ("speed", 1), ["nd"] = ("speed", 0.514444), ["noeud"] = ("speed", 0.514444), ["noeuds"] = ("speed", 0.514444),
            // données (base 1024)
            ["o"] = ("data", 1), ["b"] = ("data", 1),
            ["ko"] = ("data", 1024), ["kb"] = ("data", 1024),
            ["mo"] = ("data", 1048576), ["mb"] = ("data", 1048576),
            ["go"] = ("data", 1073741824), ["gb"] = ("data", 1073741824),
            ["to"] = ("data", 1099511627776), ["tb"] = ("data", 1099511627776),
            // temps
            ["s"] = ("time", 1), ["sec"] = ("time", 1), ["min"] = ("time", 60), ["h"] = ("time", 3600),
            ["j"] = ("time", 86400), ["jour"] = ("time", 86400), ["jours"] = ("time", 86400),
            ["sem"] = ("time", 604800), ["an"] = ("time", 31557600),
            // température (traitée à part)
            ["c"] = ("temp", 0), ["f"] = ("temp", 0), ["k"] = ("temp", 0),
        };

        static readonly Dictionary<string, string> CurrencySymbols = new()
        {
            ["€"] = "EUR", ["$"] = "USD", ["£"] = "GBP", ["¥"] = "JPY",
        };

        public static async Task<string> EvaluateAsync(string input)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(input)) return null;
                input = input.Trim();

                // 1) conversions
                var m = ConvRx.Match(input);
                if (m.Success)
                {
                    var r = await ConvertAsync(m.Groups[1].Value, m.Groups[2].Value, m.Groups[3].Value);
                    if (r != null) return r;
                }
                m = ConvSymRx.Match(input);
                if (m.Success)
                {
                    var r = await ConvertAsync(m.Groups[2].Value, m.Groups[1].Value, m.Groups[3].Value);
                    if (r != null) return r;
                }

                // 2) pourcentage
                m = PercentRx.Match(input);
                if (m.Success && TryNum(m.Groups[1].Value, out double pct))
                {
                    double baseVal = new Parser(m.Groups[2].Value).Run();
                    return Fmt(pct / 100.0 * baseVal);
                }

                // 3) expression
                double v = new Parser(input).Run();
                return Fmt(v);
            }
            catch { return null; }
        }

        // ---------- conversions ----------

        static async Task<string> ConvertAsync(string numText, string fromRaw, string toRaw)
        {
            if (!TryNum(numText, out double v)) return null;
            string from = CleanUnit(fromRaw), to = CleanUnit(toRaw);

            if (Units.TryGetValue(from, out var a) && Units.TryGetValue(to, out var b) && a.Cat == b.Cat)
            {
                double res;
                if (a.Cat == "temp") res = FromKelvin(ToKelvin(v, from), to);
                else res = v * a.F / b.F;
                string label = a.Cat == "temp" ? "°" + to.ToUpperInvariant() : toRaw.Trim();
                if (a.Cat == "temp" && to == "k") label = "K";
                return FmtConv(res) + " " + label;
            }

            string cf = Currency(fromRaw), ct = Currency(toRaw);
            if (cf == null || ct == null) return null;
            if (cf == ct) return FmtMoney(v) + " " + ct;
            var rates = await RatesAsync(cf);
            if (rates == null || !rates.TryGetValue(ct, out double rate)) return null;
            return FmtMoney(v * rate) + " " + ct;
        }

        static string CleanUnit(string u)
        {
            u = (u ?? "").Trim().ToLowerInvariant().Replace("°", "");
            return u;
        }

        static string Currency(string raw)
        {
            raw = (raw ?? "").Trim();
            if (CurrencySymbols.TryGetValue(raw, out var code)) return code;
            if (raw.Length == 3 && char.IsLetter(raw[0]) && char.IsLetter(raw[1]) && char.IsLetter(raw[2]))
                return raw.ToUpperInvariant();
            return null;
        }

        static double ToKelvin(double v, string u) => u switch
        {
            "c" => v + 273.15,
            "f" => (v - 32) * 5 / 9 + 273.15,
            _ => v,
        };

        static double FromKelvin(double k, string u) => u switch
        {
            "c" => k - 273.15,
            "f" => (k - 273.15) * 9 / 5 + 32,
            _ => k,
        };

        static async Task<Dictionary<string, double>> RatesAsync(string from)
        {
            lock (CacheLock)
            {
                if (RateCache.TryGetValue(from, out var c) && DateTime.UtcNow - c.At < TimeSpan.FromHours(1)) return c.Rates;
            }
            try
            {
                var json = await Http.GetStringAsync("https://open.er-api.com/v6/latest/" + Uri.EscapeDataString(from));
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                if (root.TryGetProperty("result", out var res) && res.GetString() != "success") return null;
                if (!root.TryGetProperty("rates", out var ratesEl)) return null;
                var rates = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                foreach (var p in ratesEl.EnumerateObject())
                    if (p.Value.ValueKind == JsonValueKind.Number) rates[p.Name] = p.Value.GetDouble();
                lock (CacheLock) RateCache[from] = (DateTime.UtcNow, rates);
                return rates;
            }
            catch { return null; }
        }

        // ---------- formatage ----------

        static double Clean(double v)
        {
            if (double.IsNaN(v) || double.IsInfinity(v)) throw new ArithmeticException();
            if (Math.Abs(v) < 1e-12) return 0;
            return v;
        }

        static string Fmt(double v) => Clean(v).ToString("#,##0.######", Fr);

        static string FmtConv(double v)
        {
            v = Clean(v);
            return v.ToString(Math.Abs(v) >= 1 ? "#,##0.##" : "#,##0.######", Fr);
        }

        static string FmtMoney(double v) => Clean(v).ToString("#,##0.##", Fr);

        static bool TryNum(string s, out double v)
        {
            s = (s ?? "").Trim();
            int ci = s.IndexOf(','), di = s.IndexOf('.');
            if (ci >= 0 && di >= 0)
            {
                // les deux présents : le premier est le séparateur de milliers
                if (ci < di) s = s.Replace(",", "");
                else s = s.Replace(".", "").Replace(",", ".");
            }
            else s = s.Replace(',', '.');
            return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v);
        }

        // ---------- analyseur à descente récursive ----------

        sealed class Parser
        {
            static readonly string[] Names =
            {
                "racine", "sqrt", "round", "sin", "cos", "tan", "log", "abs", "ln", "pi", "π", "e",
            };

            readonly string _s;
            int _i;

            public Parser(string s)
            {
                s = (s ?? "").ToLowerInvariant()
                    .Replace('×', '*').Replace('÷', '/').Replace('−', '-')
                    .Replace(',', '.');
                _s = s;
            }

            public double Run()
            {
                Skip();
                if (_i >= _s.Length) throw new FormatException();
                double v = Expr();
                Skip();
                if (_i < _s.Length) throw new FormatException();
                return v;
            }

            void Skip() { while (_i < _s.Length && char.IsWhiteSpace(_s[_i])) _i++; }

            char Peek() { Skip(); return _i < _s.Length ? _s[_i] : '\0'; }

            double Expr()
            {
                double v = Term();
                while (true)
                {
                    char c = Peek();
                    if (c == '+') { _i++; v += Term(); }
                    else if (c == '-') { _i++; v -= Term(); }
                    else return v;
                }
            }

            double Term()
            {
                double v = Unary();
                while (true)
                {
                    char c = Peek();
                    if (c == '*') { _i++; v *= Unary(); }
                    else if (c == '/') { _i++; v /= Unary(); }
                    else if (c == '%')
                    {
                        _i++;
                        char n = Peek();
                        // « 50% » seul (ou suivi d'un opérateur) = pourcentage, sinon modulo
                        if (n == '\0' || n == ')' || n == '+' || n == '-' || n == '*' || n == '/' || n == '^') v /= 100.0;
                        else v %= Unary();
                    }
                    else if (char.IsDigit(c) || c == '.' || c == '(' || char.IsLetter(c)) v *= Power(); // multiplication implicite
                    else return v;
                }
            }

            double Unary()
            {
                char c = Peek();
                if (c == '-') { _i++; return -Unary(); }
                if (c == '+') { _i++; return Unary(); }
                return Power();
            }

            double Power()
            {
                double b = Primary();
                if (Peek() == '^') { _i++; return Math.Pow(b, Unary()); }
                return b;
            }

            double Primary()
            {
                char c = Peek();
                if (c == '(')
                {
                    _i++;
                    double v = Expr();
                    if (Peek() != ')') throw new FormatException();
                    _i++;
                    return v;
                }
                if (char.IsDigit(c) || c == '.')
                {
                    int start = _i;
                    while (_i < _s.Length && (char.IsDigit(_s[_i]) || _s[_i] == '.')) _i++;
                    return double.Parse(_s.Substring(start, _i - start), NumberStyles.Float, CultureInfo.InvariantCulture);
                }
                if (char.IsLetter(c))
                {
                    foreach (var name in Names)
                    {
                        if (string.CompareOrdinal(_s, _i, name, 0, name.Length) != 0) continue;
                        _i += name.Length;
                        switch (name)
                        {
                            case "pi": case "π": return Math.PI;
                            case "e": return Math.E;
                        }
                        // argument = élément primaire ; un exposant éventuel s'applique au résultat (sin(30)^2)
                        bool neg = false;
                        if (Peek() == '-') { _i++; neg = true; }
                        double a = Primary();
                        if (neg) a = -a;
                        const double Deg = Math.PI / 180.0;
                        return name switch
                        {
                            "sqrt" or "racine" => Math.Sqrt(a),
                            "sin" => Math.Sin(a * Deg),
                            "cos" => Math.Cos(a * Deg),
                            "tan" => Math.Tan(a * Deg),
                            "log" => Math.Log10(a),
                            "ln" => Math.Log(a),
                            "abs" => Math.Abs(a),
                            "round" => Math.Round(a, MidpointRounding.AwayFromZero),
                            _ => throw new FormatException(),
                        };
                    }
                }
                throw new FormatException();
            }
        }
    }
}
