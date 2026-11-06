using UOClient.Core;
using UOClient.Net;

namespace UOClient.Tests;

public sealed class TradeTests
{
    [Fact]
    public void PairsBuyPricesWithStockByPosition()
    {
        var session = new GameSession(new LoginSettings());
        ShopList? shop = null;
        session.ShopOpened += s => shop = s;

        // As ModernUO sends them: contents last-to-first, each tagged x = list index + 1 ...
        const uint pack = 0x40000010, vendor = 0x00001234;
        var contents = new PacketWriter(0x3C).UInt16(2);
        contents.UInt32(0x40000002).UInt16(0x0F3F).Byte(0).UInt16(20).UInt16(2).UInt16(1).Byte(0).UInt32(pack).UInt16(0);  // arrows
        contents.UInt32(0x40000001).UInt16(0x13B2).Byte(0).UInt16(5).UInt16(1).UInt16(1).Byte(0).UInt32(pack).UInt16(0);   // bows
        session.Inject(contents.ToArray(), true);

        // ... the buy gump for the vendor, then prices first-to-last.
        session.Inject(new PacketWriter(0x24, 9).UInt32(vendor).UInt16(0x30).UInt16(0).ToArray(), false);
        var prices = new PacketWriter(0x74).UInt32(pack).Byte(2);
        prices.UInt32(35).Byte(4).Ascii("bow", 3).Byte(0);
        prices.UInt32(2).Byte(6).Ascii("arrow", 5).Byte(0);
        session.Inject(prices.ToArray(), true);

        Assert.NotNull(shop);
        Assert.Equal(vendor, shop!.Vendor);
        Assert.False(shop.Selling);
        Assert.Equal(new[] { "Bow", "Arrow" }, shop.Items.Select(i => i.Name));
        Assert.Equal(new[] { 35, 2 }, shop.Items.Select(i => i.Price));
        Assert.Equal(new[] { 5, 20 }, shop.Items.Select(i => i.Amount));
        Assert.Equal(0x40000001u, shop.Items[0].Serial);
    }

    [Fact]
    public void BuyListWaitsForTheVendorThatModernUoNamesLast()
    {
        // ModernUO's order: contents (0x3C), prices (0x74), then the buy gump naming the vendor (0x24).
        var session = new GameSession(new LoginSettings());
        ShopList? shop = null;
        session.ShopOpened += s => shop = s;
        const uint pack = 0x40000010, vendor = 0x00004321;
        session.Inject(new PacketWriter(0x3C).UInt16(1)
            .UInt32(0x40000001).UInt16(0x0EF3).Byte(0).UInt16(640).UInt16(1).UInt16(1).Byte(0).UInt32(pack).UInt16(0).ToArray(), true);
        session.Inject(new PacketWriter(0x74).UInt32(pack).Byte(1).UInt32(5).Byte(13).Ascii("blank scroll", 12).Byte(0).ToArray(), true);
        Assert.Null(shop); // not yet: no vendor to send the order to
        session.Inject(new PacketWriter(0x24, 9).UInt32(vendor).UInt16(0x30).UInt16(0).ToArray(), false);
        Assert.NotNull(shop);
        Assert.Equal(vendor, shop!.Vendor);
        Assert.Equal("Blank scroll", shop.Items.Single().Name);
    }

    [Fact]
    public void MovingAnItemClearsItsOldPlace()
    {
        var session = new GameSession(new LoginSettings());
        var removed = new List<uint>();
        session.World.EntityRemoved += removed.Add;
        const uint item = 0x40000099, pack = 0x40000010;
        // On the ground (0xF3), then into a container (0x25): the ground view must hear about it.
        session.Inject(new PacketWriter(0xF3, 26).UInt16(1).Byte(0).UInt32(item).UInt16(0x0F52).Byte(0)
            .UInt16(1).UInt16(1).UInt16(100).UInt16(200).Byte(0).Byte(0).UInt16(0).Byte(0).UInt16(0).ToArray(), false);
        Assert.True(session.World.Items.ContainsKey(item));
        session.Inject(new PacketWriter(0x25, 21).UInt32(item).UInt16(0x0F52).Byte(0).UInt16(1).UInt16(50).UInt16(60).Byte(0).UInt32(pack).UInt16(0).ToArray(), false);
        Assert.False(session.World.Items.ContainsKey(item));
        Assert.Contains(item, removed);
        Assert.Single(session.World.ItemsIn(pack));
        // Back on the ground: out of the container.
        session.Inject(new PacketWriter(0xF3, 26).UInt16(1).Byte(0).UInt32(item).UInt16(0x0F52).Byte(0)
            .UInt16(1).UInt16(1).UInt16(101).UInt16(200).Byte(0).Byte(0).UInt16(0).Byte(0).UInt16(0).ToArray(), false);
        Assert.Empty(session.World.ItemsIn(pack));
        Assert.False(session.World.Items[item].IsMulti);
    }

