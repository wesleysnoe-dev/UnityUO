using System;
using System.Collections.Generic;
using UnityEngine;
using UOClient.Core;
using UOClient.IO.Formats;

namespace UOClient.Unity.UI
{
    /// <summary>
    /// A window (gump) on screen. Coordinates are UO pixels; the manager scales them for the display.
    /// UO conventions: drag anywhere on the background to move, right-click to close.
    /// </summary>
    public abstract class UiWindow
    {
        private static int _nextId = 7000;

        public readonly int Id = _nextId++;
        public Rect Rect;
        internal UiManager Ui;

        /// <summary>Identifies what the window shows, so opening it again brings the existing one forward.</summary>
        public abstract string Key { get; }

        public virtual bool CanClose => true;

        /// <summary>Whether Esc closes it (the topmost such window goes); server gumps never do, as in TazUO.</summary>
        public virtual bool ClosesOnEsc => CanClose;

        /// <summary>False for fixed parts of the screen (the top bar): dragging the background does nothing.</summary>
        public virtual bool Movable => true;

        /// <summary>Whether a new window steps down-right off one already open at its spot (UiManager.PlaceNew).</summary>
        public virtual bool StepsAsideOnOpen => true;

        /// <summary>Draws the window's contents in window-local UO pixels.</summary>
        public abstract void Draw();

        /// <summary>Called once when the window is first opened (art and session are available from here on).</summary>
        public virtual void OnOpened() { }

        /// <summary>Called once when the window is closed (by right-click or code).</summary>
        public virtual void OnClosed() { }

        /// <summary>A dragged item was released over this window at a local position; return true if handled.</summary>
        public virtual bool Drop(DraggedItem item, Vector2 local) => false;

        /// <summary>Called every frame before drawing, for windows that track the world (e.g. close when their container goes away).</summary>
        public virtual bool StillValid => true;

        /// <summary>The item or mobile under the mouse in this window (for tooltips), or 0; windows set it while drawing.</summary>
        public uint Hovered { get; protected set; }

        /// <summary>A plain text tooltip under the mouse (a server gump's cliloc tooltip), or null.</summary>
        public string HoveredText { get; protected set; }

        /// <summary>
        /// What HoveredText belongs to, when its text changes while hovered (a buff's time left): the tooltip keeps
        /// showing while this stays the same. Null compares the text itself.
        /// </summary>
        public string HoveredTextId { get; protected set; }

        protected GameSession Session => Ui.Session;
        protected GumpArt Art => Ui.Art;

        protected void Close() => Ui.Close(this);

        /// <summary>The mouse is on this window and no window above covers that spot (TazUO's MouseIsOver), so its items may highlight.</summary>
        protected bool MouseIsOver => Ui.TopWindowAt(Rect.position + Event.current.mousePosition) == this;

        /// <summary>Draws UI art at a local position, at native size.</summary>
        protected static void DrawImage(UiImage img, float x, float y)
        {
            if (img != null)
                GUI.DrawTexture(new Rect(x, y, img.Width, img.Height), img.Texture);
        }

        /// <summary>A resizable background: nine gumps from <paramref name="id"/> (corners, edges, center).</summary>
        protected void DrawResizePic(int id, float x, float y, float w, float h)
        {
            UiImage tl = Art.Gump(id), t = Art.Gump(id + 1), tr = Art.Gump(id + 2);
            UiImage l = Art.Gump(id + 3), m = Art.Gump(id + 4), rr = Art.Gump(id + 5);
            UiImage bl = Art.Gump(id + 6), b = Art.Gump(id + 7), br = Art.Gump(id + 8);
            if (tl == null || br == null)
            {
                DrawImage(Art.Gump(id), x, y); // not a nine-part set: a single picture
                return;
            }

            float left = tl.Width, top = tl.Height, right = br.Width, bottom = br.Height;
            DrawTiled(m, x + left, y + top, w - left - right, h - top - bottom);
            DrawTiled(t, x + left, y, w - left - (tr?.Width ?? right), top);
            DrawTiled(b, x + (bl?.Width ?? left), y + h - bottom, w - (bl?.Width ?? left) - right, bottom);
            DrawTiled(l, x, y + top, left, h - top - (bl?.Height ?? bottom));
            DrawTiled(rr, x + w - right, y + (tr?.Height ?? top), right, h - (tr?.Height ?? top) - bottom);
            DrawImage(tl, x, y);
            DrawImage(tr, x + w - (tr?.Width ?? 0), y);
            DrawImage(bl, x, y + h - (bl?.Height ?? 0));
            DrawImage(br, x + w - right, y + h - bottom);
        }

