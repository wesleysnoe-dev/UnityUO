using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UOClient.IO.Formats;

namespace UOClient.Unity.UI
{
    /// <summary>
    /// A spellbook, from its 0xBF 0x1B content. Magery, Necromancy and Chivalry books are TazUO's classic SpellbookGump
    /// (SpellbookGump.cs): the book art, index spreads listing the held spells, then two spells to a spread with icon,
    /// words, reagents and requirements. As TazUO: a click on an index name opens its spread once the double-click time
    /// has passed, a double-click on a name or a detail icon casts it (0xBF 0x1C, no book), and an icon dragged 5 px
    /// comes out as a floating icon; a single click never casts. Magery has the circle buttons; the corners turn pages
    /// (double-click: first / last); the left edge minimizes and a double-click restores. Any other school is the plain
    /// list: click a spell to cast it from this book.
    /// </summary>
    public sealed partial class SpellbookWindow : UiWindow
    {
        private readonly SpellbookState _s;
        private readonly Dictionary<(string, int, int), UiImage> _labels = new Dictionary<(string, int, int), UiImage>();
        private readonly SpellbookClicks _clicks = new SpellbookClicks();
        private bool _minimized;
        private Vector2 _scroll;
        private float _lastCornerClick;
        private int _lastCorner;
        private int _pressId;

        public SpellbookWindow(uint book, int first, ulong bits)
        {
            _s = new SpellbookState(book, first, bits);
            Rect = new Rect(420, 90, 360, 480);
        }

        public override string Key => "spellbook:" + _s.Book;

        /// <summary>The book, its spells and the spread open (SpellbookState).</summary>
        public SpellbookState State => _s;

        /// <summary>The server sent the book's content (again): take its spells, staying on the page if it still exists.</summary>
        public void SetContent(int first, ulong bits) => _s.SetContent(first, bits);

        public override void OnClosed()
        {
            // Closed mid-press (the server re-sent or closed the book): no Up will come to give the hot control back.
            if (_clicks.HoldingIcon)
                ReleaseHot(_pressId);
            _clicks.Cancel();
            foreach (UiImage img in _labels.Values)
                GumpArt.Release(img);
            _labels.Clear();
        }

        public override void Draw()
        {
            _pressId = GUIUtility.GetControlID(FocusType.Passive); // every event, so the id holds while an icon is pressed
            HoveredText = null; // a detail icon's tooltip, set again while the mouse is on one
            UiImage bg =_s.Classic ? Art.Gump(_minimized ? _s.Info.MinimizedGraphic : _s.Info.BookGraphic) : null;
            if (bg == null)
            {
                DrawList();
                return;
            }
            Rect.width = bg.Width;
            Rect.height = bg.Height;
            DrawImage(bg, 0, 0);

            Event e = Event.current;
            if (_minimized)
            {
                if (e.type == EventType.MouseDown && e.button == 0 && e.clickCount == 2)
                {
                    _minimized = false;
                    e.Use();
                }
                return;
            }

            // Before every button, so each left press is counted toward a double-click as TazUO's one click clock does.
            Pointer(e, _pressId);
            int queued = _clicks.Tick(Time.realtimeSinceStartupAsDouble);
            if (queued >= 0)
                _s.GoTo(queued);

            if (_s.Info.Kind == SpellbookKind.Magery)
                foreach ((int gump, int x, int y, int page) in CircleButtons)
                    if (GumpButton(gump, gump, x, y))
                        _s.GoTo(page);

            if (_s.OnIndex)
                DrawIndex();
            else
                DrawDetails();

            if (GUI.Button(new Rect(MinimizeX, MinimizeY, MinimizeW, MinimizeH), GUIContent.none, GUIStyle.none))
                _minimized = true;
            if (_s.Page != 1 && GumpButton(PrevCorner, PrevCorner, PrevCornerX, CornerY))
                Turn(-1, PrevCorner, 1);
            if (_s.Page != _s.MaxPage && GumpButton(NextCorner, NextCorner, NextCornerX, CornerY))
                Turn(1, NextCorner, _s.MaxPage);
        }

        /// <summary>A click turns one page; a second click on the same corner (TazUO's double-click) goes to the end.</summary>
        private void Turn(int by, int corner, int end)
        {
            bool twice = _lastCorner == corner && Time.realtimeSinceStartup - _lastCornerClick <= DoubleClickSeconds;
            _s.GoTo(twice ? end : _s.Page + by);
            _lastCorner = twice ? 0 : corner;
            _lastCornerClick = Time.realtimeSinceStartup;
        }