    [Fact]
    public void ReadsSellListsAndContextMenus()
    {
        var session = new GameSession(new LoginSettings());
        ShopList? shop = null;
        ContextMenu? menu = null;
        session.ShopOpened += s => shop = s;
        session.ContextMenuOpened += m => menu = m;

        var sell = new PacketWriter(0x9E).UInt32(0x99).UInt16(1)
            .UInt32(0x40000005).UInt16(0x1BF2).UInt16(0).UInt16(12).UInt16(3).UInt16(5).Ascii("ingot", 5);
        session.Inject(sell.ToArray(), true);
        Assert.True(shop!.Selling);
        Assert.Equal("Ingot", shop.Items[0].Name);
        Assert.Equal(12, shop.Items[0].Amount);
        Assert.Equal(3, shop.Items[0].Price);

        // Newer (type 2) context menu: full cliloc, index, flags (1 = disabled).
        var cm = new PacketWriter(0xBF).UInt16(0x14).UInt16(2).UInt32(0x99).Byte(2)
            .UInt32(3006103).UInt16(0).UInt16(0)
            .UInt32(3006106).UInt16(1).UInt16(1);
        session.Inject(cm.ToArray(), true);
        Assert.Equal(0x99u, menu!.Serial);
        Assert.Equal(new[] { 3006103, 3006106 }, menu.Entries.Select(e => e.Cliloc));
        Assert.Equal(new[] { false, true }, menu.Entries.Select(e => e.Disabled));
        Assert.Equal(1, menu.Entries[1].Index);
    }

    [Fact]
    public void SingleClickSendsTheLookThenTheContextMenuRequest()
    {
        const uint vendor = 0x00001234;

        // Without tooltips: the look (0x09), then ModernUO's ContextMenuRequest (0xBF 0x13, serial) for Buy/Sell/Train.
        byte[][] both = GameSession.SingleClickRequests(vendor, true, true);
        Assert.Equal(2, both.Length);
        Assert.Equal(new byte[] { 0x09, 0x00, 0x00, 0x12, 0x34 }, both[0]);
        Assert.Equal(new byte[] { 0xBF, 0x00, 0x09, 0x00, 0x13, 0x00, 0x00, 0x12, 0x34 }, both[1]);

        // With AOS tooltips (every ModernUO shard from AOS on): only the menu request, as TazUO sends it.
        Assert.Equal(new[] { both[1] }, GameSession.SingleClickRequests(vendor, false, true));
        // A shard without context menus: only the look.
        Assert.Equal(new[] { both[0] }, GameSession.SingleClickRequests(vendor, true, false));
    }

    // TazUO's DelayedObjectClickManager.Update: look if !TooltipsEnabled || (item is IsLocked && weight 255 && !container).
    [Theory]
    [InlineData(false, false, 0x00, 0, 0u, true)]         // no tooltips: everything is named
    [InlineData(true, false, 0x00, 0, 0u, false)]         // a mobile (vendor): the tooltip names it
    [InlineData(true, true, 0x20, 255, 0u, false)]        // movable: not locked
    [InlineData(true, true, 0x00, 255, 0u, true)]         // a locked fixture (a sign, a forge)
    [InlineData(true, true, 0x00, 90, 0u, false)]         // locked but not weight 255
    [InlineData(true, true, 0x00, 255, 0x200000u, false)] // a locked container
    public void SingleClickNamesWhatTazUONames(bool tooltips, bool isItem, int flags, int weight, uint tileFlags, bool look)
    {
        Assert.Equal(look, GameSession.SingleClickLooks(tooltips, isItem, (byte)flags, (byte)weight, (UOClient.IO.Formats.TileFlags)tileFlags));
    }

