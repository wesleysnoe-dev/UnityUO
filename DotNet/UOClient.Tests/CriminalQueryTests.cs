using UOClient.Core;
using UOClient.Net;
using UOClient.Unity.UI;

namespace UOClient.Tests;

/// <summary>
/// TazUO's criminal-action query before an attack (GameActions.Attack, QuestionGump) from an offline session: notoriety
/// injected as ModernUO writes it (0x22, 0x77), what we send checked byte for byte. No server, no Unity.
/// </summary>
public sealed class CriminalQueryTests
{
    private const uint Me = 0x00000042, Orc = 0x00000077, Guard = 0x00000078;
    private static readonly byte[] AttackOrc = { 0x05, 0, 0, 0, 0x77 };

    // ModernUO's Notoriety (Notoriety.cs:7-13).
    private const byte Innocent = 1, Ally = 2, CanBeAttacked = 3, Criminal = 4, Enemy = 5, Murderer = 6, Invulnerable = 7;

    private static GameSession InWorld(List<byte[]> sent, byte myNotoriety)
    {
        var session = new GameSession(new LoginSettings { AnnounceChatLinks = false });
        session.StartOffline(sent.Add);
        session.Inject(new PacketWriter(0x1B, 37).UInt32(Me).UInt32(0).UInt16(0x190).UInt16(1495).UInt16(1629).UInt16(0)
            .Byte(0).Zero(9).UInt16(7168).UInt16(4096).Zero(6).ToArray(), false);
        session.Inject(new byte[] { 0x55 }, false);
        Assert.Equal(SessionState.InWorld, session.State);
        session.Inject(new byte[] { 0x22, 0, myNotoriety }, false); // ModernUO's MovementAck: sequence, our notoriety
        sent.Clear();
        return session;
    }

    // ModernUO's MobileMoving (0x77): serial, body, x, y, z, direction, hue, flags, notoriety.
    private static void Moving(GameSession session, uint serial, byte notoriety) =>
        session.Inject(new PacketWriter(0x77, 17).UInt32(serial).UInt16(0x190).UInt16(1496).UInt16(1629).Byte(0).Byte(2)
            .UInt16(0x83EA).Byte(0).Byte(notoriety).ToArray(), false);

    [Theory]
    [InlineData(Innocent)]
    [InlineData(Ally)]
    public void InnocentTargetAsksAndSendsNothingUntilYes(byte me)
    {
        var sent = new List<byte[]>();
        GameSession session = InWorld(sent, me);
        Moving(session, Orc, Innocent);
        var asked = new List<uint>();
        session.AttackQueryRequested += asked.Add;

        session.Attack(Orc);

        Assert.Equal(new[] { Orc }, asked);
        Assert.Equal(Orc, session.PendingAttackQuery);
        Assert.Empty(sent);
        Assert.Equal(0u, session.LastAttack);

        session.AnswerAttackQuery(Orc, true);

        Assert.Equal(AttackOrc, Assert.Single(sent));
        Assert.Equal(0u, session.PendingAttackQuery);
    }

    [Theory]
    [InlineData(CanBeAttacked)]
    [InlineData(Criminal)]
    [InlineData(Enemy)]
    [InlineData(Murderer)]
    [InlineData(Invulnerable)]
    public void NonInnocentTargetAttacksWithoutAsking(byte target)
    {
        var sent = new List<byte[]>();
        GameSession session = InWorld(sent, Innocent);
        Moving(session, Orc, target);
        var asked = new List<uint>();
        session.AttackQueryRequested += asked.Add;

        session.Attack(Orc);

        Assert.Empty(asked);
        Assert.Equal(AttackOrc, Assert.Single(sent));
        Assert.Equal(Orc, session.LastAttack);
    }

    [Theory]
    [InlineData(CanBeAttacked)]
    [InlineData(Criminal)]
    [InlineData(Murderer)]
    public void AlreadyFlaggedPlayerIsNotAsked(byte me)
    {
        var sent = new List<byte[]>();
        GameSession session = InWorld(sent, me);
        Moving(session, Orc, Innocent);
        var asked = new List<uint>();
        session.AttackQueryRequested += asked.Add;

        session.Attack(Orc);

        Assert.Empty(asked);
        Assert.Equal(AttackOrc, Assert.Single(sent));
    }