        private void DrawIndex()
        {
            for (int half = 0; half < 2; half++)
            {
                Label(SpellbookSpells.IndexText, IndexTitleX(half), IndexTitleY);
                if (_s.Info.Kind == SpellbookKind.Magery)
                {
                    int circle = CircleOfHalf(_s.Page, half);
                    Label(Localized(SpellbookSpells.CircleCliloc + circle, SpellbookSpells.CircleNames[circle]), IndexDataX(half), IndexSubtitleY);
                }
            }
            if (_s.Page == 1 && _s.Info.Kind == SpellbookKind.Chivalry)
            {
                string tithing = Localized(SpellbookSpells.TithingCliloc, null) is string t && Localized(SpellbookSpells.AvailableCliloc, null) is string a
                    ? t + "\n" + a
                    : SpellbookSpells.TithingText;
                Label($"{tithing} {Session.World.Player?.Tithe ?? 0}", TithingX, TithingY);
            }

            Event e = Event.current;
            foreach (IndexEntry entry in _s.Index)
            {
                if (entry.Page != _s.Page)
                    continue;
                string name = SpellName(entry.Spell);
                bool hover = IndexRect(entry).Contains(e.mousePosition);
                Label(name, entry.X, entry.Y, hover ? HoverHue : InkHue, IndexNameWidth);
            }
        }

        /// <summary>An index name's HoveredLabel box: the cropped label, or a row if the font is missing.</summary>
        private Rect IndexRect(IndexEntry entry)
        {
            UiImage img = LabelImage(SpellName(entry.Spell), InkHue, IndexNameWidth);
            return new Rect(entry.X, entry.Y, img?.Width ?? IndexNameWidth, img?.Height ?? IndexRowHeight);
        }

        private Rect IconRect(DetailEntry entry)
        {
            UiImage icon = Art.Gump(IconOf(_s.Info, entry.Spell));
            return new Rect(entry.IconX, IconY, icon?.Width ?? 44, icon?.Height ?? 44);
        }

        /// <summary>What on the open spread a window-local point is over: a held spell's icon or index name, or nothing.</summary>
        private (BookTarget Target, int Spell, int ToPage) HitTest(Vector2 local)
        {
            if (_s.OnIndex)
            {
                foreach (IndexEntry entry in _s.Index)
                    if (entry.Page == _s.Page && IndexRect(entry).Contains(local))
                        return (BookTarget.Index, entry.Spell, entry.ToPage);
            }
            else
            {
                foreach (DetailEntry entry in _s.Detail)
                    if (entry.Page == _s.Page && IconRect(entry).Contains(local))
                        return (BookTarget.Icon, entry.Spell, 0);
            }
            return (BookTarget.None, -1, 0);
        }

        /// <summary>
        /// The left button on the classic book, decided by SpellbookClicks.Route. An icon press takes the window's hot
        /// control itself (no GUI.Button, which would hold the press and keep its drags from us; and not left to
        /// GUI.DragWindow, so the book stays put): at 5 px it lets go and the floating icon rides the mouse
        /// (UiManager.BeginWindowDrag, as a health bar dragged out of the world). An index name's press is left alone, so
        /// it drags the book as TazUO's CanMove label does, and its release queues the page only if the book did not move.
        /// </summary>
        private void Pointer(Event e, int id)
        {
            // A held icon whose MouseUp never reached us (let go where IMGUI gave it to no window of ours): the button is
            // up, so drop the press and the hot control rather than leave every other control locked out. Polled as
            // UiManager.FollowWindowDrag does; not on mouse events, whose own Up does this.
            if (_clicks.HoldingIcon && !e.isMouse && !Input.GetMouseButton(0))
            {
                _clicks.Cancel();
                ReleaseHot(id);
                return;
            }
            if (!e.isMouse || e.button != 0)
                return;
            PointerEvent kind;
            switch (e.type)
            {
                case EventType.MouseDown: kind = PointerEvent.Down; break;
                case EventType.MouseDrag: kind = PointerEvent.Drag; break;
                case EventType.MouseUp: kind = PointerEvent.Up; break;
                default: return;
            }
            Vector2 at = Rect.position + e.mousePosition;
            (BookTarget target, int spell, int toPage) = kind == PointerEvent.Down ? HitTest(e.mousePosition) : (BookTarget.None, -1, 0);
            (BookAction action, bool use) = _clicks.Route(kind, target, spell, toPage, at.x, at.y, Time.realtimeSinceStartupAsDouble);
            if (_clicks.HoldingIcon)
                GUIUtility.hotControl = id;
            else
                ReleaseHot(id);
            if (action == BookAction.Cast)
                CastBookSpell(Session, _s.Info.First, _clicks.Spell);
            else if (action == BookAction.DragOut)
                DragIconOut(_clicks.Spell, e.mousePosition);
            if (use)
                e.Use();
        }

        private static void ReleaseHot(int id)
        {
            if (GUIUtility.hotControl == id)
                GUIUtility.hotControl = 0;
        }

