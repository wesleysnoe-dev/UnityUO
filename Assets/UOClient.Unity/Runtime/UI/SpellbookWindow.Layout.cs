using System.Collections.Generic;
using System.Text;

namespace UOClient.Unity.UI
{
    // No UnityEngine here: the test project compiles this file on its own to check the layout.

    /// <summary>The three AoS books drawn as TazUO's classic SpellbookGump; other schools keep the plain list.</summary>
    public enum SpellbookKind { Magery, Necromancy, Chivalry }

    /// <summary>TazUO SpellbookGump.GetBookInfo (SpellbookGump.cs:955-1054) for one book.</summary>
    public readonly struct SpellbookInfo
    {
        public readonly SpellbookKind Kind;
        public readonly int First, BookGraphic, MinimizedGraphic, IconStart, MaxSpells;

        public SpellbookInfo(SpellbookKind kind, int first, int book, int minimized, int iconStart, int maxSpells)
        {
            Kind = kind;
            First = first;
            BookGraphic = book;
            MinimizedGraphic = minimized;
            IconStart = iconStart;
            MaxSpells = maxSpells;
        }

        /// <summary>spellsOnPage: Math.Min(maxSpellsCount >> 1, 8).</summary>
        public int SpellsOnPage => System.Math.Min(MaxSpells >> 1, 8);

        /// <summary>dictionaryPagesCount: Math.Ceiling(maxSpellsCount / 8.0f), rounded up to even.</summary>
        public int DictionaryPages
        {
            get
            {
                int pages = (MaxSpells + 7) / 8;
                return pages % 2 != 0 ? pages + 1 : pages;
            }
        }

        /// <summary>CreateBook's pagesToFill (:217-220): the index spreads, two index halves to a spread.</summary>
        public int IndexPages => DictionaryPages >> 1;
    }

    public sealed partial class SpellbookWindow
    {
        /// <summary>The ink of every label in the book (SpellbookGump's 0x0288), and an index name under the mouse (HoveredLabel 0x33).</summary>
        public const int InkHue = 0x0288, HoverHue = 0x0033;

        /// <summary>The page-turn corners (BuildGump :135-144): 0x08BB at (50, 8) back, 0x08BC at (321, 8) forward.</summary>
        public const int PrevCorner = 0x08BB, PrevCornerX = 50, NextCorner = 0x08BC, NextCornerX = 321, CornerY = 8;

        /// <summary>The minimize hit box (BuildGump :131): (0, 98) 27 x 23 on the open book.</summary>
        public const int MinimizeX = 0, MinimizeY = 98, MinimizeW = 27, MinimizeH = 23;

        /// <summary>Index halves (:341-349): "INDEX" at (106 | 269, 10), circle names at (62 | 225, 30), names from y 52, 15 apart.</summary>
        public const int IndexTitleY = 10, IndexSubtitleY = 30, IndexFirstY = 52, IndexRowHeight = 15, IndexNameWidth = 130;

        /// <summary>Detail pages (:556-771): top text at y 6 (+4 for Magery), icon at y 40, name / words at y 34 (80 wide).</summary>
        public const int TopTextY = 6, IconY = 40, NameY = 34, WordsWidth = 80;

        /// <summary>Reagents (:739-758): the 0x0835 rule at y 88 (120 x 5), "Reagents:" at y 92, the list at y 114.</summary>
        public const int ReagentRule = 0x0835, ReagentRuleY = 88, ReagentRuleW = 120, ReagentRuleH = 5, ReagentsTitleY = 92, ReagentListY = 114;

        /// <summary>GetSpellRequires (:1229-1317): mana and skill at y 162 for every non-Magery book.</summary>
        public const int RequiresY = 162;

        /// <summary>The Chivalry tithing label on the first index spread (:315-339), at (62, 162).</summary>
        public const int TithingX = 62, TithingY = 162;

        /// <summary>The magery circle buttons (:226-307): gump, position and the index spread each opens.</summary>
        public static readonly (int Gump, int X, int Y, int Page)[] CircleButtons =
        {
            (0x08B1, 58, 175, 1), (0x08B2, 93, 175, 1), (0x08B3, 130, 175, 2), (0x08B4, 164, 175, 2),
            (0x08B5, 227, 175, 3), (0x08B6, 260, 175, 3), (0x08B7, 297, 175, 4), (0x08B8, 332, 175, 4),
        };

