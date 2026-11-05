using UOClient.Core;
using UOClient.Net;
using UOClient.Unity.UI;

namespace UOClient.Tests;

/// <summary>
/// Party style health bars, quick heal / cure and the heal target, and renaming from a bar (TazUO's HealthBarGump,
/// GameActions.QuickHeal / QuickCure / Rename, TargetCursor.cs) from an offline session: packets injected as ModernUO
/// writes them, what we send checked byte for byte against ModernUO's readers. No server, no Unity.
/// </summary>
public sealed class PartyHealthBarTests
{
    private const uint Me = 0x00000042, Friend = 0x00000077, Pet = 0x00000078, Stranger = 0x00000079;
    private const uint Cursor = 0x01020304;

    // ModernUO's Notoriety (Notoriety.cs:7-13).
    private const byte Innocent = 1, Criminal = 4;

    private static GameSession InWorld(List<byte[]> sent)
    {
        var session = new GameSession(new LoginSettings { AnnounceChatLinks = false });
        session.StartOffline(sent.Add);
        session.Inject(new PacketWriter(0x1B, 37).UInt32(Me).UInt32(0).UInt16(0x190).UInt16(1495).UInt16(1629).UInt16(0)
            .Byte(0).Zero(9).UInt16(7168).UInt16(4096).Zero(6).ToArray(), false);
        session.Inject(new byte[] { 0x55 }, false);
        Assert.Equal(SessionState.InWorld, session.State);
        session.Inject(new byte[] { 0x22, 0, Innocent }, false); // ModernUO's MovementAck: sequence, our notoriety
        session.Update(1.0); // the first update pings; get it out of the way
        sent.Clear();
        return session;
    }

    // ModernUO's MobileMoving (0x77): serial, body, x, y, z, direction, hue, flags, notoriety.
    private static void Moving(GameSession session, uint serial, byte notoriety) =>
        session.Inject(new PacketWriter(0x77, 17).UInt32(serial).UInt16(0x190).UInt16(1496).UInt16(1629).Byte(0).Byte(2)
            .UInt16(0x83EA).Byte(0).Byte(notoriety).ToArray(), false);

    // ModernUO's CreateMobileStatus, version 0 (another's): serial, name (30), hits, max, can rename, version.
    private static void Status(GameSession session, uint serial, string name, bool renamable) =>
        session.Inject(new PacketWriter(0x11).UInt32(serial).Ascii(name, 30).UInt16(20).UInt16(25).Bool(renamable).Byte(0)
            .ToArray(), true);

    // ModernUO's PartyMemberList (0xBF 0x06 0x01): count, serials.
    private static void PartyOf(GameSession session, params uint[] members)
    {
        var w = new PacketWriter(0xBF).UInt16(0x06).Byte(0x01).Byte(members.Length);
        foreach (uint m in members)
            w.UInt32(m);
        session.Inject(w.ToArray(), true);
    }

    // ModernUO's TargetReq (0x6C): type, cursor id, flags, 12 zero bytes.
    private static void TargetCursor(GameSession session, byte flags) =>
        session.Inject(new PacketWriter(0x6C, 19).Byte(0).UInt32(Cursor).Byte(flags).Zero(12).ToArray(), false);

    private static byte[] TargetAnswer(uint serial, byte flags) =>
        new PacketWriter(0x6C, 19).Byte(0).UInt32(Cursor).Byte(flags).UInt32(serial).UInt16(0).UInt16(0).Byte(0).Byte(0)
            .UInt16(0).ToArray();

    // GameActions.CastSpell as we send it: 0x12 type 0x56, the spell number (ModernUO's TextCommand CastSpell).
    private static byte[] Cast(int spell) => new PacketWriter(0x12).Byte(0x56).AsciiZ(spell.ToString()).ToArray();

    private static List<byte[]> NoPings(List<byte[]> sent) => sent.Where(p => p[0] != 0x73).ToList();

    // ---- quick heal / cure and the heal target ----

