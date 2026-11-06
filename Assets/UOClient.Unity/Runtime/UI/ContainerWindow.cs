using System;
using System.Collections.Generic;
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

        public override void OnOpened()
        {
            UiImage bg = Art.Gump(_gump);
            if (bg != null)
                Rect.size = new Vector2(bg.Width, bg.Height);
        }

        public override void OnClosed() => Session.World.OpenContainers.Remove(_serial);

        public override void Draw()
        {
            DrawImage(Art.Gump(_gump), 0, 0);

            // Laid out first, so the item under the mouse is known before anything is drawn.
            _drawn.Clear();
            foreach (ContainedItem item in Session.World.ItemsIn(_serial))
            {
                UiImage img = Art.Item(item.Graphic, item.Hue);
                if (img == null)
                    continue;
                bool stack = item.Amount > 1 && (Art.Data.TileData.GetStatic(item.Graphic).Flags & TileFlags.Generic) != 0;
                _drawn.Add((item, img, new Rect(item.X, item.Y, img.Width + (stack ? 5 : 0), img.Height + (stack ? 5 : 0))));
            }
            ContainedItem over = MouseIsOver ? ItemAt(Event.current.mousePosition)?.Item : null;
            foreach (var (item, own, rect) in _drawn)
            {
                // ItemGump with HighlightOnMouseOver: the item the mouse is on is drawn wholly in 0x0035, and outlined
                // (both copies of a stack, under them, so the pair shows one edge).
                UiImage img = item == over ? Art.Item(item.Graphic, ObjectHighlight.WindowItemHue(item.Hue, true)) ?? own : own;
                if (item == over)
                {
                    DrawOutline(img, item.X, item.Y);
                    if (rect.width > img.Width)
                        DrawOutline(img, item.X + 5, item.Y + 5);
                }
                DrawImage(img, item.X, item.Y);
                if (rect.width > img.Width)
                    DrawImage(img, item.X + 5, item.Y + 5); // the classic two-copies look of a stack
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
            foreach (var (item, _, rect) in _drawn)
            {
                if (!(Session.World.RecentLine(item.Serial, now, 3) is JournalEntry line))
                    continue;
                var r = new Rect(rect.center.x - 90, Mathf.Max(0, rect.y - 16), 180, 16); // the window clips anything above its top
                Text(new Rect(r.x + 1, r.y + 1, r.width, r.height), line.Text, Color.black, 11, TextAnchor.MiddleCenter);
                Text(r, line.Text, Art.HueColor(line.Hue, new Color(0.95f, 0.95f, 0.9f)), 11, TextAnchor.MiddleCenter);
            }
        }

        private (ContainedItem Item, Vector2 Offset)? ItemAt(Vector2 local)
        {
            int px = 0, py = 0;
            int i = ObjectHighlight.Topmost(_drawn.Count, k =>
            {
                var (item, img, rect) = _drawn[k];
                px = (int)(local.x - item.X);
                py = (int)(local.y - item.Y);
                return rect.Contains(local) && (img.HitTest(px, py) || img.HitTest(px - 5, py - 5));
            });
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
            UiImage art = Art.Item(item.Graphic, item.Hue);
            int x = Mathf.Clamp((int)(local.x - item.Grab.x), 0, Mathf.Max(0, (int)Rect.width - (art?.Width ?? 20)));
            int y = Mathf.Clamp((int)(local.y - item.Grab.y), 0, Mathf.Max(0, (int)Rect.height - (art?.Height ?? 20)));
            Session.DropInto(item.Serial, _serial, x, y);
            return true;
        }
    }
}
