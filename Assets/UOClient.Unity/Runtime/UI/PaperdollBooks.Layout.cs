using System.Collections.Generic;
using UOClient.Core;

namespace UOClient.Unity.UI
{
    // No UnityEngine here: the test project and gumppreview compile this file on their own to check and draw the layout.

    /// <summary>
    /// TazUO's CombatBookGump (CombatBookGump.cs) at CV_7000 and later: book 0x2B02, three index spreads (nine names on
    /// the left page, four on the right), the worn weapon's primary and secondary icons under the right page's names on
    /// every index spread, then one spread per ability: its icon, name, a rule and the weapons that have it.
    /// </summary>
    public sealed partial class CombatBookWindow
    {
        public const int Background = 0x2B02;

        /// <summary>_dictionaryPagesCount at CV_7000+.</summary>
        public const int IndexPages = 3;

        public static int AbilityCount => WeaponAbilities.Count;

        /// <summary>The last spread: the index, then one per ability.</summary>
        public static int MaxPage => IndexPages + AbilityCount;

        /// <summary>The spread that describes an ability (1-based), as the index name's LocalSerial (maxPages++).</summary>
        public static int PageOf(int ability) => IndexPages + ability;

        public const int IndexTitleLeftX = 96, IndexTitleRightX = 259, IndexTitleY = 6;
        public const int IndexLeftX = 52, IndexRightX = 215, IndexFirstY = 42, IndexRowHeight = 15, IndexLeftRows = 9, IndexRightRows = 4;

        public readonly struct IndexEntry
        {
            public readonly int Ability, Page, X, Y;
            public IndexEntry(int ability, int page, int x, int y)
            {
                Ability = ability;
                Page = page;
                X = x;
                Y = y;
            }
            public int ToPage => PageOf(Ability);
        }

        /// <summary>BuildGump's index loop (:70-124): page by page, the left page's nine names then the right's four.</summary>
        public static readonly IndexEntry[] Index = BuildIndex();

        private static IndexEntry[] BuildIndex()
        {
            var list = new List<IndexEntry>();
            int ability = 1;
            for (int page = 1; page <= IndexPages; page++)
                for (int j = 0; j < 2; j++)
                {
                    int rows = j == 0 ? IndexLeftRows : IndexRightRows, x = j == 0 ? IndexLeftX : IndexRightX;
                    for (int i = 0; i < rows && ability <= AbilityCount; i++, ability++)
                        list.Add(new IndexEntry(ability, page, x, IndexFirstY + IndexRowHeight * i));
                }
            return list.ToArray();
        }

        /// <summary>The primary / secondary icons and their labels (:128-170): shown on the index spreads only (SetActivePage).</summary>
        public const int AbilityIconX = 215, PrimaryIconY = 105, SecondaryIconY = 150, IconLabelX = 265, IconLabelWidth = 80;
        public const string PrimaryText = "Primary Ability Icon", SecondaryText = "Secondary Ability Icon";

        public static bool ShowsAbilityIcons(int page) => page <= IndexPages;

        /// <summary>An ability's spread (:180-250): icon (62, 40), name (110, 34) in 80, a 0x0835 rule (62, 88) 128 wide.</summary>
        public const int DetailIconX = 62, DetailIconY = 40, DetailNameX = 110, DetailNameY = 34, DetailNameWidth = 80;
        public const int Rule = 0x0835, RuleX = 62, RuleY = 88, RuleWidth = 128;

        /// <summary>The weapon names: from (62, 98) down 16; the seventh on goes to the right page from (215, 34).</summary>
        public static (int X, int Y) ItemLine(int j) => j < 6 ? (62, 98 + 16 * j) : (215, 34 + 16 * (j - 6));

        /// <summary>The name's tooltip (1061693 + index − 1) and the primary / secondary icon's (1028838 + index − 1).</summary>
        public static int NameCliloc(int ability) => 1061693 + ability - 1;
        public static int IconCliloc(int ability) => 1028838 + ability - 1;

        /// <summary>The icon's hue while its ability is on (the 0x80 flag): 38.</summary>
        public const int ActiveHue = 38;

        /// <summary>GetItemsList (:452-818): the weapon graphics listed under each ability (index − 1).</summary>
        public static readonly int[][] Items =
        {
            new[] { 3908, 5048, 3935, 5119, 9927, 5181, 5040, 5121, 3939, 9932, 11554, 16497, 16502, 16494, 16491 },
            new[] { 3779, 5115, 3912, 3910, 5185, 9924, 5127, 5040, 3720, 5125, 11552, 16499, 16498 },
            new[] { 5048, 3912, 5183, 5179, 3933, 5113, 3722, 9930, 3920, 11556, 16487, 16500 },
            new[] { 5050, 3914, 3935, 3714, 5092, 5179, 5127, 5177, 9926, 4021, 10146, 11556, 11560, 5109, 16500, 16495 },
            new[] { 5111, 3718, 3781, 3908, 3573, 3714, 3933, 5125, 11558, 11560, 5109, 9934, 16493, 16494 },
            new[] { 3918, 3914, 9927, 3573, 5044, 3720, 9930, 5117, 16501, 16495 },
            new[] { 3718, 5187, 3916, 5046, 5119, 9931, 3722, 9929, 9933, 10148, 10153, 16488, 16493, 16496 },
            new[] { 5111, 3779, 3922, 9928, 5121, 9929, 11553, 16490, 16488 },
            new[] { 3910, 9925, 9931, 5181, 9926, 5123, 3920, 5042, 16499, 16502, 16496, 16491 },
            new[] { 5117, 9932, 9933, 16492 },
            new[] { 5050, 3918, 5046, 9924, 9925, 5113, 3569, 9928, 3939, 5042, 16497, 16498 },
            new[] { 3781, 5187, 5185, 5092, 5044, 3922, 5123, 4021, 11553, 16490 },
            new[] { 5115, 5183, 3916, 5177, 3569, 10157, 11559, 9934, 16501 },
            new[] { 10146 },
            new[] { 10148, 10150, 10151 },
            new[] { 10147, 10158, 10159, 11557 },
            new[] { 10151, 10157, 11561 },
            new[] { 10152 },
            new[] { 10155 },
            new[] { 10152, 10153, 10158, 11554 },
            new[] { 10155 },
            new[] { 10149 },
            new[] { 10149, 10159 },
            new[] { 11555, 11558, 11559, 11561 },
            new[] { 11550 },
            new[] { 11551 },
            new[] { 11551, 11552 },
            new[] { 11550 },
            new[] { 11557 },
            new[] { 16492 },
            new[] { 16487 },
        };

