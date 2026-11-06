using UOClient.Core;
using UOClient.Net;
using UOClient.Unity;
using UOClient.Unity.UI;

namespace UOClient.Tests;

/// <summary>
/// Options batch 4: TazUO's MobileHPType / "smart" / ShowTargetIndicator (HealthLinesManager.Draw), AutoOpenCorpses
/// (PlayerMobile.TryOpenCorpses), HueContainerGumps (ContainerGump) and HoldDownKeyTab (GameSceneInputHandler), plus the
/// options file and draft for the new keys.
/// </summary>
public sealed class OptionsBatch4Tests
{
    private const int Percent = GameOptions.MobileHPPercent, LineOnly = GameOptions.MobileHPLine, Both = GameOptions.MobileHPBoth;
    private const int Always = GameOptions.MobileHPAlways, Damaged = GameOptions.MobileHPWhenDamaged, Smart = GameOptions.MobileHPSmart;

    private static (bool Line, bool Percent) Show(bool showHP, int type, int when, int hits, int max,
                                                   bool dead = false, bool alsoWhenFull = false, bool target = false, bool indicator = false) =>
        GameOptions.HealthDisplay(showHP, type, when, hits, max, dead, alsoWhenFull, target, indicator);

    [Fact]
    public void HealthType_PercentLineOrBoth()
    {
        Assert.Equal((false, true), Show(true, Percent, Always, 10, 20));
        Assert.Equal((true, false), Show(true, LineOnly, Always, 10, 20));
        Assert.Equal((true, true), Show(true, Both, Always, 10, 20));
        Assert.Equal((false, false), Show(true, Both, Always, 10, 0)); // no known maximum, nothing
        Assert.Equal((true, false), Show(true, Both, Always, 0, 20));  // TazUO: no % text at 0 (HitsPercentage != 0)
        Assert.Equal((true, false), Show(true, Both, Always, 10, 20, dead: true)); // a ghost: no % text
    }

    [Fact]
    public void ShowWhen_SmartHidesOnlyTheFullPercent()
    {
        // Smart: the line always, the % text only below full (TazUO: showWhen == 2 && current != max).
        Assert.Equal((true, false), Show(true, Both, Smart, 20, 20));
        Assert.Equal((true, true), Show(true, Both, Smart, 19, 20));
        Assert.Equal((false, false), Show(true, Percent, Smart, 20, 20));
        // When damaged: nothing at full (TazUO's skip), except this client's extras (the combatant, ourselves in war).
        Assert.Equal((false, false), Show(true, Both, Damaged, 20, 20));
        Assert.Equal((true, true), Show(true, Both, Damaged, 20, 20, alsoWhenFull: true));
        // Always: full health shows its [100%] too.
        Assert.Equal((true, true), Show(true, Both, Always, 20, 20));
    }

    [Fact]
    public void Targets_KeepTheirLine()
    {
        // TazUO's forceDraw: with Percent only, a target still gets its line.
        Assert.Equal((true, true), Show(true, Percent, Always, 10, 20, target: true));
        // ShowMobilesHP off: nothing, unless ShowTargetIndicator keeps a target's line (never the % text).
        Assert.Equal((false, false), Show(false, Both, Always, 10, 20, target: true));
        Assert.Equal((true, false), Show(false, Both, Always, 10, 20, target: true, indicator: true));
        Assert.Equal((false, false), Show(false, Both, Always, 10, 20, target: false, indicator: true));
        Assert.Equal((true, false), Show(false, Percent, Damaged, 20, 20, target: true, indicator: true)); // even at full
        Assert.Equal((false, false), Show(false, Both, Always, 10, 0, target: true, indicator: true));
        // ShowMobilesHP on, when damaged, at full: a target follows the show-when rule, as TazUO's forceDraw does.
        Assert.Equal((false, false), Show(true, Both, Damaged, 20, 20, target: true, indicator: true));
    }

