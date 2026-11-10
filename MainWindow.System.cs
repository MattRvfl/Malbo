using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace DynamicIsland
{
    public partial class MainWindow
    {
        class Stat { public TextBlock Val, Sub; public ProgressBar Bar; }
        Stat _cpu, _ram, _gpu, _temp, _net, _bat, _wifi, _priv;
        bool? _lastCharging;
        int _lastBatWarn = 101;
        string _lastBtNames = null;

        void InitSystem()
        {
            _cpu = MakeStat("\uE610", "Processeur", true);
            _ram = MakeStat("\uE9C4", "Mémoire", true);
            _gpu = MakeStat("\uE612", "GPU", true);
            _temp = MakeStat("\uE5C6", "Température", false);
            _net = MakeStat("\uE288", "Réseau", false);
            _bat = MakeStat("\uE0C2", "Batterie", true);
            _wifi = MakeStat("\uE4EA", "Wi-Fi", true);
            _priv = MakeStat("\uE326", "Micro / Caméra", false);
            _sys.BrightnessChanged += b => Dispatcher.InvokeAsync(() =>
            {
                if (!S.BrightnessOsd) return;
                ShowPeek(new PeekData { Key = "osd", Glyph = "\uE472", Title = $"Luminosité  {b} %", Progress = b / 100.0, Duration = TimeSpan.FromSeconds(1.6), Width = 300, Height = 48 });
            });
            _sys.Start();
        }

        Stat MakeStat(string glyph, string label, bool bar)
        {
            var card = new Border { Style = (Style)FindResource("Card"), Padding = new Thickness(11, 9, 11, 9) };
            var sp = new StackPanel();
            var head = new StackPanel { Orientation = Orientation.Horizontal };
            var g = new TextBlock { Text = glyph, FontFamily = (FontFamily)FindResource("Icons"), FontSize = 11, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
            g.SetResourceReference(TextBlock.ForegroundProperty, "Accent");
            var l = new TextBlock { Text = label, FontSize = 11 };
            l.SetResourceReference(TextBlock.ForegroundProperty, "SubFg");
            head.Children.Add(g); head.Children.Add(l);
            var val = new TextBlock { Text = "—", FontSize = 19, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 3, 0, 2), TextTrimming = TextTrimming.CharacterEllipsis };
            val.SetResourceReference(TextBlock.ForegroundProperty, "Fg");
            var sub = new TextBlock { FontSize = 10.5, TextTrimming = TextTrimming.CharacterEllipsis };
            sub.SetResourceReference(TextBlock.ForegroundProperty, "SubFg");
            sp.Children.Add(head); sp.Children.Add(val);
            ProgressBar pb = null;
            if (bar) { pb = new ProgressBar { Style = (Style)FindResource("Thin"), Maximum = 100, Height = 4, Margin = new Thickness(0, 1, 0, 4) }; sp.Children.Add(pb); }
            sp.Children.Add(sub);
            card.Child = sp;
            StatsGrid.Children.Add(card);
            return new Stat { Val = val, Sub = sub, Bar = pb };
        }

        static string Speed(double kbs) => kbs >= 1024 ? $"{kbs / 1024:0.0} Mo/s" : $"{kbs:0} Ko/s";

        void UpdateSystemUI()
        {
            var s = _sys.Latest;
            _cpu.Val.Text = $"{s.Cpu:0} %"; _cpu.Bar.Value = s.Cpu; _cpu.Sub.Text = $"{Environment.ProcessorCount} threads";
            _ram.Val.Text = $"{s.Ram:0} %"; _ram.Bar.Value = s.Ram; _ram.Sub.Text = $"{s.RamUsed / 1073741824.0:0.0} / {s.RamTotal / 1073741824.0:0} Go";
            if (s.Gpu >= 0) { _gpu.Val.Text = $"{s.Gpu:0} %"; _gpu.Bar.Value = s.Gpu; _gpu.Sub.Text = "Moteur 3D"; }
            else { _gpu.Val.Text = "—"; _gpu.Bar.Value = 0; _gpu.Sub.Text = "Indisponible"; }
            _temp.Val.Text = s.Temp > 0 ? $"{s.Temp:0} °C" : "—";
            _temp.Sub.Text = (s.Temp > 0 ? "" : "Capteur non exposé · ") + $"Allumé depuis {(int)s.Uptime.TotalHours} h {s.Uptime.Minutes:00}";
            _net.Val.Text = "↓ " + Speed(s.Down);
            _net.Sub.Text = "↑ " + Speed(s.Up);
            if (s.HasBattery)
            {
                _bat.Val.Text = $"{s.Battery} %" ;
                _bat.Bar.Value = s.Battery;
                _bat.Sub.Text = s.Charging ? "En charge" : s.BatteryMinutes > 0 ? $"≈ {s.BatteryMinutes / 60} h {s.BatteryMinutes % 60:00} restantes" : "Sur batterie";
            }
            else { _bat.Val.Text = "Secteur"; _bat.Bar.Value = 100; _bat.Sub.Text = "PC fixe, pas de batterie"; }
            _wifi.Val.Text = s.Wifi;
            _wifi.Bar.Value = Math.Max(0, s.WifiSignal);
            _wifi.Sub.Text = s.WifiSignal >= 0 ? $"Signal {s.WifiSignal} %" : "";
            _priv.Val.Text = s.Mic || s.Cam ? (s.Mic && s.Cam ? "Les deux" : s.Mic ? "Micro" : "Caméra") : "Inactifs";
            _priv.Sub.Text = s.Mic || s.Cam ? "par " + string.Join(", ", new[] { s.Mic ? s.MicApp : null, s.Cam ? s.CamApp : null }.Where(x => x != null).Distinct()) : "Aucune appli ne les utilise";

            DiskList.ItemsSource = s.Disks;
            BtList.ItemsSource = s.Bluetooth;
            BtEmpty.Visibility = s.Bluetooth.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            var dl = DownloadRows();
            DlList.ItemsSource = dl;
            DlEmpty.Visibility = dl.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        readonly HashSet<string> _alerted = new();
        int _hotSeconds;

        /// <summary>Alertes utiles, une seule fois par problème : disque presque plein, surchauffe, batterie d'un appareil Bluetooth.</summary>
        void SystemAlerts(SysStats s)
        {
            foreach (var d in s.Disks)
            {
                // Sub = « 97 % utilisé sur 476 Go »
                var m = System.Text.RegularExpressions.Regex.Match(d.Sub ?? "", @"^(\d+)\s*%");
                if (!m.Success || !int.TryParse(m.Groups[1].Value, out int used)) continue;
                var key = "disk:" + d.Left;
                if (used >= 95) { if (_alerted.Add(key)) Alert("Disque presque plein", $"{d.Left.Trim()} : {d.Right}", "\uE2A0"); }
                else if (used < 92) _alerted.Remove(key);
            }

            if (s.Temp >= 90) { if (++_hotSeconds == 20 && _alerted.Add("temp")) Alert("Température élevée", $"{s.Temp:0} °C depuis 20 s", "\uE5C6"); }
            else { _hotSeconds = 0; if (s.Temp < 80) _alerted.Remove("temp"); }

            foreach (var b in s.Bluetooth)
            {
                var m = System.Text.RegularExpressions.Regex.Match(b.Right ?? "", @"^(\d+)\s*%");
                if (!m.Success || !int.TryParse(m.Groups[1].Value, out int lvl)) continue;
                var key = "bt:" + b.Left;
                if (lvl <= 15) { if (_alerted.Add(key)) Alert("Batterie faible", $"{b.Left} : {lvl} %", "\uE0DA"); }
                else if (lvl > 25) _alerted.Remove(key);
            }
        }

        /// <summary>Événements système à pousser dans l'île (chargeur, batterie faible, Bluetooth).</summary>
        void SystemSecond()
        {
            var s = _sys.Latest;
            if (s.HasBattery && s.Battery >= 0)
            {
                if (_lastCharging.HasValue && _lastCharging != s.Charging)
                    ShowPeek(new PeekData { Key = "alert", Glyph = s.Charging ? "\uE0BA" : "\uE0C2", Title = s.Charging ? "En charge" : "Sur batterie", Body = $"{s.Battery} %" + (!s.Charging && s.BatteryMinutes > 0 ? $" · ≈ {s.BatteryMinutes / 60} h {s.BatteryMinutes % 60:00} restantes" : ""), Progress = s.Battery / 100.0, Duration = TimeSpan.FromSeconds(2.5), Width = 300, Height = 68 });
                _lastCharging = s.Charging;
                if (s.Charging) _lastBatWarn = 101;
                else foreach (var lvl in new[] { 20, 10, 5 })
                    {
                        if (s.Battery <= lvl && _lastBatWarn > lvl)
                        {
                            _lastBatWarn = lvl;
                            Alert("Batterie faible", $"Plus que {s.Battery} %. Branche le chargeur.", "\uE0C8");
                            break;
                        }
                    }
            }

            if (S.SystemAlerts) SystemAlerts(s);

            var names = string.Join("|", s.Bluetooth.Select(b => b.Left).OrderBy(x => x));
            if (_lastBtNames != null && names != _lastBtNames)
            {
                var prev = _lastBtNames.Split('|', StringSplitOptions.RemoveEmptyEntries);
                foreach (var b in s.Bluetooth.Where(b => !prev.Contains(b.Left)))
                    ShowPeek(new PeekData { Key = "alert", Glyph = "\uE0DA", Title = b.Left, Body = "Connecté · " + b.Right, Duration = TimeSpan.FromSeconds(3), Width = 340, Height = 68 });
            }
            _lastBtNames = names;
        }
    }
}
