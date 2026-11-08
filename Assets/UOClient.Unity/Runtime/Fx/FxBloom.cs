using UnityEngine;

namespace UOClient.Unity.Fx
{
    /// <summary>
    /// Bloom on the world camera (built-in pipeline, OnRenderImage): the brightest pixels (flames, spells, lit windows)
    /// are picked out, blurred down a mip chain and added back, so light bleeds softly. It is strongest at night
    /// (<see cref="Lighting.LocalLightScale"/>) and faint by day, so sunlit walls do not glow. Resources/FxBloom.shader.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public sealed class FxBloom : MonoBehaviour
    {
        private const int Levels = 5;
        private Material _mat;
        private readonly RenderTexture[] _chain = new RenderTexture[Levels];

        private void OnRenderImage(RenderTexture src, RenderTexture dst)
        {
            if (_mat == null)
            {
                Shader s = Shader.Find("UOClient/FxBloom");
                if (s == null || !s.isSupported)
                {
                    enabled = false;
                    Graphics.Blit(src, dst);
                    return;
                }
                _mat = new Material(s) { hideFlags = HideFlags.HideAndDontSave };
            }
            float night = Lighting.LocalLightScale;
            _mat.SetFloat("_Threshold", Mathf.Lerp(0.92f, 0.72f, night));
            _mat.SetFloat("_Intensity", Mathf.Lerp(0.12f, 0.55f, night));

            int w = src.width / 2, h = src.height / 2;
            RenderTexture cur = RenderTexture.GetTemporary(w, h, 0, src.format);
            Graphics.Blit(src, cur, _mat, 0); // bright pass + first downsample
            _chain[0] = cur;
            int n = 1;
            for (; n < Levels; n++)
            {
                w /= 2; h /= 2;
                if (w < 2 || h < 2)
                    break;
                RenderTexture next = RenderTexture.GetTemporary(w, h, 0, src.format);
                Graphics.Blit(cur, next, _mat, 1); // blur down
                _chain[n] = next;
                cur = next;
            }
            for (int i = n - 2; i >= 0; i--)
            {
                _mat.SetTexture("_Lower", cur);
                RenderTexture up = RenderTexture.GetTemporary(_chain[i].width, _chain[i].height, 0, src.format);
                Graphics.Blit(_chain[i], up, _mat, 2); // blur up, adding the coarser level
                if (cur != _chain[n - 1])
                    RenderTexture.ReleaseTemporary(cur);
                cur = up;
            }
            _mat.SetTexture("_Bloom", cur);
            Graphics.Blit(src, dst, _mat, 3); // composite
            if (cur != _chain[n - 1])
                RenderTexture.ReleaseTemporary(cur);
            for (int i = 0; i < n; i++)
            {
                RenderTexture.ReleaseTemporary(_chain[i]);
                _chain[i] = null;
            }
        }

        private void OnDestroy()
        {
            if (_mat != null)
                Destroy(_mat);
        }
    }
}
