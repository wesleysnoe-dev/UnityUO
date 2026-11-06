using UnityEngine;

namespace UOClient.GroundPrototype
{
    public enum CompareMode
    {
        Terrain = 0,
        Mesh = 1,
        SideBySide = 2,
    }

    /// <summary>
    /// Fixed orthographic isometric camera (pitch 30, yaw 45). No free-fly.
    /// Both sides are viewed with the identical camera pose relative to each side's origin, so
    /// switching Terrain/Mesh (or the split view) shows the same framing of the same sample.
    /// Side-by-side uses a second runtime camera on the right half with the same settings.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public sealed class IsoCompareCamera : MonoBehaviour
    {
        public float pitch = 30f;
        public float yaw = 45f;
        public float distance = 200f;
        public float framingMargin = 1.08f;

        public CompareMode Mode { get; private set; } = CompareMode.SideBySide;

        private Camera _main;
        private Camera _right;
        private Bounds _terrainBounds;
        private Bounds _meshBounds;
        private bool _configured;

        public void Configure(Bounds terrainBounds, Bounds meshBounds, CompareMode initial)
        {
            _main = GetComponent<Camera>();
            _terrainBounds = terrainBounds;
            _meshBounds = meshBounds;
            Setup(_main);

            if (_right == null)
            {
                var go = new GameObject("Iso Compare Camera (right, runtime)");
                go.transform.SetParent(transform.parent, false);
                _right = go.AddComponent<Camera>();
            }
            _right.CopyFrom(_main);
            Setup(_right);
            _right.depth = _main.depth + 1;
            _configured = true;
            SetMode(initial);
        }

        public void SetMode(CompareMode mode)
        {
            Mode = mode;
            if (!_configured) return;

            switch (mode)
            {
                case CompareMode.Terrain:
                    Place(_main, _terrainBounds, new Rect(0f, 0f, 1f, 1f));
                    _right.enabled = false;
                    break;
                case CompareMode.Mesh:
                    Place(_main, _meshBounds, new Rect(0f, 0f, 1f, 1f));
                    _right.enabled = false;
                    break;
                default:
                    Place(_main, _terrainBounds, new Rect(0f, 0f, 0.5f, 1f));
                    Place(_right, _meshBounds, new Rect(0.5f, 0f, 0.5f, 1f));
                    _right.enabled = true;
                    break;
            }
        }

        private void Setup(Camera cam)
        {
            cam.orthographic = true;
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = distance * 4f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.06f, 0.06f, 0.07f, 1f);
        }

        private void Place(Camera cam, Bounds target, Rect viewport)
        {
            var rot = Quaternion.Euler(pitch, yaw, 0f);
            cam.rect = viewport;
            cam.transform.rotation = rot;
            cam.transform.position = target.center - rot * Vector3.forward * distance;

            // Fit the 8 bounds corners into the view (same math for both sides; sides share bounds size).
            float halfUp = 0f, halfRight = 0f;
            Vector3 e = target.extents;
            for (int i = 0; i < 8; i++)
            {
                var c = new Vector3((i & 1) == 0 ? -e.x : e.x, (i & 2) == 0 ? -e.y : e.y, (i & 4) == 0 ? -e.z : e.z);
                var local = Quaternion.Inverse(rot) * c;
                halfRight = Mathf.Max(halfRight, Mathf.Abs(local.x));
                halfUp = Mathf.Max(halfUp, Mathf.Abs(local.y));
            }
            float aspect = Mathf.Max(0.1f, Screen.width * viewport.width / Mathf.Max(1f, Screen.height * viewport.height));
            cam.orthographicSize = Mathf.Max(halfUp, halfRight / aspect) * framingMargin;
        }

        private void LateUpdate()
        {
            // Re-fit on resolution change (Game view resize); pose itself never moves.
            if (_configured && (Screen.width != _lastW || Screen.height != _lastH))
            {
                _lastW = Screen.width;
                _lastH = Screen.height;
                SetMode(Mode);
            }
        }

        private int _lastW, _lastH;
    }
}
