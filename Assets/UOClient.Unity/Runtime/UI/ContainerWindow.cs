using System.Collections.Generic;
using UnityEngine;
using UOClient.Core;
using UOClient.IO.Formats;

namespace UOClient.Unity.UI
{
    /// <summary>
    /// A container gump (backpack, bank box, chest) with its items at the positions the server gives.
    /// Double-click an item to use it (a bag opens in its own window), drag items to move them within or
    /// between containers, onto the paperdoll, or out into the world.
    /// </summary>
    public sealed class ContainerWindow : UiWindow
    {
        private readonly uint _serial;
        private readonly ushort _gump;
        private readonly List<(ContainedItem Item, UiImage Image, Rect Rect)> _drawn = new List<(ContainedItem, UiImage, Rect)>();
        private ContainedItem _pressed;
        private Vector2 _pressAt, _pressOffset;
        private float _lastClick;
        private uint _lastClickSerial;
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

            _drawn.Clear();
            foreach (ContainedItem item in Session.World.ItemsIn(_serial))
            {
                UiImage img = Art.Item(item.Graphic, item.Hue);
                if (img == null)
                    continue;
                bool stack = item.Amount > 1 && (Art.Data.TileData.GetStatic(item.Graphic).Flags & TileFlags.Generic) != 0;
                DrawImage(img, item.X, item.Y);
                if (stack)
                    DrawImage(img, item.X + 5, item.Y + 5); // the classic two-copies look of a stack
                _drawn.Add((item, img, new Rect(item.X, item.Y, img.Width + (stack ? 5 : 0), img.Height + (stack ? 5 : 0))));
            }

            if (Event.current.type == EventType.Repaint)
                Hovered = ItemAt(Event.current.mousePosition)?.Item.Serial ?? 0;
            HandleMouse();
        }

        private (ContainedItem Item, Vector2 Offset)? ItemAt(Vector2 local)
        {
            for (int i = _drawn.Count - 1; i >= 0; i--)
            {
                var (item, img, rect) = _drawn[i];
                if (!rect.Contains(local))
                    continue;
                int px = (int)(local.x - item.X), py = (int)(local.y - item.Y);
                if (img.HitTest(px, py) || img.HitTest(px - 5, py - 5))
                    return (item, new Vector2(px, py));
            }
            return null;
        }

        private void HandleMouse()
        {
            Event e = Event.current;
            switch (e.type)
            {
                case EventType.MouseDown when e.button == 0:
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
                        Session.RequestContextMenu(_pressed.Serial);
                        _pressed = null;
                        e.Use();
                        return;
                    }
                    if (Time.realtimeSinceStartup - _lastClick < 0.4f && _lastClickSerial == _pressed.Serial)
                    {
                        Session.Use(_pressed.Serial);
                        _lastClick = 0;
                        _pressed = null;
                    }
                    else
                    {
                        _lastClick = Time.realtimeSinceStartup;
                        _lastClickSerial = _pressed.Serial;
                    }
                    e.Use();
                    break;
                }
                case EventType.MouseDrag when e.button == 0 && _pressed != null:
                    if ((e.mousePosition - _pressAt).sqrMagnitude > 16)
                    {
                        Ui.BeginDrag(new DraggedItem
                        {
                            Serial = _pressed.Serial,
                            Graphic = _pressed.Graphic,
                            Hue = _pressed.Hue,
                            Amount = _pressed.Amount,
                            FromContainer = _serial,
                            FromX = _pressed.X,
                            FromY = _pressed.Y,
                            Grab = _pressOffset
                        });
                        _pressed = null;
                    }
                    e.Use();
                    break;
                case EventType.MouseUp when e.button == 0:
                    _pressed = null;
                    break;
            }
        }

        public override bool Drop(DraggedItem item, Vector2 local)
        {
            // The item's top-left goes where it was grabbed from relative to the cursor, kept inside the gump.
            UiImage art = Art.Item(item.Graphic, item.Hue);
            int x = Mathf.Clamp((int)(local.x - item.Grab.x), 0, Mathf.Max(0, (int)Rect.width - (art?.Width ?? 20)));
            int y = Mathf.Clamp((int)(local.y - item.Grab.y), 0, Mathf.Max(0, (int)Rect.height - (art?.Height ?? 20)));
            Session.DropInto(item.Serial, _serial, x, y);
            return true;
        }
    }
}
