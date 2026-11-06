using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Media;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace DynamicIsland
{
    public partial class MainWindow
    {
        enum TMode { Timer, Chrono, Pomo }
        TMode _tm = TMode.Timer;
        bool _tRun, _pomoWork = true;
        int _pomoCycle = 1;
        TimeSpan _tTotal = TimeSpan.FromMinutes(5), _tLeft = TimeSpan.FromMinutes(5), _chrono = TimeSpan.Zero;
        DateTime _tLast = DateTime.Now;
        ObservableCollection<TodoItem> _todos;
        ObservableCollection<AlarmData> _alarms;
        readonly HashSet<string> _firedAlarms = new();

        void InitTools()
        {
            _todos = new ObservableCollection<TodoItem>(S.Todos);
            TodoList.ItemsSource = _todos;
            _alarms = new ObservableCollection<AlarmData>(S.Alarms);
            AlarmList.ItemsSource = _alarms;
            UpdateTimerUI();
        }

        static string FmtT(TimeSpan t) => t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"mm\:ss");

        void TimerTick()
        {
            var now = DateTime.Now;
            if (_tRun)
            {
                var dt = now - _tLast;
                if (_tm == TMode.Chrono) _chrono += dt;
                else
                {
                    _tLeft -= dt;
                    if (_tLeft <= TimeSpan.Zero) { _tLeft = TimeSpan.Zero; TimerDone(); }
                }
            }
            _tLast = now;
            if (_mode == Mode.Expanded && PanelTools.Visibility == Visibility.Visible) UpdateTimerUI();
        }

        void TimerDone()
        {
            if (_tm == TMode.Timer)
            {
                _tRun = false;
                Alert("Minuteur terminé", $"{FmtT(_tTotal)} écoulées ⏰", "\uE916");
            }
            else if (_tm == TMode.Pomo)
            {
                _pomoWork = !_pomoWork;
                if (_pomoWork) _pomoCycle++;
                _tTotal = _tLeft = TimeSpan.FromMinutes(_pomoWork ? S.PomodoroWork : S.PomodoroBreak);
                Alert(_pomoWork ? "Au travail !" : "Pause !",
                      _pomoWork ? $"Cycle {_pomoCycle} · {S.PomodoroWork} min de concentration" + (S.FocusDnd ? " · notifications en pause" : "") : $"{S.PomodoroBreak} min de pause bien méritée",
                      "\uE916");
            }
            UpdateTimerUI();
            UpdateCompact();
        }

        void Alert(string title, string body, string glyph)
        {
            try { SystemSounds.Exclamation.Play(); } catch { }
            ShowPeek(new PeekData { Key = "alert", Title = title, Body = body, Glyph = glyph, Duration = TimeSpan.FromSeconds(8), Width = 400, Height = 76 });
        }

        void UpdateTimerUI()
        {
            TimerDisplay.Text = _tm == TMode.Chrono ? FmtT(_chrono) : FmtT(_tLeft);
            TimerProgress.Value = _tm == TMode.Chrono || _tTotal.TotalSeconds <= 0 ? 0 : 1 - _tLeft.TotalSeconds / _tTotal.TotalSeconds;
            TimerSub.Text = _tm switch
            {
                TMode.Chrono => "Chronomètre",
                TMode.Pomo => _pomoWork ? (S.FocusDnd && _tRun ? "Focus · notifications en pause" : $"Concentration · cycle {_pomoCycle}") : "Pause",
                _ => "Minuteur"
            };
            TimerStartBtn.Content = _tRun ? "Pause" : (_tm == TMode.Chrono && _chrono > TimeSpan.Zero) || (_tm != TMode.Chrono && _tLeft < _tTotal) ? "Reprendre" : "Démarrer";
            TimerPresets.Visibility = _tm == TMode.Timer ? Visibility.Visible : Visibility.Collapsed;
        }

        void TimerMode_Checked(object sender, RoutedEventArgs e)
        {
            if (!_ready) return;
            _tRun = false;
            _tm = sender == TmChrono ? TMode.Chrono : sender == TmPomo ? TMode.Pomo : TMode.Timer;
            if (_tm == TMode.Pomo) { _pomoWork = true; _pomoCycle = 1; _tTotal = _tLeft = TimeSpan.FromMinutes(S.PomodoroWork); }
            else if (_tm == TMode.Timer) { _tLeft = _tTotal = TimeSpan.FromMinutes(5); }
            UpdateTimerUI();
            UpdateCompact();
        }

        void Preset_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.Tag is string m && int.TryParse(m, out var min)) SetTimer(min);
        }

        void TimerCustom_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && double.TryParse(TimerCustom.Text.Replace(',', '.'), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var m) && m > 0)
                SetTimer(m);
        }

        void SetTimer(double minutes)
        {
            _tTotal = _tLeft = TimeSpan.FromMinutes(minutes);
            _tLast = DateTime.Now;
            _tRun = true;
            UpdateTimerUI();
            UpdateCompact();
        }

        /// <summary>Libellé court du minuteur pour l'île compacte.</summary>
        string TimerLabel => _tm switch
        {
            TMode.Chrono => "Chrono",
            TMode.Pomo => _pomoWork ? "Focus" : "Pause",
            _ => "Minuteur"
        };

        void FocusPeek()
        {
            if (!S.FocusDnd) return;
            ShowPeek(new PeekData
            {
                Key = "alert", Glyph = "\uE708", Title = "Focus",
                Body = $"Notifications en pause pendant {Math.Ceiling(_tLeft.TotalMinutes)} min",
                Duration = TimeSpan.FromSeconds(3), Width = 360, Height = 68
            });
        }

        void TimerStart_Click(object sender, RoutedEventArgs e)
        {
            if (_tm != TMode.Chrono && _tLeft <= TimeSpan.Zero) _tLeft = _tTotal;
            _tRun = !_tRun;
            if (_tRun && _tm == TMode.Pomo && _pomoWork && _tLeft >= _tTotal) FocusPeek();
            _tLast = DateTime.Now;
            UpdateTimerUI();
            UpdateCompact();
        }

        void TimerReset_Click(object sender, RoutedEventArgs e)
        {
            _tRun = false;
            _chrono = TimeSpan.Zero;
            if (_tm == TMode.Pomo) { _pomoWork = true; _pomoCycle = 1; _tTotal = TimeSpan.FromMinutes(S.PomodoroWork); }
            _tLeft = _tTotal;
            UpdateTimerUI();
            UpdateCompact();
        }

        // ---------- Alarmes ----------
        void AddAlarm_Click(object sender, RoutedEventArgs e)
        {
            if (!TimeSpan.TryParse(AlarmTime.Text.Trim().Replace('h', ':'), out var t)) { AlarmTime.Text = "HH:mm"; return; }
            _alarms.Add(new AlarmData { Time = $"{t.Hours:00}:{t.Minutes:00}", Label = string.IsNullOrWhiteSpace(AlarmLabel.Text) ? "Alarme" : AlarmLabel.Text.Trim() });
            var sorted = _alarms.OrderBy(a => a.Time).ToList();
            _alarms.Clear();
            foreach (var a in sorted) _alarms.Add(a);
            SaveTools();
        }

        void AlarmDelete_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.Tag is AlarmData a) { _alarms.Remove(a); SaveTools(); }
        }

        void CheckAlarms()
        {
            var now = DateTime.Now.ToString("HH:mm");
            foreach (var a in _alarms)
            {
                var key = DateTime.Today.ToString("yyyyMMdd") + a.Time + a.Label;
                if (a.Time == now && _firedAlarms.Add(key))
                    Alert(a.Label, $"Il est {a.Time}", "\uEA8F");
            }
        }

        // ---------- To-do ----------
        void TodoInput_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter || string.IsNullOrWhiteSpace(TodoInput.Text)) return;
            _todos.Add(new TodoItem { Text = TodoInput.Text.Trim() });
            TodoInput.Clear();
            SaveTools();
        }

        void TodoDelete_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.Tag is TodoItem t) { _todos.Remove(t); SaveTools(); }
        }

        void Todo_Changed(object sender, RoutedEventArgs e) => SaveTools();

        void SaveTools()
        {
            S.Todos = _todos.ToList();
            S.Alarms = _alarms.ToList();
            S.Save();
        }
    }
}
