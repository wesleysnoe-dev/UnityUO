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
    /// Our own paperdoll also has TazUO's equipment slot columns (each worn piece's item art in a 19x20 slot, picked,
    /// used and dragged as the piece on the body) and, when 0xA9 says so, the combat and racial book icons.
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
        // Our own slots this frame: the piece worn on each (if any), its item art and the art's real bounds.
        private readonly List<(Slot Slot, EquippedItem? Item, UiImage Art, (int X, int Y, int W, int H) Bounds)> _slots =
            new List<(Slot, EquippedItem?, UiImage, (int, int, int, int))>();
        private readonly Dictionary<UiImage, (int, int, int, int)> _bounds = new Dictionary<UiImage, (int, int, int, int)>();
        private UiImage _combatBook, _racialBook;
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
            if (Modern)
            {
                Rect.size = new Vector2(ModernWidth(_serial == Session.World.PlayerSerial), ModernHeight);
                return;
            }
            UiImage bg = Art.Gump(_serial == Session.World.PlayerSerial ? BackgroundPlayer : BackgroundOther) ?? Art.Gump(BackgroundPlayer);
            if (bg != null)
                Rect.size = new Vector2(bg.Width, bg.Height);
        }

        public override void Draw()
        {
            if (!Session.World.Mobiles.TryGetValue(_serial, out Mobile m))
                return;
            if (Modern)
            {
                DrawModern(m);
                return;
            }

            bool self = _serial == Session.World.PlayerSerial;
            UiImage background = Art.Gump(self ? BackgroundPlayer : BackgroundOther) ?? Art.Gump(BackgroundPlayer);
            if (background != null)
                Rect.size = new Vector2(background.Width, background.Height); // back from the modern size
            DrawImage(background, 0, 0);

            // The virtue gem on every paperdoll, under the body (TazUO adds its PaperDollInteractable after the gem).
            UiImage virtue = Art.Gump(VirtueGem);
            DrawImage(virtue, VirtueX, VirtueY);

            // The books sit above the body (added after PaperDollInteractable); laid out now so the pick knows them.
            bool books = self && ShowsPaperdollBooks(true, Session.CharacterListFlags);
            bool racial = books && ShowsRacialBook(true, Session.CharacterListFlags, Session.Version);
            _combatBook = books ? Art.Gump(CombatBookIcon) : null;
            _racialBook = racial ? Art.Gump(RacialBookIcon) : null;

            // Our own slot columns, under the body as AddEquipSlot comes before the PaperDollInteractable.
            _slots.Clear();
            if (self)
                foreach (Slot s in Slots)
                {
                    EquippedItem? worn = m.Equipment.TryGetValue(s.Layer, out EquippedItem e) ? e : (EquippedItem?)null;
                    UiImage art = worn != null ? Art.Item(worn.Value.Graphic, worn.Value.Hue) : null;
                    _slots.Add((s, worn, art, art != null ? BoundsOf(art) : default));
                }

            bool female = IsFemale(m.Body);
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
            // outline alone is the mouse-over cue, ObjectHighlight). Only what is hit is outlined: the piece on the
            // body, or its slot's picture.
            (int Slot, byte Layer, EquippedItem? Item) hit = MouseIsOver ? HitAt(Event.current.mousePosition) : (Slot: -1, Layer: (byte)0, Item: null);
            uint over = hit.Item?.Serial ?? 0;
            HoveredText = hit.Slot >= 0 && hit.Item == null ? SlotName(_slots[hit.Slot].Slot.Layer) : null;

            for (int i = 0; i < _slots.Count; i++)
                DrawSlot(_slots[i], over != 0 && hit.Slot == i);

            DrawImage(Art.Gump(female ? 0x000D : 0x000C, m.Hue), BodyX, BodyY);
            foreach (var (_, e, img, _) in _drawn)
            {
                if (over != 0 && hit.Slot < 0 && e.Serial == over)
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
            // With the racial book it moves right to make room (Racial_Abilities_Width).
            UiImage profile = Art.Gump(ScrollGump);
            int profileX = ProfileXFor(racial);
            DrawImage(profile, profileX, ProfileY);

            // Our own paperdoll's party manifest: the same scroll, 4 pixels into the profile's right edge.
            UiImage party = null;
            float partyX = 0;
            if (self && (party = Art.Gump(ScrollGump)) != null)
            {
                partyX = PartyX(party.Width, racial);
                DrawImage(party, partyX, ProfileY);
            }

            DrawImage(_racialBook, RacialBookX, RacialBookY);
            DrawImage(_combatBook, CombatBookX, CombatBookY);

            // The button column: ours in full (Quests and Guild from CV_500A), anyone else's Status alone.
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

            // The books are above the worn items, so they take their double-clicks first (HitAt skips their pixels).
            if (DoubleClicked(_combatBook, CombatBookX, CombatBookY))
                Ui.Open(new CombatBookWindow()); // GameActions.OpenAbilitiesBook: one at a time
            else if (DoubleClicked(_racialBook, RacialBookX, RacialBookY))
                Ui.Open(new RacialAbilitiesWindow());

            HandleMouse();

            // After the worn items, which sit on top and take their clicks first.
            if (DoubleClicked(virtue, VirtueX, VirtueY))
                Session.RequestVirtueGump(_serial);
            else if (DoubleClicked(party, partyX, ProfileY))
                Ui.OpenWithIntro(new PartyWindow(), WindowIntro.Scroll, UiManager.ScrollGraphic); // opens it (unrolling), or brings the open one forward
            // The party scroll overlaps the profile's right edge by 4px and sits above it, so it's tested first.
            else if (DoubleClicked(profile, profileX, ProfileY))
                Session.RequestProfile(_serial);
        }

        /// <summary>
        /// One slot (EquipmentSlot): the 0x243A stone tiled to 19x20, the 0x2344 frame, and the worn piece's item art,
        /// its real bounds squeezed into 18x18 or centred if smaller (ItemGumpFixed), in its own hue. Under the mouse it
        /// gets the outline, scaled with the art.
        /// </summary>
        private void DrawSlot((Slot Slot, EquippedItem? Item, UiImage Art, (int X, int Y, int W, int H) Bounds) s, bool outlined)
        {
            DrawTiled(Art.Gump(SlotBackground), s.Slot.X, s.Slot.Y, SlotWidth, SlotHeight);
            DrawImage(Art.Gump(SlotBorder), s.Slot.X, s.Slot.Y);
            UiImage art = s.Art;
            var (bx, by, bw, bh) = s.Bounds;
            if (art == null || bw <= 0 || bh <= 0)
                return;
            var (rx, ry, rw, rh) = SlotItemRect(bw, bh);
            var dest = new Rect(s.Slot.X + rx, s.Slot.Y + ry, rw, rh);
            if (outlined && Art.Outline(art) is UiImage o)
            {
                // The outline is the art one pixel larger each way: the same bounds, one more pixel round, scaled alike.
                float sx = rw / (float)bw, sy = rh / (float)bh;
                GUI.DrawTextureWithTexCoords(new Rect(dest.x - sx, dest.y - sy, dest.width + 2 * sx, dest.height + 2 * sy), o.Texture,
                    TexCoords(o, bx, by, bw + 2, bh + 2));
            }
            GUI.DrawTextureWithTexCoords(dest, art.Texture, TexCoords(art, bx, by, bw, bh));
        }

        /// <summary>A pixel rectangle of a picture as texture coordinates (textures are stored bottom row first).</summary>
        private static Rect TexCoords(UiImage img, int x, int y, int w, int h) =>
            new Rect(x / (float)img.Width, 1f - (y + h) / (float)img.Height, w / (float)img.Width, h / (float)img.Height);

        private (int, int, int, int) BoundsOf(UiImage art)
        {
            if (!_bounds.TryGetValue(art, out var b))
                _bounds[art] = b = ArtBounds(art.Bitmap.Pixels, art.Width, art.Height);
            return b;
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

        /// <summary>Topmost worn item under a local point, on the body or in its slot (hair and beard can't be picked).</summary>
        private (byte Layer, EquippedItem Item)? ItemAt(Vector2 local)
        {
            var (_, layer, item) = HitAt(local);
            return item == null ? ((byte, EquippedItem)?)null : (layer, item.Value);
        }

        /// <summary>
        /// What a local point is on, top first as TazUO stacks the controls: a book icon's pixels (nothing here: the
        /// book takes it), a worn piece's pixels on the body (Slot −1), else a slot's 19x20 box (ItemGumpFixed.Contains
        /// is the whole box), with the piece worn there or none. Nothing: (−1, 0, null).
        /// </summary>
        private (int Slot, byte Layer, EquippedItem? Item) HitAt(Vector2 local)
        {
            if (Modern)
                return ModernHitAt(local);
            if (OnPicture(_combatBook, CombatBookX, CombatBookY, local) || OnPicture(_racialBook, RacialBookX, RacialBookY, local))
                return (-1, 0, null);
            var (i, _, _) = MousePick.WindowItem(_drawn.Count, local.x, local.y, Ui.Scale, _ => (BodyX, BodyY),
                _ => false, (k, x, y) => _drawn[k].Image.HitTest(x, y));
            if (i >= 0)
            {
                var (layer, item, _, _) = _drawn[i];
                return layer == 11 || layer == 16 ? (-1, (byte)0, (EquippedItem?)null) : (-1, layer, item);
            }
            int slot = SlotAt(_slots.Count, k => _slots[k].Slot, local.x, local.y, Ui.Scale);
            return slot < 0 ? (-1, (byte)0, (EquippedItem?)null) : (slot, _slots[slot].Slot.Layer, _slots[slot].Item);
        }

        private bool OnPicture(UiImage img, float x, float y, Vector2 local)
        {
            if (img == null)
                return false;
            var (px, py) = MousePick.UiPixel(local.x, local.y, x, y, Ui.Scale);
            return img.HitTest(px, py);
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
                    if (Session.Target != null && OnDoll(e.mousePosition))
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
            // Onto the backpack icon: put it in the pack. Anywhere else, a slot included (empty or not): wear it. The
            // layer sent is the item's own; ModernUO's EquipReq skips that byte and equips by the item's layer anyway.
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
