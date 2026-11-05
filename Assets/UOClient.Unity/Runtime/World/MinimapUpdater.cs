using System;
using System.Threading.Tasks;
using UOClient.IO;

namespace UOClient.Unity
{
    /// <summary>Recomputes the minimap grid on a background thread when the player nears its edge.</summary>
    public sealed class MinimapUpdater
    {
        private Task<DemoHud.RadarData> _radarTask;

        public void Update(UOData data, int map, int x, int y, DemoHud hud)
        {
            if (_radarTask != null)
            {
                if (!_radarTask.IsCompleted)
                    return;
                if (_radarTask.Status == TaskStatus.RanToCompletion)
                    hud.SetRadar(_radarTask.Result);
                _radarTask = null;
            }

            bool far = Math.Max(Math.Abs(x - hud.RadarCenterX), Math.Abs(y - hud.RadarCenterY)) > 40;
            if (hud.RadarCenterX == int.MinValue || far)
                _radarTask = Task.Run(() => DemoHud.ComputeRadar(data, map, x, y));
        }
    }
}