    [Theory]
    [InlineData(0x08u | 0x20u | 0x80u, true, true)] // ModernUO's SE expansion: ContextMenus | AOS | SE
    [InlineData(0x08u, true, false)]                // pre-AOS expansions: ContextMenus only
    [InlineData(0x00u, false, false)]
    public void CharacterListFlagsTurnOnPopupsAndTooltips(uint flags, bool popup, bool tooltips)
    {
        // ModernUO's SendCharacterList for a 7.0.13+ client: 5 empty slots, one 89-byte city, flags, then -1.
        var session = new GameSession(new LoginSettings { Interactive = true });
        var p = new PacketWriter(0xA9).Byte(5).Zero(5 * 60).Byte(1)
            .Byte(0).Ascii("Britain", 32).Ascii("Inn", 32).Zero(24)
            .UInt32(flags).UInt16(0xFFFF);
        session.Inject(p.ToArray(), true);
        Assert.Single(session.Cities);
        Assert.Equal(flags, session.CharacterListFlags);
        Assert.Equal(popup, session.PopupEnabled);
        Assert.Equal(tooltips, session.TooltipsEnabled);
    }

    // ---- offline round trips: what ModernUO sends in, what its incoming handlers read back out ----

    private const uint Me = 0x00000042, Vendor = 0x00001234, BuyPack = 0x40000010;

    private static GameSession InWorld(List<byte[]> sent)
    {
        var session = new GameSession(new LoginSettings { AnnounceChatLinks = false });
        session.StartOffline(sent.Add);
        session.Inject(new PacketWriter(0x1B, 37).UInt32(Me).UInt32(0).UInt16(0x190).UInt16(1495).UInt16(1629).UInt16(0)
            .Byte(0).Zero(9).UInt16(7168).UInt16(4096).Zero(6).ToArray(), false);
        session.Inject(new byte[] { 0x55 }, false);
        Assert.Equal(SessionState.InWorld, session.State);
        sent.Clear();
        return session;
    }

    /// <summary>
    /// ModernUO's BaseVendor.VendorBuy: SendVendorBuyContent (0x3C, last to first, x = list index + 1, grid byte),
    /// SendVendorBuyList (0x74, first to last, Latin-1 names with the terminator counted), SendDisplayBuyList
    /// (0x24 gump 0x30, High Seas 9 bytes).
    /// </summary>
    private static void OpenBuy(GameSession session, params (uint Serial, ushort Graphic, int Amount, int Price, string Name)[] stock)
    {
        var contents = new PacketWriter(0x3C).UInt16(stock.Length);
        for (int i = stock.Length - 1; i >= 0; i--)
            contents.UInt32(stock[i].Serial).UInt16(stock[i].Graphic).Byte(0).UInt16(stock[i].Amount)
                .UInt16(i + 1).UInt16(1).Byte(0).UInt32(BuyPack).UInt16(0);
        session.Inject(contents.ToArray(), true);
        var prices = new PacketWriter(0x74).UInt32(BuyPack).Byte(stock.Length);
        foreach (var s in stock)
            prices.UInt32((uint)s.Price).Byte(s.Name.Length + 1).Ascii(s.Name, s.Name.Length).Byte(0);
        session.Inject(prices.ToArray(), true);
        session.Inject(new PacketWriter(0x24, 9).UInt32(Vendor).UInt16(0x30).UInt16(0).ToArray(), false);
    }

