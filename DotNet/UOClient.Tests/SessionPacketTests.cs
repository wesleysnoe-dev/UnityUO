using UOClient.Core;
using UOClient.Net;

namespace UOClient.Tests;

/// <summary>Smaller world packets, injected as ModernUO sends them.</summary>
public sealed class SessionPacketTests
{
    private static GameSession InWorldAs(uint player, int x, int y)
    {
        var session = new GameSession(new LoginSettings());
        // 0x1B login confirm: serial, 0, body, x, y, z, direction, 9 unknown, map width, height (37 bytes).
        session.Inject(new PacketWriter(0x1B, 37).UInt32(player).UInt32(0).UInt16(0x190).UInt16(x).UInt16(y).UInt16(0)
            .Byte(0).Zero(9).UInt16(7168).UInt16(4096).Zero(6).ToArray(), false);
        return session;
    }

    [Fact]
    public void ServerChangeMovesThePlayerAndResizesTheMap()
    {
        GameSession session = InWorldAs(0x00000042, 1495, 1629);
        (int, int, sbyte)? changed = null;
        session.ServerChanged += (x, y, z) => changed = (x, y, z);
        Mobile? updated = null;
        session.World.MobileUpdated += m => updated = m;

        // ModernUO's SendServerChange: x, y, z, 0, 0, 0, map width, map height.
        byte[] packet = new PacketWriter(0x76, 16).UInt16(1200).UInt16(800).UInt16(unchecked((ushort)-5)).Byte(0)
            .UInt16(0).UInt16(0).UInt16(2304).UInt16(1600).ToArray();
        Assert.Equal(PacketLengths.For(ClientVersion.Default)[0x76], packet.Length);
        session.Inject(packet, false);

        Assert.Equal((1200, 800, (sbyte)-5), changed);
        Assert.Same(session.World.Player, updated);
        Assert.Equal(1200, session.World.Player.X);
        Assert.Equal(800, session.World.Player.Y);
        Assert.Equal(-5, session.World.Player.Z);
        Assert.Equal(2304, session.World.MapWidth);
        Assert.Equal(1600, session.World.MapHeight);
        Assert.Contains("1200,800,-5", session.Status);
    }
}
