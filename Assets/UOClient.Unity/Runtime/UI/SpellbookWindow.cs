using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UOClient.IO.Formats;

namespace UOClient.Unity.UI
{
    /// <summary>
    /// A spellbook, from its 0xBF 0x1B content. Magery, Necromancy and Chivalry books are TazUO's classic SpellbookGump
    /// (SpellbookGump.cs): the book art, index spreads listing the held spells (double-click a name for its page), then
    /// two spells to a spread with icon, words, reagents and requirements; a click on a spell's icon or name casts it
    /// from this book. Magery has the circle buttons; the corners turn pages (double-click: first / last); the left
    /// edge minimizes and a double-click restores. Any other school is the plain list: click a spell to cast it.
    /// </summary>
    public sealed partial class SpellbookWindow : UiWindow
    {
        private const float DoubleClickTime = 0.4f;

        private readonly SpellbookState _s;
        private readonly Dictionary<(string, int, int), UiImage> _labels = new Dictionary<(string, int, int), UiImage>();
        private bool _minimized;
        private Vector2 _scroll;
        private float _lastCornerClick;
        private int _lastCorner;
        private int _pressSpell = -1; // the detail icon a left press began on, until it drags out or is released
        private Vector2 _pressAt;

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
            foreach (UiImage img in _labels.Values)
                GumpArt.Release(img);
            _labels.Clear();
        }

        public override void Draw()
        {
            UiImage bg = _s.Classic ? Art.Gump(_minimized ? _s.Info.MinimizedGraphic : _s.Info.BookGraphic) : null;
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
            bool twice = _lastCorner == corner && Time.realtimeSinceStartup - _lastCornerClick < DoubleClickTime;
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
                UiImage img = LabelImage(name, InkHue, IndexNameWidth);
                var r = new Rect(entry.X, entry.Y, img?.Width ?? IndexNameWidth, img?.Height ?? IndexRowHeight);
                bool hover = r.Contains(e.mousePosition);
                Label(name, entry.X, entry.Y, hover ? HoverHue : InkHue, IndexNameWidth);
                // TazUO's index label: a double-click opens the spell's page; a press still drags the book.
                if (hover && e.type == EventType.MouseDown && e.button == 0 && e.clickCount == 2)
                {
                    _s.GoTo(entry.ToPage);
                    e.Use();
                }
            }
        }

        private void DrawDetails()
        {
            foreach (DetailEntry entry in _s.Detail)
            {
                if (entry.Page != _s.Page || !(SpellbookSpells.Get(_s.Info.Kind, entry.Spell) is SpellbookSpells.Spell spell))
                    continue;
                string name = Localized(spell.Cliloc, spell.Name);
                Rect nameRect;
                if (_s.Info.Kind == SpellbookKind.Magery)
                {
                    int circle = entry.Spell >> 3;
                    Label(Localized(SpellbookSpells.CircleCliloc + circle, SpellbookSpells.CircleNames[circle]), entry.TopTextX, TopTextY + 4);
                    nameRect = Label(name, entry.IconTextX, NameY, InkHue, WordsWidth);
                    Label(SpellbookSpells.Abbreviate(spell.Words), entry.IconTextX, MageryWordsY((int)nameRect.height));
                }
                else
                {
                    nameRect = Label(name, entry.TopTextX, TopTextY);
                    Label(spell.Words, entry.IconTextX, NameY, InkHue, WordsWidth);
                }

                UiImage icon = Art.Gump(IconOf(_s.Info, entry.Spell));
                var iconRect = new Rect(entry.IconX, IconY, icon?.Width ?? 44, icon?.Height ?? 44);
                DrawImage(icon, iconRect.x, iconRect.y);
                DragIconOut(iconRect, entry.Spell, name); // before the button, which takes the press and its drags
                if (GUI.Button(iconRect, GUIContent.none, GUIStyle.none) || GUI.Button(nameRect, GUIContent.none, GUIStyle.none))
                    CastFromBook(Session, _s.Book, _s.Info.First, entry.Spell);

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
        /// TazUO's OnIconDragBegin (:806-830): a press on a spell's icon that moves 5 px puts a floating spell icon under
        /// the mouse (replacing one already out for that spell) and carries it until the button is let go. The press no
        /// longer casts from the book. The icon lives in this session's UiManager only: nothing is saved.
        /// </summary>
        private void DragIconOut(Rect iconRect, int index, string name)
        {
            Event e = Event.current;
            if (e.button != 0)
                return;
            if (e.type == EventType.MouseDown && iconRect.Contains(e.mousePosition))
            {
                _pressSpell = index;
                _pressAt = e.mousePosition;
            }
            else if (e.type == EventType.MouseUp)
                _pressSpell = -1;
            else if (e.type == EventType.MouseDrag && _pressSpell == index && StartsDrag(e.mousePosition.x - _pressAt.x, e.mousePosition.y - _pressAt.y))
            {
                _pressSpell = -1;
                GUIUtility.hotControl = 0; // the icon's button lets go, so the release casts nothing
                var grab = new Vector2(FloatingIconGrab, FloatingIconGrab);
                SpellIconWindow floating = Ui.Open(new SpellIconWindow(_s.Info.First + index, IconOf(_s.Info, index), name));
                floating.Rect.position = Rect.position + e.mousePosition - grab;
                Ui.BeginWindowDrag(floating, grab);
                e.Use();
            }
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
    /// edit. Double-click casts the spell (0xBF 0x1C, SpellbookWindow.CastIcon); drag moves it; right-click closes it;
    /// Esc leaves it. Session-only: it belongs to this session's UiManager and goes with it, and nothing is written.
    /// </summary>
    public sealed class SpellIconWindow : UiWindow
    {
        private readonly int _spell, _gump;
        private readonly string _name;

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
            UiImage icon = Art.Gump(_gump);
            Rect.width = icon?.Width ?? 44;
            Rect.height = icon?.Height ?? 44;
            DrawImage(icon, 0, 0);

            Event e = Event.current;
            bool inside = new Rect(0, 0, Rect.width, Rect.height).Contains(e.mousePosition);
            HoveredText = inside ? Tooltip() : null;
            if (inside && e.type == EventType.MouseDown && e.button == 0 && e.clickCount == 2)
            {
                SpellbookWindow.CastIcon(Session, _spell);
                e.Use();
            }
        }

        private string Tooltip()
        {
            int cliloc = SpellbookWindow.SpellIconTooltip(_spell);
            return cliloc != 0 && Ui.Cliloc?.Get(cliloc) is string s && s.Length > 0 ? s : _name;
        }
    }
}