    [Fact]
    public void PercentText_AsTazUOsEntity()
    {
        Assert.Equal(0, GameOptions.HitsPercent(0, 50));
        Assert.Equal(0, GameOptions.HitsPercent(5, 0));
        Assert.Equal(66, GameOptions.HitsPercent(2, 3)); // rounded down
        Assert.Equal(100, GameOptions.HitsPercent(120, 100));
        Assert.Equal(0x21, GameOptions.HitsPercentHue(29));
        Assert.Equal(0x30, GameOptions.HitsPercentHue(30));
        Assert.Equal(0x30, GameOptions.HitsPercentHue(49));
        Assert.Equal(0x58, GameOptions.HitsPercentHue(79));
        Assert.Equal(0x44, GameOptions.HitsPercentHue(80));
        Assert.Equal("[57%]", GameOptions.HitsPercentText(57));
    }

    [Fact]
    public void ContainerGumpHue_TakesTheContainersHue_NeverACorpses()
    {
        Assert.Equal((ushort)0x0481, GameOptions.ContainerGumpHue(true, 0x003C, 0x0481));
        Assert.Equal((ushort)0, GameOptions.ContainerGumpHue(false, 0x003C, 0x0481));
        Assert.Equal((ushort)0, GameOptions.ContainerGumpHue(true, GameOptions.CorpseGump, 0x0481));
        Assert.Equal((ushort)0, GameOptions.ContainerGumpHue(true, 0x003C, 0));
    }

    [Fact]
    public void Tab_TogglesOnPress_OrHoldsWar()
    {
        bool holding = false;
        // Off: the press toggles; the release does nothing.
        Assert.True(GameOptions.TabWarMode(false, true, false, false, ref holding));
        Assert.False(GameOptions.TabWarMode(false, true, false, true, ref holding));
        Assert.Null(GameOptions.TabWarMode(false, false, true, true, ref holding));
        Assert.False(holding);

        // On: the press asks for war once, the release for peace.
        Assert.True(GameOptions.TabWarMode(true, true, false, false, ref holding));
        Assert.True(holding);
        Assert.Null(GameOptions.TabWarMode(true, true, false, true, ref holding)); // key repeat while held
        Assert.False(GameOptions.TabWarMode(true, false, true, true, ref holding));
        Assert.False(holding);
        Assert.Null(GameOptions.TabWarMode(true, false, true, false, ref holding)); // a second release: nothing

        // On, already at war: the press sends nothing, the release still asks for peace (TazUO's _requestedWarMode).
        Assert.Null(GameOptions.TabWarMode(true, true, false, true, ref holding));
        Assert.True(holding);
        Assert.False(GameOptions.TabWarMode(true, false, true, true, ref holding));

        // A release with no held press (typed in a text box meanwhile): nothing.
        holding = false;
        Assert.Null(GameOptions.TabWarMode(true, false, true, true, ref holding));
    }

    // ---- auto-open corpses ----

    private const uint Me = 0x00000042;
    private const int MeX = 1495, MeY = 1629;

    private static GameSession InWorld(List<byte[]> sent)
    {
        var session = new GameSession(new LoginSettings { AnnounceChatLinks = false });
        session.StartOffline(sent.Add);
        session.Inject(new PacketWriter(0x1B, 37).UInt32(Me).UInt32(0).UInt16(0x190).UInt16(MeX).UInt16(MeY).UInt16(0)
            .Byte(0).Zero(9).UInt16(7168).UInt16(4096).Zero(6).ToArray(), false);
        session.Inject(new byte[] { 0x55 }, false);
        Assert.Equal(SessionState.InWorld, session.State);
        sent.Clear();
        return session;
    }

    // ModernUO's 0xF3 world item: 1, type, serial, graphic, direction, amount x2, x, y, z, light, hue, flags.
    private static void GroundItem(GameSession session, uint serial, ushort graphic, int x, int y) =>
        session.Inject(new PacketWriter(0xF3, 26).UInt16(1).Byte(0).UInt32(serial).UInt16(graphic).Byte(0).UInt16(1).UInt16(1)
            .UInt16(x).UInt16(y).Byte(0).Byte(0).UInt16(0).Byte(0).UInt16(0).ToArray(), false);

