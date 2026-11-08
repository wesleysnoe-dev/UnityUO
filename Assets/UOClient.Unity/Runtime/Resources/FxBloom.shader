// Bloom for UOClient.Unity.Fx.FxBloom: 0 bright pass + downsample, 1 blur down, 2 blur up (+ coarser level), 3 composite.
Shader "UOClient/FxBloom"
{
    Properties
    {
        _MainTex ("Source", 2D) = "black" {}
    }
    CGINCLUDE
    #include "UnityCG.cginc"
    sampler2D _MainTex;
    float4 _MainTex_TexelSize;
    sampler2D _Lower;
    sampler2D _Bloom;
    float _Threshold;
    float _Intensity;

    half3 Box(float2 uv)
    {
        float4 o = _MainTex_TexelSize.xyxy * float4(-1, -1, 1, 1);
        return (tex2D(_MainTex, uv + o.xy).rgb + tex2D(_MainTex, uv + o.zy).rgb +
                tex2D(_MainTex, uv + o.xw).rgb + tex2D(_MainTex, uv + o.zw).rgb) * 0.25;
    }
    ENDCG

    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass // 0: bright pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            half4 frag(v2f_img i) : SV_Target
            {
                half3 c = Box(i.uv);
                half b = max(c.r, max(c.g, c.b));
                half soft = saturate((b - _Threshold + 0.1) / 0.2);
                half k = max(b - _Threshold, 0) / max(b, 1e-4);
                return half4(c * max(k, soft * soft * 0.1), 1);
            }
            ENDCG
        }
        Pass // 1: blur down
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            half4 frag(v2f_img i) : SV_Target { return half4(Box(i.uv), 1); }
            ENDCG
        }
        Pass // 2: blur up
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            half4 frag(v2f_img i) : SV_Target { return half4(Box(i.uv) + tex2D(_Lower, i.uv).rgb, 1); }
            ENDCG
        }
        Pass // 3: composite
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            half4 frag(v2f_img i) : SV_Target
            {
                half4 c = tex2D(_MainTex, i.uv);
                c.rgb += tex2D(_Bloom, i.uv).rgb * _Intensity;
                return c;
            }
            ENDCG
        }
    }
}
