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

    [Fact]
    public void TextEntryDialogIsReadAndAnswered()
    {
        var session = new GameSession(new LoginSettings());
        TextEntryRequest? entry = null;
        session.TextEntryRequested += e => entry = e;

        // serial, parent, button, caption (length incl. terminator), cancel, style 2 (number), max, description.
        session.Inject(new PacketWriter(0xAB).UInt32(0x01020304).Byte(5).Byte(9)
            .UInt16(12).AsciiZ("How many?!!").Bool(true).Byte(2).UInt32(500)
            .UInt16(9).AsciiZ("1 to 500").ToArray(), true);
        Assert.Equal(-1, PacketLengths.For(ClientVersion.Default)[0xAB]);

        Assert.NotNull(entry);
        Assert.Equal(0x01020304u, entry!.Serial);
        Assert.Equal((5, 9), (entry.ParentId, entry.ButtonId));
        Assert.Equal("How many?!!", entry.Text);
        Assert.True(entry.CanCancel);
        Assert.True(entry.Numeric);
        Assert.Equal(500, entry.Max);
        Assert.Equal("1 to 500", entry.Description);

        // 0xAC: serial, parent, button, 1 = OK, length incl. terminator, text.
        Assert.Equal(new byte[] { 0xAC, 0, 16, 1, 2, 3, 4, 5, 9, 1, 0, 4, (byte)'4', (byte)'2', (byte)'0', 0 },
            GameSession.TextEntryReply(entry, "420"));
        // Cancel: code 0, empty text.
        Assert.Equal(new byte[] { 0xAC, 0, 13, 1, 2, 3, 4, 5, 9, 0, 0, 1, 0 }, GameSession.TextEntryReply(entry, null));
    }

    [Fact]
    public void QuestArrowShowsAndClearsOnHighSeasClients()
    {
        GameSession session = InWorldAs(0x00000042, 1495, 1629);
        var changes = new List<QuestArrow?>();
        session.QuestArrowChanged += a => changes.Add(a);

        // ModernUO's SendSetArrow for a High Seas client: 1, x, y, tracked serial (10 bytes).
        byte[] set = new PacketWriter(0xBA, 10).Byte(1).UInt16(1520).UInt16(1650).UInt32(0x00001234).ToArray();
        Assert.Equal(PacketLengths.For(ClientVersion.Default)[0xBA], set.Length);
        session.Inject(set, false);

        Assert.NotNull(session.QuestArrow);
        Assert.Equal((1520, 1650, 0x1234u), (session.QuestArrow!.X, session.QuestArrow.Y, session.QuestArrow.Serial));
        Assert.Single(changes);
        Assert.Same(session.QuestArrow, changes[0]);

        // SendCancelArrow on High Seas still carries the last location and serial.
        session.Inject(new PacketWriter(0xBA, 10).Byte(0).UInt16(1520).UInt16(1650).UInt32(0x00001234).ToArray(), false);
        Assert.Null(session.QuestArrow);
        Assert.Equal(2, changes.Count);
        Assert.Null(changes[1]);
    }

    [Fact]
    public void QuestArrowShowsAndClearsOnOlderClients()
    {
        GameSession session = InWorldAs(0x00000042, 1495, 1629);
        var old = new ClientVersion(7, 0, 8, 0);

        // Before 7.0.9 the packet is 6 bytes: 1, x, y to set; 0, -1, -1 to cancel.
        byte[] set = new PacketWriter(0xBA, 6).Byte(1).UInt16(300).UInt16(400).ToArray();
        Assert.Equal(PacketLengths.For(old)[0xBA], set.Length);
        session.Inject(set, false);
        Assert.Equal((300, 400, 0u), (session.QuestArrow!.X, session.QuestArrow.Y, session.QuestArrow.Serial));

        session.Inject(new PacketWriter(0xBA, 6).Byte(0).UInt16(unchecked((ushort)-1)).UInt16(unchecked((ushort)-1)).ToArray(), false);
        Assert.Null(session.QuestArrow);
    }
}