        /// <summary>StringHelper.CapitalizeAllWords: each word's first letter upper case (tiledata names are lower case).</summary>
        public static string CapitalizeAllWords(string s)
        {
            if (string.IsNullOrEmpty(s))
                return s;
            char[] c = s.ToCharArray();
            for (int i = 0; i < c.Length; i++)
                if (i == 0 || c[i - 1] == ' ')
                    c[i] = char.ToUpperInvariant(c[i]);
            return new string(c);
        }
    }

    /// <summary>
    /// TazUO's RacialAbilitiesBookGump: book 0x2B29, two index spreads of three names a page, then two abilities a
    /// spread with icon, name and "Passive". Every human and elf ability is passive. The gargoyle's Flying is the one
    /// TazUO activates (0xBF 0x32); we do not send it, so it is shown without an action (LIVE_PARITY).
    /// </summary>
    public sealed partial class RacialAbilitiesWindow
    {
        public const int Background = 0x2B29;
        public const int IndexPages = 2, IndexRows = 3;
        public const int IndexTitleLeftX = 106, IndexTitleRightX = 269, IndexTitleY = 10;
        public const int IndexLeftX = 62, IndexRightX = 225, IndexFirstY = 52, IndexRowHeight = 15;
        public const int LeftIconX = 62, RightIconX = 225, LeftTextX = 112, RightTextX = 275, IconY = 40, NameY = 34, NameWidth = 100, PassiveY = 64;
        public const int Rule = 0x0835, RuleY = 88, RuleWidth = 120, RuleHeight = 4;
        public const string PassiveText = "Passive";

        public enum Race { Human, Elf, Gargoyle }

        /// <summary>The race a body is (ModernUO's Race bodies: human 400-403, elf 605-608, gargoyle 666/667/694/695).</summary>
        public static Race RaceOf(int body)
        {
            switch (body)
            {
                case 605: case 606: case 607: case 608: return Race.Elf;
                case 666: case 667: case 694: case 695: return Race.Gargoyle;
                default: return Race.Human;
            }
        }

        public readonly struct Book
        {
            public readonly string[] Names;
            public readonly int FirstIcon, FirstCliloc;
            public Book(string[] names, int firstIcon, int firstCliloc)
            {
                Names = names;
                FirstIcon = firstIcon;
                FirstCliloc = firstCliloc;
            }
            public int Count => Names.Length;

            /// <summary>_pagesCount: the index spreads plus count / 2 (TazUO's rule; an even count leaves the last spread empty).</summary>
            public int MaxPage => IndexPages + (Count >> 1);
        }

        /// <summary>GetSummaryBookInfo and the name lists (:16-23, :239-267).</summary>
        public static Book BookOf(Race race)
        {
            switch (race)
            {
                case Race.Elf:
                    return new Book(new[] { "Night Sight", "Infused with Magic", "Knowledge of Nature", "Difficult to Track", "Perception", "Wisdom" }, 0x5DD4, 1112202);
                case Race.Gargoyle:
                    return new Book(new[] { "Flying", "Berserk", "Master Artisan", "Deadly Aim", "Mystic Insight" }, 0x5DDA, 1112208);
                default:
                    return new Book(new[] { "Strong Back", "Tough", "Workhorse", "Jack of All Trades" }, 0x5DD0, 1112198);
            }
        }

        /// <summary>Only the gargoyle's first (Flying) is not passive (GetAbilityName).</summary>
        public static bool IsPassive(Race race, int i) => !(race == Race.Gargoyle && i == 0);

        /// <summary>The spread describing ability i (0-based): two to a spread from the second (TazUO's topage / page1).</summary>
        public static int PageOf(int i) => IndexPages + (i >> 1);

        /// <summary>Ability i's index line: page 1's left three, its right three, then page 2's.</summary>
        public static (int Page, int X, int Y) IndexLine(int i)
        {
            int column = i / IndexRows;
            return (1 + column / 2, column % 2 == 0 ? IndexLeftX : IndexRightX, IndexFirstY + IndexRowHeight * (i % IndexRows));
        }

        /// <summary>Ability i's icon and name x on its spread: even on the left page, odd on the right.</summary>
        public static (int IconX, int TextX) DetailX(int i) => i % 2 == 0 ? (LeftIconX, LeftTextX) : (RightIconX, RightTextX);
    }
}
