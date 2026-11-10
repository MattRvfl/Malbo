using System;
using System.Linq;
using System.Threading.Tasks;
using Windows.Devices.Radios;

namespace DynamicIsland
{
    /// <summary>Bascule Wi-Fi / Bluetooth via les radios Windows.</summary>
    public static class RadioService
    {
        static async Task<Radio> Find(RadioKind kind)
        {
            var access = await Radio.RequestAccessAsync();
            if (access != RadioAccessStatus.Allowed) throw new InvalidOperationException("Windows refuse l'accès aux radios (Paramètres › Confidentialité › Radios).");
            var radios = await Radio.GetRadiosAsync();
            return radios.FirstOrDefault(r => r.Kind == kind);
        }

        /// <summary>Renvoie true/false (allumé/éteint), ou null si l'appareil n'a pas cette radio.</summary>
        public static async Task<bool?> StateAsync(RadioKind kind)
        {
            try { var r = await Find(kind); return r == null ? null : r.State == RadioState.On; }
            catch { return null; }
        }

        public static async Task<bool?> ToggleAsync(RadioKind kind)
        {
            var r = await Find(kind);
            if (r == null) return null;
            bool on = r.State != RadioState.On;
            var res = await r.SetStateAsync(on ? RadioState.On : RadioState.Off);
            if (res != RadioAccessStatus.Allowed) throw new InvalidOperationException("Windows a refusé le changement.");
            return on;
        }
    }
}
