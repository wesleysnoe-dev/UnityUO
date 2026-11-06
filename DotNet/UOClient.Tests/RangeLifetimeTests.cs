using UOClient.Core;
using UOClient.Net;

namespace UOClient.Tests;

/// <summary>
/// Out-of-range lifetime (TazUO's World.Update removal, World.cs:463-541) against ModernUO's send rules: the player's own
/// moves and teleports never bring a 0x1D for what fell behind (Mobile.cs:7381-7462), so the client drops it, never inside
/// what the server still counts as in view, and never the player's own things.
/// </summary>
public sealed class RangeLifetimeTests
{
    private const uint Me = 0x00000042, Orc = 0x00000077, Ettin = 0x00000090, Llama = 0x00000101;
    private const int X = 1495, Y = 1629;

    private static GameSession InWorld(List<byte[]> sent)
    {
        var session = new GameSession(new LoginSettings { AnnounceChatLinks = false });
        session.StartOffline(sent.Add);
        session.Inject(new PacketWriter(0x1B, 37).UInt32(Me).UInt32(0).UInt16(0x190).UInt16(X).UInt16(Y).UInt16(0)
            .Byte(0).Zero(9).UInt16(7168).UInt16(4096).Zero(6).ToArray(), false);
        session.Inject(new byte[] { 0x55 }, false);
        Assert.Equal(SessionState.InWorld, session.State);
        sent.Clear();
        return session;
    }

    // ModernUO's MobileMoving (0x77): serial, body, x, y, z, direction, hue, flags, notoriety.
    private static void Moving(GameSession s, uint serial, int x, int y, byte notoriety = 3) =>
        s.Inject(new PacketWriter(0x77, 17).UInt32(serial).UInt16(0x190).UInt16(x).UInt16(y).Byte(0).Byte(2)
            .UInt16(0x83EA).Byte(0).Byte(notoriety).ToArray(), false);

    // MobileIncoming (0x78), wearing a backpack (layer 21) unless it is 0; it asks for the name (0x98).
    private static void Incoming(GameSession s, uint serial, int x, int y, uint backpack = 0)
    {
        PacketWriter w = new PacketWriter(0x78).UInt32(serial).UInt16(0xDC).UInt16(x).UInt16(y).Byte(0).Byte(2).UInt16(0).Byte(0).Byte(3);
        if (backpack != 0)
            w.UInt32(backpack).UInt16(0x0E75).Byte(21);
        s.Inject(w.UInt32(0).ToArray(), true);
    }

    // 0x20: the server puts the player somewhere (teleport, resync); ModernUO sends it before what is around the new spot.
    private static void Teleport(GameSession s, int x, int y) =>
        s.Inject(new PacketWriter(0x20, 19).UInt32(Me).UInt16(0x190).Byte(0).UInt16(0x83EA).Byte(0).UInt16(x).UInt16(y)
            .UInt16(0).Byte(0).Byte(0).ToArray(), false);

    // 0xF3 (HS world item): type 0 item, 2 multi.
    private static void Ground(GameSession s, uint serial, int x, int y, ushort graphic = 0x0E75, int type = 0) =>
        s.Inject(new PacketWriter(0xF3, 24).UInt16(1).Byte(type).UInt32(serial).UInt16(graphic).Byte(0).UInt16(1).UInt16(1)
            .UInt16(x).UInt16(y).Byte(0).Byte(0).UInt16(0).Byte(0x20).ToArray(), false);

    // 0x25: an item into a container.
    private static void Into(GameSession s, uint serial, uint container) =>
        s.Inject(new PacketWriter(0x25, 21).UInt32(serial).UInt16(0x0EED).Byte(0).UInt16(1).UInt16(50).UInt16(50).Byte(0)
            .UInt32(container).UInt16(0).ToArray(), false);

    // 0x24: the server opened a container for us.
    private static void Open(GameSession s, uint container) =>
        s.Inject(new PacketWriter(0x24, 9).UInt32(container).UInt16(0x3C).UInt16(0x7D).ToArray(), false);

    // 0x2E: worn by a mobile.
    private static void Wear(GameSession s, uint serial, ushort graphic, byte layer, uint parent) =>
        s.Inject(new PacketWriter(0x2E, 15).UInt32(serial).UInt16(graphic).Byte(0).Byte(layer).UInt32(parent).UInt16(0).ToArray(), false);

    private static int NameRequests(List<byte[]> sent, uint serial) =>
        sent.Count(p => p[0] == 0x98 && p.Length >= 7 && (uint)(p[3] << 24 | p[4] << 16 | p[5] << 8 | p[6]) == serial);