    [Fact]
    public void QuickHealCastsAndTheNextCursorTargetsThatMember()
    {
        var sent = new List<byte[]>();
        GameSession session = InWorld(sent);
        Moving(session, Friend, Innocent);
        sent.Clear();

        session.Update(10.0);
        session.QuickHeal(Friend);
        Assert.Equal(Cast(29), Assert.Single(NoPings(sent)));
        Assert.Equal(Friend, session.PartyHealTarget);

        sent.Clear();
        session.Update(10.3);
        TargetCursor(session, 2); // Greater Heal's beneficial cursor

        Assert.Equal(TargetAnswer(Friend, 2), Assert.Single(NoPings(sent)));
        Assert.Null(session.Target);
        Assert.Equal(0u, session.PartyHealTarget);
    }

    [Fact]
    public void QuickCureCastsSpellElevenAndTargetsOurselves()
    {
        var sent = new List<byte[]>();
        GameSession session = InWorld(sent);

        session.Update(5.0);
        session.QuickCure(Me);
        Assert.Equal(Cast(11), Assert.Single(NoPings(sent)));

        sent.Clear();
        session.Update(5.1);
        TargetCursor(session, 2);

        Assert.Equal(TargetAnswer(Me, 2), Assert.Single(NoPings(sent)));
    }

    [Fact]
    public void ACursorBeforeTheDelayIsLeftUpAndTheHealTargetWaitsForTheNext()
    {
        var sent = new List<byte[]>();
        GameSession session = InWorld(sent);
        Moving(session, Friend, Innocent);
        session.Update(10.0);
        session.QuickHeal(Friend);
        sent.Clear();

        TargetCursor(session, 2); // same frame: TazUO's PartyHealTimer (50 ms) has not passed
        Assert.Empty(NoPings(sent));
        Assert.NotNull(session.Target);
        Assert.Equal(Friend, session.PartyHealTarget);

        session.Update(10.2);
        TargetCursor(session, 2);
        Assert.Equal(TargetAnswer(Friend, 2), Assert.Single(NoPings(sent)));
    }

    [Fact]
    public void ACancelledCursorForgetsTheHealTargetAndSendsNothing()
    {
        var sent = new List<byte[]>();
        GameSession session = InWorld(sent);
        Moving(session, Friend, Innocent);
        session.Update(10.0);
        session.QuickHeal(Friend);
        sent.Clear();

        session.Update(10.2);
        TargetCursor(session, 3);

        Assert.Empty(NoPings(sent));
        Assert.Equal(0u, session.PartyHealTarget);
        TargetCursor(session, 2); // a later, unrelated cursor is the player's
        Assert.Empty(NoPings(sent));
        Assert.NotNull(session.Target);
    }

    [Fact]
    public void AHealTargetOutOfViewLeavesTheCursorToThePlayer()
    {
        var sent = new List<byte[]>();
        GameSession session = InWorld(sent);
        session.Update(10.0);
        session.QuickHeal(Stranger); // never seen
        sent.Clear();

        session.Update(10.2);
        TargetCursor(session, 2);

        Assert.Empty(NoPings(sent));
        Assert.NotNull(session.Target);
        Assert.Equal(0u, session.PartyHealTarget);
    }

    [Fact]
    public void TheHealTargetGoesThroughTheCriminalQuestion()
    {
        var sent = new List<byte[]>();
        GameSession session = InWorld(sent);
        session.BeneficialCriminalActionQuery = true;
        Moving(session, Friend, Criminal);
        var asked = new List<GameSession.TargetQuery>();
        session.TargetQueryRequested += asked.Add;
        session.Update(10.0);
        session.QuickHeal(Friend);
        sent.Clear();

        session.Update(10.2);
        TargetCursor(session, 2);

        GameSession.TargetQuery q = Assert.Single(asked);
        Assert.Equal(Friend, q.Serial);
        Assert.Empty(NoPings(sent));
        Assert.NotNull(session.Target);

        session.AnswerTargetQuery(q, true);
        Assert.Equal(TargetAnswer(Friend, 2), Assert.Single(NoPings(sent)));
    }

    [Fact]
    public void AHarmfulCursorOnAnInnocentHealTargetAsksFirst()
    {
        var sent = new List<byte[]>();
        GameSession session = InWorld(sent);
        Moving(session, Friend, Innocent);
        var asked = new List<GameSession.TargetQuery>();
        session.TargetQueryRequested += asked.Add;
        session.Update(10.0);
        session.QuickHeal(Friend); // a QuickHealSpell set to a harmful spell
        sent.Clear();

        session.Update(10.2);
        TargetCursor(session, 1);

        Assert.Single(asked);
        Assert.Empty(NoPings(sent));
    }

