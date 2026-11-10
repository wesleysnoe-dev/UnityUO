using System.Collections.Generic;
using UnityEngine;
using UOClient.Core;
using UOClient.IO;
using UOClient.IO.Formats;

namespace UOClient.Unity.UI
{
    /// <summary>
    /// Our modern paperdoll (on by default; the options' "Modern paperdoll" turns the classic one back on, with the top
    /// bar): an iron and gilt frame round a dark niche where the character stands, larger and smoothed; the worn pieces'
    /// pictures in slots beside the body, each at the height it is worn; the classic scroll buttons in a column on the
    /// right; a row of round buttons along the top for what the top bar had (profile, party, maps, journal, chat,
    /// groups); the virtue gem in the top-left corner; and the name and title on a plaque along the foot.
    /// The art is PaperdollSkin's (painted pieces in Resources/Paperdoll when present, else drawn here).
    /// </summary>
    public sealed partial class PaperdollWindow
    {
        /// <summary>The modern paperdoll (and no top bar); off: the classic one.</summary>
        public static bool Modern { get; set; } = true;

        // ---- layout, in UI units ----
        private const float Border = 18f, IconBand = 46f, DollW = 330f, DollH = 344f, ColumnGap = 10f, ColumnW = 176f;
        private const float ButtonH = 36f, ButtonGap = 6f, PlaqueGap = 8f, PlaqueH = 60f;
        private const float DollScale = 1.45f, SlotSize = 34f, IconSize = 32f, IconGap = 10f;

        private static float ModernWidth(bool self) => Border + DollW + (self ? ColumnGap + ColumnW : 0f) + Border;
        private const float ModernHeight = Border + IconBand + DollH + PlaqueGap + PlaqueH + Border;
        private static Rect DollRect => new Rect(Border, Border + IconBand, DollW, DollH);

        /// <summary>The slots beside the body: the side, and how far down the doll each layer is worn (0 top, 1 feet).</summary>
        private static readonly (byte Layer, bool Right, float Down)[] SlotPlaces =
        {
            (0x06, false, 0.06f), (0x12, false, 0.14f), (0x0A, false, 0.22f), (0x0D, false, 0.32f), (0x13, false, 0.40f),
            (0x0E, false, 0.50f), (0x07, false, 0.58f), (0x08, false, 0.66f), (0x01, false, 0.74f),
            (0x14, true, 0.10f), (0x16, true, 0.20f), (0x11, true, 0.30f), (0x05, true, 0.38f), (0x0C, true, 0.48f),
            (0x02, true, 0.56f), (0x09, true, 0.64f), (0x04, true, 0.72f), (0x17, true, 0.80f), (0x03, true, 0.90f),
        };

        private readonly List<(Rect At, byte Layer, EquippedItem Item, UiImage Art)> _modernSlots = new List<(Rect, byte, EquippedItem, UiImage)>();
        private Vector2 _bodyAt;                // where the body's art origin is drawn
        private Vector2 _combatAt, _racialAt;   // the book icons on the plaque
        private Vector2 _virtueAt;

        /// <summary>The top row's buttons: (command, label, icon name).</summary>
        private static readonly (string Command, string Label, string Icon)[] TopIconsSelf =
        {
            ("profile", "Profile", "profile"), ("party", "Party", "party"), ("minimap", "Map", "map"),
            ("worldmap", "World Map", "worldmap"), ("journal", "Journal", "journal"), ("worldchat", "Chat", "chat"),
            ("groups", "Groups", "groups"),
        };

        private static readonly (string Command, string Label, string Icon)[] TopIconsOther =
        {
            ("profile", "Profile", "profile"), ("status", "Status", "status"),
        };