        protected static void DrawTiled(UiImage img, float x, float y, float w, float h)
        {
            if (img == null || w <= 0 || h <= 0)
                return;
            GUI.BeginGroup(new Rect(x, y, w, h));
            for (float ty = 0; ty < h; ty += img.Height)
                for (float tx = 0; tx < w; tx += img.Width)
                    GUI.DrawTexture(new Rect(tx, ty, img.Width, img.Height), img.Texture);
            GUI.EndGroup();
        }

        /// <summary>A gump button (normal / pressed art, and optional mouse-over art); returns true when clicked.</summary>
        protected bool GumpButton(int normal, int pressed, float x, float y, int over = -1)
        {
            UiImage up = Art.Gump(normal);
            if (up == null)
                return false;
            var r = new Rect(x, y, up.Width, up.Height);
            bool inside = r.Contains(Event.current.mousePosition);
            bool down = inside && Input.GetMouseButton(0);
            UiImage hover = inside && over >= 0 ? Art.Gump(over) : null;
            DrawImage(down ? Art.Gump(pressed) ?? up : hover ?? up, x, y);
            return GUI.Button(r, GUIContent.none, GUIStyle.none);
        }

        private static GUIStyle _label;

        /// <summary>Plain text in a UI color (dark ink by default, for parchment and stone backgrounds).</summary>
        protected static void Text(Rect r, string text, Color? color = null, int size = 13, TextAnchor align = TextAnchor.UpperLeft, bool bold = false)
        {
            _label ??= new GUIStyle(GUI.skin.label) { wordWrap = false, clipping = TextClipping.Clip, padding = new RectOffset(), richText = true };
            _label.fontSize = size;
            _label.alignment = align;
            _label.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
            _label.normal.textColor = color ?? new Color(0.92f, 0.88f, 0.78f);
            GUI.Label(r, text, _label);
        }

        /// <summary>
        /// While the server wants a target, a click on something in a window targets it instead of using it.
        /// Returns true if the click was taken for targeting.
        /// </summary>
        protected bool TryTarget(uint serial)
        {
            if (Session.Target == null)
                return false;
            Session.TargetObject(serial);
            Event.current.Use();
            return true;
        }
    }

    /// <summary>An item picked up from a window or the ground, following the cursor until dropped.</summary>
    public sealed class DraggedItem
    {
        public uint Serial;
        public ushort Graphic;
        public ushort Hue;
        public int Amount;
        public uint FromContainer;    // 0 if it came from the paperdoll or the ground
        public bool FromGround;       // picked up in the world at (FromX, FromY, FromZ)
        public int FromX, FromY, FromZ;
        public Vector2 Grab;           // where in the art it was grabbed (art pixels)
    }

    /// <summary>
    /// Owns the open windows: draws them with IMGUI windows (which give proper focus and stacking),
    /// routes item drag and drop between them, and tells the world whether the pointer is over UI.
    /// </summary>
    public sealed class UiManager
    {
        private readonly List<UiWindow> _windows = new List<UiWindow>();
        private bool _restack; // the list order changed outside a click: IMGUI's stacking is to follow it (OnGUI)

        public UiManager(GameSession session, GumpArt art, Cliloc cliloc)
        {
            Session = session;
            Art = art;
            Cliloc = cliloc;
            _rectOf = RectOf;
        }

        private readonly Func<int, (float, float, float, float)> _rectOf;

        public GameSession Session { get; }
        public GumpArt Art { get; }
        public Cliloc Cliloc { get; }

        /// <summary>Display pixels per UO pixel.</summary>
        public float Scale { get; set; } = 1f;

        public DraggedItem Dragging { get; private set; }

        /// <summary>Where a drag released outside every window goes (the world); null cancels the drag.</summary>
        public Action<DraggedItem, Vector2> DropOutside { get; set; }

        /// <summary>Buttons that act on the game rather than the window (paperdoll: "help", "skills", "war"...).</summary>
        public Action<string> Command { get; set; }

        public IReadOnlyList<UiWindow> Windows => _windows;

        /// <summary>Where server gumps were left, by type id, for this session (TazUO's position cache).</summary>
        public GumpPositions ServerGumpPositions { get; } = new GumpPositions();

