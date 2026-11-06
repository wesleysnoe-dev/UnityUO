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

        /// <summary>The party scroll overlaps the profile's right edge by 4 (PaperDollGump.cs:264-267).</summary>
        public static int PartyX(int profileWidth) => ProfileX + profileWidth - 4;

        /// <summary>The body and its worn items (Position_X/Y_Avatar), and the title (39, 262), 185 wide.</summary>
        public const int BodyX = 8, BodyY = 19, TitleX = 39, TitleY = 262, TitleWidth = 185;

        /// <summary>
        /// TazUO's showPaperdollBooks: our own paperdoll when 0xA9's flags carry 0x20 (ClientFeatures.PaperdollBooks,
        /// CLF_PALADIN_NECROMANCER_TOOLTIPS, which ModernUO calls CharacterListFlags.AOS); the racial book also needs
        /// CV_7000. We have no combat or racial book gump yet, so the window does not draw them (LIVE_PARITY).
        /// </summary>
        public static bool ShowsPaperdollBooks(bool self, uint characterListFlags) => self && (characterListFlags & 0x20) != 0;

        public static bool ShowsRacialBook(bool self, uint characterListFlags, ClientVersion v) =>
            ShowsPaperdollBooks(self, characterListFlags) && v >= RacialBookFrom;
    }
}