        private void DrawModern(Mobile m)
        {
            bool self = _serial == Session.World.PlayerSerial;
            Rect.width = ModernWidth(self);
            Rect.height = ModernHeight;
            float w = Rect.width, h = Rect.height;
            Rect doll = DollRect;

            PaperdollSkin.Frame(new Rect(0, 0, w, h), Border);
            PaperdollSkin.Niche(doll);

            // The books, laid out now (on the plaque's left end) so the pick knows them.
            bool books = self && ShowsPaperdollBooks(true, Session.CharacterListFlags);
            bool racial = books && ShowsRacialBook(true, Session.CharacterListFlags, Session.Version);
            _combatBook = books ? Art.Gump(CombatBookIcon) : null;
            _racialBook = racial ? Art.Gump(RacialBookIcon) : null;
            var plaque = new Rect(Border, doll.yMax + PlaqueGap, w - Border * 2f, PlaqueH);
            float bx = plaque.x + 16f;
            if (_combatBook != null)
            {
                _combatAt = new Vector2(bx, plaque.y + (plaque.height - _combatBook.Height) / 2f);
                bx += _combatBook.Width + 6f;
            }
            if (_racialBook != null)
                _racialAt = new Vector2(bx, plaque.y + (plaque.height - _racialBook.Height) / 2f);

            // The body and what is worn, as the classic paperdoll layers them; larger, centred in the niche.
            bool female = IsFemale(m.Body);
            UiImage body = Art.Gump(female ? 0x000D : 0x000C, m.Hue);
            CollectWorn(m, female);
            // Centred by the body's own pixels (its art has room around the figure).
            var (fx, fy, fw, fh) = body != null ? BoundsOf(body) : (0, 0, 180, 240);
            _bodyAt = new Vector2(doll.x + doll.width / 2f - (fx + fw / 2f) * DollScale,
                                  doll.y + doll.height / 2f - (fy + fh / 2f) * DollScale + 6f);

            LayoutSlots(m, doll);

            (int Slot, byte Layer, EquippedItem? Item) hit = MouseIsOver ? HitAt(Event.current.mousePosition) : (Slot: -1, Layer: (byte)0, Item: null);
            uint over = hit.Item?.Serial ?? 0;
            HoveredText = null;

            GUI.BeginGroup(doll);
            var origin = _bodyAt - doll.position;
            DrawDollPiece(body, female ? 0x000D : 0x000C, m.Hue, false, origin, false);
            foreach (var (_, e, img, gump) in _drawn)
                DrawDollPiece(img, gump, e.Hue, PartialHue(e.Graphic), origin, over != 0 && hit.Slot < 0 && e.Serial == over);
            GUI.EndGroup();

            for (int i = 0; i < _modernSlots.Count; i++)
            {
                var s = _modernSlots[i];
                bool lit = over != 0 && (hit.Slot == i || hit.Slot < 0 && s.Item.Serial == over);
                PaperdollSkin.Slot(s.At, lit);
                DrawSlotArt(s.Art, s.At, lit);
            }

            // The top row: round buttons for what the top bar had.
            var icons = self ? TopIconsSelf : TopIconsOther;
            float rowW = icons.Length * IconSize + (icons.Length - 1) * IconGap;
            float ix = (w - rowW) / 2f, iy = Border + (IconBand - IconSize) / 2f;
            for (int i = 0; i < icons.Length; i++)
            {
                var r = new Rect(ix + i * (IconSize + IconGap), iy, IconSize, IconSize);
                bool hot = r.Contains(Event.current.mousePosition) && MouseIsOver;
                PaperdollSkin.IconButton(r, icons[i].Icon, icons[i].Label, hot);
                if (hot)
                    HoveredText = icons[i].Label;
                if (GUI.Button(r, GUIContent.none, GUIStyle.none))
                    RunTopIcon(icons[i].Command);
            }

            // The virtue gem, in the top-left corner's medallion.
            UiImage virtue = Art.Gump(VirtueGem);
            if (virtue != null)
            {
                _virtueAt = new Vector2(Border * 0.9f - virtue.Width / 2f, Border * 0.9f - virtue.Height / 2f);
                DrawImage(virtue, _virtueAt.x, _virtueAt.y);
            }

            // The classic scroll buttons, in a column on the right (our own paperdoll).
            if (self)
            {
                Button[] buttons = Buttons(Session.Version, true, Session.WarMode);
                float cx = doll.xMax + ColumnGap;
                float total = buttons.Length * ButtonH + (buttons.Length - 1) * ButtonGap;
                float cy = doll.y + (doll.height - total) / 2f;
                foreach (Button b in buttons)
                {
                    var r = new Rect(cx, cy, ColumnW, ButtonH);
                    cy += ButtonH + ButtonGap;
                    (string label, string icon) = ButtonFace(b.Command, Session.WarMode);
                    bool hot = r.Contains(Event.current.mousePosition) && MouseIsOver;
                    PaperdollSkin.ScrollButton(r, icon, label, hot, hot && Input.GetMouseButton(0));
                    if (GUI.Button(r, GUIContent.none, GUIStyle.none))
                        Ui.Command?.Invoke(b.Command);
                }
            }

            // The plaque: the name large, the rest of the title under it.
            PaperdollSkin.Plaque(plaque);
            DrawImage(_combatBook, _combatAt.x, _combatAt.y);
            DrawImage(_racialBook, _racialAt.x, _racialAt.y);
            (string name, string rest) = SplitTitle(_title ?? m.Name ?? "");
            float textLeft = plaque.x + 60f, textW = plaque.width - 120f;
            PaperdollSkin.Title(new Rect(textLeft, plaque.y + (rest.Length > 0 ? 6f : 16f), textW, 28f), name, 22, PaperdollSkin.Gold);
            if (rest.Length > 0)
                PaperdollSkin.Title(new Rect(textLeft, plaque.y + 34f, textW, 20f), rest, 14, PaperdollSkin.GoldFaded);

            if (Event.current.type == EventType.Repaint)
                Hovered = over;
            if (HoveredText == null && hit.Slot >= 0 && hit.Item == null)
                HoveredText = SlotName(hit.Layer);

            if (DoubleClicked(_combatBook, _combatAt.x, _combatAt.y))
                Ui.Open(new CombatBookWindow());
            else if (DoubleClicked(_racialBook, _racialAt.x, _racialAt.y))
                Ui.Open(new RacialAbilitiesWindow());

            HandleMouse();

            if (DoubleClicked(virtue, _virtueAt.x, _virtueAt.y))
                Session.RequestVirtueGump(_serial);
        }

