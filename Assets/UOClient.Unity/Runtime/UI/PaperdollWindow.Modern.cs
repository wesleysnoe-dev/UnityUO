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
        private const float Border = 18f, IconBand = 46f, DollW = 266f, DollH = 344f, ColumnGap = 4f, ColumnW = 142f;
        private const float ButtonH = 36f, ButtonGap = 6f, PlaqueGap = 8f, PlaqueH = 60f;
        private const float DollScale = 1.45f, SlotSize = 34f, IconSize = 32f, IconGap = 10f;

        /// <summary>The painted niche's opening as shares of its picture: between the columns, from under the arch to the floor.</summary>
        private const float NicheLeft = 0.21f, NicheRight = 0.79f, NicheTop = 0.19f, NicheFloor = 0.905f;

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
        private float _dollScale = DollScale;   // the doll's scale this frame (smaller when what is worn needs it)
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
            // The books at the top band's right end (clear of the round buttons, which are centred).
            float bx = w - Border - 10f;
            if (_racialBook != null)
            {
                bx -= _racialBook.Width;
                _racialAt = new Vector2(bx, Border + (IconBand - _racialBook.Height) / 2f);
                bx -= 6f;
            }
            if (_combatBook != null)
            {
                bx -= _combatBook.Width;
                _combatAt = new Vector2(bx, Border + (IconBand - _combatBook.Height) / 2f);
            }

            // The body and what is worn, as the classic paperdoll layers them; larger, centred in the niche.
            bool female = IsFemale(m.Body);
            UiImage body = Art.Gump(female ? 0x000D : 0x000C, m.Hue);
            CollectWorn(m, female);
            Compose(body, female ? 0x000D : 0x000C, m.Hue);
            // Everything drawn (the body, a halberd above the head, a cloak, the pack) fits the niche: centred by its
            // pixels, at DollScale or smaller when it would not fit.
            int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
            void Grow(UiImage img)
            {
                if (img == null)
                    return;
                var (bx0, by0, bw0, bh0) = BoundsOf(img);
                if (bw0 <= 0 || bh0 <= 0)
                    return;
                minX = Mathf.Min(minX, bx0); minY = Mathf.Min(minY, by0);
                maxX = Mathf.Max(maxX, bx0 + bw0 + 1); maxY = Mathf.Max(maxY, by0 + bh0 + 1);
            }
            Grow(_comp);
            if (minX > maxX)
            {
                minX = 0; minY = 0; maxX = 180; maxY = 240;
            }
            // With the painted niche, the doll stands inside its arch with its feet on the niche's floor (the opening's
            // shares of the picture, measured from Resources/Paperdoll/doll_backdrop: between the columns, under the arch,
            // down to where the back wall meets the floor). Without it, centred in the doll's area as before.
            Rect fit = PaperdollSkin.Piece("doll_backdrop") != null
                ? new Rect(doll.x + doll.width * NicheLeft, doll.y + doll.height * NicheTop,
                           doll.width * (NicheRight - NicheLeft), doll.height * (NicheFloor - NicheTop))
                : new Rect(doll.x + 8f, doll.y + 8f, doll.width - 16f, doll.height - 16f);
            _dollScale = Mathf.Min(DollScale, fit.width / (maxX - minX), fit.height / (maxY - minY));
            bool onFloor = PaperdollSkin.Piece("doll_backdrop") != null;
            _bodyAt = new Vector2(fit.center.x - (minX + maxX) / 2f * _dollScale,
                                  onFloor ? fit.yMax - maxY * _dollScale : fit.center.y - (minY + maxY) / 2f * _dollScale);

            LayoutSlots(m, doll);

            (int Slot, byte Layer, EquippedItem? Item) hit = MouseIsOver ? HitAt(Event.current.mousePosition) : (Slot: -1, Layer: (byte)0, Item: null);
            uint over = hit.Item?.Serial ?? 0;
            HoveredText = null;

            GUI.BeginGroup(doll);
            var origin = _bodyAt - doll.position;
            // The hovered piece's outline, then the composed doll over it (only the outline's rim shows).
            foreach (var (_, e, img, _) in _drawn)
                if (over != 0 && hit.Slot < 0 && e.Serial == over && Art.Outline(img) is UiImage o)
                    GUI.DrawTexture(new Rect(origin.x - _dollScale, origin.y - _dollScale, o.Width * _dollScale, o.Height * _dollScale), o.Texture);
            if (_comp != null)
                GUI.DrawTexture(new Rect(origin.x, origin.y, _comp.Width * _dollScale, _comp.Height * _dollScale), _comp.Texture);
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
            // The plaque's knotwork caps stretch with it: about a tenth of its width at each end (the art's dark panel
            // runs from 104 to 920 of 1024).
            float caps = plaque.width * 0.105f + 4f;
            float textLeft = plaque.x + caps, textW = plaque.width - caps * 2f;
            PaperdollSkin.Title(new Rect(textLeft, plaque.y + (rest.Length > 0 ? 6f : 16f), textW, 28f), name, 22, PaperdollSkin.Gold, minSize: 13);
            if (rest.Length > 0)
                PaperdollSkin.Title(new Rect(textLeft, plaque.y + 34f, textW, 20f), rest, 15, PaperdollSkin.GoldFaded, minSize: 12);

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

        private string _compKey;
        private UiImage _comp;   // the doll composed at its art's size (Bitmap) and smoothed (Texture)
        private readonly List<Bitmap32> _covers = new List<Bitmap32>(); // per worn piece: its unhued art when it carries a cover, else null

        /// <summary>
        /// UO's darkest blue (colour 1), which the classic paperdoll art paints round a helmet, the arms, a hood: a
        /// cover that hides what is under it (hair under a helmet) against the classic paperdoll's dark background.
        /// </summary>
        private static bool IsCover(uint p) =>
            (p >> 24) != 0 && ((p >> 16) & 0xFF) <= 4 && ((p >> 8) & 0xFF) <= 4 && (p & 0xFF) <= 12;

        /// <summary>A piece carries a cover when it has a patch of colour 1, not a stray dark pixel or two (the body has some).</summary>
        private const int CoverPixels = 60;

        /// <summary>
        /// The body and what is worn laid into one picture, back to front: a piece's cover pixels clear what is under them
        /// (so the niche shows there, not black). Kept at the art's size (Bitmap: bounds, the hit test) and composed again
        /// at 4x from each piece's HD art (the HD pass; Scale2x of what has none) for the texture. Remade when what is worn changes.
        /// </summary>
        private void Compose(UiImage body, int bodyGump, int bodyHue)
        {
            var key = new System.Text.StringBuilder().Append(bodyGump).Append(':').Append(bodyHue);
            foreach (var d in _drawn)
                key.Append('|').Append(d.Gump).Append(':').Append(d.Item.Hue);
            string k = key.ToString();
            if (k == _compKey && _comp != null)
                return;
            _compKey = k;
            int w = body?.Width ?? 1, h = body?.Height ?? 1;
            foreach (var d in _drawn)
            {
                w = Mathf.Max(w, d.Image.Width);
                h = Mathf.Max(h, d.Image.Height);
            }
            var comp = new Bitmap32(w, h);
            void Lay(Bitmap32 src, Bitmap32 cover)
            {
                for (int y = 0; y < src.Height; y++)
                    for (int x = 0; x < src.Width; x++)
                    {
                        if (cover != null && x < cover.Width && y < cover.Height && IsCover(cover[x, y]))
                            comp[x, y] = 0;
                        else if ((src[x, y] >> 24) != 0)
                            comp[x, y] = src[x, y];
                    }
            }
            if (body != null)
                Lay(body.Bitmap, null);
            _covers.Clear();
            foreach (var d in _drawn)
            {
                Bitmap32 raw = Art.Data.GetGump(d.Gump);
                int n = 0;
                if (raw != null)
                    foreach (uint p in raw.Pixels)
                        if (IsCover(p))
                            n++;
                Bitmap32 cover = n > CoverPixels ? raw : null;
                _covers.Add(cover);
                Lay(d.Image.Bitmap, cover);
            }
            // The same at 4x, from the HD art: soft outline rims laid over what is under them.
            var big = new Bitmap32(w * DollArtScale, h * DollArtScale);
            void LayHd(UiImage img, Bitmap32 cover)
            {
                Bitmap32 src = HdAt4(img);
                for (int y = 0; y < src.Height && y < big.Height; y++)
                    for (int x = 0; x < src.Width && x < big.Width; x++)
                    {
                        int cx = x / DollArtScale, cy = y / DollArtScale;
                        if (cover != null && cx < cover.Width && cy < cover.Height && IsCover(cover[cx, cy]))
                        {
                            big[x, y] = 0;
                            continue;
                        }
                        uint p = src[x, y];
                        uint a = p >> 24;
                        if (a == 0)
                            continue;
                        big[x, y] = a == 255 ? p : Over(p, big[x, y]);
                    }
            }
            if (body != null)
                LayHd(body, null);
            for (int i = 0; i < _drawn.Count; i++)
                LayHd(_drawn[i].Image, _covers[i]);

            if (_comp != null)
                Object.Destroy(_comp.Texture);
            Texture2D tex = UnityTextures.ToTexture(GumpArt.Bleed(big, 2), "paperdoll doll");
            tex.filterMode = FilterMode.Bilinear;
            _comp = new UiImage { Bitmap = comp, Texture = tex };
        }

        /// <summary>The doll's texture: 4x its art (the HD pass's scale).</summary>
        private const int DollArtScale = 4;

        /// <summary>A piece at 4x: its HD art when it has it at 4x (or 2x, doubled), else its art doubled twice (Scale2x).</summary>
        private static Bitmap32 HdAt4(UiImage img)
        {
            if (img.Hd != null && img.HdScale == DollArtScale)
                return img.Hd;
            if (img.Hd != null && img.HdScale == 2)
                return GumpArt.Scale2x(img.Hd);
            return GumpArt.Scale2x(GumpArt.Scale2x(img.Bitmap));
        }

        /// <summary>A part-transparent pixel laid over another (straight alpha).</summary>
        private static uint Over(uint top, uint under)
        {
            float a = (top >> 24) / 255f, b = (under >> 24) / 255f;
            float o = a + b * (1f - a);
            if (o <= 0f)
                return 0;
            uint Mix(int shift) => (uint)Mathf.Clamp(Mathf.RoundToInt((((top >> shift) & 0xFF) * a + ((under >> shift) & 0xFF) * b * (1f - a)) / o), 0, 255);
            return (uint)Mathf.RoundToInt(o * 255f) << 24 | Mix(16) << 16 | Mix(8) << 8 | Mix(0);
        }

        public override void OnClosed()
        {
            if (_comp != null)
                Object.Destroy(_comp.Texture);
            _comp = null;
            _compKey = null;
        }

        /// <summary>The worn pieces with paperdoll art, back to front (as the classic Draw collects them).</summary>
        private void CollectWorn(Mobile m, bool female)
        {
            _drawn.Clear();
            var worn = WornGraphics(m);
            foreach (byte layer in OrderFor(m))
            {
                // What another piece hides (hair under a hood) is not drawn, as on the character in the world.
                if (!m.Equipment.TryGetValue(layer, out EquippedItem e) || MobileLook.IsCovered(worn, layer))
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
                GUI.DrawTexture(new Rect(origin.x - _dollScale, origin.y - _dollScale, o.Width * _dollScale, o.Height * _dollScale), o.Texture);
            UiImage smooth = Art.GumpSmooth(gump, hue, partial) ?? img;
            GUI.DrawTexture(new Rect(origin.x, origin.y, img.Width * _dollScale, img.Height * _dollScale), smooth.Texture);
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
                int px = Mathf.FloorToInt((local.x - _bodyAt.x) / _dollScale), py = Mathf.FloorToInt((local.y - _bodyAt.y) / _dollScale);
                for (int i = _drawn.Count - 1; i >= 0; i--)
                {
                    var (layer, item, img, _) = _drawn[i];
                    if (!img.HitTest(px, py) || i < _covers.Count && _covers[i] != null && IsCover(_covers[i][px, py]))
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
