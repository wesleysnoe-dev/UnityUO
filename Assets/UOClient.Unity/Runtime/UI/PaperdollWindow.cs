// EquipConvEntry is the top-level struct in UOClient.IO (ClientOverrides.cs).
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
    /// take them off, drop items onto it to put them on. Double-click the scroll for the character's profile;
    /// double-click the virtue gem for the virtue gump, and (our own) the party scroll for the party window.
    /// The button column is PaperdollWindow.Layout's (TazUO's PaperDollGump for our client version).
    /// </summary>
    public sealed partial class PaperdollWindow : UiWindow
    {
        // Back to front, as the classic paperdoll layers them.
        private static readonly byte[] PaperdollOrder =
        {
            20, 5, 4, 3, 24, 19, 13, 17, 8, 14, 7, 23, 22, 12, 10, 11, 16, 18, 6, 1, 2, 9, 21
        };

        private readonly uint _serial;
        private string _title;
        private GUIStyle _titleStyle;
        private readonly List<(byte Layer, EquippedItem Item, UiImage Image, int Gump)> _drawn = new List<(byte, EquippedItem, UiImage, int)>();
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
            UiImage bg = Art.Gump(_serial == Session.World.PlayerSerial ? BackgroundPlayer : BackgroundOther) ?? Art.Gump(BackgroundPlayer);
            if (bg != null)
                Rect.size = new Vector2(bg.Width, bg.Height);
        }

        public override void Draw()
        {
            if (!Session.World.Mobiles.TryGetValue(_serial, out Mobile m))
                return;

            DrawImage(Art.Gump(_serial == Session.World.PlayerSerial ? BackgroundPlayer : BackgroundOther) ?? Art.Gump(BackgroundPlayer), 0, 0);

            // The virtue gem on every paperdoll, under the body (TazUO adds its PaperDollInteractable after the gem).
            UiImage virtue = Art.Gump(VirtueGem);
            DrawImage(virtue, VirtueX, VirtueY);

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
                UiImage img = female ? Art.Gump(gump + 60000, e.Hue, partial) : null;
                gump += img != null ? 60000 : 50000;
                img ??= Art.Gump(gump, e.Hue, partial);
                if (img == null)
                    continue;
                _drawn.Add((layer, e, img, gump));
            }
            // Laid out first, so the worn item under the mouse is known: it keeps its own hue and is outlined (the
            // outline alone is the mouse-over cue, ObjectHighlight).
            uint over = MouseIsOver ? ItemAt(Event.current.mousePosition)?.Item.Serial ?? 0 : 0;
            foreach (var (_, e, img, _) in _drawn)
            {
                if (over != 0 && e.Serial == over)
                    DrawOutline(img, BodyX, BodyY);
                DrawImage(img, BodyX, BodyY);
            }

            if (_titleStyle == null)
            {
                _titleStyle = new GUIStyle(GUI.skin.label) { fontSize = 13, wordWrap = true, alignment = TextAnchor.UpperLeft };
                _titleStyle.normal.textColor = new Color(0.12f, 0.1f, 0.06f);
            }
            GUI.Label(new Rect(TitleX, TitleY, TitleWidth, 40), _title ?? m.Name ?? "", _titleStyle);

            // The profile scroll (where ClassicUO puts it): a double-click asks for this character's profile (0xB8).
            UiImage profile = Art.Gump(ScrollGump);
            DrawImage(profile, ProfileX, ProfileY);

            // Our own paperdoll's party manifest: the same scroll, 4 pixels into the profile's right edge.
            UiImage party = null;
            float partyX = 0;
            if (_serial == Session.World.PlayerSerial && (party = Art.Gump(ScrollGump)) != null)
            {
                partyX = PartyX(party.Width);
                DrawImage(party, partyX, ProfileY);
            }

            // The button column: ours in full (Quests and Guild from CV_500A), anyone else's Status alone.
            bool self = _serial == Session.World.PlayerSerial;
            foreach (Button b in Buttons(Session.Version, self, Session.WarMode))
            {
                if (!GumpButton(b.Normal, b.Pressed, b.X, b.Y))
                    continue;
                if (self)
                    Ui.Command?.Invoke(b.Command);
                else
                    OpenOtherBar();
            }

            if (Event.current.type == EventType.Repaint)
                Hovered = over;
            HandleMouse();

            // After the worn items, which sit on top and take their clicks first.
            if (DoubleClicked(virtue, VirtueX, VirtueY))
                Session.RequestVirtueGump(_serial);
            else if (DoubleClicked(party, partyX, ProfileY))
                Ui.Open(new PartyWindow()); // opens it, or brings the open one forward
            // The party scroll overlaps the profile's right edge by 4px and sits above it, so it's tested first.
            else if (DoubleClicked(profile, ProfileX, ProfileY))
                Session.RequestProfile(_serial);
        }

        /// <summary>Someone else's Status button: their health bar under the mouse, unless they already have one.</summary>
        private void OpenOtherBar()
        {
            if (Ui.Find(HealthBarWindow.KeyFor(_serial)) != null)
                return;
            Vector2 mouse = Rect.position + Event.current.mousePosition;
            int width = Art.Gump(HealthBarWindow.BackgroundOther)?.Width ?? 130;
            (float x, float y) = OtherBarAt(mouse.x, mouse.y, width);
            Ui.Open(new HealthBarWindow(_serial, x, y));
        }

        /// <summary>A left double-click on a picture's opaque pixels (TazUO's GumpPic with a MouseDoubleClick handler).</summary>
        private bool DoubleClicked(UiImage img, float x, float y)
        {
            Event e = Event.current;
            if (img == null || e.type != EventType.MouseDown || e.button != 0 || e.clickCount != 2)
                return false;
            var (px, py) = MousePick.UiPixel(e.mousePosition.x, e.mousePosition.y, x, y, Ui.Scale);
            if (!img.HitTest(px, py))
                return false;
            e.Use();
            return true;
        }

        /// <summary>Topmost worn item under a local point (hair and beard can't be picked).</summary>
        private (byte Layer, EquippedItem Item)? ItemAt(Vector2 local)
        {
            var (i, _, _) = MousePick.WindowItem(_drawn.Count, local.x, local.y, Ui.Scale, _ => (BodyX, BodyY),
                _ => false, (k, x, y) => _drawn[k].Image.HitTest(x, y));
            if (i < 0)
                return null;
            var (layer, item, _, _) = _drawn[i];
            return layer == 11 || layer == 16 ? ((byte, EquippedItem)?)null : (layer, item);
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
                            Grab = art != null ? new Vector2(MousePick.CentreGrab(art.Width), MousePick.CentreGrab(art.Height)) : Vector2.zero
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
