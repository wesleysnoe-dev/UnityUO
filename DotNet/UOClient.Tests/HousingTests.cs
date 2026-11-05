using System.IO.Compression;
using UOClient.Core;
using UOClient.IO.Formats;
using UOClient.Net;

namespace UOClient.Tests;

public sealed class HousingTests
{
    // A 7x7 foundation in multi.mul (-3..3); ModernUO's design adds a row of steps, so it is 7 wide, 8 deep.
    private static readonly MultiComponent[] Foundation =
    {
        new MultiComponent { Graphic = 0x0001, X = -3, Y = -3 },
        new MultiComponent { Graphic = 0x0001, X = 3, Y = 3 }
    };

    private const int Width = 7, Height = 8;

    /// <summary>Builds 0xD8 the way ModernUO's HousePackets.CreateHouseDesignStateDetailed does.</summary>
    private static byte[] DesignPacket(uint serial, int revision, int count, params (int Id, byte[] Raw)[] planes)
    {
        var w = new PacketWriter(0xD8).Byte(3).Byte(0).UInt32(serial).Int32(revision).UInt16(count).UInt16(0).Byte(planes.Length);
        foreach (var (id, raw) in planes)
        {
            byte[] packed = Zlib(raw);
            w.Byte(id).Byte((byte)raw.Length).Byte((byte)packed.Length).Byte(((raw.Length >> 4) & 0xF0) | ((packed.Length >> 8) & 0x0F));
            foreach (byte b in packed)
                w.Byte(b);
        }
        return w.ToArray();
    }

    private static byte[] Zlib(byte[] raw)
    {
        using var ms = new MemoryStream();
        using (var z = new ZLibStream(ms, CompressionLevel.Optimal, true))
            z.Write(raw);
        return ms.ToArray();
    }

    private static void Put(byte[] grid, int index, ushort id)
    {
        grid[index * 2] = (byte)(id >> 8);
        grid[index * 2 + 1] = (byte)id;
    }

    [Fact]
    public void DecodesDesignPlanesAgainstTheFoundation()
    {
        var session = new GameSession(new LoginSettings());
        HouseDesign? changed = null;
        session.World.HouseDesignChanged += d => changed = d;

        // Ground plane: column-major over the full design, x = 1, y = 2 from the corner -> (-2, -1, 0).
        var ground = new byte[Width * Height * 2];
        Put(ground, 1 * Height + 2, 0x0031);
        // Floor on level 1 (z 7): grid inset by one, column length height - 2; x 0, y 0 -> (-2, -2, 7).
        var floor = new byte[(Width - 1) * (Height - 2) * 2];
        Put(floor, 0, 0x0519);
        // Wall on level 1: grid column length height - 1; x 2, y 1 -> (-1, -2, 7).
        var walls = new byte[Width * (Height - 1) * 2];
        Put(walls, 2 * (Height - 1) + 1, 0x0066);
        // Stairs list: id, x, y, z (signed offsets).
        byte[] stairs = { 0x07, 0x51, 0xFE, 0x04, 0x0A };

        session.Inject(DesignPacket(0x40001234, 7, 4, (0x20, ground), (0x21, floor), (0x25, walls), (0x09, stairs)), true);

        Assert.NotNull(changed);
        Assert.Equal(0x40001234u, changed!.Serial);
        Assert.Equal(7, changed.Revision);
        Assert.Same(changed, session.World.HouseDesigns[0x40001234]);

        List<MultiComponent> parts = changed.Components(Foundation);
        Assert.Equal(4, parts.Count);
        Assert.All(parts, p => Assert.True(p.Fixed));
        Assert.Contains(parts, p => p.Graphic == 0x0031 && p.X == -2 && p.Y == -1 && p.Z == 0);
        Assert.Contains(parts, p => p.Graphic == 0x0519 && p.X == -2 && p.Y == -2 && p.Z == 7);
        Assert.Contains(parts, p => p.Graphic == 0x0066 && p.X == -1 && p.Y == -2 && p.Z == 7);
        Assert.Contains(parts, p => p.Graphic == 0x0751 && p.X == -2 && p.Y == 4 && p.Z == 10);
    }

    [Fact]
    public void UpperPlanesUseTheirFloorHeights()
    {
        Assert.Equal(0, HouseDesign.PlaneZ(0));
        Assert.Equal(new[] { 7, 27, 47, 67 }, new[] { 1, 2, 3, 4 }.Select(HouseDesign.PlaneZ));
        Assert.Equal(new[] { 7, 27, 47, 67 }, new[] { 5, 6, 7, 8 }.Select(HouseDesign.PlaneZ));
    }

    [Fact]
    public void TracksRevisionsAndCustomizeMode()
    {
        var session = new GameSession(new LoginSettings());
        var modes = new List<(uint, bool)>();
        session.HouseCustomizationChanged += (s, b) => modes.Add((s, b));

        session.Inject(new PacketWriter(0xBF).UInt16(0x1D).UInt32(0x40000077).Int32(12).ToArray(), true);
        Assert.Equal(12, session.World.HouseRevisions[0x40000077]);

        // ModernUO's begin/end customization: serial, command, 0x0000, 0xFFFF, 0xFFFF, 0xFF.
        session.Inject(new PacketWriter(0xBF).UInt16(0x20).UInt32(0x40000077).Byte(4).UInt16(0).UInt16(0xFFFF).UInt16(0xFFFF).Byte(0xFF).ToArray(), true);
        Assert.Equal(0x40000077u, session.CustomizingHouse);
        session.Inject(new PacketWriter(0xBF).UInt16(0x20).UInt32(0x40000077).Byte(5).UInt16(0).UInt16(0xFFFF).UInt16(0xFFFF).Byte(0xFF).ToArray(), true);
        Assert.Equal(0u, session.CustomizingHouse);
        Assert.Equal(new[] { (0x40000077u, true), (0x40000077u, false) }, modes);
    }

    [Fact]
    public void DesignQueryIsExtendedCommand0x1E()
    {
        Assert.Equal(new byte[] { 0xBF, 0x00, 0x09, 0x00, 0x1E, 0x40, 0x00, 0x12, 0x34 }, GameSession.HouseDesignQuery(0x40001234));
        Assert.Equal(-1, PacketLengths.For(ClientVersion.Default)[0xD8]);
    }
}
