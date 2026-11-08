using UnityEngine;

namespace UOClient.Unity
{
    /// <summary>The orthographic camera that follows the player.</summary>
    public sealed class CameraRig
    {
        private readonly Camera _camera;

        public CameraRig(Camera camera)
        {
            _camera = camera;
        }

        /// <summary>The scene's main camera (made if there is none), set up for the world.</summary>
        public static Camera EnsureMainCamera()
        {
            Camera camera = Camera.main;
            if (camera == null)
            {
                var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
                camera = camGo.AddComponent<Camera>();
            }
            camera.orthographic = true;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.05f, 0.05f, 0.06f);
            // Depth keys span roughly +-5000 units around the depth origin (see DepthKey).
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 20000f;
            if (Fx.FxCatalog.Enabled && camera.GetComponent<Fx.FxBloom>() == null)
                camera.gameObject.AddComponent<Fx.FxBloom>(); // flames, spells and lit windows bleed light
            return camera;
        }

        /// <summary>Eases toward the player's anchor (UO pixels) at the given zoom.</summary>
        public void Follow(Vector2 anchorPixels, WorldStreamer world, int artScale, float zoom)
        {
            _camera.orthographicSize = Screen.height / 2f / zoom;
            // Center on the body (about 45 UO pixels above the feet), then drop the view a little so the
            // character sits above the journal panel.
            Vector2 target = world.ToUnity(anchorPixels) + new Vector2(0, 45 * artScale - Screen.height * 0.12f / zoom);
            Vector3 pos = _camera.transform.position;
            Vector2 smoothed = Vector2.Lerp(new Vector2(pos.x, pos.y), target, 1f - Mathf.Exp(-Time.deltaTime * 10f));
            if ((new Vector2(pos.x, pos.y) - target).sqrMagnitude > 4_000_000f || pos.z > -1)
                smoothed = target;
            _camera.transform.position = new Vector3(Mathf.Round(smoothed.x), Mathf.Round(smoothed.y), -10000f);
        }
    }
}