    // ---- move away / return ----

    [Fact]
    public void WalkingAwayDropsWhatFellBehindAndItComesBackOnReturn()
    {
        var sent = new List<byte[]>();
        GameSession s = InWorld(sent);
        var removed = new List<uint>();
        s.World.EntityRemoved += removed.Add;
        Incoming(s, Orc, X + 5, Y);
        Ground(s, 0x40000010, X + 5, Y + 1);
        Assert.Equal(1, NameRequests(sent, Orc));

        s.Update(1.0);
        Assert.True(s.World.Mobiles.ContainsKey(Orc));
        Assert.True(s.World.Items.ContainsKey(0x40000010));

        // The server moves us 40 tiles on (no 0x1D comes for what we left behind).
        Teleport(s, X + 45, Y);
        s.Update(1.1);
        Assert.False(s.World.Mobiles.ContainsKey(Orc));
        Assert.False(s.World.Items.ContainsKey(0x40000010));
        Assert.Equal(new[] { Orc, 0x40000010u }, removed.OrderBy(r => r));
        Assert.Equal(2, s.PrunedCount);
        Assert.NotNull(s.World.Player);

        // Back again: the server sends them as new, and the mobile's name is asked for again.
        Teleport(s, X, Y);
        Incoming(s, Orc, X + 5, Y);
        Ground(s, 0x40000010, X + 5, Y + 1);
        s.Update(1.2);
        Assert.True(s.World.Mobiles.ContainsKey(Orc));
        Assert.True(s.World.Items.ContainsKey(0x40000010));
        Assert.Equal(2, NameRequests(sent, Orc));
    }

    [Fact]
    public void OnlyRunsInTheWorldOnTheCadence()
    {
        var sent = new List<byte[]>();
        GameSession s = InWorld(sent);
        s.Update(1.0);
        Moving(s, Orc, X + 40, Y);
        s.Update(1.01); // within 50 ms of the last pass
        Assert.True(s.World.Mobiles.ContainsKey(Orc));
        s.Update(1.06);
        Assert.False(s.World.Mobiles.ContainsKey(Orc));
    }

    [Fact]
    public void NothingIsDroppedBeforeTheWorldIsEntered()
    {
        var sent = new List<byte[]>();
        var s = new GameSession(new LoginSettings { AnnounceChatLinks = false });
        s.StartOffline(sent.Add);
        Moving(s, Orc, 100, 100); // no player yet
        s.Update(1);
        Assert.True(s.World.Mobiles.ContainsKey(Orc));
        Assert.Empty(s.World.PruneOutOfRange());

        // 0x1B (EnteringWorld): the world's contents arrive before 0x55, none dropped yet.
        s.Inject(new PacketWriter(0x1B, 37).UInt32(Me).UInt32(0).UInt16(0x190).UInt16(X).UInt16(Y).UInt16(0)
            .Byte(0).Zero(9).UInt16(7168).UInt16(4096).Zero(6).ToArray(), false);
        s.Update(2);
        Assert.True(s.World.Mobiles.ContainsKey(Orc));
    }

    // ---- ranges ----

    [Theory]
    [InlineData(18, true)]
    [InlineData(22, true)]
    [InlineData(24, true)]
    [InlineData(25, false)]
    public void MobilesAndItemsGoOnlyPastTheViewRange(int distance, bool kept)
    {
        var sent = new List<byte[]>();
        GameSession s = InWorld(sent);
        Moving(s, Orc, X, Y - distance);
        Ground(s, 0x40000011, X - distance, Y + distance);
        s.World.PruneOutOfRange();
        Assert.Equal(kept, s.World.Mobiles.ContainsKey(Orc));
        Assert.Equal(kept, s.World.Items.ContainsKey(0x40000011));
    }

    [Fact]
    public void ARangeBelowTheServersPlusPredictionIsRaised()
    {
        var sent = new List<byte[]>();
        GameSession s = InWorld(sent);
        Moving(s, Orc, X + 22, Y);
        Moving(s, Ettin, X + 23, Y);
        s.World.PruneOutOfRange(10); // never below 18 + 4
        Assert.True(s.World.Mobiles.ContainsKey(Orc));
        Assert.False(s.World.Mobiles.ContainsKey(Ettin));
    }

