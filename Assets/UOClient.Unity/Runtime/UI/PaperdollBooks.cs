using System.Collections.Generic;
using UnityEngine;
using UOClient.Core;
using UOClient.IO.Formats;

namespace UOClient.Unity.UI
{
    /// <summary>
    /// What the paperdoll's two books share (TazUO's CombatBookGump / RacialAbilitiesBookGump): the book art, the page
    /// corners (a click turns one spread, a second click on the same corner goes to the first / last), index names that
    /// turn to their spread once the double-click time has passed (_enqueuePage), and labels in unicode font 1 (TazUO's
    /// ASCII fonts 6 and 9; as SpellbookWindow). Session-only; nothing is saved.
    /// </summary>
    public abstract class PaperdollBookWindow : UiWindow
    {
        private readonly Dictionary<(string, int, int), UiImage> _labels = new Dictionary<(string, int, int), UiImage>();
        private float _lastCornerClick;
        private int _lastCorner;
        private int _queuedPage = -1;
        private double _queuedAt;

        protected int Page = 1;

        protected PaperdollBookWindow() => Rect = new Rect(100, 100, 406, 229); // TazUO opens both at (100, 100)

        protected abstract int BookGump { get; }
        protected abstract int LastPage { get; }
        protected abstract void DrawPage();

        public override void OnClosed()
        {
            foreach (UiImage img in _labels.Values)
                GumpArt.Release(img);
            _labels.Clear();
        }

        public override void Draw()
        {
            HoveredText = null;
            UiImage bg = Art.Gump(BookGump);
            if (bg != null)
                Rect.size = new Vector2(bg.Width, bg.Height);
            DrawImage(bg, 0, 0);

            if (_queuedPage >= 0 && Time.realtimeSinceStartupAsDouble - _queuedAt >= SpellbookWindow.DoubleClickSeconds)
            {
                GoTo(_queuedPage);
                _queuedPage = -1;
            }

            DrawPage();

            if (Page != 1 && GumpButton(SpellbookWindow.PrevCorner, SpellbookWindow.PrevCorner, SpellbookWindow.PrevCornerX, SpellbookWindow.CornerY))
                Turn(-1, SpellbookWindow.PrevCorner, 1);
            if (Page != LastPage && GumpButton(SpellbookWindow.NextCorner, SpellbookWindow.NextCorner, SpellbookWindow.NextCornerX, SpellbookWindow.CornerY))
                Turn(1, SpellbookWindow.NextCorner, LastPage);
        }

        protected void GoTo(int page) => Page = Mathf.Clamp(page, 1, LastPage);

        private void Turn(int by, int corner, int end)
        {
            bool twice = _lastCorner == corner && Time.realtimeSinceStartup - _lastCornerClick <= SpellbookWindow.DoubleClickSeconds;
            GoTo(twice ? end : Page + by);
            _lastCorner = twice ? 0 : corner;
            _lastCornerClick = Time.realtimeSinceStartup;
        }

        /// <summary>
        /// An index name (TazUO's HoveredLabel, hue 0x0288, 0x33 under the mouse): a click queues its spread. The press is
        /// not used, so it still drags the book as TazUO's CanMove gump does.
        /// </summary>
        protected void IndexName(string name, int x, int y, int toPage, string tooltip)
        {
            Rect r = LabelRect(name, x, y);
            bool hover = r.Contains(Event.current.mousePosition);
            Label(name, x, y, hover ? SpellbookWindow.HoverHue : SpellbookWindow.InkHue);
            if (!hover)
                return;
            if (!string.IsNullOrEmpty(tooltip))
                HoveredText = tooltip;
            Event e = Event.current;
            if (e.type == EventType.MouseDown && e.button == 0)
            {
                _queuedPage = toPage;
                _queuedAt = Time.realtimeSinceStartupAsDouble;
            }
        }

        /// <summary>A left double-click on a picture drawn at (x, y), on its opaque pixels.</summary>
        protected bool DoubleClicked(UiImage img, float x, float y)
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

        protected bool Over(UiImage img, float x, float y) =>
            img != null && new Rect(x, y, img.Width, img.Height).Contains(Event.current.mousePosition);

        protected string Localized(int number, string fallback) =>
            Ui.Cliloc?.Get(number) is string s && s.Length > 0 ? s : fallback;

        protected Rect Label(string text, float x, float y, int hue = SpellbookWindow.InkHue, int maxWidth = 0)
        {
            if (string.IsNullOrEmpty(text))
                return new Rect(x, y, 0, 0);
            UiImage img = LabelImage(text, hue, maxWidth);
            if (img != null)
            {
                DrawImage(img, x, y);
                return new Rect(x, y, img.Width, img.Height);
            }
            var r = new Rect(x, y, maxWidth > 0 ? maxWidth : 160, 16);
            Text(r, text, Art.GumpTextColor(hue), 12);
            return r;
        }

        private Rect LabelRect(string text, float x, float y)
        {
            UiImage img = LabelImage(text, SpellbookWindow.InkHue, 0);
            return new Rect(x, y, img?.Width ?? 120, img?.Height ?? SpellbookWindow.IndexRowHeight);
        }

