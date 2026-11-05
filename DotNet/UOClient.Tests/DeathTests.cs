using UOClient.Core;
using UOClient.Net;

namespace UOClient.Tests;

/// <summary>Death and the ghost state, injected as ModernUO's <c>Mobile.OnDeath</c> and <c>Resurrect</c> send them.</summary>
public sealed class DeathTests
{
    private const uint Player = 0x00000042;

    private static GameSession InWorld(ushort body = 0x190)
    {
        var session = new GameSession(new LoginSettings());
        session.Inject(new PacketWriter(0x1B, 37).UInt32(Player).UInt32(0).UInt16(body).UInt16(1495).UInt16(1629).UInt16(0)
            .Byte(0).Zero(9).UInt16(7168).UInt16(4096).Zero(6).ToArray(), false);
        return session;
    }

    // 0x20 as ModernUO's SendMobileUpdate writes it: serial, body, 0, hue, flags, x, y, 0, direction, z.
    private static byte[] PlayerUpdate(uint serial, ushort body) =>
        new PacketWriter(0x20, 19).UInt32(serial).UInt16(body).Byte(0).UInt16(0).Byte(0)
            .UInt16(1495).UInt16(1629).UInt16(0).Byte(0).Byte(0).ToArray();

    [Fact]
    public void DeathStatusStartsTheDeathScreen()
    {
        GameSession session = InWorld();
        int died = 0;
        session.PlayerDied += () => died++;

        byte[] packet = { 0x2C, 2 }; // ModernUO's SendDeathStatus
        Assert.Equal(PacketLengths.For(ClientVersion.Default)[0x2C], packet.Length);
        session.Inject(packet, false);

        Assert.Equal(1, died);
    }

    [Theory]
    [InlineData((ushort)402)]
    [InlineData((ushort)403)]
    [InlineData((ushort)607)]
    [InlineData((ushort)608)]
    [InlineData((ushort)694)]
    [InlineData((ushort)695)]
    public void GhostBodyMakesThePlayerDeadAndTheLivingBodyResurrects(ushort ghost)
    {
        GameSession session = InWorld();
        var changes = new List<bool>();
        session.PlayerDeadChanged += changes.Add;
        Assert.False(session.PlayerDead);

        session.Inject(PlayerUpdate(Player, ghost), false);
        Assert.True(session.PlayerDead);
        Assert.True(session.World.Player.IsDead);

        session.Inject(PlayerUpdate(Player, ghost), false); // a repeat is no change
        session.Inject(PlayerUpdate(Player, 0x190), false);
        Assert.False(session.PlayerDead);
        Assert.Equal(new[] { true, false }, changes);
    }

    [Fact]
    public void OtherGhostsDoNotMakeThePlayerDead()
    {
        GameSession session = InWorld();
        bool changed = false;
        session.PlayerDeadChanged += _ => changed = true;

        // 0x77 for another mobile: serial, body, x, y, z, direction, hue, flags, notoriety.
        session.Inject(new PacketWriter(0x77, 17).UInt32(0x00000099).UInt16(402).UInt16(1496).UInt16(1629).Byte(0)
            .Byte(0).UInt16(0).Byte(0).Byte(1).ToArray(), false);

        Assert.True(session.World.Mobiles[0x00000099].IsDead);
        Assert.False(session.PlayerDead);
        Assert.False(changed);
    }

    [Fact]
    public void LoggingInAsAGhostIsDead()
    {
        GameSession session = InWorld(403);
        Assert.True(session.PlayerDead);
    }
}
