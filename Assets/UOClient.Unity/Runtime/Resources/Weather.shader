// The weather over the world (WeatherView.cs): one screen quad in front of the main camera, after every sprite and before
// the UI, drawn procedurally from hashes and _Time (no textures, no particles). Anchored to the world (_WOrigin: the
// camera's position in world pixels), so walking moves through the rain instead of dragging it along.
//   _WRain   0..1  streaks in three depths, slanted by the wind, and splashes on the ground
//   _WSnow   0..1  flakes in three depths, drifting
//   _WFog    0..1  rolling banks of mist and an even haze
//   _WWind   slant of rain and drift of snow and fog (screen x per screen y)
//   _WFlash  0..1  lightning
//   _WClouds 0..1  shadows of clouds drifting over the ground (only while the sun casts: 1 - _UOSunFade)
// Lit by the time of day (_UOAmbient), so night rain is dark and dawn mist is warm.
Shader "UOClient/Weather"
{
    Properties
    {
        _WRain ("Rain", Range(0, 1)) = 0
        _WSnow ("Snow", Range(0, 1)) = 0
        _WFog ("Fog", Range(0, 1)) = 0
        _WWind ("Wind", Range(-1, 1)) = 0.2
        _WFlash ("Lightning", Range(0, 1)) = 0
        _WClouds ("Cloud shadows", Range(0, 1)) = 0
        _WOrigin ("Camera in world pixels", Vector) = (0, 0, 0, 0)
        _WScale ("Pixels per art pixel", Float) = 1
    }

    SubShader
    {
        Tags { "Queue" = "Overlay" "RenderType" = "Transparent" "IgnoreProjector" = "True" }

        Pass
        {
            ZWrite Off
            ZTest Always
            Cull Off
            Blend One OneMinusSrcAlpha

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            float _WRain, _WSnow, _WFog, _WWind, _WFlash, _WScale, _WClouds;
            float _UOSunFade; // CharacterShadow.SetSun: the sun's shadows fade (night, low sun, overcast)
            float4 _WOrigin, _UOAmbient;

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            float Hash(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float Noise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(Hash(i), Hash(i + float2(1, 0)), u.x), lerp(Hash(i + float2(0, 1)), Hash(i + float2(1, 1)), u.x), u.y);
            }

            float Fbm(float2 p)
            {
                float v = 0.0, a = 0.5;
                for (int k = 0; k < 4; k++)
                {
                    v += a * Noise(p);
                    p = p * 2.03 + float2(17.1, 9.2);
                    a *= 0.5;
                }
                return v;
            }

            // One depth of rain: a drop per cell (some cells empty), a streak brightening toward its head.
            // p: world pixels, y down. Returns coverage 0..1.
            float RainLayer(float2 p, float cellW, float cellH, float speed, float len, float width, float density, float seed)
            {
                p.y -= _Time.y * speed;
                p.x -= p.y * _WWind;                  // lean with the wind
                float2 cell = floor(float2(p.x / cellW, p.y / cellH));
                float2 f = float2(frac(p.x / cellW), frac(p.y / cellH));
                if (Hash(cell + seed) > density)
                    return 0.0;
                float x0 = 0.1 + 0.8 * Hash(cell + seed + 7.13);
                float y0 = Hash(cell + seed + 3.71) * (1.0 - len);
                float across = saturate(1.0 - abs(f.x - x0) * cellW / width);
                float t = (f.y - y0) / len;           // 0 tail .. 1 head
                float along = step(0.0, t) * step(t, 1.0) * (0.35 + 0.65 * t);
                return across * along;
            }

            // Splashes: a ring widening and fading where a drop lands (flattened, lying on the ground).
            float Splashes(float2 p, float density)
            {
                const float cell = 46.0;
                float2 c = floor(p / cell);
                float2 f = frac(p / cell) - 0.5;
                float h = Hash(c + 31.7);
                if (h > density)
                    return 0.0;
                float phase = frac(_Time.y * (1.1 + h) + Hash(c + 5.1));
                float2 at = float2(Hash(c + 1.3), Hash(c + 2.9)) * 0.6 - 0.3;
                float2 d = (f - at) * float2(1.0, 2.0);  // the iso ground: twice as wide as deep
                float r = length(d) * cell;
                float radius = phase * 7.0;
                float ring = saturate(1.0 - abs(r - radius) / 1.2);
                return ring * (1.0 - phase) * (1.0 - phase);
            }

            // One depth of snow: a soft round flake per cell, swaying as it falls.
            float SnowLayer(float2 p, float cell, float speed, float size, float density, float seed)
            {
                p.y -= _Time.y * speed;
                p.x -= _Time.y * speed * _WWind * 1.5;
                float2 c = floor(p / cell);
                float2 f = frac(p / cell);
                float h = Hash(c + seed);
                if (h > density)
                    return 0.0;
                float2 at = float2(0.25 + 0.5 * Hash(c + seed + 1.7) + sin(_Time.y * (0.8 + h) + h * 40.0) * 0.18,
                                   0.25 + 0.5 * Hash(c + seed + 8.3));
                float r = length(f - at) * cell;
                return (1.0 - smoothstep(size * 0.45, size, r)) * (0.65 + 0.35 * Hash(c + seed + 4.4));
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float2 screen = float2(i.uv.x, 1.0 - i.uv.y) * _ScreenParams.xy / max(_WScale, 0.01); // y down, in art pixels
                float2 world = screen + float2(_WOrigin.x, -_WOrigin.y);
                float3 amb = _UOAmbient.rgb;
                amb = dot(amb, 1) <= 0.0001 ? float3(1, 1, 1) : amb;
                float light = saturate(dot(amb, float3(0.3, 0.59, 0.11)));

                float3 col = 0;
                float a = 0;

                // Cloud shadows first (on the ground and everything on it): big soft shapes drifting with the wind, a
                // few tiles to a screen across, only while the sun casts shadows (none by night or under full overcast).
                float sunCast = 1.0 - saturate(_UOSunFade);
                if (_WClouds > 0.001 && sunCast > 0.01)
                {
                    float2 drift = float2(1.0, 0.35 + _WWind) * _Time.y * 14.0;
                    float2 q = (world - drift) * 0.00085;
                    float n = Fbm(q) * 0.7 + Fbm(q * 2.3 + 41.0) * 0.3;
                    float shade = smoothstep(0.50, 0.68, n) * 0.30 * _WClouds * sunCast;
                    col = col * (1.0 - shade);
                    a = a + shade * (1.0 - a);
                }

                // Fog (behind rain and snow): banks rolling with the wind over an even haze.
                if (_WFog > 0.001)
                {
                    float2 q = world * 0.0022 + float2(_Time.y * 0.018 * (0.4 + _WWind), _Time.y * 0.004);
                    float banks = Fbm(q) * 0.65 + Fbm(q * 2.7 + 13.0) * 0.35;
                    float fog = _WFog * (0.35 + 0.65 * smoothstep(0.35, 0.75, banks));
                    float3 fogCol = lerp(float3(0.62, 0.66, 0.72), float3(0.86, 0.88, 0.9), light) * amb;
                    fog = saturate(fog * 0.75);
                    col = col * (1.0 - fog) + fogCol * fog;
                    a = a + fog * (1.0 - a);
                }

                if (_WRain > 0.001)
                {
                    float r = 0;
                    // Far, middle and near: smaller, slower and fainter far away; near streaks long and few.
                    r += RainLayer(world * 1.6 + 300.0, 8.0, 110.0, 900.0, 0.24, 1.3, _WRain * 0.75, 1.0) * 0.5;
                    r += RainLayer(world * 1.15 + 77.0, 12.0, 160.0, 1250.0, 0.28, 1.7, _WRain * 0.6, 2.0) * 0.75;
                    r += RainLayer(world, 20.0, 240.0, 1700.0, 0.32, 2.3, _WRain * 0.45, 3.0);
                    float s = Splashes(world, _WRain * 0.7) * 0.7;
                    float3 rainCol = lerp(float3(0.55, 0.6, 0.68), float3(0.85, 0.9, 0.97), light) * amb;
                    float ra = saturate(r + s) * 0.7;
                    col = col * (1.0 - ra) + rainCol * ra;
                    a = a + ra * (1.0 - a);
                    // Rain greys the scene a little (a wet, overcast look).
                    float dim = _WRain * 0.12;
                    col = col * (1.0 - dim) + float3(0.25, 0.28, 0.33) * amb * dim;
                    a = a + dim * (1.0 - a);
                }

                if (_WSnow > 0.001)
                {
                    float f = 0;
                    f += SnowLayer(world * 1.7 + 50.0, 22.0, 26.0, 2.6, _WSnow * 0.9, 1.0) * 0.55;
                    f += SnowLayer(world * 1.25 + 9.0, 30.0, 40.0, 3.6, _WSnow * 0.75, 2.0) * 0.8;
                    f += SnowLayer(world, 46.0, 62.0, 5.5, _WSnow * 0.55, 3.0);
                    float3 snowCol = lerp(float3(0.75, 0.8, 0.9), float3(1, 1, 1), light) * max(amb, 0.45);
                    float fa = saturate(f) * 0.9;
                    col = col * (1.0 - fa) + snowCol * fa;
                    a = a + fa * (1.0 - a);
                }

                if (_WFlash > 0.001)
                {
                    float fl = _WFlash * 0.55;
                    col = col * (1.0 - fl) + float3(0.85, 0.9, 1.0) * fl;
                    a = a + fl * (1.0 - a);
                }
                return fixed4(col, a);
            }
            ENDCG
        }
    }
    Fallback Off
}