        /// <summary>
        /// GetBookInfo for a book by the first spell id of its 0xBF 0x1B content (ModernUO's NewSpellbookContent offset):
        /// 1 Magery, 101 Necromancy, 201 Chivalry. Any other school is not drawn classic here.
        /// </summary>
        public static bool TryGetBookInfo(int first, out SpellbookInfo info)
        {
            switch (first)
            {
                case 1:
                    info = new SpellbookInfo(SpellbookKind.Magery, 1, 0x08AC, 0x08BA, 0x08C0, SpellNames.Magery.Length);
                    return true;
                case 101:
                    info = new SpellbookInfo(SpellbookKind.Necromancy, 101, 0x2B00, 0x2B03, 0x5000, SpellNames.Necromancy.Length);
                    return true;
                case 201:
                    info = new SpellbookInfo(SpellbookKind.Chivalry, 201, 0x2B01, 0x2B04, 0x5100, SpellNames.Chivalry.Length);
                    return true;
                default:
                    info = default;
                    return false;
            }
        }

        /// <summary>
        /// Whether the book holds its index-th spell (0-based): bit index of the content's 64 bits, as ModernUO writes
        /// Spellbook.Content. TazUO reads the same from the contained items' Amount; we have only the bits.
        /// </summary>
        public static bool Knows(SpellbookInfo info, ulong bits, int index) =>
            index >= 0 && index < info.MaxSpells && index < 64 && (bits & (1UL << index)) != 0;

        /// <summary>The book's spells by index, in order.</summary>
        public static List<int> KnownSpells(SpellbookInfo info, ulong bits)
        {
            var known = new List<int>();
            for (int i = 0; i < info.MaxSpells; i++)
                if (Knows(info, bits, i))
                    known.Add(i);
            return known;
        }

        /// <summary>_maxPage (:222): the index spreads, then one spread for every two spells held.</summary>
        public static int MaxPage(SpellbookInfo info, ulong bits) => info.IndexPages + ((KnownSpells(info, bits).Count + 1) >> 1);

        /// <summary>The spread the n-th held spell (0-based) is described on, and whether on its right page (:556-586).</summary>
        public static (int Page, bool Right) DetailSlot(SpellbookInfo info, int n) => (info.IndexPages + 1 + n / 2, n % 2 != 0);

        /// <summary>One name on an index page: its spread and half, the spell, where it is drawn and the spread it opens.</summary>
        public readonly struct IndexEntry
        {
            public readonly int Page, Half, Spell, X, Y, ToPage;

            public IndexEntry(int page, int half, int spell, int x, int y, int toPage)
            {
                Page = page;
                Half = half;
                Spell = spell;
                X = x;
                Y = y;
                ToPage = toPage;
            }
        }

        /// <summary>
        /// The index pages (:309-554): each spread has two halves of spellsOnPage spells (a magery circle each); the held
        /// ones are listed from y 52, 15 apart, each opening the spread that describes it (topage).
        /// </summary>
        public static List<IndexEntry> IndexEntries(SpellbookInfo info, ulong bits)
        {
            var list = new List<IndexEntry>();
            int done = 0, spell = 0;
            for (int page = 1; page <= info.IndexPages; page++)
            {
                for (int half = 0; half < 2; half++)
                {
                    int x = IndexDataX(half), y = 0;
                    for (int k = 0; k < info.SpellsOnPage; k++, spell++)
                    {
                        if (!Knows(info, bits, spell))
                            continue;
                        list.Add(new IndexEntry(page, half, spell, x, IndexFirstY + y, DetailSlot(info, done).Page));
                        done++;
                        y += IndexRowHeight;
                    }
                }
            }
            return list;
        }

        /// <summary>Where an index half's names and circle title start (dataX): 62 on the left, 225 on the right.</summary>
        public static int IndexDataX(int half) => half % 2 != 0 ? 225 : 62;

