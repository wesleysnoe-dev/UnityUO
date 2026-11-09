using System;
using UnityEngine;

namespace UOClient.Unity.Fx
{
    /// <summary>
    /// Colour grading on the world camera, after the bloom (Resources/FxGrade.shader): a gentle contrast curve, a little
    /// more colour, warm highlights and cool shadows, and a soft vignette, keeping UO's palette recognisable. Cooler and
    /// lower in contrast at night, so the dark stays readable. -nograde turns it off.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public sealed class FxGrade : MonoBehaviour
    {
        public static readonly bool Enabled = !Array.Exists(Environment.GetCommandLineArgs(),
            a => string.Equals(a, "-nograde", StringComparison.OrdinalIgnoreCase));

        private static readonly int StrengthId = Shader.PropertyToID("_Strength"), ContrastId = Shader.PropertyToID("_Contrast"),
            SaturationId = Shader.PropertyToID("_Saturation"), VignetteId = Shader.PropertyToID("_Vignette"),
            WarmId = Shader.PropertyToID("_Warm"), CoolId = Shader.PropertyToID("_Cool");

        private Material _mat;

        private void OnRenderImage(RenderTexture src, RenderTexture dst)
        {
            if (_mat == null)
            {
                Shader s = Resources.Load<Shader>("FxGrade");
                if (!Enabled || s == null || !s.isSupported)
                {
                    enabled = false;
                    Graphics.Blit(src, dst);
                    return;
                }
                _mat = new Material(s) { hideFlags = HideFlags.HideAndDontSave };
            }
            float night = Lighting.LocalLightScale;
            _mat.SetFloat(StrengthId, 1f);
            _mat.SetFloat(ContrastId, Mathf.Lerp(0.28f, 0.12f, night));
            _mat.SetFloat(SaturationId, Mathf.Lerp(1.1f, 1.0f, night));
            _mat.SetFloat(VignetteId, 0.18f);
            _mat.SetVector(WarmId, new Vector4(0.06f, 0.035f, 0.0f, Mathf.Lerp(0.6f, 0.25f, night)));
            _mat.SetVector(CoolId, new Vector4(-0.01f, 0.0f, 0.035f, Mathf.Lerp(0.5f, 0.9f, night)));
            Graphics.Blit(src, dst, _mat);
        }

        private void OnDestroy()
        {
            if (_mat != null)
                Destroy(_mat);
        }
    }
}
