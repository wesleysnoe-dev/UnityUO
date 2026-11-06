using UOClient.Core;
using UOClient.Net;
using UOClient.Unity.UI;

namespace UOClient.Tests;

/// <summary>
/// Health bars and the AoS status (TazUO's HealthBarGump / StatusGumpModern) from an offline session: packets injected as
/// ModernUO writes them, what we send checked against ModernUO's readers. No server, no Unity.
/// </summary>
public sealed class HealthBarStatusTests
{
    private const uint Me = 0x00000042, Orc = 0x00000077, Door = 0x40000123;

    private static GameSession InWorld(List<byte[]> sent)
    {
        var session = new GameSession(new LoginSettings { AnnounceChatLinks = false });
        session.StartOffline(sent.Add);
        session.Inject(new PacketWriter(0x1B, 37).UInt32(Me).UInt32(0).UInt16(0x190).UInt16(1495).UInt16(1629).UInt16(0)
            .Byte(0).Zero(9).UInt16(7168).UInt16(4096).Zero(6).ToArray(), false);
        session.Inject(new byte[] { 0x55 }, false);
        Assert.Equal(SessionState.InWorld, session.State);
        sent.Clear();
        return session;
    }

    // ModernUO's CreateMobileStatus: serial, name (30), hits, max (normalized to 25 for version 0), can rename, version.
    private static PacketWriter Status(uint serial, string name, int hits, int max, bool renamable, int version) =>
        new PacketWriter(0x11).UInt32(serial).Ascii(name, 30).UInt16(hits).UInt16(max).Bool(renamable).Byte(version);

    // A version 5 (ML) full status, as ModernUO sends our own.
    private static byte[] FullStatus(uint serial, string name) =>
        Status(serial, name, 80, 100, false, 5).Bool(true) // female
            .UInt16(90).UInt16(45).UInt16(25)                 // str dex int
            .UInt16(30).UInt16(45).UInt16(10).UInt16(25)      // stam, stam max, mana, mana max
            .Int32(1234).UInt16(12).UInt16(150)               // gold, armor, weight
            .UInt16(390).Byte(1)                              // max weight, race
            .UInt16(225).Byte(1).Byte(5)                      // stat cap, followers, followers max
            .UInt16(20).UInt16(15).UInt16(10).UInt16(5).UInt16(100) // fire cold poison energy resists, luck
            .UInt16(11).UInt16(13).Int32(0).ToArray();        // damage min, max, tithe

    // ---- last attack ----

    [Fact]
    public void AttackSetsLastAttackAndAsksForTheBar()
    {
        var sent = new List<byte[]>();
        GameSession session = InWorld(sent);
        var raised = new List<uint>();
        session.LastAttackSet += raised.Add;

        session.Attack(Orc);

        Assert.Equal(Orc, session.LastAttack);
        Assert.Equal(new[] { Orc }, raised);
        Assert.Equal(new byte[] { 0x05, 0, 0, 0, 0x77 }, Assert.Single(sent));
    }

    [Fact]
    public void ServerCombatantSetsLastAttackOnlyForAMobile()
    {
        var sent = new List<byte[]>();
        GameSession session = InWorld(sent);
        var raised = new List<uint>();
        session.LastAttackSet += raised.Add;

        // ModernUO's SendChangeCombatant (0xAA, 5 bytes): the combatant, or 0 when the fight ends.
        session.Inject(new PacketWriter(0xAA, 5).UInt32(Orc).ToArray(), false);
        session.Inject(new PacketWriter(0xAA, 5).UInt32(0).ToArray(), false);
        session.Inject(new PacketWriter(0xAA, 5).UInt32(Door).ToArray(), false);

        Assert.Equal(new[] { Orc }, raised); // TazUO's LastAttack setter: SerialHelper.IsMobile only
        Assert.Equal(Door, session.LastAttack);
        Assert.Empty(sent); // nothing attacks on its own
    }

    // ---- what the bar shows ----