        /// <summary>Where an index half's "INDEX" goes (indexX): 106 on the left, 269 on the right.</summary>
        public static int IndexTitleX(int half) => half % 2 != 0 ? 269 : 106;

        /// <summary>The circle (0-7) an index half of a magery spread shows: (page - 1) * 2 + half.</summary>
        public static int CircleOfHalf(int page, int half) => (page - 1) * 2 + half % 2;

        /// <summary>A described spell: the spread, its side's icon x, top text x and text-beside-icon x (:566-584).</summary>
        public readonly struct DetailEntry
        {
            public readonly int Page, Spell, IconX, TopTextX, IconTextX;
            public readonly bool Right;

            public DetailEntry(int page, bool right, int spell)
            {
                Page = page;
                Right = right;
                Spell = spell;
                IconX = right ? 225 : 62;
                TopTextX = right ? 224 : 87;
                IconTextX = right ? 275 : 112;
            }
        }

        /// <summary>The detail pages: the held spells two to a spread, left then right.</summary>
        public static List<DetailEntry> DetailEntries(SpellbookInfo info, ulong bits)
        {
            List<int> known = KnownSpells(info, bits);
            var list = new List<DetailEntry>(known.Count);
            for (int n = 0; n < known.Count; n++)
            {
                (int page, bool right) = DetailSlot(info, n);
                list.Add(new DetailEntry(page, right, known[n]));
            }
            return list;
        }

        /// <summary>The spell's icon (GumpIconSmallID): 0x08C0 + i for Magery, 0x5000 + i Necromancy, 0x5100 + i Chivalry.</summary>
        public static int IconOf(SpellbookInfo info, int index) => info.IconStart + index;

        /// <summary>Where a magery spell's abbreviated words go under its name label of the given height (:610-617).</summary>
        public static int MageryWordsY(int nameHeight) => (nameHeight < 24 ? 31 : 26) + nameHeight;

        /// <summary>The spread a page turn lands on (SetActivePage :1319-1340): clamped to 1..max.</summary>
        public static int ClampPage(int page, int maxPage) => page < 1 ? 1 : page > maxPage ? System.Math.Max(1, maxPage) : page;
    }

    /// <summary>A reagent: its cliloc name and TazUO's language.ini fallback (SpellDefinition.CreateReagentListString).</summary>
    public readonly struct Reagent
    {
        public readonly int Cliloc;
        public readonly string Name;

        public Reagent(int cliloc, string name)
        {
            Cliloc = cliloc;
            Name = name;
        }

        public static readonly Reagent BlackPearl = new Reagent(1044353, "Black Pearl"), Bloodmoss = new Reagent(1044354, "Bloodmoss"),
            Garlic = new Reagent(1044355, "Garlic"), Ginseng = new Reagent(1044356, "Ginseng"), MandrakeRoot = new Reagent(1044357, "Mandrake Root"),
            Nightshade = new Reagent(1044358, "Nightshade"), SulfurousAsh = new Reagent(1044359, "Sulfurous Ash"),
            SpidersSilk = new Reagent(1044360, "Spiders Silk"), BatWing = new Reagent(1023960, "Bat Wing"),
            GraveDust = new Reagent(1023983, "Grave Dust"), DaemonBlood = new Reagent(1023965, "Daemon Blood"),
            NoxCrystal = new Reagent(1023982, "Nox Crystal"), PigIron = new Reagent(1023978, "Pig Iron");

        /// <summary>Two-letter codes used by the spell tables below.</summary>
        public static Reagent FromCode(string code)
        {
            switch (code)
            {
                case "BP": return BlackPearl;
                case "BM": return Bloodmoss;
                case "GA": return Garlic;
                case "GI": return Ginseng;
                case "MR": return MandrakeRoot;
                case "NS": return Nightshade;
                case "SA": return SulfurousAsh;
                case "SS": return SpidersSilk;
                case "BW": return BatWing;
                case "GD": return GraveDust;
                case "DB": return DaemonBlood;
                case "NC": return NoxCrystal;
                case "PI": return PigIron;
                default: throw new System.ArgumentException("unknown reagent " + code);
            }
        }
    }

