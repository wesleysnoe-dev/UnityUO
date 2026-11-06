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

        private readonly uint _book;
        private readonly Dictionary<(string, int, int), UiImage> _labels = new Dictionary<(string, int, int), UiImage>();
        private int _first;
        private ulong _bits;
        private bool _classic;
        private SpellbookInfo _info;
        private List<IndexEntry> _index;
        private List<DetailEntry> _detail;
        private int _maxPage, _page = 1;
        private bool _minimized;
        private Vector2 _scroll;
        private float _lastCornerClick;
        private int _lastCorner;

        public SpellbookWindow(uint book, int first, ulong bits)
        {
            _book = book;
            Rect = new Rect(420, 90, 360, 480);
            SetContent(first, bits);
        }

        public override string Key => "spellbook:" + _book;

        /// <summary>The server sent the book's content (again): take its spells, staying on the page if it still exists.</summary>
        public void SetContent(int first, ulong bits)
        {
            _first = first;
            _bits = bits;
            _classic = TryGetBookInfo(first, out _info);
            if (!_classic)
                return;
            _index = IndexEntries(_info, bits);
            _detail = DetailEntries(_info, bits);
            _maxPage = MaxPage(_info, bits);
            _page = ClampPage(_page, _maxPage);
        }

        public override void OnClosed()
        {
            foreach (UiImage img in _labels.Values)
                GumpArt.Release(img);
            _labels.Clear();
        }

        public override void Draw()
        {
            UiImage bg = _classic ? Art.Gump(_minimized ? _info.MinimizedGraphic : _info.BookGraphic) : null;
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

            if (_info.Kind == SpellbookKind.Magery)
                foreach ((int gump, int x, int y, int page) in CircleButtons)
                    if (GumpButton(gump, gump, x, y))
                        _page = ClampPage(page, _maxPage);

            if (_page <= _info.IndexPages)
                DrawIndex();
            else
                DrawDetails();

            if (GUI.Button(new Rect(MinimizeX, MinimizeY, MinimizeW, MinimizeH), GUIContent.none, GUIStyle.none))
                _minimized = true;
            if (_page != 1 && GumpButton(PrevCorner, PrevCorner, PrevCornerX, CornerY))
                Turn(-1, PrevCorner, 1);
            if (_page != _maxPage && GumpButton(NextCorner, NextCorner, NextCornerX, CornerY))
                Turn(1, NextCorner, _maxPage);
        }

        /// <summary>A click turns one page; a second click on the same corner (TazUO's double-click) goes to the end.</summary>
        private void Turn(int by, int corner, int end)
        {
            bool twice = _lastCorner == corner && Time.realtimeSinceStartup - _lastCornerClick < DoubleClickTime;
            _page = ClampPage(twice ? end : _page + by, _maxPage);
            _lastCorner = twice ? 0 : corner;
            _lastCornerClick = Time.realtimeSinceStartup;
        }

        private void DrawIndex()
        {
            for (int half = 0; half < 2; half++)
            {
                Label(SpellbookSpells.IndexText, IndexTitleX(half), IndexTitleY);
                if (_info.Kind == SpellbookKind.Magery)
                {
                    int circle = CircleOfHalf(_page, half);
                    Label(Localized(SpellbookSpells.CircleCliloc + circle, SpellbookSpells.CircleNames[circle]), IndexDataX(half), IndexSubtitleY);
                }
            }
            if (_page == 1 && _info.Kind == SpellbookKind.Chivalry)
            {
                string tithing = Localized(SpellbookSpells.TithingCliloc, null) is string t && Localized(SpellbookSpells.AvailableCliloc, null) is string a
                    ? t + "\n" + a
                    : SpellbookSpells.TithingText;
                Label($"{tithing} {Session.World.Player?.Tithe ?? 0}", TithingX, TithingY);
            }

            Event e = Event.current;
            foreach (IndexEntry entry in _index)
            {
                if (entry.Page != _page)
                    continue;
                string name = SpellName(entry.Spell);
                UiImage img = LabelImage(name, InkHue, IndexNameWidth);
                var r = new Rect(entry.X, entry.Y, img?.Width ?? IndexNameWidth, img?.Height ?? IndexRowHeight);
                bool hover = r.Contains(e.mousePosition);
                Label(name, entry.X, entry.Y, hover ? HoverHue : InkHue, IndexNameWidth);
                // TazUO's index label: a double-click opens the spell's page; a press still drags the book.
                if (hover && e.type == EventType.MouseDown && e.button == 0 && e.clickCount == 2)
                {
                    _page = ClampPage(entry.ToPage, _maxPage);
                    e.Use();
                }
            }
        }

        private void DrawDetails()
        {
            foreach (DetailEntry entry in _detail)
            {
                if (entry.Page != _page || !(SpellbookSpells.Get(_info.Kind, entry.Spell) is SpellbookSpells.Spell spell))
                    continue;
                string name = Localized(spell.Cliloc, spell.Name);
                Rect nameRect;
                if (_info.Kind == SpellbookKind.Magery)
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

                UiImage icon = Art.Gump(IconOf(_info, entry.Spell));
                var iconRect = new Rect(entry.IconX, IconY, icon?.Width ?? 44, icon?.Height ?? 44);
                DrawImage(icon, iconRect.x, iconRect.y);
                if (GUI.Button(iconRect, GUIContent.none, GUIStyle.none) || GUI.Button(nameRect, GUIContent.none, GUIStyle.none))
                    CastFromBook(Session, _book, _info.First, entry.Spell);

                if (spell.Reagents.Length > 0)
                {
                    DrawTiled(Art.Gump(ReagentRule), entry.IconX, ReagentRuleY, ReagentRuleW, ReagentRuleH);
                    Label(Localized(SpellbookSpells.ReagentsCliloc, SpellbookSpells.ReagentsText), entry.IconX, ReagentsTitleY);
                    Label(string.Join("\n", spell.Reagents.Select(r => Localized(r.Cliloc, r.Name))), entry.IconX, ReagentListY);
                }
                if (_info.Kind != SpellbookKind.Magery)
                    Label(SpellbookSpells.Requires(spell.Mana, spell.MinSkill), entry.IconX, RequiresY);
            }
        }

        private string SpellName(int index) =>
            SpellbookSpells.Get(_info.Kind, index) is SpellbookSpells.Spell s ? Localized(s.Cliloc, s.Name) : "";

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
            string[] names = SpellNames.School(_first, out string school, out int iconBase);
            Text(new Rect(0, 10, Rect.width, 22), school, new Color(1f, 0.9f, 0.6f), 16, TextAnchor.UpperCenter, true);

            var known = Enumerable.Range(0, 64).Where(i => (_bits & (1UL << i)) != 0 && i < names.Length).ToList();
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
                string circle = _first == 1 ? $"Circle {i / 8 + 1}" : "";
                Text(new Rect(56, y + 6, 240, 20), names[i], new Color(0.95f, 0.92f, 0.82f), 14);
                Text(new Rect(56, y + 24, 240, 20), circle, new Color(0.7f, 0.66f, 0.55f), 12);
                if (GUI.Button(new Rect(0, y, view.width - 18, row), GUIContent.none, GUIStyle.none))
                    CastFromBook(Session, _book, _first, i);
            }
            GUI.EndScrollView();
        }
    }
}
