using UOClient.Core;
using UOClient.Net;

namespace UOClient.Tests;

/// <summary>
/// The server's delete (0x1D) as TazUO's DeleteObject (DeleteObject.cs): the player is never removed, and what the object
/// held goes with it at any depth (RemoveItem / RemoveMobile, World.cs:729-793). ModernUO sends 0x1D when an item is
/// deleted, moved out of view, unequipped or taken out of a container (Item.SendRemovePacket, Mobile.RemoveItem
/// Mobile.cs:6727-6736), and when a bank box closes (Containers.cs:63-70).
/// </summary>
public sealed class ServerDeleteCascadeTests
{
    private const uint Me = 0x00000042, Llama = 0x00000101;
    private const uint Chest = 0x40000100, Bag = 0x40000101, Gold = 0x40000102, Gem = 0x40000103;
    private const uint Pack = 0x40000200, PackGold = 0x40000201, MyPack = 0x40000300, MyGold = 0x40000301;

    private static GameSession InWorld()
    {
        var session = new GameSession(new LoginSettings { AnnounceChatLinks = false });
        session.StartOffline(_ => { });
        session.Inject(new PacketWriter(0x1B, 37).UInt32(Me).UInt32(0).UInt16(0x190).UInt16(1495).UInt16(1629).UInt16(0)
            .Byte(0).Zero(9).UInt16(7168).UInt16(4096).Zero(6).ToArray(), false);
        session.Inject(new byte[] { 0x55 }, false);
        return session;
    }

    private static void Delete(GameSession s, uint serial) => s.Inject(new PacketWriter(0x1D, 5).UInt32(serial).ToArray(), false);

    private static void Ground(GameSession s, uint serial) =>
        s.Inject(new PacketWriter(0xF3, 24).UInt16(1).Byte(0).UInt32(serial).UInt16(0x0E40).Byte(0).UInt16(1).UInt16(1)
            .UInt16(1496).UInt16(1630).Byte(0).Byte(0).UInt16(0).Byte(0x20).ToArray(), false);

    private static void Into(GameSession s, uint serial, uint container) =>
        s.Inject(new PacketWriter(0x25, 21).UInt32(serial).UInt16(0x0EED).Byte(0).UInt16(1).UInt16(50).UInt16(50).Byte(0)
            .UInt32(container).UInt16(0).ToArray(), false);

    private static void Open(GameSession s, uint container) =>
        s.Inject(new PacketWriter(0x24, 9).UInt32(container).UInt16(0x3C).UInt16(0x7D).ToArray(), false);

    private static void Wear(GameSession s, uint serial, ushort graphic, byte layer, uint parent) =>
        s.Inject(new PacketWriter(0x2E, 15).UInt32(serial).UInt16(graphic).Byte(0).Byte(layer).UInt32(parent).UInt16(0).ToArray(), false);

    private static void IncomingLlama(GameSession s) =>
        s.Inject(new PacketWriter(0x78).UInt32(Llama).UInt16(0xDC).UInt16(1497).UInt16(1629).Byte(0).Byte(2).UInt16(0)
            .Byte(0).Byte(3).UInt32(0).ToArray(), true);

    [Fact]
    public void ADeletedGroundChestTakesItsContentsAndWindowsAtEveryDepth()
    {
        GameSession s = InWorld();
        Ground(s, Chest);
        Into(s, Bag, Chest);
        Into(s, Gold, Chest);
        Into(s, Gem, Bag);
        Open(s, Chest);
        Open(s, Bag);
        var removed = new List<uint>();
        var changed = new List<uint>();
        s.World.EntityRemoved += removed.Add;
        s.World.ContainerChanged += changed.Add;

        Delete(s, Chest);

        Assert.False(s.World.Items.ContainsKey(Chest));
        Assert.Empty(s.World.Contained);
        Assert.Empty(s.World.OpenContainers);
        Assert.Equal(new[] { Chest, Bag, Gold, Gem }.OrderBy(x => x), removed.OrderBy(x => x));
        Assert.Contains(Chest, changed);
        Assert.Contains(Bag, changed);
    }

