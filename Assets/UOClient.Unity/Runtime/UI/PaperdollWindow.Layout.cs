using System;
using UOClient.Net;

namespace UOClient.Unity.UI
{
    // No UnityEngine here: the test project and gumppreview compile this file on their own to check and draw the layout.

    /// <summary>
    /// TazUO's PaperDollGump.Settings (PaperDollGump.cs:1025-1150) and the button column BuildLayout adds (:138-290):
    /// our own paperdoll gets Help, Options, Log Out, Journal (Quests from CV_500A), Skills, Guild, Peace / War and
    /// Status at x 185, 27 apart from y 44; anyone else's gets Status alone. Before CV_500A the sixth button was Chat;
    /// TazUO adds Guild at every version, so the CV_500A swap ClientVersion.cs documents is Journal → Quests here.
    /// </summary>
    public sealed partial class PaperdollWindow
    {
        public const int BackgroundPlayer = 0x07D0, BackgroundOther = 0x07D1;

        /// <summary>CV_500A (5.0.0a): Quests instead of Journal (PaperDollGump.cs:173).</summary>
        public static readonly ClientVersion QuestsFrom = new ClientVersion(5, 0, 0, 'a');

        /// <summary>CV_7000 (7.0.0.0): the racial abilities book, when the paperdoll books show at all (PaperDollGump.cs:135-136).</summary>
        public static readonly ClientVersion RacialBookFrom = new ClientVersion(7, 0, 0, 0);

        public const int ButtonX = 185, ButtonTop = 44, ButtonStep = 27;

        /// <summary>One button: its art (normal / pressed), where it sits, and the command it runs.</summary>
        public readonly struct Button
        {
            public readonly int Normal, Pressed, X, Y;
            public readonly string Command;
            public Button(int normal, int pressed, int row, string command)
            {
                Normal = normal;
                Pressed = pressed;
                X = ButtonX;
                Y = ButtonTop + ButtonStep * row;
                Command = command;
            }
        }

        public const int HelpNormal = 0x07EF, HelpPressed = 0x07F0;
        public const int OptionsNormal = 2006, OptionsPressed = 2007;
        public const int LogoutNormal = 2009, LogoutPressed = 2010;
        public const int JournalNormal = 2012, JournalPressed = 2013;
        public const int QuestNormal = 22453, QuestPressed = 22455;
        public const int SkillsNormal = 2015, SkillsPressed = 2016;
        public const int GuildNormal = 22450, GuildPressed = 22452;
        public const int WarNormal = 0x07E8, WarPressed = 0x07E9, PeaceNormal = 0x07E5, PeacePressed = 0x07E6;
        public const int StatusNormal = 2027, StatusPressed = 2028;

        /// <summary>The paperdoll's buttons, top to bottom, for our own (<paramref name="self"/>) or someone else's.</summary>
        public static Button[] Buttons(ClientVersion v, bool self, bool war)
        {
            Button status = new Button(StatusNormal, StatusPressed, 7, "status");
            if (!self)
                return new[] { status };
            bool quests = v >= QuestsFrom;
            return new[]
            {
                new Button(HelpNormal, HelpPressed, 0, "help"),
                new Button(OptionsNormal, OptionsPressed, 1, "options"),
                new Button(LogoutNormal, LogoutPressed, 2, "logout"),
                quests ? new Button(QuestNormal, QuestPressed, 3, "quests") : new Button(JournalNormal, JournalPressed, 3, "journal"),
                new Button(SkillsNormal, SkillsPressed, 4, "skills"),
                new Button(GuildNormal, GuildPressed, 5, "guild"),
                war ? new Button(WarNormal, WarPressed, 6, "war") : new Button(PeaceNormal, PeacePressed, 6, "war"),
                status,
            };
        }

        /// <summary>
        /// Someone else's Status button (PaperDollGump.cs:760-795): a health bar for them, unless one is open, its 0x0804
        /// gump centred across the mouse and its top 5 above it.
        /// </summary>
        public static (float X, float Y) OtherBarAt(float mouseX, float mouseY, int barWidth) => (mouseX - (barWidth >> 1), mouseY - 5);

