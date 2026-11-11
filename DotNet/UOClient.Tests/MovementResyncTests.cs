using System.Collections.Generic;
using UOClient.Core;
using UOClient.Net;
using Xunit;

namespace UOClient.Tests;

/// <summary>Steps in flight when the server resets movement: its late answers to them must not undo the next step.</summary>
public sealed class MovementResyncTests
{
    private const uint Me = 0x00000042;

    private static GameSession InWorld(List<byte[]> sent)
    {
        var session = new GameSession(new LoginSettings { AnnounceChatLinks = false });
        session.StartOffline(sent.Add);
        session.Inject(new PacketWriter(0x1B, 37).UInt32(Me).UInt32(0).UInt16(0x190).UInt16(1495).UInt16(1629).UInt16(0)
            .Byte(0).Zero(9).UInt16(7168).UInt16(4096).Zero(6).ToArray(), false);
        session.Inject(new byte[] { 0x55 }, false);
        session.Update(1.0);
        sent.Clear();
        return session;
    }

    private static byte[] Reject(int seq, int x, int y) =>
        new PacketWriter(0x21, 8).Byte(seq).UInt16(x).UInt16(y).Byte(0).Byte(0).ToArray();

    private static byte[] PlayerUpdate(int x, int y) =>
        new PacketWriter(0x20, 19).UInt32(Me).UInt16(0x190).Byte(0).UInt16(0).Byte(0).UInt16(x).UInt16(y).UInt16(0).Byte(0).Byte(0).ToArray();

    [Fact]
    public void A_late_reject_for_a_step_from_before_the_resync_is_ignored()
    {
        var sent = new List<byte[]>();
        GameSession session = InWorld(sent);
        Assert.True(session.RequestStep(0, false)); // seq 0
        Assert.True(session.RequestStep(0, false)); // seq 1
        Assert.True(session.RequestStep(0, false)); // seq 2

        session.Inject(PlayerUpdate(2000, 2000), false); // the server teleports us: movement starts over
        Assert.Equal(0, session.PendingSteps);
        Assert.True(session.RequestStep(2, false)); // the new step, numbered 0 again
        session.World.Player.X = 2001;              // the caller's prediction

        int rejected = 0;
        session.MoveRejected += (_, _, _, _) => rejected++;
        session.Inject(Reject(1, 2000, 2000), false); // the server's answers to the old steps
        session.Inject(Reject(2, 2000, 2000), false);

        Assert.Equal(0, rejected);
        Assert.Equal(1, session.PendingSteps);
        Assert.Equal(2001, session.World.Player.X);

        session.Inject(new byte[] { 0x22, 0, 1 }, false); // and its yes to the new one
        Assert.Equal(0, session.PendingSteps);
    }

    [Fact]
    public void A_reject_for_a_step_in_flight_still_pulls_the_player_back()
    {
        var sent = new List<byte[]>();
        GameSession session = InWorld(sent);
        Assert.True(session.RequestStep(0, false));
        Assert.True(session.RequestStep(0, false));
        int rejected = 0;
        session.MoveRejected += (_, _, _, _) => rejected++;
        session.Inject(Reject(0, 1495, 1629), false);
        Assert.Equal(1, rejected);
        Assert.Equal(0, session.PendingSteps);
        Assert.Equal(1495, session.World.Player.X);
        // Nothing in flight now: the server's reject of the second old step is harmless and still applies its position.
        session.Inject(Reject(1, 1495, 1629), false);
        Assert.Equal(2, rejected);
    }

    [Fact]
    public void An_ack_answers_the_step_it_names()
    {
        var sent = new List<byte[]>();
        GameSession session = InWorld(sent);
        session.RequestStep(0, false);
        session.RequestStep(0, false);
        session.Inject(new byte[] { 0x22, 9, 1 }, false); // not one of ours
        Assert.Equal(2, session.PendingSteps);
        session.Inject(new byte[] { 0x22, 0, 1 }, false);
        Assert.Equal(1, session.PendingSteps);
    }
}