    private static byte[] UseOf(uint serial) => new PacketWriter(0x06, 5).UInt32(serial).ToArray();

    [Fact]
    public void CorpseRules_DistanceAndOptions()
    {
        Assert.Equal(3, CorpseAutoOpen.Distance(10, 10, 13, 8));
        Assert.Equal(2, CorpseAutoOpen.Distance(10, 10, 8, 12));
        Assert.True(CorpseAutoOpen.Opens(true, 2, 0, 2, false, false, false));
        Assert.False(CorpseAutoOpen.Opens(true, 2, 0, 3, false, false, false));  // out of range
        Assert.False(CorpseAutoOpen.Opens(false, 2, 0, 0, false, false, false)); // off
        Assert.False(CorpseAutoOpen.Opens(true, 2, 0, 0, true, false, false));   // opened before
        Assert.False(CorpseAutoOpen.Opens(true, 9, 0, 6, false, false, false));  // the range tops out at 5
        Assert.True(CorpseAutoOpen.Opens(true, 0, 0, 0, false, false, false));   // 0: only underfoot
        // TazUO's CorpseOpenOptions: 1 not while targeting, 2 not while hidden, 3 neither.
        Assert.False(CorpseAutoOpen.HeldOff(0, true, true));
        Assert.True(CorpseAutoOpen.HeldOff(1, true, false));
        Assert.False(CorpseAutoOpen.HeldOff(1, false, true));
        Assert.True(CorpseAutoOpen.HeldOff(2, false, true));
        Assert.False(CorpseAutoOpen.HeldOff(2, true, false));
        Assert.True(CorpseAutoOpen.HeldOff(3, true, false));
        Assert.True(CorpseAutoOpen.HeldOff(3, false, true));
    }

    [Fact]
    public void Corpses_InRangeOpenOnce_OthersNever()
    {
        var sent = new List<byte[]>();
        GameSession session = InWorld(sent);
        GroundItem(session, 0x40000001, WorldItem.CorpseGraphic, MeX + 2, MeY - 1); // 2 tiles
        GroundItem(session, 0x40000002, WorldItem.CorpseGraphic, MeX + 3, MeY);     // 3 tiles: out of the default range
        GroundItem(session, 0x40000003, 0x0E75, MeX, MeY);                          // a backpack underfoot: not a corpse
        var open = new CorpseAutoOpen { Enabled = true }; // off by default (batch 5)

        Assert.Equal(0x40000001u, open.Tick(session, MeX, MeY));
        Assert.Equal(UseOf(0x40000001), Assert.Single(sent));
        Assert.Equal(0u, session.LastObject); // not the player's double-click: no "last object", no macro line
        Assert.Equal(0u, open.Tick(session, MeX, MeY));
        Assert.Single(sent); // once only

        // Stepping closer brings the second one in range: queued behind the server's action delay, not sent yet.
        Assert.Equal(0x40000002u, open.Tick(session, MeX + 1, MeY));
        Assert.True(open.Opened(0x40000002));
        Assert.Single(sent);
        Assert.Equal(1, session.QueuedActions);
        Assert.Equal(0u, open.Tick(session, MeX + 1, MeY)); // nothing else in range
        // Another corpse while something is still queued waits (the player's own uses never queue behind a pile of corpses).
        GroundItem(session, 0x40000004, WorldItem.CorpseGraphic, MeX + 1, MeY + 1);
        Assert.Equal(0u, open.Tick(session, MeX + 1, MeY));
        Assert.False(open.Opened(0x40000004));

        // Off, or a new session: nothing / everything forgotten.
        open.Enabled = false;
        Assert.Equal(0u, open.Tick(session, MeX + 3, MeY));
        var sent2 = new List<byte[]>();
        GameSession next = InWorld(sent2);
        GroundItem(next, 0x40000001, WorldItem.CorpseGraphic, MeX, MeY);
        open.Enabled = true;
        Assert.Equal(0x40000001u, open.Tick(next, MeX, MeY));
        Assert.Equal(UseOf(0x40000001), Assert.Single(sent2));
    }

