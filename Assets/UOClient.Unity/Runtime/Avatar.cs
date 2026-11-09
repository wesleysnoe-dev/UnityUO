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
            _outline = new OutlineQuad(_go.transform, shader);
        }

        private readonly OutlineQuad _outline;

        /// <summary>
        /// A frame with a spectral layer (MobileArt.IsSpectral) is drawn blended, after the cutouts, as TazUO draws its
        /// SPECTRAL texels (black at alpha 1 - 1.5 red over what is behind); any other frame keeps the cutout state. The
        /// near-zero cutoff drops only the clear texels, so the silhouette's alpha-1 texels add nothing visible.
        /// </summary>
        public static void SetSpectral(Material m, bool spectral)
        {
            bool now = m.renderQueue == SpectralQueue;
            if (now == spectral)
                return;
            m.SetFloat("_SrcBlend", spectral ? (float)BlendMode.SrcAlpha : (float)BlendMode.One);
            m.SetFloat("_DstBlend", spectral ? (float)BlendMode.OneMinusSrcAlpha : (float)BlendMode.Zero);
            m.SetFloat("_Cutoff", spectral ? 0.5f / 255f : 0.5f);
            m.renderQueue = spectral ? SpectralQueue : -1;
        }

        private const int SpectralQueue = (int)RenderQueue.Transparent;

        /// <summary>The mouse-over outline is drawn around the frame last drawn.</summary>
        public bool Outlined => _outline.Visible;

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

        /// <summary>The frame last drawn and whether it was mirrored, for pixel-exact picking.</summary>
        public AtlasSlot LastSlot { get; private set; }
        public bool LastMirror { get; private set; }

        /// <summary>Under the mouse in the world (TazUO's SelectedObject.Object): outlined with HighlightGameObjects, in its own hues.</summary>
        public bool Selected { get; set; }

        /// <summary>The upper-floor cut height the frame was last drawn with (WorldSprite.shader hides it under the cut).</summary>
        public float LastCut { get; private set; }

        /// <summary>The frame last drawn, as the world pick (<see cref="MousePick"/>) sees it.</summary>
        public MousePick.Sprite PickSprite => MobileArt.PickSprite(LastRect, LastSlot, LastMirror, LastDepth, LastCut);

        /// <summary>Remaining time of the current step, in seconds.</summary>
        public float StepRemaining => IsMoving ? _stepDuration - _stepTime : 0f;

        public void SetLook(MobileLook look)
        {
            _look = look;
            _lookKey = look.Key;
        }

        /// <summary>In war mode (UOP creatures stand and walk ready) and dead (ghosts never take war actions).</summary>
        public bool WarMode { get; private set; }
        public bool Dead { get; private set; }

        public void SetCombat(bool warMode, bool dead)
        {
            WarMode = warMode;
            Dead = dead;
        }

        /// <summary>The fall being played (0xAF), or -1.</summary>
        public int DeathAction { get; private set; } = -1;
        public bool Dying => DeathAction >= 0;

        /// <summary>The fall has played through once (it then holds its last frame until the view drops it).</summary>
        public bool DeathFinished { get; private set; }

        private float _deathTime;

        /// <summary>Plays the body's fall once, as TazUO's DisplayDeath does for the dying mobile (DisplayDeath.cs:50-63).</summary>
        public void BeginDeath(bool second)
        {
            DeathAction = second ? _look.DeathActions.Second : _look.DeathActions.First;
            DeathFinished = false;
            _deathTime = 0;
            _stepDuration = 0;
            _server = null; // the fall replaces whatever the server was playing
        }

        public MobileLook Look => _look;

        private ServerPlayback _server;
        private float _serverPending;

        /// <summary>The server animation being played (0x6E / 0xE2), or null.</summary>
        public ServerPlayback ServerPlaying => _server;

        /// <summary>
        /// Plays a server animation, replacing any before it (TazUO's SetAnimation); <see cref="ServerAnimationGroups.None"/>
        /// ends it instead. A dying avatar keeps its fall.
        /// </summary>
        public void PlayServerAnimation(int action, int delay, int frameCount, int repeatCount, bool repeat, bool forward)
        {
            if (Dying)
                return;
            _server = action < 0 || action == ServerAnimationGroups.None
                ? null
                : new ServerPlayback(action, delay, frameCount, repeatCount, repeat, forward);
            _serverPending = 0;
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
            (Vector2 currentTile, float currentZ, bool currentLand) = StepPosition();
            _from = new Vector2Int(X, Y);
            _fromZ = Z;
            _fromPixels = current;
            _fromTile = currentTile;
            _fromZf = currentZ;
            _fromLand = currentLand;
            X = x;
            Y = y;
            Z = z;
            _running = run;
            _stepTime = 0;
            _stepDuration = StepTime(run);
            _server = null; // a step ends a server animation (Mobile.ProcessSteps, Mobile.cs:747-750)
        }

        private Vector2 _fromPixels;
        private MobileFrame[] _lastFrames;
        private bool _lastMirror;
        private int _prefetchedDir = -1;
        private MobileLook _prefetchedLook;
        private bool _prefetchedWar;

        public void Tick(float dt)
        {
            _animTime += dt;
            if (_server != null)
                _serverPending += dt; // stepped in Render, where the action's frame count is known
            if (Dying)
                _deathTime += dt;
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
                if (GroundFollow.On)
                {
                    // Smooth ground (GroundFollow): the feet on the drawn ground at the interpolated point of the step.
                    (Vector2 tile, float z, bool _) = StepPosition();
                    float t = _stepDuration <= 0 ? 1f : _stepTime / _stepDuration;
                    float drawn = GroundFollow.Z(tile, z, _stepDuration <= 0 ? OnLandNow : _fromLand, OnLandNow, t);
                    return new Vector2((tile.x - tile.y) * 22f, (tile.x + tile.y) * 22f + 22f - drawn * 4f) * _scale;
                }
                Vector2 to = TileAnchor(X, Y, Z);
                return _stepDuration <= 0 ? to : Vector2.Lerp(_fromPixels, to, _stepTime / _stepDuration);
            }
        }

        // GroundFollow: where the current step began (the interpolated tile and server Z, and whether that was on land).
        private Vector2 _fromTile;
        private float _fromZf;
        private bool _fromLand;

        private bool OnLandNow => GroundFollow.OnLand(X, Y, Z);

        /// <summary>The interpolated tile position and server Z right now, and whether the avatar is on land.</summary>
        private (Vector2 Tile, float Z, bool Land) StepPosition()
        {
            if (_stepDuration <= 0)
                return (new Vector2(X, Y), Z, OnLandNow);
            float t = _stepTime / _stepDuration;
            return (Vector2.Lerp(_fromTile, new Vector2(X, Y), t), Mathf.Lerp(_fromZf, Z, t), t < 0.5f ? _fromLand : OnLandNow);
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
            // Worn layers stack by the facing itself (TazUO's layerDir, taken before GetAnimDirection; MobileView.cs:151-154).
            int layerDir = Direction & 7;
            int fileDir = AnimationFile.FileDirection(layerDir, out bool mirror);
            bool war = WarMode && !Dead;
            if (layerDir != _prefetchedDir || !ReferenceEquals(_look, _prefetchedLook) || war != _prefetchedWar)
            {
                // A new direction, look or mode: ask for all its actions at once, so turning and starting to walk don't wait.
                _art.Prefetch(_look, _lookKey, fileDir, layerDir, WarMode, Dead);
                _prefetchedDir = layerDir;
                _prefetchedLook = _look;
                _prefetchedWar = war;
            }

            bool moving = _stepDuration > 0 && !Dying;
            MobileFrame[] frames = null;
            bool falling = false;
            if (Dying)
            {
                MobileFrame[] fall = _art.Get(_look, _lookKey, DeathAction, fileDir, layerDir);
                if (fall != null && fall.Length > 0)
                {
                    frames = fall;
                    falling = true;
                }
                else if (fall != null)
                    DeathFinished = true; // no fall for this body: gone at once (TazUO, Mobile.cs:720-724)
                else
                    _deathTime = 0;       // still being built: the fall starts when it can be seen
            }
            bool serverAnim = false;
            if (!falling && _server != null)
            {
                MobileFrame[] act = _art.Get(_look, _lookKey, _server.Action, fileDir, layerDir);
                if (act != null && act.Length == 0)
                    _server = null;               // the body has no such action: carry on as normal, never blank
                else if (act != null)
                {
                    _server.Advance(_serverPending, act.Length);
                    if (_server.Done)
                        _server = null;           // played out: back to walk / run / stand
                    else
                    {
                        frames = act;
                        serverAnim = true;
                    }
                }
                _serverPending = 0;               // still being built: the clock waits, the normal action shows meanwhile
            }
            if (!falling && !serverAnim)
            {
                // War actions a body lacks fall back to the peace ones, then to standing.
                var peace = MobileArt.Actions(_look);
                int action = MobileActions.Pick(MobileArt.Actions(_look, WarMode, Dead), moving, _running);
                frames = MobileActions.WithFallback(action, MobileActions.Pick(peace, moving, _running), peace.Stand,
                    a => _art.Get(_look, _lookKey, a, fileDir, layerDir), out _);
            }

            if (frames == null)
            {
                // Still being built: keep the last frames shown (if any) rather than flicker or stall.
                frames = _lastFrames;
                mirror = _lastMirror;
            }
            if (frames == null || frames.Length == 0)
            {
                _renderer.enabled = false;
                _outline.Update(false, _art, default, default, false, 0, 0);
                return;
            }
            _lastFrames = frames;
            _lastMirror = mirror;
            _renderer.enabled = true;

            MobileFrame f;
            if (falling)
            {
                // Once through, then hold the last frame.
                f = frames[MobileActions.DeathFrame(_deathTime, frames.Length)];
                DeathFinished = MobileActions.DeathDone(_deathTime, frames.Length);
            }
            else if (serverAnim)
                f = frames[_server.Frame(frames.Length)];
            else
            {
                // Walk/run cycles span two tiles; standing idles slowly.
                float frameDuration = !moving ? 0.25f : StepTime(_running) * 2f / frames.Length;
                f = frames[(int)(_animTime / frameDuration) % frames.Length];
            }

            Vector2 anchor = AnchorPixels;
            float left = anchor.x - (mirror ? f.Slot.Width - f.AnchorX : f.AnchorX);
            float top = anchor.y - f.AnchorY;
            Rect r = view.ToUnityRect(left, top, f.Slot.Width, f.Slot.Height);

            _material.mainTexture = _art.Atlas.Page(f.Slot.Page);
            SetSpectral(_material, _art.IsSpectral(f.Slot));
            LastSlot = f.Slot;
            LastMirror = mirror;
            LastRect = r;
            LastDepth = view.DepthFor(SortItem);
            LastCut = MeshBuilder.CutHeight(Z);
            // Characters on a floor above us are cut away with it (we ourselves always stand below the cut).
            if (MeshBuilder.FootMetadata)
            {
                // LiveGroundCompare sprite shadow (TEST): the feet where they are drawn, at the height between the two tiles.
                Vector2 foot = view.ToUnity(anchor);
                float footZ = _stepDuration <= 0 ? Z : Mathf.Lerp(_fromZ, Z, _stepTime / _stepDuration);
                MeshBuilder.SetQuad(_mesh, r, _art.Atlas.Uv(f.Slot), LastDepth, mirror, LastCut, new Vector4(foot.x, foot.y, footZ, 1f));
                if (!Dead) // a ghost casts no shadow
                    MeshBuilder.ShadowCasters?.Mobile(this, anchor, footZ);
            }
            else
                MeshBuilder.SetQuad(_mesh, r, _art.Atlas.Uv(f.Slot), LastDepth, mirror, LastCut);
            // The frame is every worn layer composited, so its outline is the whole equipped silhouette.
            _outline.Update(ObjectHighlight.OutlinesInWorld(Selected, true, false, ObjectHighlight.HighlightGameObjects),
                _art, f.Slot, r, mirror, LastDepth, LastCut);
        }

        private Vector2 TileAnchor(int x, int y, int z)
        {
            return new Vector2((x - y) * 22, (x + y) * 22 + 22 - z * 4) * _scale;
        }

        public void Dispose()
        {
            _outline.Dispose();
            UnityEngine.Object.Destroy(_material);
            UnityEngine.Object.Destroy(_mesh);
            UnityEngine.Object.Destroy(_go);
        }
    }
}
