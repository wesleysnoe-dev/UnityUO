using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UOClient.Core;
using UOClient.IO.Formats;

namespace UOClient.Unity.UI
{
    /// <summary>
    /// A container gump (backpack, bank box, chest) with its items at the positions the server gives.
    /// Click an item to name it, double-click to use it (a bag opens in its own window), drag items to move them within or
    /// between containers, onto the paperdoll, or out into the world.
    /// </summary>
    public sealed class ContainerWindow : UiWindow
    {
        private readonly uint _serial;
        private readonly ushort _gump;
        private readonly List<(ContainedItem Item, UiImage Image, Rect Rect)> _drawn = new List<(ContainedItem, UiImage, Rect)>();
        private ContainedItem _pressed;
        private bool _shiftPressed; // pressed with Shift on a stack: a drag splits it, a release opens its context menu
        private Vector2 _pressAt, _pressOffset;
        private float _lastClick;
        private uint _lastClickSerial;
        private uint _pendingLook; // a single click held until it can't be the start of a double-click or a drag

        private const float DoubleClickTime = 0.4f;
        private static int _cascade;

        /// <summary>The options' ContainersScale in percent (TazUO's, 50-200): every container gump and its items draw at this size.</summary>
        public static int ScalePercent { get; set; } = 100;

        /// <summary>The options' DoubleClickToLootInsideContainers (TazUO's): GameOptions.LootsOnDoubleClick.</summary>
        public static bool DoubleClickToLoot { get; set; }

        /// <summary>The options' HighlightContainerWhenSelected (TazUO's): WorldInput selects the container under the mouse.</summary>
        public static bool HighlightWhenSelected { get; set; }

        /// <summary>
        /// The container whose window the mouse is over with <see cref="HighlightWhenSelected"/> on, else 0 (TazUO's
        /// SelectedObject.SelectedContainer, set by WorldInput each frame): outlined where it sits in another container's window.
        /// </summary>
        public static uint SelectedContainer { get; set; }

        /// <summary>The options' HueContainerGumps (TazUO's): the gump takes the container's hue (GameOptions.ContainerGumpHue).</summary>
        public static bool HueGumps { get; set; } = GameOptions.Defaults().hueContainerGumps;

        /// <summary>
        /// The container's own hue, wherever it is: on the ground, inside another container, or worn (a backpack). TazUO
        /// reads Item.Hue from its one item list; here those are three.
        /// </summary>
        private ushort ContainerHue()
        {
            World world = Session.World;
            if (world.Items.TryGetValue(_serial, out WorldItem ground))
                return ground.Hue;
            if (world.Contained.TryGetValue(_serial, out ContainedItem inside))
                return inside.Hue;
            foreach (Core.Mobile m in world.Mobiles.Values)
                foreach (EquippedItem e in m.Equipment.Values)
                    if (e.Serial == _serial)
                        return e.Hue;
            return 0;
        }

        /// <summary>This container's size: the chess and backgammon boards keep theirs (GameOptions.ContainerScale).</summary>
        private float S => GameOptions.ContainerScale(ScalePercent, _gump);

        public ContainerWindow(uint serial, ushort gump)
        {
            _serial = serial;
            _gump = gump;
            // New containers open cascaded, like the classic client.
            Rect = new Rect(420 + _cascade % 5 * 24, 120 + _cascade % 5 * 24, 180, 180);
            _cascade++;
        }

        public override string Key => $"container:{_serial}";
        public uint Serial => _serial;

        public override bool StillValid => Session.World.OpenContainers.ContainsKey(_serial);

        public override void OnOpened() => FitToGump();

        /// <summary>The window is the gump at the container scale (again each frame, so a new scale takes at once).</summary>
        private void FitToGump()
        {
            UiImage bg = Art.Gump(_gump);
            if (bg != null)
                Rect.size = new Vector2(bg.Width * S, bg.Height * S);
        }

        public override void OnClosed() => Session.World.OpenContainers.Remove(_serial);

        /// <summary>Art at a gump position (unscaled gump pixels), drawn at the container scale.</summary>
        private void DrawScaled(UiImage img, float x, float y)
        {
            float s = S;
            if (img != null)
                GUI.DrawTexture(new Rect(x * s, y * s, img.Width * s, img.Height * s), img.Texture);
        }