        /// <summary>The worn pieces with paperdoll art, back to front (as the classic Draw collects them).</summary>
        private void CollectWorn(Mobile m, bool female)
        {
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
                if (img != null)
                    _drawn.Add((layer, e, img, gump));
            }
        }

        private bool PartialHue(ushort graphic) => (Art.Data.TileData.GetStatic(graphic).Flags & TileFlags.PartialHue) != 0;

        /// <summary>One piece of the doll at DollScale, from its smoothed art; outlined under the mouse.</summary>
        private void DrawDollPiece(UiImage img, int gump, int hue, bool partial, Vector2 origin, bool outlined)
        {
            if (img == null)
                return;
            if (outlined && Art.Outline(img) is UiImage o)
                GUI.DrawTexture(new Rect(origin.x - DollScale, origin.y - DollScale, o.Width * DollScale, o.Height * DollScale), o.Texture);
            UiImage smooth = Art.GumpSmooth(gump, hue, partial) ?? img;
            GUI.DrawTexture(new Rect(origin.x, origin.y, img.Width * DollScale, img.Height * DollScale), smooth.Texture);
        }

        /// <summary>
        /// The worn pieces' slots: on their side of the doll at the height each is worn, pushed apart where they would
        /// overlap (down first, then back up from the foot).
        /// </summary>
        private void LayoutSlots(Mobile m, Rect doll)
        {
            _modernSlots.Clear();
            foreach (bool right in new[] { false, true })
            {
                var side = new List<(float Y, byte Layer, EquippedItem Item)>();
                foreach (var p in SlotPlaces)
                    if (p.Right == right && m.Equipment.TryGetValue(p.Layer, out EquippedItem e))
                        side.Add((doll.y + p.Down * doll.height - SlotSize / 2f, p.Layer, e));
                float step = SlotSize + 6f, top = doll.y + 8f, bottom = doll.yMax - 8f - SlotSize;
                var ys = new float[side.Count];
                for (int i = 0; i < side.Count; i++)
                    ys[i] = Mathf.Max(side[i].Y, i == 0 ? top : ys[i - 1] + step);
                for (int i = side.Count - 1; i >= 0; i--)
                    ys[i] = Mathf.Min(ys[i], i == side.Count - 1 ? bottom : ys[i + 1] - step);
                float x = right ? doll.xMax - 10f - SlotSize : doll.x + 10f;
                for (int i = 0; i < side.Count; i++)
                    _modernSlots.Add((new Rect(x, ys[i], SlotSize, SlotSize), side[i].Layer, side[i].Item, Art.Item(side[i].Item.Graphic, side[i].Item.Hue)));
            }
        }