        public T Open<T>(T window) where T : UiWindow
        {
            // Windows already gone (a server gump closed just before this one is sent again) close first, so the
            // new one sees where they were left and does not step aside from them.
            for (int i = _windows.Count - 1; i >= 0; i--)
                if (!_windows[i].StillValid)
                    Close(_windows[i]);

            for (int i = 0; i < _windows.Count; i++)
            {
                if (_windows[i].Key == window.Key)
                {
                    // Already open: keep its position, bring it forward.
                    UiWindow existing = _windows[i];
                    _windows.RemoveAt(i);
                    _windows.Add(existing);
                    _restack = true;
                    return existing as T ?? window;
                }
            }
            window.Ui = this;
            _windows.Add(window);
            _restack = true;
            window.OnOpened();
            PlaceNew(window);
            return window;
        }

        /// <summary>Keeps a new window on screen, and steps it down-right off any window opened at the same spot.</summary>
        private void PlaceNew(UiWindow window)
        {
            Rect r = window.Rect;
            float maxX = Screen.width / Scale, maxY = Screen.height / Scale;
            for (int tries = 0; window.StepsAsideOnOpen && tries < 12; tries++)
            {
                bool clash = false;
                foreach (UiWindow w in _windows)
                    if (w != window && Mathf.Abs(w.Rect.x - r.x) < 12 && Mathf.Abs(w.Rect.y - r.y) < 12)
                        clash = true;
                if (!clash)
                    break;
                r.x += 28;
                r.y += 28;
            }
            r.x = Mathf.Clamp(r.x, 0, Mathf.Max(0, maxX - Mathf.Min(r.width, 120)));
            r.y = Mathf.Clamp(r.y, 0, Mathf.Max(0, maxY - 40));
            window.Rect = r;
        }

        public void Close(UiWindow window)
        {
            if (_windows.Remove(window))
                window.OnClosed();
        }

        private UiWindow _dragWindow; // a window carried by the held left button from a press outside it (BeginWindowDrag)
        private Vector2 _dragGrab;

        /// <summary>
        /// Carries a window with the mouse while the left button stays down, held at <paramref name="grab"/> (window-local):
        /// TazUO's UIManager.AttemptDragControl for a health bar opened by dragging a mobile, whose press began in the world
        /// (so IMGUI's own GUI.DragWindow never saw it).
        /// </summary>
        public void BeginWindowDrag(UiWindow window, Vector2 grab)
        {
            _dragWindow = window;
            _dragGrab = grab;
        }

        private void FollowWindowDrag()
        {
            if (_dragWindow == null)
                return;
            if (!_windows.Contains(_dragWindow) || !Input.GetMouseButton(0))
            {
                _dragWindow = null;
                return;
            }
            (float x, float y) = UiRouting.ScreenToUi(Input.mousePosition.x, Input.mousePosition.y, Screen.height, Scale);
            _dragWindow.Rect.position = new Vector2(x, y) - _dragGrab;
        }

        public UiWindow Find(string key) => _windows.Find(w => w.Key == key);

        /// <summary>
        /// True when a screen point (Unity screen coordinates, bottom-left origin) is over a window: the whole window rect,
        /// the one IMGUI takes the window's events in and clips its drawing to, at any ui.Scale (UiRouting.ScreenToUi).
        /// </summary>
        public bool IsOverUi(Vector2 screen)
        {
            (float x, float y) = UiRouting.ScreenToUi(screen.x, screen.y, Screen.height, Scale);
            return TopWindowAt(new Vector2(x, y)) != null;
        }

        /// <summary>
        /// The front window at a UI point, or null. Our list order is the stacking order: OnGUI makes IMGUI's follow it,
        /// so this is also the window IMGUI hands the pointer's events to.
        /// </summary>
        public UiWindow TopWindowAt(Vector2 uiPoint)
        {
            int top = UiRouting.TopAt(_windows.Count, _rectOf, uiPoint.x, uiPoint.y);
            return top >= 0 ? _windows[top] : null;
        }

        private (float, float, float, float) RectOf(int i)
        {
            Rect r = _windows[i].Rect;
            return (r.x, r.y, r.width, r.height);
        }

        /// <summary>True while a text box has the keyboard (movement keys should not walk).</summary>
        public bool HasKeyboardFocus => GUIUtility.keyboardControl != 0;

        /// <summary>Takes the keyboard from whatever text box has it, giving it back to the world (TazUO's RestoreSystemChatFocus).</summary>
        public void ReleaseKeyboard() => GUIUtility.keyboardControl = 0;