    [Fact]
    public void CancelSendsNothingAndTheNextAttackAsksAgain()
    {
        var sent = new List<byte[]>();
        GameSession session = InWorld(sent, Innocent);
        Moving(session, Orc, Innocent);
        var asked = new List<uint>();
        session.AttackQueryRequested += asked.Add;

        session.Attack(Orc);
        session.AnswerAttackQuery(Orc, false);

        Assert.Empty(sent);
        Assert.Equal(0u, session.PendingAttackQuery);
        Assert.Equal(0u, session.LastAttack);

        session.Attack(Orc);
        Assert.Equal(new[] { Orc, Orc }, asked);
        Assert.Empty(sent);
    }

    [Fact]
    public void SameSerialIsAskedOnceAndAnotherReplacesIt()
    {
        var sent = new List<byte[]>();
        GameSession session = InWorld(sent, Innocent);
        Moving(session, Orc, Innocent);
        Moving(session, Guard, Innocent);
        var asked = new List<uint>();
        session.AttackQueryRequested += asked.Add;

        session.Attack(Orc);
        session.Attack(Orc);
        Assert.Equal(new[] { Orc }, asked);

        session.Attack(Guard);
        Assert.Equal(new[] { Orc, Guard }, asked);
        Assert.Equal(Guard, session.PendingAttackQuery);

        // The replaced question's late yes is ignored; only the one now up sends.
        session.AnswerAttackQuery(Orc, true);
        Assert.Empty(sent);
        session.AnswerAttackQuery(Guard, true);
        Assert.Equal(new byte[] { 0x05, 0, 0, 0, 0x78 }, Assert.Single(sent));

        // Answered: a second yes sends nothing more.
        session.AnswerAttackQuery(Guard, true);
        Assert.Single(sent);
    }

    [Fact]
    public void WarModeDoubleClickOnAnInnocentOnlyAsksForStatus()
    {
        var sent = new List<byte[]>();
        GameSession session = InWorld(sent, Innocent);
        Moving(session, Orc, Innocent);
        session.Inject(new byte[] { 0x72, 1, 0, 0x32, 0 }, false);
        Assert.True(session.WarMode);
        var asked = new List<uint>();
        session.AttackQueryRequested += asked.Add;

        session.DoubleClick(Orc);

        Assert.Equal(new[] { Orc }, asked);
        Assert.Equal(0x34, Assert.Single(sent)[0]); // GameActions.DoubleClick's RequestMobileStatus, before the query
        Assert.DoesNotContain(sent, p => p[0] == 0x05);

        session.AnswerAttackQuery(Orc, true);
        Assert.Equal(AttackOrc, sent[^1]);
    }

    [Fact]
    public void QueryOffOrUnknownMobileAttacksStraightAway()
    {
        var sent = new List<byte[]>();
        GameSession session = InWorld(sent, Innocent);
        Moving(session, Orc, Innocent);
        var asked = new List<uint>();
        session.AttackQueryRequested += asked.Add;

        session.CriminalActionQuery = false;
        session.Attack(Orc);
        Assert.Equal(AttackOrc, Assert.Single(sent));

        // TazUO asks only about a mobile it knows (world.Mobiles.Get(serial) != null).
        session.CriminalActionQuery = true;
        sent.Clear();
        session.Attack(0x99);
        Assert.Equal(new byte[] { 0x05, 0, 0, 0, 0x99 }, Assert.Single(sent));
        Assert.Empty(asked);
    }

    [Fact]
    public void QueryIsOnByDefault() => Assert.True(new GameSession(new LoginSettings()).CriminalActionQuery);

    [Fact]
    public void QuestionLayoutMatchesTazUOsQuestionGump()
    {
        Assert.Equal(0x0816, QuestionWindow.Background);
        Assert.Equal(0x0386, QuestionWindow.LabelHue);
        Assert.Equal((33, 30, 165), (QuestionWindow.LabelX, QuestionWindow.LabelY, QuestionWindow.LabelWidth));
        Assert.Equal((0x817, 0x818, 0x819), (QuestionWindow.CancelNormal, QuestionWindow.CancelPressed, QuestionWindow.CancelOver));
        Assert.Equal((0x81A, 0x81B, 0x81C), (QuestionWindow.OkNormal, QuestionWindow.OkPressed, QuestionWindow.OkOver));
        Assert.Equal((37, 100, 75), (QuestionWindow.CancelX, QuestionWindow.OkX, QuestionWindow.ButtonY));
        Assert.Equal("This may flag\nyou criminal!", QuestionWindow.CriminalText);
        Assert.Equal((300, 245), QuestionWindow.Centered(800, 600, 200, 110));
    }
}
