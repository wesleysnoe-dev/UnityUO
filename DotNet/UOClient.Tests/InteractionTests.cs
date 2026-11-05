using System.Buffers.Binary;
using UOClient.Core;
using UOClient.IO.Formats;
using UOClient.Net;

namespace UOClient.Tests;

/// <summary>Drag and drop, as ModernUO's IncomingItemPackets reads it.</summary>
public sealed class InteractionTests
{
    [Fact]
    public void DropRequestMatchesModernUOsDropReq()
    {
        // Into a container at a gump spot: serial, x, y, z, grid index, destination.
        byte[] into = GameSession.DropRequest(0x40000010, 44, 65, 0, 0x40000001);
        Assert.Equal(new byte[] { 0x08, 0x40, 0, 0, 0x10, 0, 44, 0, 65, 0, 0, 0x40, 0, 0, 1 }, into);

        // Onto an item: x = y = -1, which ModernUO's Item.DropToItem treats as "onto" (place anywhere / merge).
        byte[] onto = GameSession.DropRequest(0x40000010, 0xFFFF, 0xFFFF, 0, 0x40000002);
        Assert.Equal(-1, BinaryPrimitives.ReadInt16BigEndian(onto.AsSpan(5)));
        Assert.Equal(-1, BinaryPrimitives.ReadInt16BigEndian(onto.AsSpan(7)));

        // On the ground: signed z, destination -1 (not an item or mobile serial).
        byte[] ground = GameSession.DropRequest(0x40000010, 1495, 1629, -5, 0xFFFFFFFF);
        Assert.Equal(15, ground.Length);
        Assert.Equal(-5, (sbyte)ground[9]);
        Assert.Equal(0xFFFFFFFFu, BinaryPrimitives.ReadUInt32BigEndian(ground.AsSpan(11)));
    }

    [Fact]
    public void SingleClickLabelOfAContainedItemIsFoundByItsSerial()
    {
        var session = new GameSession(new LoginSettings());
        // ModernUO's Item.LabelTo: 0xC1 with the item's serial and graphic, type Label (6), no name, no args.
        byte[] label = new PacketWriter(0xC1).UInt32(0x40000010).UInt16(0x0F0E).Byte(6).UInt16(0x3B2).UInt16(3)
            .Int32(1041361).Zero(30).UInt16(0).ToArray();
        session.Inject(label, true);

        DateTime now = DateTime.Now;
        JournalEntry? line = session.World.RecentLine(0x40000010, now, 3);
        Assert.NotNull(line);
        Assert.Equal(6, line!.Value.Type);
        Assert.Contains("1041361", line.Value.Text); // no Cliloc.enu loaded: the number stands in for the name
        Assert.Null(session.World.RecentLine(0x40000011, now, 3));
        Assert.Null(session.World.RecentLine(0x40000010, now.AddSeconds(5), 3)); // gone after a few seconds
    }

    [Fact]
    public void OnlyMovableGroundItemsCanBePickedUp()
    {
        var session = new GameSession(new LoginSettings());
        // ModernUO's 0xF3 world item: 1, type, serial, graphic, direction, amount x2, x, y, z, light, hue, flags (0x20 = Movable).
        byte[] Item(uint serial, byte type, ushort graphic, byte flags) => new PacketWriter(0xF3, 26).UInt16(1).Byte(type)
            .UInt32(serial).UInt16(graphic).Byte(0).UInt16(5).UInt16(5).UInt16(1500).UInt16(1600).Byte(0).Byte(0)
            .UInt16(0).Byte(flags).UInt16(0).ToArray();
        session.Inject(Item(0x40000020, 0, 0x0EED, 0x20), false);
        session.Inject(Item(0x40000021, 0, 0x0B90, 0x00), false);   // locked down / static decoration
        session.Inject(Item(0x40000022, 2, 0x0064, 0x20), false);   // a house: never

        Assert.True(session.World.Items[0x40000020].Movable);
        Assert.Equal(5, session.World.Items[0x40000020].Amount);
        Assert.False(session.World.Items[0x40000021].Movable);
        Assert.False(session.World.Items[0x40000022].Movable);

        // The lift for the whole stack, as ModernUO's LiftReq reads it: serial, amount.
        Assert.Equal(new byte[] { 0x07, 0x40, 0, 0, 0x20, 0, 5 }, GameSession.LiftRequest(0x40000020, 5));
        Assert.Equal(1, GameSession.LiftRequest(0x40000020, 0)[6]); // never 0
    }

    [Theory]
    [InlineData(0x00000123u, false, false)] // ourselves in peace: a plain use (dismount, else paperdoll)
    [InlineData(0x00000123u, true, false)]  // ourselves in war mode: still a use, never an attack on ourselves
    [InlineData(0x00000456u, false, false)] // another mobile in peace: use (its paperdoll, a pet's menu, ...)
    [InlineData(0x00000456u, true, true)]   // another mobile in war mode: attack
    [InlineData(0x40000010u, true, false)]  // an item in war mode: use (a door, a bandage)
    public void DoubleClickAttacksOnlyAnotherMobileInWarMode(uint serial, bool war, bool attacks)
    {
        Assert.Equal(attacks, GameSession.AttacksOnDoubleClick(serial, 0x123, war));
    }

    [Fact]
    public void StatusRequestMatchesModernUOsMobileQuery()
    {
        // MobileQuery: 0xEDEDEDED, type 4 (stats), serial. The war-mode double-click sends it before the 0x05 attack.
        Assert.Equal(new byte[] { 0x34, 0xED, 0xED, 0xED, 0xED, 0x04, 0x00, 0x00, 0x04, 0x56 }, GameSession.StatusRequest(0x456));
    }

    [Theory]
    [InlineData(0x0F0E, TileFlags.None, 0x0E75, TileFlags.Container, true)]   // a bottle onto a backpack
    [InlineData(0x0F0E, TileFlags.None, 0x2006, TileFlags.None, true)]        // onto a corpse
    [InlineData(0x1F4C, TileFlags.None, 0x0EFA, TileFlags.None, true)]        // a scroll onto a spellbook
    [InlineData(0x0EED, TileFlags.Generic, 0x0EED, TileFlags.Generic, true)]  // gold onto gold
    [InlineData(0x0EED, TileFlags.Generic, 0x0F0E, TileFlags.None, false)]    // gold onto a bottle: beside it
    [InlineData(0x13F6, TileFlags.None, 0x13F6, TileFlags.None, false)]       // two daggers don't stack
    [InlineData(0x0F0E, TileFlags.None, 0x0B90, TileFlags.Surface, false)]    // onto a table: on top of it, not into it
    public void DropOntoAnItemGoesIntoItOnlyWhenItCanTakeIt(int held, TileFlags heldFlags, int target, TileFlags targetFlags, bool into)
    {
        Assert.Equal(into, GameSession.DropsInto((ushort)held, heldFlags, (ushort)target, targetFlags));
    }
}
