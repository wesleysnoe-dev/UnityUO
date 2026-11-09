using System;

namespace UOClient.Core
{
    /// <summary>Where the sun (or by night the moon) stands: what the client's shadows and light follow.</summary>
    public readonly struct SunState
    {
        /// <summary>The UO hour it was computed for, 0 ≤ hour &lt; 24.</summary>
        public readonly float Hour;

        /// <summary>Unit ground direction toward the light in tile space (+x east, +y south).</summary>
        public readonly float TowardX, TowardY;

        /// <summary>The light's height above the horizon, radians (never below <see cref="SunClock.MinElevation"/>).</summary>
        public readonly float Elevation;

        /// <summary>How strong its shadows are, 0 (the sun on the horizon, or the moon handing over) to 1.</summary>
        public readonly float Strength;

        /// <summary>Golden light near sunrise and sunset, 0 to 1.</summary>
        public readonly float Warmth;

        public readonly bool Moon;

        public SunState(float hour, float towardX, float towardY, float elevation, float strength, float warmth, bool moon)
        {
            Hour = hour;
            TowardX = towardX;
            TowardY = towardY;
            Elevation = elevation;
            Strength = strength;
            Warmth = warmth;
            Moon = moon;
        }
    }

    /// <summary>
    /// The shard's clock (ModernUO's Clock.GetTime: one UO minute is 5 real seconds, so a day is 2 real hours, counted
    /// from 1 Sep 1997, plus 320 minutes per map index and one minute per 16 tiles of x), and the sun it puts in the sky.
    /// The server's light (LightCycle) is night until 4, dawn 4-6, day 6-22, dusk 22-24; the sun is up from
    /// <see cref="Sunrise"/> to <see cref="Sunset"/>, the middle of those ramps. It rises toward the screen's upper right,
    /// is highest at 14:00 where the classic art's light stands (the screen's upper left: LiveGroundLighting's reference
    /// sun), and sets toward the screen's left; shadows never fall up the screen. By night a fixed moon casts faint shadows.
    /// </summary>
    public static class SunClock
    {
        public static readonly DateTime WorldStart = new DateTime(1997, 9, 1);
        public const double SecondsPerUOMinute = 5.0;
        public const float Sunrise = 5f, Sunset = 23f;

        /// <summary>The reference sun (LiveGroundLighting's compositor light): toward (-0.6, -0.2), up 0.77.</summary>
        public const float RefX = -0.6f, RefY = -0.2f, RefUp = 0.77f;

        public static readonly float RefAzimuth = (float)(Math.Atan2(RefY, RefX) + 2 * Math.PI); // 198°: between rise and set
        public static readonly float RefElevation = (float)Math.Atan2(RefUp, Math.Sqrt(RefX * RefX + RefY * RefY));

        /// <summary>Azimuths (radians, tile space, toward the light) at sunrise and sunset; noon is <see cref="RefAzimuth"/>.</summary>
        public static readonly float RiseAzimuth = Deg(290f), SetAzimuth = Deg(140f);

        public static readonly float MoonAzimuth = Deg(225f), MoonElevation = Deg(38f);

        /// <summary>The lowest the light is taken to stand (long shadows are capped by the renderer anyway).</summary>
        public static readonly float MinElevation = Deg(6f);

        public const float MoonStrength = 0.8f;

        /// <summary>The UO hour (fractional) at (map, x) for this real UTC time, as the server's Clock.</summary>
        public static double Hour(DateTime utcNow, int mapIndex, int x)
        {
            double minutes = (utcNow - WorldStart).TotalSeconds / SecondsPerUOMinute + mapIndex * 320 + x / 16;
            minutes %= 1440.0;
            if (minutes < 0)
                minutes += 1440.0;
            return minutes / 60.0;
        }

        /// <summary>The sun or moon at UO hour <paramref name="hour"/>.</summary>
        public static SunState At(double hour)
        {
            float h = (float)(((hour % 24.0) + 24.0) % 24.0);
            if (h >= Sunrise && h <= Sunset)
            {
                float f = (h - Sunrise) / (Sunset - Sunrise); // 0 sunrise, 0.5 at 14:00, 1 sunset
                float arc = (float)Math.Sin(Math.PI * f);
                float az = f < 0.5f ? Lerp(RiseAzimuth, RefAzimuth, f * 2f) : Lerp(RefAzimuth, SetAzimuth, f * 2f - 1f);
                float el = Math.Max(MinElevation, RefElevation * arc);
                float strength = Smooth(0f, 0.22f, arc);
                float warmth = 1f - Smooth(0.10f, 0.42f, arc);
                return new SunState(h, (float)Math.Cos(az), (float)Math.Sin(az), el, strength, warmth, false);
            }
            float g = ((h - Sunset + 24f) % 24f) / (24f - (Sunset - Sunrise)); // 0 at sunset, 1 at sunrise
            float moonArc = (float)Math.Sin(Math.PI * g);
            return new SunState(h, (float)Math.Cos(MoonAzimuth), (float)Math.Sin(MoonAzimuth), MoonElevation,
                MoonStrength * Smooth(0f, 0.3f, moonArc), 0f, true);
        }

        /// <summary>
        /// The light as LiveGroundLighting's (x, y, up) vector, at the reference vector's length, so the reference sun
        /// (14:00) gives back (<see cref="RefX"/>, <see cref="RefY"/>, <see cref="RefUp"/>).
        /// </summary>
        public static (float X, float Y, float Up) Vector(SunState s)
        {
            float len = (float)Math.Sqrt(RefX * RefX + RefY * RefY + RefUp * RefUp);
            float c = (float)Math.Cos(s.Elevation) * len;
            return (s.TowardX * c, s.TowardY * c, (float)Math.Sin(s.Elevation) * len);
        }

        /// <summary>The fixed hour for a named time of day (the T key's Day, Golden hour, Dusk, Night).</summary>
        public static float HourFor(string timeName)
        {
            string n = (timeName ?? "").ToLowerInvariant();
            if (n.StartsWith("golden")) return 6.2f;
            if (n.StartsWith("dusk")) return 22.4f;
            if (n.StartsWith("night")) return 1.5f;
            return 14f;
        }

        private static float Deg(float d) => d * (float)Math.PI / 180f;

        private static float Lerp(float a, float b, float t) => a + (b - a) * t;

        private static float Smooth(float a, float b, float x)
        {
            float t = Math.Max(0f, Math.Min(1f, (x - a) / (b - a)));
            return t * t * (3f - 2f * t);
        }
    }
}