    [Fact]
    public void PredictedStepsAheadNeverDropWhatTheServerStillShows()
    {
        var sent = new List<byte[]>();
        GameSession s = InWorld(sent);
        Moving(s, Orc, X - 18, Y); // at the server's edge, behind us
        for (int i = 1; i <= 4; i++)
        {
            Assert.True(s.RequestStep(2, false));
            s.ApplyPredictedStep(X + i, Y, 0, 2);
        }
        Assert.False(s.RequestStep(2, false)); // a fifth unanswered step is refused: the lead is at most 4
        s.Update(1);
        Assert.True(s.World.Mobiles.ContainsKey(Orc));

        // The server refuses the steps: we are back where it has us, the orc still there.
        s.Inject(new PacketWriter(0x21, 8).Byte(1).UInt16(X).UInt16(Y).Byte(2).Byte(0).ToArray(), false);
        Assert.Equal(X, s.World.Player.X);
        s.Update(2);
        Assert.True(s.World.Mobiles.ContainsKey(Orc));
    }

    // ---- multis ----

    [Fact]
    public void AMultiStaysPastItsServerRangeAndItsExtent()
    {
        var sent = new List<byte[]>();
        GameSession s = InWorld(sent);
        Ground(s, 0x40000020, X + 26, Y, graphic: 0x0064, type: 2); // 22 + 4: still in view
        Ground(s, 0x40000021, X + 27, Y, graphic: 0x0064, type: 2);
        Ground(s, 0x40000022, X + 30, Y, graphic: 0x0064, type: 2);
        s.World.HouseDesigns[0x40000021] = null;
        s.World.MultiExtent = m => m.Serial == 0x40000022 ? 8 : 0;
        s.World.PruneOutOfRange();
        Assert.True(s.World.Items.ContainsKey(0x40000020));
        Assert.False(s.World.Items.ContainsKey(0x40000021));
        Assert.True(s.World.Items.ContainsKey(0x40000022));        // 30 <= 26 + 8
        Assert.True(s.World.HouseDesigns.ContainsKey(0x40000021)); // designs are kept when a house leaves view

        Teleport(s, X - 5, Y); // 35 > 34
        s.World.PruneOutOfRange();
        Assert.False(s.World.Items.ContainsKey(0x40000022));
    }

    // ---- ownership ----

    [Fact]
    public void ThePlayersOwnThingsAreNeverDropped()
    {
        var sent = new List<byte[]>();
        GameSession s = InWorld(sent);
        const uint pack = 0x40000100, bag = 0x40000101, inBag = 0x40000102, bank = 0x40000103;
        Wear(s, pack, 0x0E75, 21, Me);
        Wear(s, bank, 0x09AB, 29, Me);
        Into(s, bag, pack);
        Into(s, inBag, bag);
        Open(s, pack);
        Open(s, bag);

        Teleport(s, 100, 100);
        Moving(s, Orc, X, Y); // far now
        s.Update(1);
        Assert.False(s.World.Mobiles.ContainsKey(Orc));
        Mobile me = s.World.Player;
        Assert.NotNull(me);
        Assert.Equal(pack, s.World.Worn(me, 21));
        Assert.Equal(bank, s.World.Worn(me, 29));
        Assert.True(s.World.Contained.ContainsKey(bag));
        Assert.True(s.World.Contained.ContainsKey(inBag));
        Assert.True(s.World.OpenContainers.ContainsKey(pack));
        Assert.True(s.World.OpenContainers.ContainsKey(bag));
    }

    [Fact]
    public void AGroundContainerTakesItsNestedContentsAndWindows()
    {
        var sent = new List<byte[]>();
        GameSession s = InWorld(sent);
        const uint chest = 0x40000200, bag = 0x40000201, gem = 0x40000202, near = 0x40000203, nearItem = 0x40000204;
        Ground(s, chest, X + 10, Y);
        Ground(s, near, X + 2, Y);
        Into(s, bag, chest);
        Into(s, gem, bag);
        Into(s, nearItem, near);
        Open(s, chest);
        Open(s, bag);
        Open(s, near);
        var changed = new List<uint>();
        var removed = new List<uint>();
        s.World.ContainerChanged += changed.Add;
        s.World.EntityRemoved += removed.Add;

        Teleport(s, X - 20, Y); // chest 30 away, the near box 22
        s.Update(1);
        Assert.False(s.World.Items.ContainsKey(chest));
        Assert.False(s.World.Contained.ContainsKey(bag));
        Assert.False(s.World.Contained.ContainsKey(gem));
        Assert.False(s.World.OpenContainers.ContainsKey(chest));
        Assert.False(s.World.OpenContainers.ContainsKey(bag));
        Assert.Contains(chest, changed);
        Assert.Contains(bag, changed);
        Assert.Equal(new[] { chest, bag, gem }, removed.OrderBy(r => r));

        Assert.True(s.World.Items.ContainsKey(near));
        Assert.True(s.World.Contained.ContainsKey(nearItem));
        Assert.True(s.World.OpenContainers.ContainsKey(near));
    }