        /// <summary>A worn piece's item art in its slot: its real bounds fitted (aspect kept) into the slot's inside.</summary>
        private void DrawSlotArt(UiImage art, Rect slot, bool lit)
        {
            if (art == null)
                return;
            var (x, y, bw, bh) = BoundsOf(art);
            if (bw <= 0 || bh <= 0)
                return;
            float inner = slot.width - 8f, k = Mathf.Min(inner / bw, inner / bh, 1.6f);
            var dest = new Rect(slot.center.x - bw * k / 2f, slot.center.y - bh * k / 2f, bw * k, bh * k);
            GUI.DrawTextureWithTexCoords(dest, art.Texture, TexCoords(art, x, y, bw, bh));
        }

        /// <summary>What a local point is on in the modern paperdoll: a book (nothing), a worn piece on the body, else a slot.</summary>
        private (int Slot, byte Layer, EquippedItem? Item) ModernHitAt(Vector2 local)
        {
            if (OnPicture(_combatBook, _combatAt.x, _combatAt.y, local) || OnPicture(_racialBook, _racialAt.x, _racialAt.y, local))
                return (-1, 0, null);
            for (int i = _modernSlots.Count - 1; i >= 0; i--)
                if (_modernSlots[i].At.Contains(local))
                    return (i, _modernSlots[i].Layer, _modernSlots[i].Item);
            if (DollRect.Contains(local))
            {
                int px = Mathf.FloorToInt((local.x - _bodyAt.x) / DollScale), py = Mathf.FloorToInt((local.y - _bodyAt.y) / DollScale);
                for (int i = _drawn.Count - 1; i >= 0; i--)
                {
                    var (layer, item, img, _) = _drawn[i];
                    if (!img.HitTest(px, py))
                        continue;
                    return layer == 11 || layer == 16 ? (-1, (byte)0, (EquippedItem?)null) : (-1, layer, item);
                }
            }
            return (-1, 0, null);
        }

        /// <summary>Where a click targets the character itself (the doll), when nothing worn is under it.</summary>
        private bool OnDoll(Vector2 local) => Modern ? DollRect.Contains(local) : local.x < 185;

        private void RunTopIcon(string command)
        {
            switch (command)
            {
                case "profile":
                    Session.RequestProfile(_serial);
                    break;
                case "status" when _serial != Session.World.PlayerSerial:
                    OpenOtherBar();
                    break;
                default:
                    Ui.Command?.Invoke(command);
                    break;
            }
        }

        /// <summary>A column button's words and icon.</summary>
        private static (string Label, string Icon) ButtonFace(string command, bool war)
        {
            switch (command)
            {
                case "help": return ("Help", "help");
                case "options": return ("Options", "options");
                case "logout": return ("Log Out", "logout");
                case "quests": return ("Quests", "quests");
                case "journal": return ("Journal", "journal");
                case "skills": return ("Skills", "skills");
                case "guild": return ("Guild", "guild");
                case "war": return war ? ("War", "war") : ("Peace", "peace");
                case "status": return ("Status", "status");
                default: return (command, command);
            }
        }

        /// <summary>"Name, the rest of the title": the name for the plaque's large line, the rest for the small one.</summary>
        public static (string Name, string Subtitle) SplitTitle(string title)
        {
            int comma = title.IndexOf(", ", System.StringComparison.Ordinal);
            return comma < 0 ? (title.Trim(), "") : (title.Substring(0, comma).Trim(), title.Substring(comma + 2).Trim());
        }
    }
}