    /// <summary>
    /// What a detail page shows for one AoS spell, from TazUO's SpellsMagery / SpellsNecromancy / SpellsChivalry:
    /// power words, reagents, mana and minimum skill, and the cliloc of its name (GetLocalizedName).
    /// </summary>
    public static class SpellbookSpells
    {
        public readonly struct Spell
        {
            public readonly string Name, Words;
            public readonly int Cliloc, Mana, MinSkill;
            public readonly Reagent[] Reagents;

            public Spell(string name, int cliloc, string words, int mana, int minSkill, Reagent[] reagents)
            {
                Name = name;
                Cliloc = cliloc;
                Words = words;
                Mana = mana;
                MinSkill = minSkill;
                Reagents = reagents;
            }
        }

        /// <summary>The 0-based index-th spell of a book, or null past its end.</summary>
        public static Spell? Get(SpellbookKind kind, int index)
        {
            switch (kind)
            {
                case SpellbookKind.Magery when index >= 0 && index < MageryWords.Length:
                    return new Spell(SpellNames.Magery[index], 3002011 + index, MageryWords[index], 0, 0, Parse(MageryReagents[index]));
                case SpellbookKind.Necromancy when index >= 0 && index < NecromancyWords.Length:
                    return new Spell(SpellNames.Necromancy[index], 1060509 + index, NecromancyWords[index],
                        NecromancyRequires[index].Mana, NecromancyRequires[index].Skill, Parse(NecromancyReagents[index]));
                case SpellbookKind.Chivalry when index >= 0 && index < ChivalryWords.Length:
                    // TazUO lists no reagents for Chivalry (GetSpellNames :1143-1149).
                    return new Spell(SpellNames.Chivalry[index], 1060493 + index, ChivalryWords[index],
                        ChivalryRequires[index].Mana, ChivalryRequires[index].Skill, new Reagent[0]);
                default:
                    return null;
            }
        }

        private static Reagent[] Parse(string codes)
        {
            string[] parts = codes.Split(' ');
            var regs = new Reagent[parts.Length];
            for (int i = 0; i < parts.Length; i++)
                regs[i] = Reagent.FromCode(parts[i]);
            return regs;
        }

        /// <summary>
        /// SpellsMagery.SpecialReagentsChars (StringHelper.RemoveUpperLowerChars): the power words' capitals and spaces,
        /// "Uus Jux" → "U J".
        /// </summary>
        public static string Abbreviate(string words)
        {
            if (string.IsNullOrWhiteSpace(words))
                return "";
            var sb = new StringBuilder(words.Length);
            foreach (char c in words)
                if (char.IsUpper(c) || c == ' ')
                    sb.Append(c);
            return sb.ToString();
        }

        /// <summary>SpellsMagery.CircleNames: clilocs 1044369-1044376, with TazUO's fallbacks.</summary>
        public const int CircleCliloc = 1044369;

        public static readonly string[] CircleNames =
        {
            "First Circle", "Second Circle", "Third Circle", "Fourth Circle",
            "Fifth Circle", "Sixth Circle", "Seventh Circle", "Eighth Circle"
        };

        /// <summary>"Reagents:" (cliloc 3001017), the language.ini fallback.</summary>
        public const int ReagentsCliloc = 3001017;
        public const string ReagentsText = "Reagents:";

        /// <summary>"INDEX" (language.ini index).</summary>
        public const string IndexText = "INDEX";

        /// <summary>Tithing clilocs 1062102 and 1062103, joined by a line break; the language.ini fallback.</summary>
        public const int TithingCliloc = 1062102, AvailableCliloc = 1062103;
        public const string TithingText = "Tithing points\nAvailable:";

        /// <summary>language.ini mana_cost0_min_skill1.</summary>
        public static string Requires(int mana, int minSkill) => $"Mana cost: {mana}\nMin. Skill: {minSkill}";