        private UiImage LabelImage(string text, int hue, int maxWidth)
        {
            if (_labels.TryGetValue((text, hue, maxWidth), out UiImage img))
                return img;
            img = GumpArt.TextImage(Art.Text.Generate(text, ServerGumpWindow.GumpFont,
                ServerGumpWindow.LabelArgb(Art.Data.Hues.Entries, hue), maxWidth, UniTextAlign.Left, UniTextStyle.None));
            _labels[(text, hue, maxWidth)] = img;
            return img;
        }
    }

    /// <summary>
    /// The combat book (CombatBookWindow.Layout): the worn weapon's primary and secondary icons are double-clicked to turn
    /// that ability on or off (GameSession.UseWeaponAbility, 0xD7 0x19), hue 38 while on. TazUO's drag-out ability button
    /// (UseAbilityButtonGump) is not made.
    /// </summary>
    public sealed partial class CombatBookWindow : PaperdollBookWindow
    {
        public override string Key => "combatbook";
        protected override int BookGump => Background;
        protected override int LastPage => MaxPage;

        protected override void DrawPage()
        {
            if (ShowsAbilityIcons(Page))
            {
                Label(SpellbookSpells.IndexText, IndexTitleLeftX, IndexTitleY);
                Label(SpellbookSpells.IndexText, IndexTitleRightX, IndexTitleY);
                foreach (IndexEntry entry in Index)
                    if (entry.Page == Page)
                        IndexName(WeaponAbilities.Names[entry.Ability], entry.X, entry.Y, entry.ToPage, Ui.Cliloc?.Get(NameCliloc(entry.Ability)));
                DrawAbilityIcon(false, PrimaryIconY, PrimaryText);
                DrawAbilityIcon(true, SecondaryIconY, SecondaryText);
                return;
            }

            int ability = Page - IndexPages;
            UiImage icon = Art.Gump(WeaponAbilities.Icon(ability));
            DrawImage(icon, DetailIconX, DetailIconY);
            if (Over(icon, DetailIconX, DetailIconY))
                HoveredText = Ui.Cliloc?.Get(NameCliloc(ability));
            Label(CapitalizeAllWords(WeaponAbilities.Names[ability]), DetailNameX, DetailNameY, SpellbookWindow.InkHue, DetailNameWidth);
            DrawTiled(Art.Gump(Rule), RuleX, RuleY, RuleWidth, Art.Gump(Rule)?.Height ?? 0);
            int[] items = Items[ability - 1];
            for (int j = 0; j < items.Length; j++)
            {
                (int x, int y) = ItemLine(j);
                Label(CapitalizeAllWords(Art.Data.TileData.GetStatic(items[j]).Name), x, y);
            }
        }

        /// <summary>The worn weapon's primary or secondary icon (UpdateAbilities), hue 38 while on; double-click toggles.</summary>
        private void DrawAbilityIcon(bool secondary, int y, string label)
        {
            EquippedItem? weapon = Session.WornWeapon();
            var (primary, second) = WeaponAbilities.Of(weapon?.Graphic ?? 0, g => Art.Data.TileData.GetStatic(g).AnimId);
            int ability = secondary ? second : primary;
            UiImage icon = Art.Gump(WeaponAbilities.Icon(ability), Session.IsWeaponAbilityOn(secondary) ? ActiveHue : 0);
            DrawImage(icon, AbilityIconX, y);
            Label(label, IconLabelX, y, SpellbookWindow.InkHue, IconLabelWidth);
            if (Over(icon, AbilityIconX, y))
                HoveredText = Localized(IconCliloc(ability), WeaponAbilities.Names[ability]);
            if (DoubleClicked(icon, AbilityIconX, y))
                Session.UseWeaponAbility(secondary, ability);
        }
    }

    /// <summary>
    /// The racial abilities book (RacialAbilitiesWindow.Layout) for the player's race by body. Every human and elf ability
    /// is passive and has nothing to use; the gargoyle's Flying has no action here (0xBF 0x32 not sent).
    /// </summary>
    public sealed partial class RacialAbilitiesWindow : PaperdollBookWindow
    {
        public override string Key => "racialbook";
        protected override int BookGump => Background;
        protected override int LastPage => BookOf(CurrentRace).MaxPage;

        private Race CurrentRace => RaceOf(Session.World.Player?.Body ?? 400);

        protected override void DrawPage()
        {
            Race race = CurrentRace;
            Book book = BookOf(race);
            if (Page <= IndexPages)
            {
                Label(SpellbookSpells.IndexText, IndexTitleLeftX, IndexTitleY);
                Label(SpellbookSpells.IndexText, IndexTitleRightX, IndexTitleY);
                for (int i = 0; i < book.Count; i++)
                {
                    (int page, int x, int y) = IndexLine(i);
                    if (page == Page)
                        IndexName(book.Names[i], x, y, PageOf(i), null);
                }
            }
            for (int i = 0; i < book.Count; i++)
            {
                if (PageOf(i) != Page)
                    continue;
                (int iconX, int textX) = DetailX(i);
                Label(book.Names[i], textX, NameY, SpellbookWindow.InkHue, NameWidth);
                if (IsPassive(race, i))
                    Label(PassiveText, textX, PassiveY);
                UiImage icon = Art.Gump(book.FirstIcon + i);
                DrawImage(icon, iconX, IconY);
                if (Over(icon, iconX, IconY))
                    HoveredText = Ui.Cliloc?.Get(book.FirstCliloc + i);
                DrawTiled(Art.Gump(Rule), iconX, RuleY, RuleWidth, RuleHeight);
            }
        }
    }
}