        /// <summary>The profile and party scrolls (0x7D2) and the virtue gem (0x71).</summary>
        public const int ScrollGump = 0x07D2, VirtueGem = 0x0071;

        public const int ProfileX = 25, ProfileY = 196, VirtueX = 80, VirtueY = 4;

        /// <summary>The profile scroll moves right 14 (Racial_Abilities_Width) when the racial book shows (PaperDollGump.cs:256-263).</summary>
        public static int ProfileXFor(bool racialBook) => ProfileX + (racialBook ? RacialBookWidth : 0);

        /// <summary>The party scroll overlaps the profile's right edge by 4 (PaperDollGump.cs:264-267).</summary>
        public static int PartyX(int profileWidth, bool racialBook = false) => ProfileXFor(racialBook) + profileWidth - 4;

        /// <summary>The combat book (0x2B34) and racial book (0x2B28) icons: Settings.Position_X/Y_CombatBook / _RacialAbilities.</summary>
        public const int CombatBookIcon = 0x2B34, CombatBookX = 150, CombatBookY = 198;
        public const int RacialBookIcon = 0x2B28, RacialBookX = 23, RacialBookY = 200, RacialBookWidth = 14;

        // ---- equipment slots (PaperDollGump.AddEquipSlot :384-405, EquipmentSlot :815-1023) ----

        /// <summary>A slot: the 0x243A stone tiled 19x20 under the 0x2344 frame; a worn item's art squeezed into 18x18.</summary>
        public const int SlotBackground = 0x243A, SlotBorder = 0x2344, SlotWidth = 19, SlotHeight = 20, SlotItemSize = 18;

        /// <summary>Position_X/Y_LeftSlots / _RightSlots and Size_Height_*Slots; the right column starts one slot higher.</summary>
        public const int LeftSlotsX = 2, LeftSlotsY = 67, RightSlotsX = 166, RightSlotsY = 67, SlotStep = 21;

        /// <summary>BuildGump's order (:305-325): helmet, earrings, neck, ring, bracelet, tunic, one- and two-handed, talisman.</summary>
        public static readonly byte[] LeftSlotLayers = { 0x06, 0x12, 0x0A, 0x08, 0x0E, 0x11, 0x01, 0x02, 0x09 };

        /// <summary>Robe, gloves, torso, shirt, arms, pants, skirt, cloak, waist, shoes.</summary>
        public static readonly byte[] RightSlotLayers = { 0x16, 0x07, 0x0D, 0x05, 0x13, 0x04, 0x17, 0x14, 0x0C, 0x03 };

        public readonly struct Slot
        {
            public readonly byte Layer;
            public readonly int X, Y;
            public Slot(byte layer, int x, int y)
            {
                Layer = layer;
                X = x;
                Y = y;
            }
        }

        /// <summary>Every slot, left column then right, where AddEquipSlot puts it.</summary>
        public static readonly Slot[] Slots = BuildSlots();

        private static Slot[] BuildSlots()
        {
            var s = new Slot[LeftSlotLayers.Length + RightSlotLayers.Length];
            for (int i = 0; i < LeftSlotLayers.Length; i++)
                s[i] = new Slot(LeftSlotLayers[i], LeftSlotsX, LeftSlotsY + SlotStep * i);
            for (int i = 0; i < RightSlotLayers.Length; i++)
                s[LeftSlotLayers.Length + i] = new Slot(RightSlotLayers[i], RightSlotsX, RightSlotsY + SlotStep * (i - 1));
            return s;
        }

        /// <summary>
        /// The slot (of <paramref name="count"/>, <paramref name="slot"/>(i)) whose 19x20 box holds a window point, by the
        /// UI pixel the point shows (MousePick.UiPixel, as every window item), or −1. The whole box counts, empty or not
        /// (EquipmentSlot takes the mouse; ItemGumpFixed.Contains is true everywhere in it).
        /// </summary>
        public static int SlotAt(int count, Func<int, Slot> slot, float x, float y, float uiScale)
        {
            for (int i = count - 1; i >= 0; i--)
            {
                Slot s = slot(i);
                var (px, py) = MousePick.UiPixel(x, y, s.X, s.Y, uiScale);
                if (px >= 0 && py >= 0 && px < SlotWidth && py < SlotHeight)
                    return i;
            }
            return -1;
        }