    [Fact]
    public void BarFollowsAnOfflineMobilesAttributes()
    {
        var sent = new List<byte[]>();
        GameSession session = InWorld(sent);
        // ModernUO's compact status for someone else (normalized 18 of 25), then 0xA1 hits as the fight goes on.
        session.Inject(Status(Orc, "an orc", 18, 25, false, 0).ToArray(), true);
        Mobile orc = session.World.Mobiles[Orc];
        orc.Notoriety = 5;

        HealthBarWindow.State s = HealthBarWindow.Compute(Orc, Me, false, orc, null);
        Assert.False(s.Self);
        Assert.True(s.InRange);
        Assert.True(s.ShowsRedLine);
        Assert.Equal("an orc", s.Name);
        Assert.Equal(HealthBarWindow.BackgroundOther, s.Background);
        Assert.Equal(0x0031, s.BackgroundHue); // enemy
        Assert.Equal(HealthBarWindow.LineBlue, s.HitsLine);
        Assert.Equal(109 * 72 / 100, s.Hits);
        Assert.Equal(HealthBarWindow.NameHue, s.NameHue);

        session.Inject(new PacketWriter(0xA1, 9).UInt32(Orc).UInt16(25).UInt16(5).ToArray(), false);
        Assert.Equal(109 * 20 / 100, HealthBarWindow.Compute(Orc, Me, false, orc, null).Hits);

        // Out of range: name kept, no lines, the 0x0803 hue TazUO leaves on the background.
        HealthBarWindow.State gone = HealthBarWindow.Compute(Orc, Me, false, null, "an orc");
        Assert.False(gone.InRange);
        Assert.False(gone.ShowsRedLine);
        Assert.Equal("an orc", gone.Name);
        Assert.Equal(0x0803, gone.BackgroundHue);
    }

    [Fact]
    public void OwnBarShowsThreeLinesOnTheWarOrPeaceBackground()
    {
        var sent = new List<byte[]>();
        GameSession session = InWorld(sent);
        // ModernUO's SendMobileAttributes (0x2D, 17 bytes): hits max/cur, mana max/cur, stam max/cur.
        session.Inject(new PacketWriter(0x2D, 17).UInt32(Me).UInt16(100).UInt16(50).UInt16(40).UInt16(40).UInt16(80).UInt16(1).ToArray(), false);
        Mobile me = session.World.Player;

        HealthBarWindow.State peace = HealthBarWindow.Compute(Me, Me, false, me, null);
        Assert.True(peace.Self);
        Assert.Equal(0x0803, peace.Background);
        Assert.Equal(0, peace.BackgroundHue);
        Assert.Equal((54, 109, 1), (peace.Hits, peace.Mana, peace.Stamina)); // 1%: CalculatePercents leaves it unscaled
        Assert.Equal(0x0807, HealthBarWindow.Compute(Me, Me, true, me, null).Background);
    }

    [Fact]
    public void PercentMatchesTazUOsCalculatePercents()
    {
        Assert.Equal(0, HealthBarWindow.Percent(0, 10, 109));
        Assert.Equal(109, HealthBarWindow.Percent(25, 25, 109));
        Assert.Equal(109, HealthBarWindow.Percent(25, 40, 109)); // capped at 100%
        Assert.Equal(1, HealthBarWindow.Percent(100, 1, 109));
        Assert.Equal(0, HealthBarWindow.Percent(100, 0, 109));
        Assert.Equal(2, HealthBarWindow.Percent(100, 2, 109));   // 109 * 2 / 100
    }

    [Fact]
    public void NotorietyHuesFollowTazUOsDefaults()
    {
        Assert.Equal(0x005A, HealthBarWindow.BackgroundHue(false, true, 1)); // innocent
        Assert.Equal(0x0044, HealthBarWindow.BackgroundHue(false, true, 2)); // ally
        Assert.Equal(0, HealthBarWindow.BackgroundHue(false, true, 3));      // gray: unhued
        Assert.Equal(0, HealthBarWindow.BackgroundHue(false, true, 4));      // criminal: unhued
        Assert.Equal(0x0023, HealthBarWindow.BackgroundHue(false, true, 6)); // murderer
        Assert.Equal(0x0034, HealthBarWindow.BackgroundHue(false, true, 7)); // invulnerable
        Assert.Equal(0, HealthBarWindow.BackgroundHue(true, true, 6));       // ourselves: never
    }

