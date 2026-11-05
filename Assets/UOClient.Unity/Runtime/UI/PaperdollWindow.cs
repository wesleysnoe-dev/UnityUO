using System.Collections.Generic;
using UnityEngine;
using UOClient.Core;
using UOClient.IO;
using UOClient.IO.Formats;

namespace UOClient.Unity.UI
{
    /// <summary>
    /// The paperdoll: background gump, body gump in the skin hue, and each worn item's paperdoll art
    /// (tiledata AnimId + 50000 for male, + 60000 for female), layered like the classic client.
    /// Double-click the backpack to open it, double-click other items to use them, drag items off to
    /// take them off, drop items onto it to put them on. Click the scroll for the character's profile.
    /// </summary>
    public sealed class PaperdollWindow : UiWindow
    {
        private const float BodyX = 8, BodyY = 19;

        // Back to front, as the classic paperdoll layers them.
        private static readonly byte[] PaperdollOrder =
        {
            20, 5, 4, 3, 24, 19, 13, 17, 8, 14, 7, 23, 22, 12, 10, 11, 16, 18, 6, 1, 2, 9, 21
        };

        private readonly uint _serial;
        private string _title;
        private GUIStyle _titleStyle;
        private readonly List<(byte Layer, EquippedItem Item, UiImage Image)> _drawn = new List<(byte, EquippedItem, UiImage)>();
        private Vector2 _pressAt;
        private (byte Layer, EquippedItem Item)? _pressed;
        private float _lastClick;
        private byte _lastClickLayer = 255;

        public PaperdollWindow(uint serial, string title)
        {
            _serial = serial;
            _title = title;
            Rect = new Rect(80, 60, 262, 324);
        }

        public override string Key => $"paperdoll:{_serial}";
        public uint Serial => _serial;

        public void SetTitle(string title) => _title = title;

        public override bool StillValid => Session.World.Mobiles.ContainsKey(_serial);

        private static bool IsFemale(int body) => body == 401 || body == 403 || body == 606 || body == 745 || body == 667;

        public override void OnOpened()
        {
            UiImage bg = Art.Gump(_serial == Session.World.PlayerSerial ? 0x07D0 : 0x07D1) ?? Art.Gump(0x07D0);
            if (bg != null)
                Rect.size = new Vector2(bg.Width, bg.Height);
        }

        public override void Draw()
        {
            if (!Session.World.Mobiles.TryGetValue(_serial, out Mobile m))
                return;

            DrawImage(Art.Gump(_serial == Session.World.PlayerSerial ? 0x07D0 : 0x07D1) ?? Art.Gump(0x07D0), 0, 0);

            bool female = IsFemale(m.Body);
            DrawImage(Art.Gump(female ? 0x000D : 0x000C, m.Hue), BodyX, BodyY);

            _drawn.Clear();
            foreach (byte layer in PaperdollOrder)
            {
                if (!m.Equipment.TryGetValue(layer, out EquippedItem e))
                    continue;
                StaticTileData td = Art.Data.TileData.GetStatic(e.Graphic);
                if (td.AnimId == 0)
                    continue;
                bool partial = (td.Flags & TileFlags.PartialHue) != 0;
                int gump = Art.Data.TryEquipConv(m.Body, td.AnimId, out EquipConvEntry conv) ? conv.Gump : td.AnimId;
                UiImage img = (female ? Art.Gump(gump + 60000, e.Hue, partial) : null) ?? Art.Gump(gump + 50000, e.Hue, partial);
                if (img == null)
                    continue;
                DrawImage(img, BodyX, BodyY);
                _drawn.Add((layer, e, img));
            }

            if (_titleStyle == null)
            {
                _titleStyle = new GUIStyle(GUI.skin.label) { fontSize = 13, wordWrap = true, alignment = TextAnchor.UpperLeft };
                _titleStyle.normal.textColor = new Color(0.12f, 0.1f, 0.06f);
            }
            GUI.Label(new Rect(39, 262, 185, 40), _title ?? m.Name ?? "", _titleStyle);

            // The profile scroll (where ClassicUO puts it): asks the server for this character's profile (0xB8).
            if (GumpButton(0x07D2, 0x07D2, 25, 196))
                Session.RequestProfile(_serial);

            // Our own paperdoll carries the classic column of buttons on the right.
            if (_serial == Session.World.PlayerSerial)
            {
                bool war = Session.WarMode;
                (int Normal, int Pressed, string Command)[] buttons =
                {
                    (0x07EF, 0x07F0, "help"), (0x07D6, 0x07D7, "options"), (0x07D9, 0x07DA, "logout"),
                    (0x07DC, 0x07DD, "journal"), (0x07DF, 0x07E0, "skills"), (0x07E2, 0x07E3, "chat"),
                    (war ? 0x07E8 : 0x07E5, war ? 0x07E9 : 0x07E6, "war"), (0x07EB, 0x07EC, "status")
                };
                float[] ys = { 44, 71, 98, 124, 151, 178, 205, 233 };
                for (int i = 0; i < buttons.Length; i++)
                    if (GumpButton(buttons[i].Normal, buttons[i].Pressed, 185, ys[i]))
                        Ui.Command?.Invoke(buttons[i].Command);
            }

            if (Event.current.type == EventType.Repaint)
                Hovered = ItemAt(Event.current.mousePosition)?.Item.Serial ?? 0;
            HandleMouse();
        }