        private void DrawDetails()
        {
            foreach (DetailEntry entry in _s.Detail)
            {
                if (entry.Page != _s.Page || !(SpellbookSpells.Get(_s.Info.Kind, entry.Spell) is SpellbookSpells.Spell spell))
                    continue;
                string name = Localized(spell.Cliloc, spell.Name);
                if (_s.Info.Kind == SpellbookKind.Magery)
                {
                    int circle = entry.Spell >> 3;
                    Label(Localized(SpellbookSpells.CircleCliloc + circle, SpellbookSpells.CircleNames[circle]), entry.TopTextX, TopTextY + 4);
                    Rect nameRect = Label(name, entry.IconTextX, NameY, InkHue, WordsWidth);
                    Label(SpellbookSpells.Abbreviate(spell.Words), entry.IconTextX, MageryWordsY((int)nameRect.height));
                }
                else
                {
                    Label(name, entry.TopTextX, TopTextY);
                    Label(spell.Words, entry.IconTextX, NameY, InkHue, WordsWidth);
                }

                // The icon's double-click and drag are Pointer's; the name label takes no clicks (TazUO's plain Label).
                // Hue 38 while the server has the spell on (HueGumpPic); its tooltip is the book's own cliloc.
                DrawImage(Art.Gump(IconOf(_s.Info, entry.Spell), IconHue(Session, _s.Info.First + entry.Spell)), entry.IconX, IconY);
                if (IconRect(entry).Contains(Event.current.mousePosition)
                    && Ui.Cliloc?.Get(BookIconTooltip(_s.Info.Kind, entry.Spell)) is string tip && tip.Length > 0)
                    HoveredText = tip;

                if (spell.Reagents.Length > 0)
                {
                    DrawTiled(Art.Gump(ReagentRule), entry.IconX, ReagentRuleY, ReagentRuleW, ReagentRuleH);
                    Label(Localized(SpellbookSpells.ReagentsCliloc, SpellbookSpells.ReagentsText), entry.IconX, ReagentsTitleY);
                    Label(string.Join("\n", spell.Reagents.Select(r => Localized(r.Cliloc, r.Name))), entry.IconX, ReagentListY);
                }
                if (_s.Info.Kind != SpellbookKind.Magery)
                    Label(SpellbookSpells.Requires(spell.Mana, spell.MinSkill), entry.IconX, RequiresY);
            }
        }

        /// <summary>
        /// TazUO's OnIconDragBegin (:806-830): a floating spell icon under the mouse (replacing one already out for that
        /// spell), carried until the button is let go. It lives in this session's UiManager only: nothing is saved.
        /// </summary>
        private void DragIconOut(int index, Vector2 local)
        {
            var grab = new Vector2(FloatingIconGrab, FloatingIconGrab);
            SpellIconWindow floating = Ui.Open(new SpellIconWindow(_s.Info.First + index, IconOf(_s.Info, index), SpellName(index)));
            floating.Rect.position = Rect.position + local - grab;
            Ui.BeginWindowDrag(floating, grab);
        }

        private string SpellName(int index) =>
            SpellbookSpells.Get(_s.Info.Kind, index) is SpellbookSpells.Spell s ? Localized(s.Cliloc, s.Name) : "";

        private string Localized(int number, string fallback) =>
            Ui.Cliloc?.Get(number) is string s && s.Length > 0 ? s : fallback;

        /// <summary>
        /// A book label in a client hue: unicode font 1, as our other client-drawn labels (CachedLabel); TazUO uses ASCII
        /// fonts 6, 8 and 9. Returns where it was drawn. Without the font, IMGUI text in the hue's colour.
        /// </summary>
        private Rect Label(string text, float x, float y, int hue = InkHue, int maxWidth = 0)
        {
            if (string.IsNullOrEmpty(text))
                return new Rect(x, y, 0, 0);
            UiImage img = LabelImage(text, hue, maxWidth);
            if (img != null)
            {
                DrawImage(img, x, y);
                return new Rect(x, y, img.Width, img.Height);
            }
            int lines = text.Count(c => c == '\n') + 1;
            var r = new Rect(x, y, maxWidth > 0 ? maxWidth : 160, 16 * lines);
            Text(r, text, Art.GumpTextColor(hue), 12);
            return r;
        }

        private UiImage LabelImage(string text, int hue, int maxWidth)
        {
            if (_labels.TryGetValue((text, hue, maxWidth), out UiImage img))
                return img;
            UniTextStyle style = maxWidth == IndexNameWidth ? UniTextStyle.Cropped : UniTextStyle.None; // HoveredLabel's FontStyle.Cropped
            img = GumpArt.TextImage(Art.Text.Generate(text, ServerGumpWindow.GumpFont,
                ServerGumpWindow.LabelArgb(Art.Data.Hues.Entries, hue), maxWidth, UniTextAlign.Left, style));
            _labels[(text, hue, maxWidth)] = img;
            return img;
        }

