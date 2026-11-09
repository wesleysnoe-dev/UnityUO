using System;
using UnityEngine;
using UOClient.Core;

namespace UOClient.Unity
{
    /// <summary>
    /// The plants' globals for UOClient/WorldSprite (PlantKind marks which sprites are plants): how far a plant's top leans
    /// in the wind (<c>_UOFoliageSway</c>, scaled to the art; storms add WeatherView's <c>_UOWindBoost</c>) and which season
    /// the leaves show (<c>_UOFoliageSeason</c>, from the server's 0xBC). -nofoliagesway keeps them still;
    /// -season spring|summer|fall|winter|desolation fixes the season (captures, checks).
    /// </summary>
    public static class FoliageWind
    {
        /// <summary>A canopy's lean at full weight, in art pixels (a little over two: alive, not seasick).</summary>
        public const float SwayArtPixels = 2.4f;

        private static readonly int SwayId = Shader.PropertyToID("_UOFoliageSway"), SeasonId = Shader.PropertyToID("_UOFoliageSeason");

        private static readonly bool Still = Array.Exists(Environment.GetCommandLineArgs(),
            a => string.Equals(a, "-nofoliagesway", StringComparison.OrdinalIgnoreCase));

        private static readonly Season? Forced = ForcedSeason();

        private static Season? ForcedSeason()
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++)
                if (string.Equals(args[i], "-season", StringComparison.OrdinalIgnoreCase)
                    && Enum.TryParse(args[i + 1], true, out Season s))
                    return s;
            return null;
        }

        /// <summary>The season the leaves show: the forced one, else the server's.</summary>
        public static Season Current(GameSession session) => Forced ?? session?.Season ?? Season.Summer;

        /// <summary>The shader's code: 0 as drawn (summer), 1 spring, 2 fall, 3 winter, 4 desolation.</summary>
        public static float SeasonCode(Season season) => season switch
        {
            Season.Spring => 1f,
            Season.Fall => 2f,
            Season.Winter => 3f,
            Season.Desolation => 4f,
            _ => 0f
        };

        /// <summary>Every frame (main thread).</summary>
        public static void Apply(GameSession session, int artScale)
        {
            Shader.SetGlobalFloat(SwayId, Still ? 0f : SwayArtPixels * Mathf.Max(1, artScale));
            Shader.SetGlobalFloat(SeasonId, SeasonCode(Current(session)));
        }
    }
}
