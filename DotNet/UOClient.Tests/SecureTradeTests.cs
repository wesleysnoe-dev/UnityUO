using UOClient.Core;
using UOClient.Net;

namespace UOClient.Tests;

public sealed class SecureTradeTests
{
    private const uint Me = 0x00000042, Them = 0x00000077, MyBox = 0x40000100, TheirBox = 0x40000101;

    // ModernUO's OutgoingSecureTradePackets: 0x6F, length, command, container, two ints, a bool (17 bytes) ...
    private static byte[] Update(int command, uint container, int first, int second) =>
        new PacketWriter(0x6F).Byte(command).UInt32(container).Int32(first).Int32(second).Bool(false).ToArray();

    // ... and the display: the other player, our container, theirs, true, their name in 30 Latin-1 bytes (47).
    private static byte[] Display(string name) =>
        new PacketWriter(0x6F).Byte(0).UInt32(Them).UInt32(MyBox).UInt32(TheirBox).Bool(true).Ascii(name, 30).ToArray();

    // SendSecureTradeEquip: a 0x25 putting the trade container on its owner (21 bytes with the grid byte).
    private static byte[] Equip(uint item, ushort graphic, int amount, int x, int y, uint container) =>
        new PacketWriter(0x25, 21).UInt32(item).UInt16(graphic).Byte(0).UInt16(amount).UInt16(x).UInt16(y).Byte(0)
            .UInt32(container).UInt16(0).ToArray();

    private static GameSession OpenTrade(out List<SecureTrade> opened, out List<SecureTrade> changed, out List<SecureTrade> closed)
    {
        var session = new GameSession(new LoginSettings());
        var o = opened = new List<SecureTrade>();
        var c = changed = new List<SecureTrade>();
        var x = closed = new List<SecureTrade>();
        session.TradeOpened += o.Add;
        session.TradeChanged += c.Add;
        session.TradeClosed += x.Add;

        // SecureTrade's constructor order for the initiating side, as ModernUO sends it (7.0.45.65+ adds the ledger).
        session.Inject(Update(2, MyBox, 0, 0), true);
        session.Inject(Equip(TheirBox, 0x1E5E, 1, 0, 0, Them), false);
        session.Inject(Update(2, MyBox, 0, 0), true);
        session.Inject(Equip(MyBox, 0x1E5E, 1, 0, 0, Me), false);
        session.Inject(Display("Bob"), true);
        session.Inject(Update(2, MyBox, 0, 0), true);
        session.Inject(Update(4, MyBox, 25000, 3), true);
        return session;
    }

    [Fact]
    public void DisplayOpensTheTradeAsModernUoSendsIt()
    {
        Assert.Equal(47, Display("Bob").Length);
        Assert.Equal(17, Update(2, MyBox, 0, 0).Length);
        Assert.Equal(-1, PacketLengths.For(ClientVersion.Default)[0x6F]);

        GameSession session = OpenTrade(out var opened, out var changed, out _);
        SecureTrade t = Assert.Single(opened);
        Assert.Equal(Them, t.Them);
        Assert.Equal("Bob", t.TheirName);
        Assert.Equal(MyBox, t.MyContainer);
        Assert.Equal(TheirBox, t.TheirContainer);
        Assert.Same(t, session.Trades[MyBox]);
        Assert.True(session.IsOpen(t));
        // The two updates before the display are dropped; the one after it and the ledger count.
        Assert.Equal(2, changed.Count);
        Assert.True(t.HasLedger);
        Assert.Equal(25000, t.BalanceGold);
        Assert.Equal(3, t.BalancePlatinum);
        // The containers ride on their owners (0x25 into the mobile).
        Assert.Equal(TheirBox, Assert.Single(session.World.ItemsIn(Them)).Serial);
    }

    [Fact]
    public void UpdatesSetAcceptsGoldAndItems()
    {
        GameSession session = OpenTrade(out _, out var changed, out _);
        SecureTrade t = session.Trades[MyBox];

        session.Inject(Update(2, MyBox, 1, 0), true);
        Assert.True(t.IAccept);
        Assert.False(t.TheyAccept);
        session.Inject(Update(2, MyBox, 0, 1), true);
        Assert.False(t.IAccept);
        Assert.True(t.TheyAccept);

        session.Inject(Update(3, MyBox, 1500, 2), true);
        Assert.Equal(1500, t.TheirGold);
        Assert.Equal(2, t.TheirPlatinum);
        Assert.Same(t, changed.Last());

        // An offered item arrives as a plain 0x25 into their container.
        uint? changedContainer = null;
        session.World.ContainerChanged += c => changedContainer = c;
        session.Inject(Equip(0x40000200, 0x0EED, 500, 30, 20, TheirBox), false);
        Assert.Equal(TheirBox, changedContainer);
        ContainedItem gold = Assert.Single(session.World.ItemsIn(TheirBox));
        Assert.Equal(500, gold.Amount);

        // Updates naming a container that isn't ours in a trade are ignored.
        int before = changed.Count;
        session.Inject(Update(2, TheirBox, 1, 1), true);
        Assert.Equal(before, changed.Count);
    }

    [Fact]
    public void CloseEndsTheTrade()
    {
        GameSession session = OpenTrade(out _, out _, out var closed);
        SecureTrade t = session.Trades[MyBox];
        session.Inject(Update(1, MyBox, 0, 0), true);
        Assert.Same(t, Assert.Single(closed));
        Assert.Empty(session.Trades);
        Assert.False(session.IsOpen(t));
        session.Inject(Update(1, MyBox, 0, 0), true); // a second close is nothing
        Assert.Single(closed);
    }

    [Fact]
    public void RedisplayOnTheSameContainerReplacesTheTrade()
    {
        GameSession session = OpenTrade(out var opened, out _, out _);
        session.Inject(Display("Bob"), true);
        Assert.Equal(2, opened.Count);
        Assert.False(session.IsOpen(opened[0])); // the old window's close won't cancel the new trade
        Assert.True(session.IsOpen(opened[1]));
    }

    [Fact]
    public void GoldOfferIsClampedToTheLedger()
    {
        GameSession session = OpenTrade(out _, out _, out _);
        SecureTrade t = session.Trades[MyBox];
        session.OfferTradeGold(t, 99999, -4);
        Assert.Equal(25000, t.MyGold);
        Assert.Equal(0, t.MyPlatinum);

        var noLedger = new SecureTrade { MyContainer = 0x40000999 };
        session.OfferTradeGold(noLedger, 10, 0);
        Assert.Equal(0, noLedger.MyGold);
    }

    [Fact]
    public void RequestsAreLaidOutAsModernUoReadsThem()
    {
        // ModernUO's IncomingMobilePackets.SecureTrade: command byte, container serial, then per command.
        Assert.Equal(new byte[] { 0x6F, 0x00, 0x08, 0x01, 0x40, 0x00, 0x01, 0x00 }, GameSession.TradeCancelRequest(MyBox));
        Assert.Equal(new byte[] { 0x6F, 0x00, 0x0C, 0x02, 0x40, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x01 },
            GameSession.TradeAcceptRequest(MyBox, true));
        Assert.Equal(new byte[] { 0x6F, 0x00, 0x0C, 0x02, 0x40, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00 },
            GameSession.TradeAcceptRequest(MyBox, false));
        Assert.Equal(new byte[] { 0x6F, 0x00, 0x10, 0x03, 0x40, 0x00, 0x01, 0x00, 0x00, 0x00, 0x05, 0xDC, 0x00, 0x00, 0x00, 0x02 },
            GameSession.TradeGoldRequest(MyBox, 1500, 2));
    }
}
