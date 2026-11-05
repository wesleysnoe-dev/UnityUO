using System;
using UnityEngine;
using UnityEngine.Rendering;
using UOClient.ArtPipeline;
using UOClient.IO.Formats;

namespace UOClient.Unity
{
    /// <summary>
    /// A character on screen: one quad that depth-sorts among the world's sprites, animated from the
    /// shared <see cref="MobileArt"/> frames for its <see cref="MobileLook"/>. Moves tile to tile with
    /// UO's walk/run timing, either driven locally (the player) or from server updates (everyone else).
    /// </summary>
    public sealed class Avatar : IDisposable
    {
        public const float WalkStep = 0.4f, RunStep = 0.2f;

        /// <summary>Seconds per tile; riding halves them, as the server expects.</summary>
        public float StepTime(bool run) => (run ? RunStep : WalkStep) * (_look != null && _look.Mounted ? 0.5f : 1f);

        private readonly MobileArt _art;
        private readonly int _scale;
        private readonly GameObject _go;
        private readonly Mesh _mesh;
        private readonly Material _material;
        private readonly MeshRenderer _renderer;
        private MobileLook _look;
        private string _lookKey;

        private Vector2Int _from;
        private int _fromZ;
        private float _stepTime, _stepDuration;
        private float _animTime;
        private bool _running;

        public Avatar(MobileArt art, Transform parent, Shader shader, int scale, MobileLook look, int x, int y, int z, string name = "Avatar")
        {
            _art = art;
            _scale = scale;
            X = x;
            Y = y;
            Z = z;
            Direction = 3;
            SetLook(look);

            _go = new GameObject(name);
            _go.transform.SetParent(parent, false);
            _mesh = new Mesh { name = name + " quad" };
            _go.AddComponent<MeshFilter>().sharedMesh = _mesh;
            _material = new Material(shader) { name = name };
            _renderer = _go.AddComponent<MeshRenderer>();
            _renderer.sharedMaterial = _material;
            _renderer.shadowCastingMode = ShadowCastingMode.Off;
        }

        public int X { get; private set; }
        public int Y { get; private set; }
        public int Z { get; private set; }

        /// <summary>UO direction: 0 = north (screen up-right) clockwise to 7 = up.</summary>
        public int Direction { get; private set; }

        public bool IsMoving => _stepDuration > 0;

        /// <summary>Where the avatar was last drawn (Unity units) and its depth, for mouse picking. Empty if not drawn.</summary>
        public Rect LastRect { get; private set; }
        public float LastDepth { get; private set; }
        public bool Visible => _renderer.enabled;

        /// <summary>Remaining time of the current step, in seconds.</summary>
        public float StepRemaining => IsMoving ? _stepDuration - _stepTime : 0f;

        public void SetLook(MobileLook look)
        {
            _look = look;
            _lookKey = look.Key;
        }

        public static Vector2Int Delta(int direction)
        {
            switch (direction & 7)
            {
                case 0: return new Vector2Int(0, -1);
                case 1: return new Vector2Int(1, -1);
                case 2: return new Vector2Int(1, 0);
                case 3: return new Vector2Int(1, 1);
                case 4: return new Vector2Int(0, 1);
                case 5: return new Vector2Int(-1, 1);
                case 6: return new Vector2Int(-1, 0);
                default: return new Vector2Int(-1, -1);
            }
        }

        /// <summary>Starts moving one tile; the caller has already decided the destination is walkable.</summary>
        public void BeginStep(int direction, int newZ, bool run)
        {
            Direction = direction & 7;
            Vector2Int d = Delta(Direction);
            StartMove(X + d.x, Y + d.y, newZ, run);
        }

        /// <summary>Follows a server position: animates a one-tile move, snaps anything else.</summary>
        public void MoveTo(int x, int y, int z, int direction, bool run)
        {
            Direction = direction & 7;
            if (x == X && y == Y)
            {
                Z = z;
                return;
            }
            if (Math.Max(Math.Abs(x - X), Math.Abs(y - Y)) == 1)
                StartMove(x, y, z, run);
            else
                Teleport(x, y, z);
        }

        public void Face(int direction) => Direction = direction & 7;

        public void Teleport(int x, int y, int z)
        {
            X = x;
            Y = y;
            Z = z;
            _stepDuration = 0;
        }