        /// <summary>Picks an item up: tells the server and lets the cursor carry it.</summary>
        public void BeginDrag(DraggedItem item)
        {
            if (Dragging != null)
                return;
            Dragging = item;
            Session.Lift(item.Serial, item.Amount);
            UiImage art = Art.Item(item.Graphic, item.Hue);
            if (art != null)
                Cursor.SetCursor(art.Texture, item.Grab, CursorMode.ForceSoftware);
        }

        /// <summary>
        /// A drag starting: with Shift held on a stack, asks how many to lift first (ClassicUO's split menu); otherwise
        /// lifts the whole thing at once.
        /// </summary>
        public void Lift(DraggedItem item, bool split)
        {
            if (Dragging != null)
                return;
            if (!split || !GameSession.CanSplit(item.Amount, Art.Data.TileData.GetStatic(item.Graphic).Flags))
            {
                BeginDrag(item);
                return;
            }
            if (Find("split") is UiWindow old)
                Close(old);
            Vector2 m = Input.mousePosition;
            Open(new SplitStackWindow(item, new Vector2(m.x / Scale, (Screen.height - m.y) / Scale)));
        }

        /// <summary>Drops the cursor item without telling the server (it already refused the lift).</summary>
        public void CancelDrag() => EndDrag();

        private void EndDrag()
        {
            Dragging = null;
            Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
        }

        /// <summary>Call from OnGUI after the HUD.</summary>
        public void OnGUI()
        {
            for (int i = _windows.Count - 1; i >= 0; i--)
                if (!_windows[i].StillValid)
                    Close(_windows[i]);

            Matrix4x4 saved = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(Scale, Scale, 1));
            Event e = Event.current;

            // A drag ends on mouse-up anywhere: the topmost window under the cursor gets the drop.
            if (Dragging != null && e.type == EventType.MouseUp && e.button == 0)
            {
                DraggedItem item = Dragging;
                EndDrag();
                bool handled = false;
                for (int i = _windows.Count - 1; i >= 0 && !handled; i--)
                {
                    UiWindow w = _windows[i];
                    if (w.Rect.Contains(e.mousePosition))
                    {
                        handled = w.Drop(item, e.mousePosition - w.Rect.position);
                        break;
                    }
                }
                if (!handled)
                {
                    if (DropOutside != null && !IsPointInWindows(e.mousePosition))
                        DropOutside(item, new Vector2(e.mousePosition.x * Scale, e.mousePosition.y * Scale));
                    else if (item.FromGround)
                        Session.DropOnGround(item.Serial, item.FromX, item.FromY, item.FromZ); // put it back
                    else if (item.FromContainer != 0)
                        Session.DropInto(item.Serial, item.FromContainer, item.FromX, item.FromY); // put it back
                    else
                        Session.Equip(item.Serial, 0, Session.World.PlayerSerial);
                }
                e.Use();
            }

            FollowWindowDrag();
            foreach (UiWindow w in _windows.ToArray())
            {
                UiWindow window = w;
                window.Rect = GUI.Window(window.Id, window.Rect, _ => DrawWindow(window), GUIContent.none, GUIStyle.none);
            }

            // A window opened or brought forward by code: stack IMGUI's windows back to front in our order, so the window
            // drawn on top is the one TopWindowAt (the wheel, IsOverUi, drops) takes for the front.
            if (_restack && e.type == EventType.Layout)
            {
                foreach (UiWindow w in _windows)
                    GUI.BringWindowToFront(w.Id);
                _restack = false;
            }

            GUI.matrix = saved;
        }

        private bool IsPointInWindows(Vector2 uiPoint)
        {
            foreach (UiWindow w in _windows)
                if (w.Rect.Contains(uiPoint))
                    return true;
            return false;
        }

        private void DrawWindow(UiWindow w)
        {
            Event e = Event.current;
            // Only the front window under the pointer takes a press (mousePosition is window-local here).
            if (e.type == EventType.MouseDown && TopWindowAt(w.Rect.position + e.mousePosition) == w)
            {
                // Clicked: bring forward (our list order is the stacking order used for drops).
                if (_windows.Remove(w))
                    _windows.Add(w);
                if (e.button == 1 && w.CanClose)
                {
                    Close(w);
                    e.Use();
                    return;
                }
            }

            w.Draw();

            // Last, so controls drawn above take their clicks first: the rest of the window moves it.
            if (Dragging == null && w.Movable)
                GUI.DragWindow();
        }
    }
}