        /// <summary>Any other school (or no book art): the spells it holds as a list; click one to cast it.</summary>
        private void DrawList()
        {
            Rect.width = 360;
            Rect.height = 480;
            DrawResizePic(9200, 0, 0, Rect.width, Rect.height);
            string[] names = SpellNames.School(_s.First, out string school, out int iconBase);
            Text(new Rect(0, 10, Rect.width, 22), school, new Color(1f, 0.9f, 0.6f), 16, TextAnchor.UpperCenter, true);

            var known = Enumerable.Range(0, 64).Where(i => (_s.Bits & (1UL << i)) != 0 && i < names.Length).ToList();
            if (known.Count == 0)
            {
                Text(new Rect(20, 50, 300, 20), "This book holds no spells.");
                return;
            }
            const float row = 48;
            var view = new Rect(12, 40, Rect.width - 24, Rect.height - 56);
            _scroll = GUI.BeginScrollView(view, _scroll, new Rect(0, 0, view.width - 18, known.Count * row));
            for (int n = 0; n < known.Count; n++)
            {
                int i = known[n];
                float y = n * row;
                UiImage icon = iconBase > 0 ? Art.Gump(iconBase + i) : null;
                if (icon != null)
                    GUI.DrawTexture(new Rect(4, y + 2, 44, 44), icon.Texture);
                string circle = _s.First == 1 ? $"Circle {i / 8 + 1}" : "";
                Text(new Rect(56, y + 6, 240, 20), names[i], new Color(0.95f, 0.92f, 0.82f), 14);
                Text(new Rect(56, y + 24, 240, 20), circle, new Color(0.7f, 0.66f, 0.55f), 12);
                if (GUI.Button(new Rect(0, y, view.width - 18, row), GUIContent.none, GUIStyle.none))
                    CastFromBook(Session, _s.Book, _s.First, i);
            }
            GUI.EndScrollView();
        }
    }

    /// <summary>
    /// A spell icon dragged out of a classic book: TazUO's UseSpellButtonGump without its saving, hotkey label or macro
    /// edit. SingleClickIconUse off (TazUO's default): a double-click casts the spell (0xBF 0x1C, SpellbookWindow.CastIcon)
    /// and a single click does nothing; drag moves it; right-click closes it;
    /// Esc leaves it. Session-only: it belongs to this session's UiManager and goes with it, and nothing is written.
    /// </summary>
    public sealed class SpellIconWindow : UiWindow
    {
        private readonly int _spell, _gump;
        private readonly string _name;
        private readonly SpellbookClicks _clicks = new SpellbookClicks(); // only its double-click clock

        public SpellIconWindow(int spell, int gump, string name)
        {
            _spell = spell;
            _gump = gump;
            _name = name;
            Rect = new Rect(0, 0, 44, 44);
        }

        public override string Key => "spellicon:" + _spell;

        public override bool ClosesOnEsc => false;

        public override bool StepsAsideOnOpen => false;

        public override void Draw()
        {
            // Hue 38 while the server has the spell on. TazUO hues an open UseSpellButtonGump when 0xBF 0x25 arrives
            // (ExtendedCommand :520-531); this reads the same ActiveSpellIcons each frame, so one dragged out later matches.
            UiImage icon = Art.Gump(_gump, SpellbookWindow.IconHue(Session, _spell));
            Rect.width = icon?.Width ?? 44;
            Rect.height = icon?.Height ?? 44;
            DrawImage(icon, 0, 0);

            Event e = Event.current;
            bool inside = new Rect(0, 0, Rect.width, Rect.height).Contains(e.mousePosition);
            HoveredText = inside ? Tooltip() : null;
            // A press is counted, not used, so it still drags the icon; the second within 350 ms casts.
            if (inside && e.type == EventType.MouseDown && e.button == 0
                && _clicks.Press(BookTarget.Icon, _spell, 0, e.mousePosition.x, e.mousePosition.y, Time.realtimeSinceStartupAsDouble) == BookAction.Cast)
            {
                SpellbookWindow.CastIcon(Session, _spell);
                e.Use();
            }
        }

        /// <summary>UseSpellButtonGump.GetSpellTooltip's cliloc (Magery 3002011+, Necromancy 1060509+, Chivalry 1060585+); the name without it.</summary>
        private string Tooltip()
        {
            int cliloc = SpellbookWindow.SpellIconTooltip(_spell);
            return cliloc != 0 && Ui.Cliloc?.Get(cliloc) is string s && s.Length > 0 ? s : _name;
        }
    }
}