    [Fact]
    public void APackAnimalTakesItsBackpacksContentsAndWindow()
    {
        var sent = new List<byte[]>();
        GameSession s = InWorld(sent);
        const uint pack = 0x40000300, load = 0x40000301;
        Incoming(s, Llama, X + 3, Y, pack);
        Into(s, load, pack);
        Open(s, pack);
        Assert.Equal(pack, s.World.Worn(s.World.Mobiles[Llama], 21));

        Teleport(s, X + 40, Y);
        s.Update(1);
        Assert.False(s.World.Mobiles.ContainsKey(Llama));
        Assert.False(s.World.Contained.ContainsKey(load));
        Assert.False(s.World.OpenContainers.ContainsKey(pack));
    }

    [Fact]
    public void HandlersMayReadTheWorldWhileItIsPruned()
    {
        var sent = new List<byte[]>();
        GameSession s = InWorld(sent);
        for (uint i = 0; i < 50; i++)
        {
            Moving(s, 0x1000 + i, X + 30 + (int)i, Y);
            Ground(s, 0x40001000 + i, X - 30 - (int)i, Y);
        }
        int seen = 0;
        s.World.EntityRemoved += _ => seen += s.World.Mobiles.Count + s.World.Items.Count;
        Assert.Equal(100, s.World.PruneOutOfRange().Count);
        Assert.True(seen > 0);
        Assert.Equal(new[] { Me }, s.World.Mobiles.Keys);
        Assert.Empty(s.World.Items);
    }

    // ---- targets ----

    [Fact]
    public void TargetNextAndNearestSkipStaleMobilesBeforeAPrunePass()
    {
        var sent = new List<byte[]>();
        GameSession s = InWorld(sent);
        Moving(s, Orc, X + 3, Y, notoriety: 6);
        Moving(s, Ettin, X + 30, Y, notoriety: 6); // stale: no Update has run
        Moving(s, Llama, X + 2, Y, notoriety: 1);

        Assert.Equal(new[] { Orc, Llama, Orc }, new[] { s.TargetNext(), s.TargetNext(), s.TargetNext() });
        Assert.Equal(Orc, s.TargetNearest());

        // The nearest hostile walks off; the far one stays stale.
        Moving(s, Orc, X + 26, Y, notoriety: 6);
        Assert.Equal(0u, s.TargetNearest());
    }

    [Fact]
    public void ATargetThatLeftViewCannotBeAttacked()
    {
        var sent = new List<byte[]>();
        GameSession s = InWorld(sent);
        Moving(s, Orc, X + 3, Y, notoriety: 6);
        Assert.Equal(Orc, s.TargetNext());
        Teleport(s, X + 50, Y);
        s.Update(1);
        Assert.False(s.World.Mobiles.ContainsKey(Orc));
        sent.Clear();
        Assert.False(s.AttackLast());
        Assert.Equal(0u, s.TargetNext());
        Assert.Empty(sent);
    }

    [Fact]
    public void AGhostInViewIsStillATarget()
    {
        // TazUO's FindNext / FindNearest do not skip the dead (World.cs:835-1007).
        var sent = new List<byte[]>();
        GameSession s = InWorld(sent);
        s.Inject(new PacketWriter(0x77, 17).UInt32(Orc).UInt16(402).UInt16(X + 1).UInt16(Y).Byte(0).Byte(2)
            .UInt16(0).Byte(0).Byte(6).ToArray(), false);
        Assert.True(s.World.Mobiles[Orc].IsDead);
        Assert.Equal(Orc, s.TargetNext());
    }

    // ---- disconnect ----

    [Fact]
    public void AfterADisconnectNothingIsPrunedAndANewSessionStartsEmpty()
    {
        var sent = new List<byte[]>();
        GameSession s = InWorld(sent);
        Moving(s, Orc, X + 3, Y);
        s.SimulateConnectionLost("gone");
        s.Update(1);
        Assert.Equal(SessionState.Disconnected, s.State);
        Teleport(s, X + 60, Y);
        s.Update(2);
        Assert.True(s.World.Mobiles.ContainsKey(Orc)); // a dropped session's world is left as it was
        Assert.Equal(0, s.PrunedCount);

        GameSession next = InWorld(sent);
        Assert.Equal(new[] { Me }, next.World.Mobiles.Keys);
        Assert.Empty(next.World.Items);
        Assert.Null(next.World.MultiExtent);
    }
}