    [Fact]
    public void Corpses_HeldOffWhileTargetingOrHidden_ThenOpen()
    {
        var sent = new List<byte[]>();
        GameSession session = InWorld(sent);
        GroundItem(session, 0x40000001, WorldItem.CorpseGraphic, MeX + 1, MeY);
        var open = new CorpseAutoOpen { Enabled = true, Options = CorpseAutoOpen.NotTargetingOrHiding };

        session.Inject(new PacketWriter(0x6C, 19).Byte(0).UInt32(7).Byte(0).Zero(12).ToArray(), false); // a target cursor
        Assert.NotNull(session.Target);
        Assert.Equal(0u, open.Tick(session, MeX, MeY));
        Assert.Empty(sent);
        Assert.False(open.Opened(0x40000001)); // held off, not marked: tried again

        session.CancelTarget();
        sent.Clear();
        session.World.Player.Flags |= 0x80; // hidden
        Assert.Equal(0u, open.Tick(session, MeX, MeY));
        Assert.Empty(sent);

        open.Options = CorpseAutoOpen.NotTargeting; // hidden no longer holds it off
        Assert.Equal(0x40000001u, open.Tick(session, MeX, MeY));
        Assert.Equal(UseOf(0x40000001), Assert.Single(sent));
    }

    // ---- auto-open doors ----

    private static bool IsDoor(ushort g) => g >= 0x0675 && g <= 0x06F4 || g >= 0x2000 && g < 0x2010;
    private static readonly byte[] OpenDoorMacro = { 0x12, 0x00, 0x05, 0x58, 0x00 };

    [Fact]
    public void DoorRules_AsTazUOsDoorData()
    {
        Assert.False(DoorAutoOpen.IsOpenDoor(0x0675, IsDoor)); // MetalDoor, facing 0, closed
        Assert.True(DoorAutoOpen.IsOpenDoor(0x0676, IsDoor));
        Assert.False(DoorAutoOpen.IsOpenDoor(0x0824, null));   // IronGate block: no tiledata needed
        Assert.True(DoorAutoOpen.IsOpenDoor(0x0825, null));
        Assert.False(DoorAutoOpen.IsOpenDoor(0x2000, IsDoor)); // an unknown run counts from its start
        Assert.True(DoorAutoOpen.IsOpenDoor(0x2003, IsDoor));
        Assert.False(DoorAutoOpen.IsOpenDoor(0x0EED, IsDoor)); // not a door
        Assert.Equal((10, 9), DoorAutoOpen.Ahead(10, 10, 0));
        Assert.Equal((11, 11), DoorAutoOpen.Ahead(10, 10, 3));
        Assert.Equal((9, 9), DoorAutoOpen.Ahead(10, 10, 7));
        Assert.Equal((10, 11), DoorAutoOpen.Ahead(10, 10, 0x84)); // the run bit is ignored
        Assert.True(DoorAutoOpen.InReach(0, 15));
        Assert.False(DoorAutoOpen.InReach(0, 16));
        Assert.False(DoorAutoOpen.InReach(20, 4));
    }

