using System;
using UOClient.Net;

namespace UOClient.Core
{
    /// <summary>The weather kinds of 0x65, as the classic client numbers them (ModernUO only sends rain, snow and none).</summary>
    public enum WeatherType : byte
    {
        Rain = 0,
        FierceStorm = 1,
        Snow = 2,
        StormBrewing = 3,
        None = 0xFE
    }

    /// <summary>The server's weather: its kind, how many particles to show (ModernUO sends 0 to 70) and the temperature.</summary>
    public sealed class Weather
    {
        public WeatherType Type { get; }
        public int Count { get; }
        public int Temperature { get; }

        public Weather(WeatherType type, int count, int temperature)
        {
            Type = type;
            Count = count;
            Temperature = temperature;
        }

        /// <summary>Whether anything falls: rain, storm or snow with a non-zero count.</summary>
        public bool IsFalling => Type != WeatherType.None && Type != WeatherType.StormBrewing && Count > 0;
    }

    /// <summary>The weather (0x65) ModernUO's weather regions send every 30 seconds to players inside them.</summary>
    public sealed partial class GameSession
    {
        /// <summary>The current weather; clear until the server says otherwise.</summary>
        public Weather Weather { get; private set; } = new Weather(WeatherType.None, 0, 0);

        /// <summary>The server sent weather (also when it is unchanged).</summary>
        public event Action<Weather> WeatherChanged;

        private void RegisterWeatherHandlers()
        {
            _handlers[0x65] = WeatherChange;
        }

        /// <summary>
        /// 0x65 (4 bytes): type, count, temperature, as ModernUO's SendWeather writes them. 0xFE and 0xFF (and any
        /// other unknown type) clear the weather; the temperature is a signed byte (ModernUO negates it when extreme).
        /// </summary>
        private void WeatherChange(PacketReader r)
        {
            byte type = r.ReadByte();
            byte count = r.ReadByte();
            sbyte temperature = r.ReadSByte();
            WeatherType kind = type <= (byte)WeatherType.StormBrewing ? (WeatherType)type : WeatherType.None;
            Weather = new Weather(kind, kind == WeatherType.None ? 0 : count, temperature);
            WeatherChanged?.Invoke(Weather);
        }
    }
}
