// GroundPrototype P2 - SAMPLE, NOT MAP ART.
// Stylized transparent water: animated analytic ripples, depth-based colour/alpha fade and shore foam from
// _CameraDepthTexture (enabled only on the prototype compare cameras). Depth is linearised correctly for
// orthographic cameras, then converted to vertical water depth along the view direction.
// Limits (also in README): no reflection, no refraction, no SSR, no simulation; lit by the sun + ambient
// only (ForwardBase), so it does NOT respond to the lava point light.
Shader "UOClient/GroundPrototype/StylizedWater"
{
    Properties
    {
        _ShallowColor ("Shallow colour", Color) = (0.32, 0.68, 0.70, 1)
        _DeepColor ("Deep colour", Color) = (0.07, 0.25, 0.40, 1)
        _FoamColor ("Foam colour (a = strength)", Color) = (0.92, 0.97, 1.0, 0.85)
        _DepthFade ("Depth for full colour (world u, vertical)", Float) = 0.3
        _ShoreAlpha ("Alpha at shore", Range(0, 1)) = 0.3
        _DeepAlpha ("Alpha when deep", Range(0, 1)) = 0.88
        _FoamWidth ("Foam width (world u, vertical)", Float) = 0.05
        _WaveScale ("Ripple frequency", Float) = 2.2
        _WaveSpeed ("Ripple speed", Float) = 0.6
        _WaveStrength ("Ripple normal strength", Float) = 0.18
        _SpecStrength ("Sun glint strength", Float) = 0.45
        _EdgeFade ("Rect edge fade (0..0.5 of rect)", Float) = 0.02
        _MapOrigin ("Side origin (world)", Vector) = (0, 0, 0, 0)
    }

    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }

        Pass
        {
            Tags { "LightMode" = "ForwardBase" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"
            #include "UnityLightingCommon.cginc"

            UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture);

            fixed4 _ShallowColor, _DeepColor, _FoamColor;
            float _DepthFade, _ShoreAlpha, _DeepAlpha, _FoamWidth;
            float _WaveScale, _WaveSpeed, _WaveStrength, _SpecStrength, _EdgeFade;
            float4 _MapOrigin;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 worldPos : TEXCOORD0;
                float4 screenPos : TEXCOORD1;
                float eyeDepth : TEXCOORD2;
                float2 rectUV : TEXCOORD3;
            };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.screenPos = ComputeScreenPos(o.pos);
                o.eyeDepth = -UnityObjectToViewPos(v.vertex.xyz).z;
                o.rectUV = v.uv;
                return o;
            }

            // Scene eye depth (distance along the camera forward axis) for both projection types.
            float SceneEyeDepth (float2 suv)
            {
                float raw = SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, suv);
                if (unity_OrthoParams.w > 0.5)
                {
                    #if defined(UNITY_REVERSED_Z)
                    raw = 1.0 - raw;
                    #endif
                    return lerp(_ProjectionParams.y, _ProjectionParams.z, raw); // ortho depth is linear near..far
                }
                return LinearEyeDepth(raw);
            }

            // Sum of directional sines in local map space; returns height derivative (d/dx, d/dz).
            float2 RippleSlope (float2 p, float t)
            {
                float2 d1 = normalize(float2(1.0, 0.35)), d2 = normalize(float2(-0.45, 1.0)), d3 = normalize(float2(0.8, -0.7));
                float f1 = _WaveScale, f2 = _WaveScale * 1.7, f3 = _WaveScale * 2.9;
                float2 s = d1 * f1 * cos(dot(p, d1) * f1 + t * 1.0)
                         + d2 * f2 * cos(dot(p, d2) * f2 - t * 1.3) * 0.55
                         + d3 * f3 * cos(dot(p, d3) * f3 + t * 1.9) * 0.3;
                return s / f1;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float t = _Time.y * _WaveSpeed * 4.0;
                float2 local = i.worldPos.xz - _MapOrigin.xz;

                // Depth below the surface, converted from view-axis thickness to vertical metres.
                // camFwd = world-space camera forward; exact for ortho (all rays parallel to it).
                float3 camFwd = -UNITY_MATRIX_V[2].xyz;
                float2 suv = i.screenPos.xy / i.screenPos.w;
                float viewThick = max(0.0, SceneEyeDepth(suv) - i.eyeDepth);
                float depthV = viewThick * saturate(-camFwd.y);
                float deep = saturate(depthV / _DepthFade);

                float2 slope = RippleSlope(local, t) * _WaveStrength;
                float3 n = normalize(float3(-slope.x, 1.0, -slope.y));

                float3 L = normalize(_WorldSpaceLightPos0.xyz);
                float3 V = unity_OrthoParams.w > 0.5 ? -camFwd : normalize(_WorldSpaceCameraPos - i.worldPos);
                float ndl = saturate(dot(n, L));
                float3 ambient = ShadeSH9(float4(n, 1.0));
                float3 lit = ambient + _LightColor0.rgb * ndl;
                float spec = pow(saturate(dot(n, normalize(L + V))), 80.0) * _SpecStrength;

                fixed3 col = lerp(_ShallowColor.rgb, _DeepColor.rgb, deep) * lit + spec * _LightColor0.rgb;

                // Shore foam: thin animated band where the ground meets the surface.
                float band = 1.0 - smoothstep(0.0, _FoamWidth, depthV);
                float wobble = 0.65 + 0.35 * sin(depthV * 120.0 - t * 2.0 + (local.x + local.y) * 3.0);
                float foam = saturate(band * wobble) * _FoamColor.a;
                col = lerp(col, _FoamColor.rgb * lit, foam);

                float alpha = lerp(_ShoreAlpha, _DeepAlpha, deep);
                alpha = max(alpha, foam);
                alpha *= saturate(depthV / 0.01);                        // soften the exact ground intersection
                float2 e = min(i.rectUV, 1.0 - i.rectUV);                  // bounded rect: fade the outer lip
                alpha *= saturate(min(e.x, e.y) / max(_EdgeFade, 1e-4));
                return fixed4(col, alpha);
            }
            ENDCG
        }
    }

    FallBack Off
}