        public static readonly string[] MageryWords =
        {
            "Uus Jux", "In Mani Ylem", "Rel Wis", "In Mani", "In Por Ylem", "In Lor", "Flam Sanct", "Des Mani",
            "Ex Uus", "Uus Wis", "An Nox", "An Mani", "In Jux", "An Jux", "Uus Sanct", "Uus Mani",
            "Rel Sanct", "Vas Flam", "An Por", "In Nox", "Ort Por Ylem", "Rel Por", "Ex Por", "In Sanct Ylem",
            "Vas An Nox", "Vas Uus Sanct", "Des Sanct", "In Flam Grav", "In Vas Mani", "Por Ort Grav", "Ort Rel", "Kal Ort Por",
            "In Jux Hur Ylem", "An Grav", "Kal In Ex", "In Jux Sanct", "Por Corp Wis", "An Ex Por", "In Nox Grav", "Kal Xen",
            "An Ort", "Corp Por", "Vas Ort Flam", "An Lor Xen", "Kal Por Ylem", "Vas Des Sanct", "In Ex Grav", "Wis Quas",
            "Vas Ort Grav", "In Sanct Grav", "Kal Vas Flam", "Vas Rel Por", "Ort Sanct", "Vas An Ort", "Flam Kal Des Ylem", "Vas Ylem Rel",
            "In Vas Por", "Vas Corp Por", "An Corp", "Kal Vas Xen Hur", "Kal Vas Xen Corp", "Kal Vas Xen Ylem", "Kal Vas Xen Flam", "Kal Vas Xen An Flam"
        };

        public static readonly string[] MageryReagents =
        {
            "BM NS", "GA GI MR", "NS GI", "GA GI SS", "SA", "SS SA", "GA SS SA", "GA NS",
            "BM MR", "NS MR", "GA GI", "NS SS", "GA SS SA", "BM SA", "GA GI SA", "MR NS",
            "GA MR", "BP", "BM GA SA", "NS", "BM MR", "BM MR", "BM SA", "BM GA",
            "GA GI MR", "GA GI MR SA", "GA NS SA", "BP SS SA", "GA GI MR SS", "MR SA", "BP MR SS", "BP BM MR",
            "BP MR NS", "BP GA SS SA", "BM GA NS", "GA MR SS", "BP MR NS SA", "GA MR SS", "BP NS SS", "BM MR SS",
            "GA MR SA", "BP NS", "BM MR", "BM NS", "BP BM MR", "GA MR NS SA", "BP GI SS", "BM SA",
            "BP BM MR SA", "BP MR SS SA", "SS SA", "BP MR SA", "BP BM MR SS", "BP GA MR SA", "BM MR SS SA", "BM MR SS",
            "BM GI MR SA", "BP BM MR NS", "BM GI GA", "BM MR SS", "BM MR SS SA", "BM MR SS", "BM MR SS SA", "BM MR SS"
        };

        public static readonly string[] NecromancyWords =
        {
            "Uus Corp", "In Jux Mani Xen", "In Agle Corp Ylem", "An Sanct Gra Char", "Pas Tym An Sanct", "Rel Xen Vas Bal",
            "Rel Xen Corp Ort", "Wis An Ben", "In Sar", "In Vas Nox", "In Bal Nox", "Kal Xen Bal", "Rel Xen An Sanct",
            "Kal Xen Bal Beh", "Kal Vas An Flam", "Rel Xen Um", "Ort Corp Grav"
        };

        public static readonly string[] NecromancyReagents =
        {
            "DB GD", "DB", "BW GD", "PI", "BW NC", "BW DB", "DB GD NC", "BW DB PI", "GD PI", "NC", "DB NC", "BW DB GD",
            "BW NC PI", "BW GD PI", "GD NC PI", "NC PI", "NC GD"
        };

        public static readonly (int Mana, int Skill)[] NecromancyRequires =
        {
            (23, 40), (13, 20), (11, 20), (7, 0), (11, 20), (11, 40), (25, 70), (17, 30), (5, 20), (17, 50), (29, 65), (17, 30),
            (25, 99), (41, 80), (23, 60), (17, 20), (40, 80)
        };

        public static readonly string[] ChivalryWords =
        {
            "Expor Flamus", "Obsu Vulni", "Consecrus Arma", "Dispiro Malas", "Divinum Furis", "Forul Solum", "Augus Luminos",
            "Dium Prostra", "Extermo Vomica", "Sanctum Viatas"
        };

