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

        /// <summary>A book window's two-page art at its corner (what the opening animation turns), or null.</summary>
        public virtual UiImage IntroPages => null;

        /// <summary>The window is drawn as UO's scroll (DrawScroll): its opening unrolls exactly over it.</summary>
        public virtual bool IsScroll => false;

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

        /// <summary>The mouse-over outline of art drawn at (x, y): drawn before the art, so only its edge shows around it.</summary>
        protected void DrawOutline(UiImage img, float x, float y) => DrawImage(Art.Outline(img), x - 1, y - 1);

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

        /// <summary>Ink on parchment (the scroll windows' text), and its mouse-over red.</summary>
        protected static readonly Color Ink = new Color(0.22f, 0.13f, 0.06f), InkHot = new Color(0.55f, 0.08f, 0.05f),
            InkFaded = new Color(0.42f, 0.32f, 0.2f);

        /// <summary>UO's expandable scroll (TazUO ExpandableScroll 0x0820): its width.</summary>
        public const int ScrollArtWidth = 300;

        /// <summary>
        /// UO's expandable scroll at the window's corner, <paramref name="height"/> tall (TazUO's ExpandableScroll, graphic
        /// 0x0820): the top roll, the parchment (0x0821 and 0x0822 tiled) between, the bottom roll at its foot. False when
        /// the art is missing (the caller draws a plain background).
        /// </summary>
        protected bool DrawScroll(float height, float width = ScrollArtWidth)
        {
            // The sharpened pieces (HD, else Scale2x x2, filtered), at the art's sizes times k: crisp at any UI scale.
            UiImage top = Art.GumpDetailed(0x0820), right = Art.GumpDetailed(0x0821), mid = Art.GumpDetailed(0x0822), bottom = Art.GumpDetailed(0x0823);
            if (top == null || mid == null || bottom == null)
                return false;
            float k = width / Mathf.Max(top.Width, Mathf.Max(mid.Width, bottom.Width));
            float topH = top.Height * k, botH = bottom.Height * k, midY = topH, midH = height - topH - botH;
            ScaledTiles(right ?? mid, k, (width - (right ?? mid).Width * k) / 2f, midY, midH);
            ScaledTiles(mid, k, (width - mid.Width * k) / 2f, midY, midH);
            GUI.DrawTexture(new Rect(0, 0, top.Width * k, topH), top.Texture, ScaleMode.StretchToFill);
            int off = top.Width - bottom.Width;
            GUI.DrawTexture(new Rect((off / 2 + off / 4) * k, height - botH, bottom.Width * k, botH), bottom.Texture, ScaleMode.StretchToFill);
            return true;
        }

        /// <summary>A picture repeated downward from (x, y), each copy k times its art size, cut off at <paramref name="h"/>.</summary>
        private static void ScaledTiles(UiImage img, float k, float x, float y, float h)
        {
            if (img == null || h <= 0)
                return;
            float tw = img.Width * k, th = img.Height * k;
            GUI.BeginGroup(new Rect(x, y, tw, h));
            for (float ty = 0; ty < h; ty += th)
                GUI.DrawTexture(new Rect(0, ty, tw, th), img.Texture, ScaleMode.StretchToFill);
            GUI.EndGroup();
        }

        /// <summary>A part (art pixels) of a gump drawn at (x, y), k times its size, from the sharpened texture.</summary>
        protected void DrawGumpPart(int gump, RectInt part, float x, float y, float k)
        {
            UiImage img = Art.GumpDetailed(gump);
            if (img == null)
                return;
            var uv = new Rect((float)part.x / img.Width, 1f - (float)(part.y + part.height) / img.Height,
                              (float)part.width / img.Width, (float)part.height / img.Height);
            GUI.DrawTextureWithTexCoords(new Rect(x, y, part.width * k, part.height * k), img.Texture, uv);
        }

        /// <summary>
        /// A picture in three parts, its middle band repeated to <paramref name="midHeight"/> (TazUO's GumpPicTexture
        /// split of the shop's scrolls): the top <paramref name="topH"/> rows, the middle rows tiled, the bottom
        /// <paramref name="bottomH"/> rows. Returns the drawn height.
        /// </summary>
        protected static float DrawStretchedGump(UiImage img, float x, float y, int topH, int bottomH, float midHeight)
        {
            if (img == null)
                return 0f;
            int srcMid = img.Height - topH - bottomH;
            Part(img, x, y, 0, topH);
            GUI.BeginGroup(new Rect(x, y + topH, img.Width, midHeight));
            for (float ty = 0; ty < midHeight; ty += srcMid)
                Part(img, 0, ty, topH, srcMid);
            GUI.EndGroup();
            Part(img, x, y + topH + midHeight, img.Height - bottomH, bottomH);
            return topH + midHeight + bottomH;
        }

        /// <summary>Rows <paramref name="srcY"/>..+<paramref name="srcH"/> of a picture drawn at (x, y), native size.</summary>
        private static void Part(UiImage img, float x, float y, int srcY, int srcH)
        {
            if (srcH <= 0)
                return;
            var uv = new Rect(0f, 1f - (float)(srcY + srcH) / img.Height, 1f, (float)srcH / img.Height);
            GUI.DrawTextureWithTexCoords(new Rect(x, y, img.Width, srcH), img.Texture, uv);
        }

        /// <summary>A word in ink that reddens under the mouse and acts as a button (on parchment, where grey buttons jar).</summary>
        protected bool InkButton(Rect r, string label, int size = 13)
        {
            bool over = r.Contains(Event.current.mousePosition);
            Text(r, label, over ? InkHot : Ink, size, TextAnchor.MiddleCenter, true);
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
        public ushort DisplayGraphic;  // the art the cursor shows (ItemStack.HeldGraphic); set by BeginDrag, 0 before
        public bool Twice;             // the cursor shows two copies (ItemStack.HeldDrawsTwice)
    }

    /// <summary>
    /// Owns the open windows: draws them with IMGUI windows (which give proper focus and stacking),
    /// routes item drag and drop between them, and tells the world whether the pointer is over UI.
    /// </summary>
    public sealed partial class UiManager
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
            ForgetIntro(window);
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
        public bool IsOverUi(Vector2 screen) => TopWindowAtScreen(screen) != null;

        /// <summary>The front window at a screen point (Unity screen coordinates, bottom-left origin), or null.</summary>
        public UiWindow TopWindowAtScreen(Vector2 screen)
        {
            (float x, float y) = UiRouting.ScreenToUi(screen.x, screen.y, Screen.height, Scale);
            return TopWindowAt(new Vector2(x, y));
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
            // TazUO's ItemHold.Set: the art by the whole stack where it lay, one coin when one is lifted, two copies of a
            // lifted stack (ItemStack). Read before the lift, while the world still has the item's amount.
            int total = Session.World.Contained.TryGetValue(item.Serial, out ContainedItem inside) ? inside.Amount
                : Session.World.Items.TryGetValue(item.Serial, out WorldItem ground) ? ground.Amount : item.Amount;
            item.DisplayGraphic = (ushort)ItemStack.HeldGraphic(item.Graphic, item.Amount, Math.Max(total, item.Amount));
            item.Twice = ItemStack.HeldDrawsTwice(item.Graphic, item.DisplayGraphic, item.Amount, Art.Data.TileData.GetStatic(item.Graphic).Flags);
            Session.Lift(item.Serial, item.Amount);
            // The art drawn as windows draw it, at the UI scale, held where it was grabbed: what the cursor shows is where
            // the drop puts it (a window's Drop takes local - Grab). Not art.Texture: an HD texture is larger than the
            // stock art Grab is measured on, and window textures are not readable. A second copy goes 5 pixels down-right
            // of the first, so the grab point on the first is unchanged.
            UiImage art = Art.Item(item.DisplayGraphic, item.Hue);
            if (art != null)
            {
                int aw = art.Width + (item.Twice ? ItemStack.GumpOffset : 0), ah = art.Height + (item.Twice ? ItemStack.GumpOffset : 0);
                var (w, h, hx, hy) = MousePick.DragCursor(aw, ah, item.Grab.x, item.Grab.y, Scale);
                Cursor.SetCursor(Art.CursorTexture(art, w, h, item.Twice), new Vector2(hx, hy), CursorMode.ForceSoftware);
            }
        }

        /// <summary>
        /// The options' HoldShiftToSplitStack (TazUO's): on, a stack dragged with Shift asks how many; off, a plain drag
        /// asks and a Shift drag lifts the lot (GameOptions.OpensSplitMenu).
        /// </summary>
        public static bool HoldShiftToSplitStack { get; set; } = false;

        /// <summary>
        /// A drag starting: on a stack, asks how many to lift first (ClassicUO's split menu) when
        /// <see cref="HoldShiftToSplitStack"/> says so for <paramref name="shiftHeld"/>; otherwise lifts the whole thing at once.
        /// </summary>
        public void Lift(DraggedItem item, bool shiftHeld)
        {
            if (Dragging != null)
                return;
            if (!GameOptions.OpensSplitMenu(HoldShiftToSplitStack, shiftHeld)
                || !GameSession.CanSplit(item.Amount, Art.Data.TileData.GetStatic(item.Graphic).Flags))
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

            NoteIntroPress(Event.current); // where a book or button was pressed: an opening animation starts there
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
                if (IntroHidesWindow(window))
                    continue; // still flying in (UiManager.Intro)
                window.Rect = GUI.Window(window.Id, window.Rect, _ => DrawWindow(window), GUIContent.none, GUIStyle.none);
            }
            DrawIntroFlights();

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

            if (!DrawIntroWindow(w)) // opening (UiManager.Intro): drawn partly, behind its cover or roll
                w.Draw();

            // Last, so controls drawn above take their clicks first: the rest of the window moves it.
            if (Dragging == null && w.Movable)
                GUI.DragWindow();
        }
    }
}