    [Fact]
    public void NothingIsCastOutOfWorld()
    {
        var sent = new List<byte[]>();
        var session = new GameSession(new LoginSettings { AnnounceChatLinks = false });
        session.StartOffline(sent.Add);
        session.QuickHeal(Friend);
        Assert.Empty(sent);
        Assert.Equal(0u, session.PartyHealTarget);
    }

    // ---- party style ----

    [Fact]
    public void PartyMembersUseThePartyStyleWhileItIsOn()
    {
        var sent = new List<byte[]>();
        GameSession session = InWorld(sent);
        Assert.False(session.ShowsPartyHealthBar(Friend));

        PartyOf(session, Me, Friend);
        Assert.True(session.ShowsPartyHealthBar(Friend));
        Assert.True(session.ShowsPartyHealthBar(Me));
        Assert.False(session.ShowsPartyHealthBar(Stranger));

        session.UsePartyHealthBars = false;
        Assert.False(session.ShowsPartyHealthBar(Friend));
        session.UsePartyHealthBars = true;

        PartyOf(session); // disbanded
        Assert.False(session.ShowsPartyHealthBar(Friend));
    }

    [Fact]
    public void PartyLayoutMatchesTazUO()
    {
        // HealthBarGump.cs:1700-1794, Settings :2306-2307.
        Assert.Equal((115, 55), (HealthBarWindow.PartyWidth, HealthBarWindow.PartyHeight));
        Assert.Equal((0x0028, 0x0029), (HealthBarWindow.LineRedParty, HealthBarWindow.LineBlueParty));
        Assert.Equal((0x0938, 0x0939, 0x093A), (HealthBarWindow.HealButton, HealthBarWindow.CureButton, HealthBarWindow.HealCurePressed));
        Assert.Equal((20, 33), (HealthBarWindow.HealButtonY, HealthBarWindow.CureButtonY));
        Assert.Equal(new[] { 20, 33, 45 }, HealthBarWindow.PartyLineY);
        Assert.Equal((18, 96), (HealthBarWindow.PartyLineX, HealthBarWindow.PartyBarWidth));
    }

    [Fact]
    public void PartyStateFillsAllThreeLinesForAnyMember()
    {
        var m = new Mobile { Serial = Friend, Name = "Friend", Notoriety = Innocent, Hits = 50, HitsMax = 100, Mana = 25, ManaMax = 100, Stamina = 100, StaminaMax = 100 };

        HealthBarWindow.PartyState s = HealthBarWindow.ComputeParty(Friend, Me, m, null);

        Assert.True(s.InRange);
        Assert.False(s.Self);
        Assert.Equal((48, 24, 96), (s.Hits, s.Mana, s.Stamina));
        Assert.Equal("Friend", s.Name);
        Assert.Equal(0x005A, s.NameHue); // innocent's notoriety hue
        Assert.True(s.ShowsBarsAndButtons);
        Assert.True(s.ShowsHitsRedLine);
    }

    [Fact]
    public void OurOwnPartyBarIsSelfAndOutOfRangeHidesBarsAndButtons()
    {
        var me = new Mobile { Serial = Me, Name = "Wes", Notoriety = Innocent, Hits = 10, HitsMax = 10 };
        HealthBarWindow.PartyState self = HealthBarWindow.ComputeParty(Me, Me, me, null);
        Assert.Equal("[* SELF *]", self.Name);
        Assert.True(self.Self);

        HealthBarWindow.PartyState away = HealthBarWindow.ComputeParty(Friend, Me, null, "Friend");
        Assert.False(away.InRange);
        Assert.False(away.ShowsBarsAndButtons);
        Assert.False(away.ShowsHitsRedLine);
        Assert.Equal("Friend", away.Name);
        Assert.Equal(HealthBarWindow.NameHue, away.NameHue);
    }