    [Fact]
    public void PoisonAndYellowBarsComeFrom0x17()
    {
        var sent = new List<byte[]>();
        GameSession session = InWorld(sent);
        session.Inject(Status(Orc, "an orc", 25, 25, true, 0).ToArray(), true);
        Mobile orc = session.World.Mobiles[Orc];
        Assert.True(orc.Renamable);

        // ModernUO's CreateMobileHealthbar: 0x17, length 12, serial, 1 entry, type (1 poison, 2 yellow), level.
        byte[] Bar(int type, int level) => new PacketWriter(0x17).UInt32(Orc).UInt16(1).UInt16(type).Byte(level).ToArray();
        byte[] poison = Bar(1, 2);
        Assert.Equal(12, poison.Length);
        session.Inject(poison, true);
        Assert.True(orc.Poisoned);
        Assert.Equal(HealthBarWindow.LinePoisoned, HealthBarWindow.Compute(Orc, Me, false, orc, null).HitsLine);

        session.Inject(Bar(1, 0), true);
        session.Inject(Bar(2, 1), true);
        Assert.False(orc.Poisoned);
        Assert.True(orc.YellowHits);
        HealthBarWindow.State s = HealthBarWindow.Compute(Orc, Me, false, orc, null);
        Assert.Equal(HealthBarWindow.LineYellowHits, s.HitsLine);
        Assert.Equal(HealthBarWindow.NameHueRenamable, s.NameHue); // a pet's name
    }

    [Fact]
    public void DraggedBarIsCenteredOnThePress()
    {
        Assert.Equal((135f, 175f), HealthBarWindow.CenteredOn(200, 200, 130, 50));
        Assert.Equal((135f, 174f), HealthBarWindow.CenteredOn(200, 200, 131, 53)); // >> 1, as TazUO
        Assert.NotEqual(HealthBarWindow.KeyFor(Orc), HealthBarWindow.KeyFor(Me));
        Assert.Equal(HealthBarWindow.KeyFor(Orc), HealthBarWindow.KeyFor(Orc));
    }

    // ---- status ----

    [Fact]
    public void StatusLabelsTakeTheFullStatus()
    {
        var sent = new List<byte[]>();
        GameSession session = InWorld(sent);
        session.Inject(FullStatus(Me, "Wes"), true);
        Mobile me = session.World.Player;
        Assert.True(me.HasFullStatus);

        // StatusGumpModern without the extended status: every label TazUO adds, where it adds it, and nothing else.
        StatusWindow.Label[] labels = StatusWindow.Labels(me);
        Assert.Equal(21, labels.Length);
        Assert.Equal(labels.Length, labels.Select(l => (l.X, l.Y)).Distinct().Count());
        string At(int x, int y) => Assert.Single(labels, l => l.X == x && l.Y == y).Text;
        int WidthAt(int x, int y) => Assert.Single(labels, l => l.X == x && l.Y == y).Width;
        Assert.Equal("90", At(88, 77));
        Assert.Equal("45", At(88, 105));
        Assert.Equal("25", At(88, 133));
        Assert.Equal("80", At(141, 70));
        Assert.Equal("100", At(141, 83));
        Assert.Equal("30", At(141, 98));
        Assert.Equal("45", At(141, 111));
        Assert.Equal("10", At(141, 126));
        Assert.Equal("25", At(141, 139));
        Assert.Equal("225", At(220, 77));
        Assert.Equal("100", At(220, 105));
        Assert.Equal("150", At(210, 126));
        Assert.Equal("390", At(210, 139));
        Assert.Equal("11-13", At(280, 77));
        Assert.Equal("1234", At(280, 105));
        Assert.Equal("1/5", At(280, 133));
        Assert.Equal("12", At(354, 76)); // physical: the 0x11 armour field
        Assert.Equal("20", At(354, 92));
        Assert.Equal("15", At(354, 106));
        Assert.Equal("10", At(354, 120));
        Assert.Equal("5", At(354, 134));

        // Hits / stamina / mana and weight are centred in 40; the rest are left-aligned.
        foreach (int y in new[] { 70, 83, 98, 111, 126, 139 })
            Assert.Equal(40, WidthAt(141, y));
        Assert.Equal(40, WidthAt(210, 126));
        Assert.Equal(40, WidthAt(210, 139));
        Assert.Equal(0, WidthAt(88, 77));
        Assert.Equal(141 + (40 - 10) / 2, new StatusWindow.Label(141, 70, "", 40).TextX(10));
        Assert.Equal(88, new StatusWindow.Label(88, 77, "").TextX(10));

        // Not the old scroll's sex row, nor the extended status's hit / defence chance, casting or cost increases.
        Assert.DoesNotContain(labels, l => l.Text == "Female" || l.Text == "Male");
    }