    [Fact]
    public void Doors_FacingAClosedOneOpensIt_OncePerStepOrTurn()
    {
        var sent = new List<byte[]>();
        GameSession session = InWorld(sent);
        GroundItem(session, 0x40000010, 0x0675, MeX, MeY - 1); // closed, north of us
        GroundItem(session, 0x40000011, 0x0676, MeX + 1, MeY); // open, east of us
        var doors = new DoorAutoOpen { Enabled = true }; // off by default (batch 5)

        Assert.False(doors.Tick(session, MeX, MeY, 0, 4, IsDoor)); // a new session: where it starts is no turn
        Assert.False(doors.Tick(session, MeX, MeY, 0, 2, IsDoor)); // turn east: the door there is open
        Assert.Empty(sent);
        Assert.True(doors.Tick(session, MeX, MeY, 0, 0, IsDoor));  // turn north: closed door
        Assert.Equal(OpenDoorMacro, Assert.Single(sent));
        Assert.False(doors.Tick(session, MeX, MeY, 0, 0, IsDoor)); // standing there: nothing more
        Assert.Single(sent);
        Assert.False(doors.Tick(session, MeX, MeY, 40, 6, IsDoor));
        Assert.False(doors.Tick(session, MeX, MeY, 40, 0, IsDoor)); // too far above it
        Assert.Single(sent);

        // Hidden without "also while hidden", or a ghost, or off: nothing.
        doors.IfHidden = false;
        session.World.Player.Flags |= 0x80;
        doors.Tick(session, MeX, MeY, 0, 4, IsDoor);
        Assert.False(doors.Tick(session, MeX, MeY, 0, 0, IsDoor));
        session.World.Player.Flags = 0;
        session.World.Player.Body = 402;
        doors.Tick(session, MeX, MeY, 0, 4, IsDoor);
        Assert.False(doors.Tick(session, MeX, MeY, 0, 0, IsDoor));
        session.World.Player.Body = 0x190;
        doors.Enabled = false;
        doors.Tick(session, MeX, MeY, 0, 4, IsDoor);
        Assert.False(doors.Tick(session, MeX, MeY, 0, 0, IsDoor));
        Assert.Single(sent);

        // A step onto the tile before the door, already facing it, opens it too (TazUO's position change).
        doors.Enabled = true;
        Assert.False(doors.Tick(session, MeX, MeY + 1, 0, 0, IsDoor)); // two tiles off: nothing ahead
        Assert.True(doors.Tick(session, MeX, MeY, 0, 0, IsDoor));
        Assert.Equal(2, sent.Count);
    }

    // ---- the options file and draft ----

    [Fact]
    public void Batch4_Defaults_FollowTazUO_OrKeepThisClientsBehaviour()
    {
        var d = GameOptions.Defaults();
        // As TazUO's Profile.cs, except the two that act on the world: off until the player turns them on (batch 5).
        Assert.False(d.autoOpenCorpses);
        Assert.Equal(2, d.autoOpenCorpseRange);
        Assert.Equal(GameOptions.CorpseOpenNotTargetingOrHiding, d.corpseOpenOptions);
        Assert.True(d.hueContainerGumps);
        Assert.False(d.holdDownKeyTab);
        Assert.False(d.autoOpenDoors);
        Assert.True(d.autoOpenDoorsIfHidden);
        var doors = new DoorAutoOpen();
        Assert.Equal((d.autoOpenDoors, d.autoOpenDoorsIfHidden), (doors.Enabled, doors.IfHidden));
        Assert.False(d.showTargetIndicator);
        // Kept as this client always drew: the line (TazUO: percent).
        Assert.Equal(GameOptions.MobileHPLine, d.mobileHPType);
        // The runtime's own defaults agree with the file's.
        var open = new CorpseAutoOpen();
        Assert.Equal((d.autoOpenCorpses, d.autoOpenCorpseRange, d.corpseOpenOptions), (open.Enabled, open.Range, open.Options));
        Assert.Equal(CorpseAutoOpen.MaxRange, GameOptions.MaxAutoOpenCorpseRange);
    }