    [Fact]
    public void ADeletedMobileTakesItsPackContents()
    {
        GameSession s = InWorld();
        IncomingLlama(s);
        Wear(s, Pack, 0x0E75, 21, Llama);
        Into(s, PackGold, Pack);
        Open(s, Pack);

        Delete(s, Llama);

        Assert.False(s.World.Mobiles.ContainsKey(Llama));
        Assert.False(s.World.Contained.ContainsKey(PackGold));
        Assert.False(s.World.OpenContainers.ContainsKey(Pack));
    }

    [Fact]
    public void AnUnequippedPackTakesItsContentsButTheWearerStays()
    {
        GameSession s = InWorld();
        IncomingLlama(s);
        Wear(s, Pack, 0x0E75, 21, Llama);
        Into(s, PackGold, Pack);

        Delete(s, Pack);

        Assert.True(s.World.Mobiles.ContainsKey(Llama));
        Assert.False(s.World.Mobiles[Llama].Equipment.ContainsKey(21));
        Assert.False(s.World.Contained.ContainsKey(PackGold));
    }

    [Fact]
    public void ThePlayerIsNeverDeletedAndKeepsItsThings()
    {
        GameSession s = InWorld();
        Wear(s, MyPack, 0x0E75, 21, Me);
        Into(s, MyGold, MyPack);
        Open(s, MyPack);

        Delete(s, Me);

        Assert.NotNull(s.World.Player);
        Assert.True(s.World.Player.Equipment.ContainsKey(21));
        Assert.True(s.World.Contained.ContainsKey(MyGold));
        Assert.True(s.World.OpenContainers.ContainsKey(MyPack));
    }

    [Fact]
    public void ANearbyContainerIsUntouched()
    {
        GameSession s = InWorld();
        Ground(s, Chest);
        Into(s, Gold, Chest);
        Wear(s, MyPack, 0x0E75, 21, Me);
        Into(s, MyGold, MyPack);

        Delete(s, Chest);

        Assert.True(s.World.Contained.ContainsKey(MyGold));
        Assert.True(s.World.Player.Equipment.ContainsKey(21));
    }

    [Fact]
    public void RepeatsAndUnknownSerialsDoNothing()
    {
        GameSession s = InWorld();
        Ground(s, Chest);
        Into(s, Gold, Chest);
        Delete(s, Chest);
        var removed = new List<uint>();
        s.World.EntityRemoved += removed.Add;

        Delete(s, Chest);
        Delete(s, 0x40009999);
        Delete(s, 0);

        Assert.Empty(removed);
        Assert.NotNull(s.World.Player);
    }

    [Fact]
    public void AMalformedContainmentCycleEnds()
    {
        GameSession s = InWorld();
        Ground(s, Chest);
        // Bag in Gem and Gem in Bag (a malformed server state), both under the chest's serial chain.
        Into(s, Bag, Chest);
        Into(s, Gem, Bag);
        Into(s, Bag, Gem); // moves Bag into Gem: now Bag <-> Gem with nothing tying them to the chest
        Into(s, Gold, Gold); // an item inside itself

        Delete(s, Bag);
        Delete(s, Gold);

        Assert.False(s.World.Contained.ContainsKey(Bag));
        Assert.False(s.World.Contained.ContainsKey(Gem));
        Assert.False(s.World.Contained.ContainsKey(Gold));
        Assert.True(s.World.Items.ContainsKey(Chest));
    }

    [Fact]
    public void OldPathLeftChildrenBehind()
    {
        // What 0x1D did before: World.Remove alone. Kept as the comparison the cascade fixes (audit ITEM-09).
        var w = new World();
        w.Contained[Bag] = new ContainedItem { Serial = Bag, Container = Chest };
        w.Contained[Gem] = new ContainedItem { Serial = Gem, Container = Bag };
        w.Items[Chest] = new WorldItem { Serial = Chest };
        typeof(World).GetMethod("Remove", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .Invoke(w, new object[] { Chest });
        Assert.True(w.Contained.ContainsKey(Bag)); // the leak

        GameSession s = InWorld();
        Ground(s, Chest);
        Into(s, Bag, Chest);
        Into(s, Gem, Bag);
        Delete(s, Chest);
        Assert.False(s.World.Contained.ContainsKey(Bag));
        Assert.False(s.World.Contained.ContainsKey(Gem));
    }
}
