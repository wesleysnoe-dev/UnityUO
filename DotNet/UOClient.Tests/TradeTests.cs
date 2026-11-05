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

    [Fact]
    public void ContextMenuChoiceEchoesSerialAndIndex()
    {
        // ModernUO's ContextMenuResponse: serial, u16 index into the menu it sent.
        Assert.Equal(new byte[] { 0xBF, 0x00, 0x0B, 0x00, 0x15, 0x00, 0x00, 0x12, 0x34, 0x00, 0x01 },
            GameSession.ContextMenuChoice(0x00001234, 1));
    }
}