    [Fact]
    public void BuyOrderGoesOutAsModernUoVendorBuyReplyReadsIt()
    {
        var sent = new List<byte[]>();
        GameSession session = InWorld(sent);
        ShopList? shop = null;
        uint closed = 0;
        session.ShopOpened += s => shop = s;
        session.ShopClosed += v => closed = v;

        OpenBuy(session, (0x40000001, 0x13B2, 5, 35, "bow"), (0x40000002, 0x0F3F, 20, 2, "arrow"));
        Assert.Equal(Vendor, shop!.Vendor);
        Assert.Equal(new[] { "Bow", "Arrow" }, shop.Items.Select(i => i.Name));

        // ShopWindow's Accept: the chosen (serial, amount) lines of the list.
        session.Buy(shop.Vendor, new[] { (shop.Items[0].Serial, 2), (shop.Items[1].Serial, 20) });
        byte[] p = Assert.Single(sent);
        // VendorBuyReply: vendor, flag 0x02, then (layer, serial, int16 amount) per 7 remaining bytes.
        Assert.Equal(new byte[]
        {
            0x3B, 0x00, 0x16, 0x00, 0x00, 0x12, 0x34, 0x02,
            0x1A, 0x40, 0x00, 0x00, 0x01, 0x00, 0x02,
            0x1A, 0x40, 0x00, 0x00, 0x02, 0x00, 0x14
        }, p);
        Assert.Equal(0, (p.Length - 8) % 7);

        // SendEndVendorBuy (0x3B, 8 bytes) closes the list.
        session.Inject(new PacketWriter(0x3B).UInt32(Vendor).Byte(0).ToArray(), true);
        Assert.Equal(Vendor, closed);
    }

    [Fact]
    public void EmptyBuyOrderSendsTheCloseFlag()
    {
        var sent = new List<byte[]>();
        GameSession session = InWorld(sent);
        session.Buy(Vendor, Array.Empty<(uint, int)>());
        // Flag 0: VendorBuyReply buys nothing and answers with SendEndVendorBuy.
        Assert.Equal(new byte[] { 0x3B, 0x00, 0x08, 0x00, 0x00, 0x12, 0x34, 0x00 }, Assert.Single(sent));
    }

    [Fact]
    public void ReopenedBuyListDropsSoldOutStock()
    {
        // ModernUO skips a sold-out line (BaseVendor.VendorBuy, Amount <= 0) and numbers the rest x = 1..; its display
        // item is never deleted. TazUO's UpdateContainedItems empties the container before the new 0x3C, so the old
        // line can't keep x = 1 and take the next line's price.
        var sent = new List<byte[]>();
        GameSession session = InWorld(sent);
        ShopList? shop = null;
        session.ShopOpened += s => shop = s;

        OpenBuy(session, (0x40000001, 0x13B2, 1, 35, "bow"), (0x40000002, 0x0F3F, 20, 2, "arrow"), (0x40000003, 0x1BFB, 20, 3, "bolt"));
        Assert.Equal(3, shop!.Items.Count);

        OpenBuy(session, (0x40000002, 0x0F3F, 20, 2, "arrow"), (0x40000003, 0x1BFB, 20, 3, "bolt"));
        Assert.Equal(new[] { 0x40000002u, 0x40000003u }, shop!.Items.Select(i => i.Serial));
        Assert.Equal(new[] { "Arrow", "Bolt" }, shop.Items.Select(i => i.Name));
        Assert.Equal(new[] { 2, 3 }, shop.Items.Select(i => i.Price));
        Assert.Equal(2, session.World.ItemsIn(BuyPack).Count());

        session.Buy(shop.Vendor, new[] { (shop.Items[0].Serial, 1) });
        Assert.Equal(new byte[] { 0x3B, 0x00, 0x0F, 0x00, 0x00, 0x12, 0x34, 0x02, 0x1A, 0x40, 0x00, 0x00, 0x02, 0x00, 0x01 }, Assert.Single(sent));
    }

    [Fact]
    public void SellOrderGoesOutAsModernUoVendorSellReplyReadsIt()
    {
        var sent = new List<byte[]>();
        GameSession session = InWorld(sent);
        ShopList? shop = null;
        session.ShopOpened += s => shop = s;

        // SendVendorSellList: vendor, u16 count, (serial, graphic, hue, amount, price, u16 name length, Latin-1 name).
        session.Inject(new PacketWriter(0x9E).UInt32(Vendor).UInt16(2)
            .UInt32(0x40000005).UInt16(0x1BF2).UInt16(0).UInt16(12).UInt16(3).UInt16(5).Ascii("ingot", 5)
            .UInt32(0x40000006).UInt16(0x0F7A).UInt16(0).UInt16(4).UInt16(1).UInt16(11).Ascii("black pearl", 11).ToArray(), true);
        Assert.True(shop!.Selling);
        Assert.Equal(Vendor, shop.Vendor);

        session.Sell(shop.Vendor, new[] { (shop.Items[0].Serial, 12), (shop.Items[1].Serial, 1) });
        byte[] p = Assert.Single(sent);
        // VendorSellReply: vendor, u16 count, and exactly count * 6 bytes of (serial, int16 amount) after it.
        Assert.Equal(new byte[]
        {
            0x9F, 0x00, 0x15, 0x00, 0x00, 0x12, 0x34, 0x00, 0x02,
            0x40, 0x00, 0x00, 0x05, 0x00, 0x0C,
            0x40, 0x00, 0x00, 0x06, 0x00, 0x01
        }, p);
        Assert.Equal(2 * 6, p.Length - 9);
    }