    [Fact]
    public void Batch4_RoundTrip_ParseAndClamp()
    {
        var o = new GameOptions
        {
            mobileHPType = GameOptions.MobileHPBoth, mobileHPShowWhen = GameOptions.MobileHPSmart, showTargetIndicator = true,
            autoOpenCorpses = true, autoOpenCorpseRange = 5, corpseOpenOptions = GameOptions.CorpseOpenNotHiding,
            hueContainerGumps = false, holdDownKeyTab = true, autoOpenDoors = true, autoOpenDoorsIfHidden = false,
        };
        string json = o.ToJson();
        Assert.Contains("\"mobileHPType\": 2,", json);
        Assert.Contains("\"holdDownKeyTab\": true", json);
        GameOptions again = GameOptions.Parse(json, out var unknown);
        Assert.Empty(unknown);
        Assert.True(again.SameAs(o));
        Assert.False(again.SameAs(GameOptions.Defaults()));

        foreach (string key in new[] { "showTargetIndicator", "holdDownKeyTab", "autoOpenCorpses", "autoOpenDoors" })
            Assert.False(GameOptions.Parse("{\"" + key + "\": true}").SameAs(GameOptions.Defaults()), key);
        foreach (string key in new[] { "hueContainerGumps", "autoOpenDoorsIfHidden" })
            Assert.False(GameOptions.Parse("{\"" + key + "\": false}").SameAs(GameOptions.Defaults()), key);
        foreach (string key in new[] { "mobileHPType", "autoOpenCorpseRange", "corpseOpenOptions" })
            Assert.False(GameOptions.Parse("{\"" + key + "\": 0}").SameAs(GameOptions.Defaults()), key);

        var c = new GameOptions { mobileHPType = 7, autoOpenCorpseRange = 40, corpseOpenOptions = -3, mobileHPShowWhen = -1 }.Clamp();
        Assert.Equal(GameOptions.MobileHPBoth, c.mobileHPType);
        Assert.Equal(GameOptions.MaxAutoOpenCorpseRange, c.autoOpenCorpseRange);
        Assert.Equal(GameOptions.CorpseOpenAlways, c.corpseOpenOptions);
        Assert.Equal(GameOptions.MobileHPAlways, c.mobileHPShowWhen);
        Assert.Equal(0, GameOptions.Parse("{\"autoOpenCorpseRange\": -2}").autoOpenCorpseRange);
        Assert.Equal(GameOptions.MobileHPSmart, GameOptions.Parse("{\"mobileHPShowWhen\": 2}").mobileHPShowWhen);
    }

    [Fact]
    public void Batch4_UnknownKeysKept_AndDraftPaths()
    {
        GameOptions read = GameOptions.Parse("{\"holdDownKeyTab\": true, \"someFutureKey\": [1, 2]}", out var unknown);
        Assert.True(read.holdDownKeyTab);
        Assert.Equal("someFutureKey", Assert.Single(unknown).Key);
        Assert.Contains("\"someFutureKey\": [1, 2]", read.ToJson(unknown));

        GameOptions live = GameOptions.Defaults();
        int applies = 0, saves = 0;
        var draft = new OptionsDraft(() => live.Clone(), o => { applies++; live = o.Clone(); }, _ => saves++);
        draft.Draft.autoOpenCorpses = true;
        draft.Draft.mobileHPType = GameOptions.MobileHPPercent;
        draft.Cancel(); // dropped
        Assert.False(draft.Draft.autoOpenCorpses);
        Assert.Equal((0, 0), (applies, saves));

        draft.Draft.hueContainerGumps = false;
        draft.Apply(); // runtime only
        Assert.False(live.hueContainerGumps);
        Assert.Equal((1, 0), (applies, saves));

        draft.Defaults(); // the draft only
        Assert.True(draft.Draft.hueContainerGumps);
        Assert.False(live.hueContainerGumps);
        draft.Okay();
        Assert.True(live.hueContainerGumps);
        Assert.Equal((2, 1), (applies, saves));
    }

    [Fact]
    public void Batch4_GreysUpdated()
    {
        // The % text and "smart" are drawn now: no longer listed as left out.
        Assert.DoesNotContain(OptionsWindow.NotOffered, n => n.Text.Contains("percent", StringComparison.OrdinalIgnoreCase));
        // Nameplates stays unavailable: health type and show-when live on General, as in TazUO's ModernOptionsGump.
        Assert.Contains("Nameplates", OptionsWindow.UnavailablePages);
    }
}