        private void StartMove(int x, int y, int z, bool run)
        {
            // Keep where the avatar is drawn right now, so a step that starts mid-step does not jump.
            Vector2 current = AnchorPixels;
            _from = new Vector2Int(X, Y);
            _fromZ = Z;
            _fromPixels = current;
            X = x;
            Y = y;
            Z = z;
            _running = run;
            _stepTime = 0;
            _stepDuration = StepTime(run);
        }

        private Vector2 _fromPixels;
        private MobileFrame[] _lastFrames;
        private bool _lastMirror;
        private int _prefetchedDir = -1;
        private MobileLook _prefetchedLook;

        public void Tick(float dt)
        {
            _animTime += dt;
            if (_stepDuration <= 0)
                return;
            _stepTime += dt;
            if (_stepTime >= _stepDuration)
                _stepDuration = 0;
        }

        /// <summary>Anchor (feet, tile center) in world-screen pixels at the art scale, interpolated while stepping.</summary>
        public Vector2 AnchorPixels
        {
            get
            {
                Vector2 to = TileAnchor(X, Y, Z);
                return _stepDuration <= 0 ? to : Vector2.Lerp(_fromPixels, to, _stepTime / _stepDuration);
            }
        }

        /// <summary>The item used to sort the avatar among map placements.</summary>
        public DrawItem SortItem
        {
            get
            {
                // While moving, sort by whichever tile is nearer the viewer so the avatar never dips behind the ground it walks over.
                int x = X, y = Y, z = Z;
                if (_stepDuration > 0 && _from.x + _from.y > X + Y)
                {
                    x = _from.x;
                    y = _from.y;
                    z = Math.Max(Z, _fromZ);
                }
                return new DrawItem { Kind = DrawKind.Mobile, X = x, Y = y, Z = (sbyte)z, PriorityZ = z + 2 };
            }
        }

        public void Render(IWorldSpace view)
        {
            int fileDir = AnimationFile.FileDirection(Direction, out bool mirror);
            if (fileDir != _prefetchedDir || !ReferenceEquals(_look, _prefetchedLook))
            {
                // A new direction or look: ask for all its actions at once, so turning and starting to walk don't wait.
                _art.Prefetch(_look, _lookKey, fileDir);
                _prefetchedDir = fileDir;
                _prefetchedLook = _look;
            }

            var (walk, run, stand) = MobileArt.Actions(_look);
            int action = _stepDuration > 0 ? (_running ? run : walk) : stand;
            MobileFrame[] frames = _art.Get(_look, _lookKey, action, fileDir);
            if ((frames == null || frames.Length == 0) && action != stand)
                frames = _art.Get(_look, _lookKey, stand, fileDir) ?? frames;

            if (frames == null)
            {
                // Still being built: keep the last frames shown (if any) rather than flicker or stall.
                frames = _lastFrames;
                mirror = _lastMirror;
            }
            if (frames == null || frames.Length == 0)
            {
                _renderer.enabled = false;
                return;
            }
            _lastFrames = frames;
            _lastMirror = mirror;
            _renderer.enabled = true;

            // Walk/run cycles span two tiles; standing idles slowly.
            float frameDuration = action == stand ? 0.25f : StepTime(_running) * 2f / frames.Length;
            MobileFrame f = frames[(int)(_animTime / frameDuration) % frames.Length];

            Vector2 anchor = AnchorPixels;
            float left = anchor.x - (mirror ? f.Slot.Width - f.AnchorX : f.AnchorX);
            float top = anchor.y - f.AnchorY;
            Rect r = view.ToUnityRect(left, top, f.Slot.Width, f.Slot.Height);

            _material.mainTexture = _art.Atlas.Page(f.Slot.Page);
            LastRect = r;
            LastDepth = view.DepthFor(SortItem);
            // Characters on a floor above us are cut away with it (we ourselves always stand below the cut).
            MeshBuilder.SetQuad(_mesh, r, _art.Atlas.Uv(f.Slot), LastDepth, mirror, MeshBuilder.CutHeight(Z));
        }

        private Vector2 TileAnchor(int x, int y, int z)
        {
            return new Vector2((x - y) * 22, (x + y) * 22 + 22 - z * 4) * _scale;
        }

        public void Dispose()
        {
            UnityEngine.Object.Destroy(_material);
            UnityEngine.Object.Destroy(_mesh);
            UnityEngine.Object.Destroy(_go);
        }
    }
}