        public static readonly (int Mana, int Skill)[] ChivalryRequires =
        {
            (10, 5), (10, 0), (10, 15), (10, 35), (10, 25), (20, 45), (20, 55), (20, 65), (20, 5), (20, 5)
        };
    }

    /// <summary>Spell names per school, by the school's first spell id.</summary>
    public static class SpellNames
    {
        public static string[] School(int first, out string name, out int iconBase)
        {
            switch (first)
            {
                case 101: name = "Necromancy"; iconBase = 0x5000; return Necromancy;
                case 201: name = "Chivalry"; iconBase = 0x5100; return Chivalry;
                case 401: name = "Bushido"; iconBase = 0x5400; return Bushido;
                case 501: name = "Ninjitsu"; iconBase = 0x5300; return Ninjitsu;
                case 601: name = "Spellweaving"; iconBase = 0x59D8; return Spellweaving;
                case 678: name = "Mysticism"; iconBase = 0x5DC0; return Mysticism;
                default: name = "Magery"; iconBase = 0x08C0; return Magery;
            }
        }

        public static readonly string[] Magery =
        {
            "Clumsy", "Create Food", "Feeblemind", "Heal", "Magic Arrow", "Night Sight", "Reactive Armor", "Weaken",
            "Agility", "Cunning", "Cure", "Harm", "Magic Trap", "Magic Untrap", "Protection", "Strength",
            "Bless", "Fireball", "Magic Lock", "Poison", "Telekinesis", "Teleport", "Unlock", "Wall of Stone",
            "Arch Cure", "Arch Protection", "Curse", "Fire Field", "Greater Heal", "Lightning", "Mana Drain", "Recall",
            "Blade Spirits", "Dispel Field", "Incognito", "Magic Reflection", "Mind Blast", "Paralyze", "Poison Field", "Summon Creature",
            "Dispel", "Energy Bolt", "Explosion", "Invisibility", "Mark", "Mass Curse", "Paralyze Field", "Reveal",
            "Chain Lightning", "Energy Field", "Flamestrike", "Gate Travel", "Mana Vampire", "Mass Dispel", "Meteor Swarm", "Polymorph",
            "Earthquake", "Energy Vortex", "Resurrection", "Air Elemental", "Summon Daemon", "Earth Elemental", "Fire Elemental", "Water Elemental"
        };

        public static readonly string[] Necromancy =
        {
            "Animate Dead", "Blood Oath", "Corpse Skin", "Curse Weapon", "Evil Omen", "Horrific Beast", "Lich Form", "Mind Rot",
            "Pain Spike", "Poison Strike", "Strangle", "Summon Familiar", "Vampiric Embrace", "Vengeful Spirit", "Wither", "Wraith Form", "Exorcism"
        };

        public static readonly string[] Chivalry =
        {
            "Cleanse by Fire", "Close Wounds", "Consecrate Weapon", "Dispel Evil", "Divine Fury", "Enemy of One", "Holy Light",
            "Noble Sacrifice", "Remove Curse", "Sacred Journey"
        };

        public static readonly string[] Bushido =
        {
            "Honorable Execution", "Confidence", "Evasion", "Counter Attack", "Lightning Strike", "Momentum Strike"
        };

        public static readonly string[] Ninjitsu =
        {
            "Focus Attack", "Death Strike", "Animal Form", "Ki Attack", "Surprise Attack", "Backstab", "Shadowjump", "Mirror Image"
        };

        public static readonly string[] Spellweaving =
        {
            "Arcane Circle", "Gift of Renewal", "Immolating Weapon", "Attune Weapon", "Thunderstorm", "Nature's Fury", "Summon Fey",
            "Summon Fiend", "Reaper Form", "Wildfire", "Essence of Wind", "Dryad Allure", "Ethereal Voyage", "Word of Death",
            "Gift of Life", "Arcane Empowerment"
        };

        public static readonly string[] Mysticism =
        {
            "Nether Bolt", "Healing Stone", "Purge Magic", "Enchant", "Sleep", "Eagle Strike", "Animated Weapon", "Stone Form",
            "Spell Trigger", "Mass Sleep", "Cleansing Winds", "Bombard", "Spell Plague", "Hail Storm", "Nether Cyclone", "Rising Colossus"
        };
    }
}