        public override void Draw()
        {
            // Items and their hit tests are in the gump's own pixels; only drawing and the mouse go through the scale.
            FitToGump();
            // The size and hit tests come from the plain art (FitToGump); a hue only recolours it.
            ushort hue = HueGumps ? GameOptions.ContainerGumpHue(true, _gump, ContainerHue()) : (ushort)0;
            DrawScaled(Art.Gump(_gump, hue) ?? Art.Gump(_gump), 0, 0);

            // Laid out first, so the item under the mouse is known before anything is drawn.
            _drawn.Clear();
            foreach (ContainedItem item in Session.World.ItemsIn(_serial))
            {
                // A coin pile's art; any other stack twice (ItemStack, as ContainerGump/ItemGump draw them).
                UiImage img = Art.Item(ItemStack.DisplayedGraphic(item.Graphic, item.Amount), item.Hue);
                if (img == null)
                    continue;
                bool stack = ItemStack.DrawsTwice(item.Graphic, item.Amount, Art.Data.TileData.GetStatic(item.Graphic).Flags);
                int extra = stack ? MousePick.StackOffset : 0;
                _drawn.Add((item, img, new Rect(item.X, item.Y, img.Width + extra, img.Height + extra)));
            }
            ContainedItem over = MouseIsOver ? ItemAt(Event.current.mousePosition)?.Item : null;
            foreach (var (item, img, rect) in _drawn)
            {
                // The item the mouse is on keeps its own hue and is outlined (both copies of a stack, under them, so the
                // pair shows one edge): the outline alone is the mouse-over cue (ObjectHighlight). A bag whose own window
                // the mouse is over gets it too (ItemGump: LocalSerial == SelectedObject.SelectedContainer).
                if (item == over || (SelectedContainer != 0 && item.Serial == SelectedContainer))
                {
                    DrawScaled(Art.Outline(img), item.X - 1, item.Y - 1);
                    if (rect.width > img.Width)
                        DrawScaled(Art.Outline(img), item.X + MousePick.StackOffset - 1, item.Y + MousePick.StackOffset - 1);
                }
                DrawScaled(img, item.X, item.Y);
                if (rect.width > img.Width)
                    DrawScaled(img, item.X + MousePick.StackOffset, item.Y + MousePick.StackOffset); // the classic two-copies look of a stack
            }

            if (Event.current.type == EventType.Repaint)
            {
                Hovered = over?.Serial ?? 0;
                DrawLabels();
            }
            // As in the world, a single click names the item (0x09) once it can't be a double-click any more.
            if (_pendingLook != 0 && Time.realtimeSinceStartup - _lastClick >= DoubleClickTime)
            {
                if (Session.Target == null)
                    Session.Look(_pendingLook);
                _pendingLook = 0;
            }
            HandleMouse();
        }

        /// <summary>
        /// The server answers a single click with a label from the item's serial; items in a gump aren't in the
        /// world's overhead text, so the newest line about each one is drawn over it here for a few seconds.
        /// </summary>
        private void DrawLabels()
        {
            DateTime now = DateTime.Now;
            float s = S;
            foreach (var (item, _, rect) in _drawn)
            {
                if (!(Session.World.RecentLine(item.Serial, now, 3) is JournalEntry line))
                    continue;
                var r = new Rect(rect.center.x * s - 90, Mathf.Max(0, rect.y * s - 16), 180, 16); // the window clips anything above its top
                Text(new Rect(r.x + 1, r.y + 1, r.width, r.height), line.Text, Color.black, 11, TextAnchor.MiddleCenter);
                Text(r, line.Text, Art.HueColor(line.Hue, new Color(0.95f, 0.95f, 0.9f)), 11, TextAnchor.MiddleCenter);
            }
        }

        /// <summary>
        /// The topmost item on an opaque pixel under a window point, and where in its art (MousePick.WindowItem). The point
        /// is taken back to gump pixels, each of which is Ui.Scale x the container scale display pixels.
        /// </summary>
        private (ContainedItem Item, Vector2 Offset)? ItemAt(Vector2 local)
        {
            float s = S;
            var (i, px, py) = MousePick.WindowItem(_drawn.Count, local.x / s, local.y / s, Ui.Scale * s, k => (_drawn[k].Item.X, _drawn[k].Item.Y),
                k => _drawn[k].Rect.width > _drawn[k].Image.Width, (k, x, y) => _drawn[k].Image.HitTest(x, y));
            return i < 0 ? ((ContainedItem, Vector2)?)null : (_drawn[i].Item, new Vector2(px, py));
        }