    [Fact]
    public void SkillUseAndLockSendTheZeroBasedSkillIndex()
    {
        var sent = new List<byte[]>();
        GameSession session = InWorld(sent);

        // SendSkillsUpdate (type 0x02): SkillID + 1, value, base, lock, cap; 0 ends the list.
        session.Inject(new PacketWriter(0x3A).Byte(0x02)
            .UInt16(22).UInt16(500).UInt16(500).Byte(0).UInt16(1000)
            .UInt16(47).UInt16(800).UInt16(800).Byte(1).UInt16(1000)
            .UInt16(0).ToArray(), true);
        SkillEntry hiding = session.Skills[21];
        Assert.Equal(0, hiding.Lock);
        Assert.Equal(1, session.Skills[46].Lock);

        // ChangeSkillLock: Skills[int16 index] (0-based), lock byte (0 up, 1 down, 2 locked).
        session.SetSkillLock(hiding.Id, 2);
        Assert.Equal(new byte[] { 0x3A, 0x00, 0x06, 0x00, 0x15, 0x02 }, Assert.Single(sent));
        sent.Clear();

        // TextCommand 0x24: the first token is the 0-based skill index (TazUO's Send_UseSkill "{idx} 0").
        session.UseSkill(hiding.Id);
        Assert.Equal(new byte[] { 0x12, 0x00, 0x09, 0x24, (byte)'2', (byte)'1', (byte)' ', (byte)'0', 0x00 }, Assert.Single(sent));
    }

    [Fact]
    public void ContextChoiceSendsTheEntryIndexNotItsPosition()
    {
        var sent = new List<byte[]>();
        GameSession session = InWorld(sent);
        ContextMenu? menu = null;
        session.ContextMenuOpened += m => menu = m;

        session.RequestContextMenu(Vendor);
        Assert.Equal(new byte[] { 0xBF, 0x00, 0x09, 0x00, 0x13, 0x00, 0x00, 0x12, 0x34 }, Assert.Single(sent));
        sent.Clear();

        // Classic (type 1) entries: index, cliloc - 3000000, flags, hue when flags has 0x20. Indices out of list order.
        session.Inject(new PacketWriter(0xBF).UInt16(0x14).UInt16(1).UInt32(Vendor).Byte(3)
            .UInt16(4).UInt16(6103).UInt16(0x20).UInt16(0x0481) // Buy, coloured
            .UInt16(7).UInt16(6106).UInt16(0)                    // Sell
            .UInt16(2).UInt16(6098).UInt16(0)                    // Train
            .ToArray(), true);
        Assert.Equal(new[] { 3006103, 3006106, 3006098 }, menu!.Entries.Select(e => e.Cliloc));
        Assert.Equal(new[] { 4, 7, 2 }, menu.Entries.Select(e => e.Index));

        // ContextMenuWindow and CaptureTour choose with entry.Index; ModernUO's ContextMenuResponse indexes its menu by it.
        ContextMenuEntry train = menu.Entries[2];
        session.ChooseContextMenu(menu.Serial, train.Index);
        Assert.Equal(new byte[] { 0xBF, 0x00, 0x0B, 0x00, 0x15, 0x00, 0x00, 0x12, 0x34, 0x00, 0x02 }, Assert.Single(sent));
    }

    [Fact]
    public void ContextMenuChoiceEchoesSerialAndIndex()
    {
        // ModernUO's ContextMenuResponse: serial, u16 index into the menu it sent.
        Assert.Equal(new byte[] { 0xBF, 0x00, 0x0B, 0x00, 0x15, 0x00, 0x00, 0x12, 0x34, 0x00, 0x01 },
            GameSession.ContextMenuChoice(0x00001234, 1));
    }
}
