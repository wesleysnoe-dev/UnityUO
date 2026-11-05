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

    [Fact]
    public void MultiTargetCursorCarriesThePlacement()
    {
        var session = new GameSession(new LoginSettings());
        TargetRequest? asked = null;
        session.TargetRequested += t => asked = t;

        // ModernUO's SendMultiTargetReq for a 7.0.9+ client: allow ground, id, flags, 11 zero, multi, offsets, 0.
        byte[] packet = new PacketWriter(0x99, 30).Byte(1).UInt32(0x0000ABCD).Byte(0).Zero(11)
            .UInt16(0x0064).UInt16(0).UInt16(4).UInt16(0).UInt32(0).ToArray();
        Assert.Equal(PacketLengths.For(ClientVersion.Default)[0x99], packet.Length);
        session.Inject(packet, false);

        Assert.NotNull(asked);
        Assert.Same(asked, session.Target);
        Assert.True(asked!.IsMulti);
        Assert.True(asked.AllowGround);
        Assert.Equal(0xABCDu, asked.CursorId);
        Assert.Equal(0x64, asked.MultiGraphic);
        Assert.Equal((0, 4, 0), (asked.MultiOffsetX, asked.MultiOffsetY, asked.MultiOffsetZ));
        TargetRequest house = asked;

        // A boat deed's multi 0 and negative offsets.
        session.Inject(new PacketWriter(0x99, 30).Byte(1).UInt32(7).Byte(0).Zero(11)
            .UInt16(0).UInt16(unchecked((ushort)-1)).UInt16(7).UInt16(unchecked((ushort)-3)).UInt32(0).ToArray(), false);
        Assert.True(session.Target.IsMulti);
        Assert.Equal(0, session.Target.MultiGraphic);
        Assert.Equal((-1, 7, -3), (session.Target.MultiOffsetX, session.Target.MultiOffsetY, session.Target.MultiOffsetZ));

        // Answered with a 0x6C ground target, as ModernUO's TargetResponse reads it.
        byte[] reply = GameSession.GroundTargetReply(house, 1500, 1600, 5);
        Assert.Equal(new byte[] { 0x6C, 1, 0, 0, 0xAB, 0xCD, 0, 0, 0, 0, 0, 0x05, 0xDC, 0x06, 0x40, 0, 5, 0, 0 }, reply);
    }
}