        private void HandleMouse()
        {
            Event e = Event.current;
            switch (e.type)
            {
                case EventType.MouseDown when e.button == 0 && Ui.Dragging == null: // holding a split stack, the click is its drop
                {
                    var hit = ItemAt(e.mousePosition);
                    _pressed = hit?.Item;
                    _pressOffset = hit?.Offset ?? Vector2.zero;
                    _pressAt = e.mousePosition;
                    if (_pressed == null)
                        return;
                    if (TryTarget(_pressed.Serial))
                    {
                        _pressed = null;
                        return;
                    }
                    if (e.shift)
                    {
                        // On a stack the press may yet become a split drag: its context menu waits for the release.
                        if (GameSession.CanSplit(_pressed.Amount, Art.Data.TileData.GetStatic(_pressed.Graphic).Flags))
                        {
                            _shiftPressed = true;
                            e.Use();
                            return;
                        }
                        Session.RequestContextMenu(_pressed.Serial);
                        _pressed = null;
                        e.Use();
                        return;
                    }
                    _shiftPressed = false;
                    if (Time.realtimeSinceStartup - _lastClick < DoubleClickTime && _lastClickSerial == _pressed.Serial)
                    {
                        if (!TryLoot(_pressed, e.control))
                            Session.Use(_pressed.Serial);
                        _lastClick = 0;
                        _pendingLook = 0;
                        _pressed = null;
                    }
                    else
                    {
                        if (_pendingLook != 0 && Session.Target == null)
                            Session.Look(_pendingLook); // a quick click on another item: the first one still counts
                        _lastClick = Time.realtimeSinceStartup;
                        _lastClickSerial = _pressed.Serial;
                        _pendingLook = _pressed.Serial;
                    }
                    e.Use();
                    break;
                }
                case EventType.MouseDrag when e.button == 0 && _pressed != null:
                    if ((e.mousePosition - _pressAt).sqrMagnitude > 16)
                    {
                        _pendingLook = 0; // a drag, not a click
                        Ui.Lift(new DraggedItem
                        {
                            Serial = _pressed.Serial,
                            Graphic = _pressed.Graphic,
                            Hue = _pressed.Hue,
                            Amount = _pressed.Amount,
                            FromContainer = _serial,
                            FromX = _pressed.X,
                            FromY = _pressed.Y,
                            Grab = _pressOffset
                        }, e.shift || _shiftPressed);
                        _pressed = null;
                        _shiftPressed = false;
                    }
                    e.Use();
                    break;
                case EventType.MouseUp when e.button == 0:
                    if (_shiftPressed && _pressed != null)
                        Session.RequestContextMenu(_pressed.Serial); // a shift-click, not a split
                    _pressed = null;
                    _shiftPressed = false;
                    break;
            }
        }

        /// <summary>
        /// A double-click that loots (GameOptions.LootsOnDoubleClick, TazUO's ItemGump.OnMouseDoubleClick): the item goes
        /// whole into the backpack, as TazUO's GameActions.GrabItem (lift, then drop into the pack where it fits).
        /// </summary>
        private bool TryLoot(ContainedItem item, bool ctrlHeld)
        {
            World world = Session.World;
            uint pack = world.Worn(world.Player, 21);
            bool isContainer = (Art.Data.TileData.GetStatic(item.Graphic).Flags & TileFlags.Container) != 0;
            bool hasContents = world.ItemsIn(item.Serial).Any();
            if (!GameOptions.LootsOnDoubleClick(DoubleClickToLoot, ctrlHeld, isContainer, hasContents, OutermostIsWorldItem(item), pack != 0))
                return false;
            Session.Lift(item.Serial, item.Amount);
            Session.DropInto(item.Serial, pack, 0xFFFF, 0xFFFF);
            return true;
        }

        /// <summary>Whether the outermost container holding the item is an item in the world (a corpse, a chest), not worn.</summary>
        private bool OutermostIsWorldItem(ContainedItem item)
        {
            uint top = item.Container;
            for (int depth = 0; depth < 32 && Session.World.Contained.TryGetValue(top, out ContainedItem parent); depth++)
                top = parent.Container;
            return Session.World.Items.ContainsKey(top);
        }

        public override bool Drop(DraggedItem item, Vector2 local)
        {
            // Onto a bag, a corpse or a like stack in here: into it (the server places it or merges the stack).
            ContainedItem under = ItemAt(local)?.Item;
            if (under != null && under.Serial != item.Serial && GameSession.DropsInto(item.Graphic,
                    Art.Data.TileData.GetStatic(item.Graphic).Flags, under.Graphic, Art.Data.TileData.GetStatic(under.Graphic).Flags))
            {
                Session.DropInto(item.Serial, under.Serial, 0xFFFF, 0xFFFF);
                return true;
            }
            // The item's top-left goes where it was grabbed from relative to the cursor, kept inside the gump.
            // In gump pixels, as ItemAt: the window is the gump at the container scale.
            float s = S;
            UiImage art = Art.Item(item.DisplayGraphic != 0 ? item.DisplayGraphic : item.Graphic, item.Hue); // the held art (ContainerGump.cs:384-388)
            (int dx, int dy) = MousePick.DropOrigin(local.x / s, local.y / s, item.Grab.x, item.Grab.y, Ui.Scale * s);
            int x = Mathf.Clamp(dx, 0, Mathf.Max(0, (int)(Rect.width / s) - (art?.Width ?? 20)));
            int y = Mathf.Clamp(dy, 0, Mathf.Max(0, (int)(Rect.height / s) - (art?.Height ?? 20)));
            Session.DropInto(item.Serial, _serial, x, y);
            return true;
        }
    }
}
