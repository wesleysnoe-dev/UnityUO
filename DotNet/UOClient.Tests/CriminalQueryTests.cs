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

    // ---- target cursor (TazUO TargetManager.Target, TargetManager.cs:379-424) ----

    private const byte Neutral = 0, Harmful = 1, Beneficial = 2;
    private const uint Cursor = 0x0000ABCD;

    // ModernUO's TargetReq (0x6C): allow ground, cursor id, flags, then zeros to 19 bytes.
    private static void Cursor6C(GameSession session, byte flags, uint id = Cursor) =>
        session.Inject(new PacketWriter(0x6C, 19).Byte(0).UInt32(id).Byte(flags).Zero(12).ToArray(), false);

    // Our 0x6C object answer: type 0, cursor id, flags, serial, then x / y / z / graphic left 0.
    private static byte[] ObjectReply(byte flags, uint serial, uint id = Cursor) =>
        new PacketWriter(0x6C, 19).Byte(0).UInt32(id).Byte(flags).UInt32(serial).Zero(8).ToArray();

    private static byte[] CancelReply(byte flags, uint id = Cursor) =>
        new PacketWriter(0x6C, 19).Byte(0).UInt32(id).Byte(flags).UInt32(0).UInt16(0xFFFF).UInt16(0xFFFF).Zero(4).ToArray();

    [Theory]
    [InlineData(Innocent)]
    [InlineData(Ally)]
    public void HarmfulCursorOnAnInnocentHoldsTheTargetUntilYes(byte me)
    {
        var sent = new List<byte[]>();
        GameSession session = InWorld(sent, me);
        Moving(session, Orc, Innocent);
        Cursor6C(session, Harmful);
        var asked = new List<GameSession.TargetQuery>();
        session.TargetQueryRequested += asked.Add;
        int ended = 0;
        session.TargetEnded += () => ended++;

        session.TargetObject(Orc);

        GameSession.TargetQuery q = Assert.Single(asked);
        Assert.Equal(Orc, q.Serial);
        Assert.Same(q, session.PendingTargetQuery);
        Assert.Empty(sent);
        Assert.NotNull(session.Target); // the cursor stays up while the question is
        Assert.Equal(0, ended);

        session.AnswerTargetQuery(q, true);

        Assert.Equal(ObjectReply(Harmful, Orc), Assert.Single(sent));
        Assert.Null(session.Target);
        Assert.Null(session.PendingTargetQuery);
        Assert.Equal(1, ended);
        Assert.Equal(Orc, session.LastTarget);

        // Answered: a second yes sends nothing more.
        session.AnswerTargetQuery(q, true);
        Assert.Single(sent);
    }

    [Theory]
    [InlineData(Criminal)]
    [InlineData(Murderer)]
    [InlineData(CanBeAttacked)] // ClassicUO's Gray
    public void BeneficialCursorOnACriminalAsksWhenTheBeneficialQueryIsOn(byte target)
    {
        var sent = new List<byte[]>();
        GameSession session = InWorld(sent, Innocent);
        session.BeneficialCriminalActionQuery = true;
        Moving(session, Orc, target);
        Cursor6C(session, Beneficial);
        var asked = new List<GameSession.TargetQuery>();
        session.TargetQueryRequested += asked.Add;

        session.TargetObject(Orc);

        Assert.Single(asked);
        Assert.Empty(sent);
        session.AnswerTargetQuery(asked[0], true);
        Assert.Equal(ObjectReply(Beneficial, Orc), Assert.Single(sent));
    }

    [Fact]
    public void BeneficialQueryIsOffByDefaultAndThenTargetsStraightAway()
    {
        var sent = new List<byte[]>();
        GameSession session = InWorld(sent, Innocent);
        Assert.False(session.BeneficialCriminalActionQuery);
        Moving(session, Orc, Criminal);
        Cursor6C(session, Beneficial);
        var asked = new List<GameSession.TargetQuery>();
        session.TargetQueryRequested += asked.Add;

        session.TargetObject(Orc);

        Assert.Empty(asked);
        Assert.Equal(ObjectReply(Beneficial, Orc), Assert.Single(sent));
        Assert.Null(session.Target);
    }

    [Theory]
    [InlineData(Neutral, Innocent, true, true)]       // a neutral cursor never asks
    [InlineData(Harmful, Criminal, true, true)]       // harmful asks only about innocents
    [InlineData(Harmful, Innocent, false, true)]      // criminal query off
    [InlineData(Beneficial, Innocent, true, true)]    // beneficial asks only about criminals, murderers, grays
    [InlineData(Beneficial, Enemy, true, true)]
    public void OtherCursorsAndNotorietiesTargetWithoutAsking(byte flags, byte target, bool harmfulQuery, bool beneficialQuery)
    {
        var sent = new List<byte[]>();
        GameSession session = InWorld(sent, Innocent);
        session.CriminalActionQuery = harmfulQuery;
        session.BeneficialCriminalActionQuery = beneficialQuery;
        Moving(session, Orc, target);
        Cursor6C(session, flags);
        var asked = new List<GameSession.TargetQuery>();
        session.TargetQueryRequested += asked.Add;

        session.TargetObject(Orc);

        Assert.Empty(asked);
        Assert.Equal(ObjectReply(flags, Orc), Assert.Single(sent));
    }

    [Theory]
    [InlineData(Criminal)]
    [InlineData(Murderer)]
    [InlineData(CanBeAttacked)]
    public void FlaggedPlayerTargetsWithoutAsking(byte me)
    {
        var sent = new List<byte[]>();
        GameSession session = InWorld(sent, me);
        session.BeneficialCriminalActionQuery = true;
        Moving(session, Orc, Innocent);
        Cursor6C(session, Harmful);
        var asked = new List<GameSession.TargetQuery>();
        session.TargetQueryRequested += asked.Add;

        session.TargetObject(Orc);

        Assert.Empty(asked);
        Assert.Equal(ObjectReply(Harmful, Orc), Assert.Single(sent));
    }

    [Fact]
    public void SelfAndUnknownMobilesTargetWithoutAsking()
    {
        var sent = new List<byte[]>();
        GameSession session = InWorld(sent, Innocent);
        var asked = new List<GameSession.TargetQuery>();
        session.TargetQueryRequested += asked.Add;

        Cursor6C(session, Harmful);
        session.TargetSelf();
        Assert.Equal(ObjectReply(Harmful, Me), Assert.Single(sent));

        Cursor6C(session, Harmful);
        session.TargetObject(0x99); // TazUO asks only about a mobile it knows
        Assert.Equal(ObjectReply(Harmful, 0x99), sent[^1]);
        Assert.Empty(asked);
    }

    [Fact]
    public void NoSendsNothingAndLeavesTheCursorUp()
    {
        var sent = new List<byte[]>();
        GameSession session = InWorld(sent, Innocent);
        Moving(session, Orc, Innocent);
        Cursor6C(session, Harmful);
        var asked = new List<GameSession.TargetQuery>();
        session.TargetQueryRequested += asked.Add;

        session.TargetObject(Orc);
        session.AnswerTargetQuery(asked[0], false);

        Assert.Empty(sent);
        Assert.Null(session.PendingTargetQuery);
        Assert.NotNull(session.Target);

        // A late yes on the answered question does nothing; clicking again asks again.
        session.AnswerTargetQuery(asked[0], true);
        Assert.Empty(sent);
        session.TargetObject(Orc);
        Assert.Equal(2, asked.Count);

        // Esc cancels the cursor as usual: only the cancel goes out, and the question's yes is then void.
        session.CancelTarget();
        Assert.Equal(CancelReply(Harmful), Assert.Single(sent));
        Assert.Null(session.PendingTargetQuery);
        session.AnswerTargetQuery(asked[1], true);
        Assert.Single(sent);
    }

    [Fact]
    public void ServerCancellingTheCursorVoidsTheQuestion()
    {
        var sent = new List<byte[]>();
        GameSession session = InWorld(sent, Innocent);
        Moving(session, Orc, Innocent);
        Cursor6C(session, Harmful);
        var asked = new List<GameSession.TargetQuery>();
        session.TargetQueryRequested += asked.Add;

        session.TargetObject(Orc);
        Cursor6C(session, 3); // ModernUO's CancelTarget: flags 3
        Assert.Null(session.Target);
        Assert.Null(session.PendingTargetQuery);

        session.AnswerTargetQuery(asked[0], true);
        Assert.Empty(sent);
    }

    [Fact]
    public void ANewCursorVoidsTheQuestionAndItsYesSendsNothing()
    {
        var sent = new List<byte[]>();
        GameSession session = InWorld(sent, Innocent);
        Moving(session, Orc, Innocent);
        Cursor6C(session, Harmful);
        var asked = new List<GameSession.TargetQuery>();
        session.TargetQueryRequested += asked.Add;

        session.TargetObject(Orc);
        Cursor6C(session, Harmful, 0x0000BEEF);
        Assert.Null(session.PendingTargetQuery);

        // The old question's yes must not answer the new cursor.
        session.AnswerTargetQuery(asked[0], true);
        Assert.Empty(sent);
        Assert.Equal(0xBEEFu, session.Target.CursorId);

        // The same mobile on the new cursor asks afresh, and the old question's late no can't void it.
        session.TargetObject(Orc);
        Assert.Equal(2, asked.Count);
        session.AnswerTargetQuery(asked[0], false);
        Assert.Same(asked[1], session.PendingTargetQuery);
        session.AnswerTargetQuery(asked[1], true);
        Assert.Equal(ObjectReply(Harmful, Orc, 0x0000BEEF), Assert.Single(sent));
    }

    [Fact]
    public void SameMobileIsAskedOnceAndAnotherReplacesIt()
    {
        var sent = new List<byte[]>();
        GameSession session = InWorld(sent, Innocent);
        Moving(session, Orc, Innocent);
        Moving(session, Guard, Innocent);
        Cursor6C(session, Harmful);
        var asked = new List<GameSession.TargetQuery>();
        session.TargetQueryRequested += asked.Add;

        session.TargetObject(Orc);
        session.TargetObject(Orc);
        Assert.Single(asked);

        session.TargetObject(Guard);
        Assert.Equal(2, asked.Count);
        Assert.Same(asked[1], session.PendingTargetQuery);

        session.AnswerTargetQuery(asked[0], true);
        Assert.Empty(sent);
        session.AnswerTargetQuery(asked[1], true);
        Assert.Equal(ObjectReply(Harmful, Guard), Assert.Single(sent));
    }

    [Fact]
    public void TargetQueryTestFollowsTazUO()
    {
        Assert.True(GameSession.QueriesTarget(Harmful, Innocent, Innocent, true, false));
        Assert.True(GameSession.QueriesTarget(Harmful, Ally, Innocent, true, false));
        Assert.False(GameSession.QueriesTarget(Harmful, Innocent, Innocent, false, true));
        Assert.False(GameSession.QueriesTarget(Harmful, Innocent, Criminal, true, true));
        Assert.True(GameSession.QueriesTarget(Beneficial, Innocent, Criminal, false, true));
        Assert.True(GameSession.QueriesTarget(Beneficial, Ally, Murderer, false, true));
        Assert.True(GameSession.QueriesTarget(Beneficial, Innocent, CanBeAttacked, false, true));
        Assert.False(GameSession.QueriesTarget(Beneficial, Innocent, Criminal, true, false));
        Assert.False(GameSession.QueriesTarget(Beneficial, Criminal, Criminal, true, true));
        Assert.False(GameSession.QueriesTarget(Beneficial, Innocent, Invulnerable, true, true));
        Assert.False(GameSession.QueriesTarget(Neutral, Innocent, Innocent, true, true));
    }

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