    [Fact]
    public void OnlyAPetsClassicBarHasHealButtons()
    {
        var pet = new Mobile { Serial = Pet, Name = "a horse", Renamable = true, Hits = 1, HitsMax = 1 };
        HealthBarWindow.State s = HealthBarWindow.Compute(Pet, Me, false, pet, null);
        Assert.True(HealthBarWindow.ShowsPetHealButtons(s, inParty: false));
        Assert.False(HealthBarWindow.ShowsPetHealButtons(s, inParty: true)); // IsPet excludes party members

        var other = new Mobile { Serial = Stranger, Name = "a stranger", Renamable = false, Hits = 1, HitsMax = 1 };
        Assert.False(HealthBarWindow.ShowsPetHealButtons(HealthBarWindow.Compute(Stranger, Me, false, other, null), false));
        Assert.False(HealthBarWindow.ShowsPetHealButtons(HealthBarWindow.Compute(Pet, Me, false, null, "a horse"), false));
        var me = new Mobile { Serial = Me, Name = "Wes", Renamable = true };
        Assert.False(HealthBarWindow.ShowsPetHealButtons(HealthBarWindow.Compute(Me, Me, false, me, null), false));
    }

    // ---- rename ----

    [Fact]
    public void RenamingARenamablePetSendsModernUOsRenameRequest()
    {
        var sent = new List<byte[]>();
        GameSession session = InWorld(sent);
        Moving(session, Pet, Innocent);
        Status(session, Pet, "a horse", renamable: true);
        sent.Clear();

        Assert.True(session.Rename(Pet, "Shadowfax"));

        byte[] p = Assert.Single(NoPings(sent));
        Assert.Equal(35, p.Length); // IncomingPackets.Register(0x75, 35, ...)
        Assert.Equal(0x75, p[0]);
        Assert.Equal(new byte[] { 0, 0, 0, 0x78 }, p[1..5]);
        Assert.Equal("Shadowfax", System.Text.Encoding.ASCII.GetString(p, 5, 9));
        Assert.All(p[14..], b => Assert.Equal(0, b));
    }

    [Fact]
    public void ALongNameIsCutToThirtyBytes()
    {
        byte[] p = GameSession.RenameRequest(Pet, new string('x', 40));
        Assert.Equal(35, p.Length);
        Assert.All(p[5..], b => Assert.Equal((byte)'x', b));
    }

    [Fact]
    public void NothingIsSentForAMobileThatCannotBeRenamed()
    {
        var sent = new List<byte[]>();
        GameSession session = InWorld(sent);
        Moving(session, Stranger, Innocent);
        Status(session, Stranger, "a stranger", renamable: false);
        sent.Clear();

        Assert.False(session.Rename(Stranger, "Bob"));
        Assert.False(session.Rename(Me, "Bob"));
        Assert.False(session.Rename(0x00000099, "Bob")); // not in view
        Assert.Empty(NoPings(sent));
    }

    [Fact]
    public void ThePetStopsBeingRenamableWhenTheServerSaysSo()
    {
        var sent = new List<byte[]>();
        GameSession session = InWorld(sent);
        Moving(session, Pet, Innocent);
        Status(session, Pet, "a horse", renamable: true);
        Status(session, Pet, "a horse", renamable: false); // transferred away
        sent.Clear();

        Assert.False(session.Rename(Pet, "Mine"));
        Assert.Empty(NoPings(sent));
    }

    [Fact]
    public void OnlyARenamableBarWithNoCursorUpCanBeEdited()
    {
        var pet = new Mobile { Serial = Pet, Name = "a horse", Renamable = true, Hits = 1, HitsMax = 1 };
        HealthBarWindow.State s = HealthBarWindow.Compute(Pet, Me, false, pet, null);
        Assert.True(HealthBarWindow.CanRename(s, targeting: false));
        Assert.False(HealthBarWindow.CanRename(s, targeting: true));

        Assert.False(HealthBarWindow.CanRename(HealthBarWindow.Compute(Pet, Me, false, null, "a horse"), false)); // out of range
        var other = new Mobile { Serial = Stranger, Name = "a stranger", Renamable = false };
        Assert.False(HealthBarWindow.CanRename(HealthBarWindow.Compute(Stranger, Me, false, other, null), false));
        var me = new Mobile { Serial = Me, Name = "Wes", Renamable = true };
        Assert.False(HealthBarWindow.CanRename(HealthBarWindow.Compute(Me, Me, false, me, null), false));
    }
}
