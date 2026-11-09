using System.Collections.Generic;
using UOClient.Core;

namespace UOClient.Tests;

/// <summary>0xF6: a boat's step moves the boat and everything aboard (ModernUO BoatPackets.CreateMoveBoatHS).</summary>
public class BoatMovingTests
{
    private static void U16(List<byte> b, int v) { b.Add((byte)(v >> 8)); b.Add((byte)v); }
    private static void U32(List<byte> b, uint v) { b.Add((byte)(v >> 24)); b.Add((byte)(v >> 16)); b.Add((byte)(v >> 8)); b.Add((byte)v); }

    private static byte[] Packet(uint boat, int x, int y, int z, params (uint Serial, int X, int Y, int Z)[] aboard)
    {
        var b = new List<byte> { 0xF6, 0, 0 };
        U32(b, boat);
        b.Add(1); b.Add(2); b.Add(2);
        U16(b, x); U16(b, y); U16(b, (ushort)(short)z);
        U16(b, aboard.Length);
        foreach (var e in aboard)
        {
            U32(b, e.Serial); U16(b, e.X); U16(b, e.Y); U16(b, (ushort)(short)e.Z);
        }
        b[1] = (byte)(b.Count >> 8);
        b[2] = (byte)b.Count;
        return b.ToArray();
    }

    [Fact]
    public void TheBoatItsCargoAndItsPassengersMove()
    {
        var s = new GameSession(new LoginSettings());
        s.World.Items[0x40000001] = new WorldItem { Serial = 0x40000001, Graphic = 0x3E, IsMulti = true, X = 100, Y = 100, Z = -5 };
        s.World.Items[0x40000002] = new WorldItem { Serial = 0x40000002, Graphic = 0x0E75, X = 101, Y = 100, Z = 0 };
        s.World.Mobiles[7] = new Mobile { Serial = 7, X = 100, Y = 101, Z = 0 };
        var carried = new List<uint>();
        var items = new List<uint>();
        s.World.MobileCarried += m => carried.Add(m.Serial);
        s.World.ItemUpdated += i => items.Add(i.Serial);

        s.Inject(Packet(0x40000001, 100, 99, -5, (7, 100, 100, 0), (0x40000002, 101, 99, 0), (99, 1, 1, 0)), true);

        Assert.Equal((100, 99, -5), (s.World.Items[0x40000001].X, s.World.Items[0x40000001].Y, (int)s.World.Items[0x40000001].Z));
        Assert.Equal((100, 100), (s.World.Mobiles[7].X, s.World.Mobiles[7].Y));
        Assert.Equal((101, 99), (s.World.Items[0x40000002].X, s.World.Items[0x40000002].Y));
        Assert.Equal(new uint[] { 7 }, carried);                    // placed, not walked
        Assert.Equal(new uint[] { 0x40000001, 0x40000002 }, items); // the unknown serial 99 is skipped
        Assert.Empty(s.Unhandled);
    }
}
