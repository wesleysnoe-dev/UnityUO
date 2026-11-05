using UOClient.Net;

namespace UOClient.Unity.UI
{
    // No UnityEngine here: the test project compiles this file on its own to check the layout.
    public sealed partial class SecureTradeWindow
    {
        /// <summary>TazUO's TradingGump: each offer box is 110 x 80.</summary>
        public const int BoxWidth = 110, BoxHeight = 80;
        /// <summary>Accept checks: 0x0867 clear (0x0868 pressed), 0x0869 ticked (0x086A pressed).</summary>
        public const int Unchecked = 0x0867, UncheckedPressed = 0x0868, Checked = 0x0869, CheckedPressed = 0x086A;

        /// <summary>The client versions (7.0.45.65+) ModernUO marks NewSecureTrading and sends the account ledger to.</summary>
        public static readonly ClientVersion NewTradingVersion = new ClientVersion(7, 0, 45, 65);

        /// <summary>Where TazUO's TradingGump puts each part, from its BuildGump and SetCheckboxes.</summary>
        public sealed class TradeLayout
        {
            public int Background;
            public (int X, int Y) MyCheck, TheirCheck, MyName;
            /// <summary>Their name ends here (TazUO: x = right - text width).</summary>
            public int TheirNameRight, TheirNameY;
            public (int X, int Y) MyBox, TheirBox;
            /// <summary>Gold and platinum (new trading only): our balance, their offer, and our offer's text entries (100 x 20).</summary>
            public (int X, int Y) MyGoldBalance, MyPlatBalance, TheirGold, TheirPlat, MyGoldEntry, MyPlatEntry;
            public bool HasCoins;
        }

        public static readonly TradeLayout Classic = new TradeLayout
        {
            Background = 0x0866,
            MyCheck = (52, 29), TheirCheck = (266, 160), MyName = (84, 40),
            TheirNameRight = 260, TheirNameY = 170,
            MyBox = (45, 70), TheirBox = (192, 70)
        };

        public static readonly TradeLayout WithCoins = new TradeLayout
        {
            Background = 0x088A,
            MyCheck = (37, 29), TheirCheck = (258, 240), MyName = (73, 32),
            TheirNameRight = 250, TheirNameY = 244,
            MyBox = (30, 110), TheirBox = (192, 110),
            MyGoldBalance = (43, 67), MyPlatBalance = (180, 67), TheirGold = (180, 190), TheirPlat = (180, 210),
            MyGoldEntry = (43, 190), MyPlatEntry = (43, 210),
            HasCoins = true
        };

        /// <summary>TazUO picks the gump by client version (CV_704565), the same test ModernUO makes before sending a ledger.</summary>
        public static TradeLayout LayoutFor(ClientVersion version) => version >= NewTradingVersion ? WithCoins : Classic;

        /// <summary>
        /// An item's place in an offer box: the server's x, y kept so the art stays inside the box (TazUO's UpdateContents).
        /// </summary>
        public static (int X, int Y) FitInBox(int x, int y, int artWidth, int artHeight)
        {
            if (x + artWidth > BoxWidth)
                x = BoxWidth - artWidth;
            if (y + artHeight > BoxHeight)
                y = BoxHeight - artHeight;
            return (x < 0 ? 0 : x, y < 0 ? 0 : y);
        }

        /// <summary>
        /// Where an item let go at (x, y) inside our box (box-relative) lands, as TazUO's TradingGump.OnMouseUp sends it:
        /// the art centred on the cursor, then kept inside the box.
        /// </summary>
        public static (int X, int Y) DropInBox(int x, int y, int artWidth, int artHeight) =>
            FitInBox(x - (artWidth >> 1), y - (artHeight >> 1), artWidth, artHeight);
    }
}