    [Fact]
    public void ModernStatusIsTheAosStandardAndNotExtendedFor7_0_23_1()
    {
        var v = ClientVersion.Default;
        Assert.Equal("7.0.23.1", v.ToString());
        Assert.True(StatusWindow.IsModern(v));                       // >= CV_308Z: StatusGumpModern, not StatusGumpOld
        Assert.False(StatusWindow.IsExtended(v));                    // < CV_70300 (7.0.30.0): useExtendedStatus is false
        Assert.True(StatusWindow.HasBuffButton(v));                  // >= CV_5020
        Assert.False(StatusWindow.IsModern(new ClientVersion(3, 0, 8, 'y')));
        Assert.True(StatusWindow.IsModern(new ClientVersion(3, 0, 8, 'z')));
        Assert.True(StatusWindow.IsExtended(new ClientVersion(7, 0, 30, 0)));
        Assert.False(StatusWindow.IsExtended(new ClientVersion(7, 0, 29, 99)));
        Assert.False(StatusWindow.HasBuffButton(new ClientVersion(5, 0, 1, 0)));
    }

    [Fact]
    public void StatLocksComeFromExtendedStatsAndCycle()
    {
        var sent = new List<byte[]>();
        GameSession session = InWorld(sent);
        Mobile me = session.World.Player;

        // ModernUO's SendStatLockInfo: 0xBF, 12, 0x19, version 2, serial, 0, str << 4 | dex << 2 | int.
        byte[] info = new PacketWriter(0xBF).UInt16(0x19).Byte(2).UInt32(Me).Byte(0).Byte((2 << 4) | (1 << 2) | 0).ToArray();
        Assert.Equal(12, info.Length);
        session.Inject(info, true);
        Assert.Equal((2, 1, 0), (me.StrLock, me.DexLock, me.IntLock));
        Assert.Equal(StatusWindow.LockLocked, StatusWindow.LockGump(StatusWindow.LockOf(me, 0)));
        Assert.Equal(StatusWindow.LockDown, StatusWindow.LockGump(StatusWindow.LockOf(me, 1)));
        Assert.Equal(StatusWindow.LockUp, StatusWindow.LockGump(StatusWindow.LockOf(me, 2)));

        // Someone else's (or a bonded pet's version 0) is not ours.
        session.Inject(new PacketWriter(0xBF).UInt16(0x19).Byte(2).UInt32(Orc).Byte(0).Byte(0x2A).ToArray(), true);
        Assert.Equal((2, 1, 0), (me.StrLock, me.DexLock, me.IntLock));

        // A click cycles up → down → locked → up, sent as ModernUO's StatLockChange reads it: 0xBF, 7, 0x1A, stat, lock.
        Assert.Equal(0, StatusWindow.NextLock(2));
        session.SetStatLock(0, StatusWindow.NextLock(me.StrLock));
        Assert.Equal(new byte[] { 0xBF, 0, 7, 0, 0x1A, 0, 0 }, Assert.Single(sent));
        Assert.Equal(0, me.StrLock);
        session.SetStatLock(3, 1); // no fourth stat
        Assert.Single(sent);
    }

    [Fact]
    public void LockArtAndCornerFollowStatusGumpModern()
    {
        Assert.Equal(0x2A6C, StatusWindow.Background);
        Assert.Equal(0x0386, StatusWindow.LabelHue);
        Assert.Equal(40, StatusWindow.LockX);
        Assert.Equal(new[] { 76, 102, 132 }, StatusWindow.LockY);
        Assert.Equal((0x0984, 0x0986, 0x082C), (StatusWindow.LockUp, StatusWindow.LockDown, StatusWindow.LockLocked));
        Assert.Equal(-1, StatusWindow.LockGump(3));
        Assert.Equal((389, 152), (StatusWindow.BarCornerX, StatusWindow.BarCornerY));
        Assert.Equal((58, 50, 320), (StatusWindow.NameX, StatusWindow.NameY, StatusWindow.NameWidth));
        Assert.Equal(0xFF383838u, StatusWindow.RuleArgb);
        Assert.Equal(new[] { (146, 138, 39), (146, 110, 39), (146, 82, 39), (216, 138, 34) },
            StatusWindow.Rules.Select(r => (r.X, r.Y, r.Width)));
    }
}