        /// <summary>The slot's tooltip: its Layer name with spaces before capitals (StringHelper.AddSpaceBeforeCapital).</summary>
        public static string SlotName(byte layer)
        {
            switch (layer)
            {
                case 0x01: return "One Handed";
                case 0x02: return "Two Handed";
                case 0x03: return "Shoes";
                case 0x04: return "Pants";
                case 0x05: return "Shirt";
                case 0x06: return "Helmet";
                case 0x07: return "Gloves";
                case 0x08: return "Ring";
                case 0x09: return "Talisman";
                case 0x0A: return "Neck";
                case 0x0C: return "Waist";
                case 0x0D: return "Torso";
                case 0x0E: return "Bracelet";
                case 0x11: return "Tunic";
                case 0x12: return "Earrings";
                case 0x13: return "Arms";
                case 0x14: return "Cloak";
                case 0x16: return "Robe";
                case 0x17: return "Skirt";
                default: return "";
            }
        }

        /// <summary>
        /// TazUO's real art bounds (Art.cs:305-330): the opaque pixels' min x / y and max − min, which is one short of
        /// the last column and row (TazUO's own off-by-one, kept: ItemGumpFixed samples exactly this rectangle).
        /// </summary>
        public static (int X, int Y, int W, int H) ArtBounds(uint[] pixels, int width, int height)
        {
            int minX = width, minY = height, maxX = 0, maxY = 0;
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                    if (pixels[y * width + x] != 0)
                    {
                        if (x < minX) minX = x;
                        if (x > maxX) maxX = x;
                        if (y < minY) minY = y;
                        if (y > maxY) maxY = y;
                    }
            return minX > maxX ? (0, 0, 0, 0) : (minX, minY, maxX - minX, maxY - minY);
        }

        /// <summary>
        /// Where in its slot ItemGumpFixed (:922-946) draws an item whose art bounds are <paramref name="boundsW"/> x
        /// <paramref name="boundsH"/>: at most 18 each way (larger art is squeezed to 18), smaller art at its own size,
        /// centred ((18 >> 1) − (size >> 1)).
        /// </summary>
        public static (int X, int Y, int W, int H) SlotItemRect(int boundsW, int boundsH)
        {
            int w = SlotItemSize, h = SlotItemSize, x = 0, y = 0;
            if (boundsW < SlotItemSize)
            {
                w = boundsW;
                x = (SlotItemSize >> 1) - (w >> 1);
            }
            if (boundsH < SlotItemSize)
            {
                h = boundsH;
                y = (SlotItemSize >> 1) - (h >> 1);
            }
            return (x, y, w, h);
        }

        /// <summary>The body and its worn items (Position_X/Y_Avatar), and the title (39, 262), 185 wide.</summary>
        public const int BodyX = 8, BodyY = 19, TitleX = 39, TitleY = 262, TitleWidth = 185;

        /// <summary>
        /// TazUO's showPaperdollBooks: our own paperdoll when 0xA9's flags carry 0x20 (ClientFeatures.PaperdollBooks,
        /// CLF_PALADIN_NECROMANCER_TOOLTIPS, which ModernUO calls CharacterListFlags.AOS); the racial book also needs
        /// CV_7000. Double-clicked, they open CombatBookWindow and RacialAbilitiesWindow.
        /// </summary>
        public static bool ShowsPaperdollBooks(bool self, uint characterListFlags) => self && (characterListFlags & 0x20) != 0;

        public static bool ShowsRacialBook(bool self, uint characterListFlags, ClientVersion v) =>
            ShowsPaperdollBooks(self, characterListFlags) && v >= RacialBookFrom;
    }
}