        /// <summary>Topmost worn item under a local point (hair and beard can't be picked).</summary>
        private (byte Layer, EquippedItem Item)? ItemAt(Vector2 local)
        {
            for (int i = _drawn.Count - 1; i >= 0; i--)
            {
                var (layer, item, img) = _drawn[i];
                if (img.HitTest((int)(local.x - BodyX), (int)(local.y - BodyY)))
                    return layer == 11 || layer == 16 ? ((byte, EquippedItem)?)null : (layer, item);
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
                    _pressed = ItemAt(e.mousePosition);
                    _pressAt = e.mousePosition;
                    // Targeting: a worn item, or the character itself when clicking the body.
                    if (Session.Target != null && e.mousePosition.x < 185)
                    {
                        TryTarget(_pressed?.Item.Serial ?? _serial);
                        _pressed = null;
                        return;
                    }
                    if (_pressed == null)
                        return;
                    // Double-click: the backpack opens, anything else is used.
                    if (Time.realtimeSinceStartup - _lastClick < 0.4f && _lastClickLayer == _pressed.Value.Layer)
                    {
                        Session.Use(_pressed.Value.Item.Serial);
                        _lastClick = 0;
                        _pressed = null;
                    }
                    else
                    {
                        _lastClick = Time.realtimeSinceStartup;
                        _lastClickLayer = _pressed.Value.Layer;
                    }
                    e.Use();
                    break;
                }
                case EventType.MouseDrag when e.button == 0 && _pressed != null:
                    if ((e.mousePosition - _pressAt).sqrMagnitude > 16 && _serial == Session.World.PlayerSerial && _pressed.Value.Layer != 21)
                    {
                        EquippedItem item = _pressed.Value.Item;
                        UiImage art = Art.Item(item.Graphic, item.Hue);
                        Ui.BeginDrag(new DraggedItem
                        {
                            Serial = item.Serial,
                            Graphic = item.Graphic,
                            Hue = item.Hue,
                            Amount = 1,
                            Grab = art != null ? new Vector2(art.Width / 2f, art.Height / 2f) : Vector2.zero
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
            if (_serial != Session.World.PlayerSerial)
                return false;
            // Onto the backpack icon: put it in the pack. Anywhere else: wear it.
            var under = ItemAt(local);
            uint pack = Session.World.Worn(Session.World.Player, 21);
            if (under != null && under.Value.Layer == 21 && pack != 0)
            {
                Session.DropInto(item.Serial, pack, 0xFFFF, 0xFFFF);
                return true;
            }
            byte layer = Art.Data.TileData.GetStatic(item.Graphic).Layer;
            Session.Equip(item.Serial, layer, _serial);
            return true;
        }
    }
}
