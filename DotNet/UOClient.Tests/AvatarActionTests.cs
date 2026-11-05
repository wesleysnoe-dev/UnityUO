using UOClient.Core;
using UOClient.IO;
using UOClient.IO.Formats;
using UOClient.Net;
using UOClient.Unity;

namespace UOClient.Tests;

/// <summary>
/// What a character on screen plays: TazUO's <c>MobileAnimation.GetGroupForAnimation</c> war/peace/mounted/dead choice,
/// the fallbacks when a body lacks frames, and the one-shot fall of <c>0xAF</c> (DisplayDeath, Mobile.ProcessAnimation).
/// </summary>
public sealed class AvatarActionTests
{
    private static readonly (int, int, int) UopPeace = (22, 24, 25), UopWar = (0, 24, 1), HumanPeace = (0, 2, 4);

    [Fact]
    public void WarModeSelectsTheWarSetForUnmountedBodies()
    {
        Assert.Equal(UopPeace, MobileActions.Select(false, UopPeace, UopWar, warMode: false, dead: false));
        Assert.Equal(UopWar, MobileActions.Select(false, UopPeace, UopWar, warMode: true, dead: false));
    }

    [Fact]
    public void RidersKeepTheMountedActionsInWarMode()
    {
        var mounted = (MobileActions.MountedWalk, MobileActions.MountedRun, MobileActions.MountedStand);
        Assert.Equal((23, 24, 25), mounted);
        Assert.Equal(mounted, MobileActions.Select(true, HumanPeace, HumanPeace, warMode: false, dead: false));
        Assert.Equal(mounted, MobileActions.Select(true, HumanPeace, (7, 2, 15), warMode: true, dead: false));
    }

    [Fact]
    public void TheDeadNeverTakeWarActions()
    {
        // MobileAnimation.cs:1166, 1288: !InWarMode || IsDead.
        Assert.Equal(UopPeace, MobileActions.Select(false, UopPeace, UopWar, warMode: true, dead: true));
    }

    [Fact]
    public void PickChoosesStandWalkOrRun()
    {
        Assert.Equal(1, MobileActions.Pick(UopWar, moving: false, running: false));
        Assert.Equal(0, MobileActions.Pick(UopWar, moving: true, running: false));
        Assert.Equal(24, MobileActions.Pick(UopWar, moving: true, running: true));
    }

    private static Func<int, int[]> Frames(params (int Action, int[] Frames)[] built)
    {
        var map = built.ToDictionary(b => b.Action, b => b.Frames);
        return a => map.TryGetValue(a, out int[] f) ? f : null;   // null: still building
    }

    [Fact]
    public void MissingWarFramesFallBackToPeaceThenStand()
    {
        // War walk 0 has no frames: the peace walk 22 is drawn.
        var get = Frames((0, Array.Empty<int>()), (22, new[] { 1, 2 }), (25, new[] { 9 }));
        Assert.Equal(new[] { 1, 2 }, MobileActions.WithFallback(0, 22, 25, get, out int chosen));
        Assert.Equal(22, chosen);

        // Neither the war nor the peace walk: standing.
        get = Frames((0, Array.Empty<int>()), (22, Array.Empty<int>()), (25, new[] { 9 }));
        Assert.Equal(new[] { 9 }, MobileActions.WithFallback(0, 22, 25, get, out chosen));
        Assert.Equal(25, chosen);

        // War frames present: used as they are.
        get = Frames((1, new[] { 5 }), (25, new[] { 9 }));
        Assert.Equal(new[] { 5 }, MobileActions.WithFallback(1, 25, 25, get, out chosen));
        Assert.Equal(1, chosen);
    }

    [Fact]
    public void FramesStillBuildingKeepWhatIsThereOrNothing()
    {
        // War stand still being built, peace stand ready: show the peace stand meanwhile.
        Assert.Equal(new[] { 9 }, MobileActions.WithFallback(1, 25, 25, Frames((25, new[] { 9 })), out int chosen));
        Assert.Equal(25, chosen);
        // Nothing built yet: null, so the avatar keeps its last frames.
        Assert.Null(MobileActions.WithFallback(1, 25, 25, Frames(), out chosen));
        // A body with no frames anywhere: an empty array, nothing drawn.
        Assert.Empty(MobileActions.WithFallback(1, 25, 25, Frames((1, Array.Empty<int>()), (25, Array.Empty<int>())), out _));
    }

    [Fact]
    public void AFallPlaysOnceAndHoldsItsLastFrame()
    {
        const float step = MobileActions.DeathFrameSeconds;
        Assert.Equal(0.08f, step);
        Assert.Equal(0, MobileActions.DeathFrame(0f, 5));
        Assert.Equal(2, MobileActions.DeathFrame(step * 2.5f, 5));
        Assert.Equal(4, MobileActions.DeathFrame(step * 4.5f, 5));
        Assert.Equal(4, MobileActions.DeathFrame(step * 50f, 5));   // held, never wraps
        Assert.False(MobileActions.DeathDone(step * 4.5f, 5));
        Assert.True(MobileActions.DeathDone(step * 5f, 5));
        Assert.True(MobileActions.DeathDone(0f, 0));                // no fall at all: over at once
        Assert.Equal(0, MobileActions.DeathFrame(1f, 0));
    }

    // ---- packets, as ModernUO sends them ----

    private const uint Player = 0x00000042, Orc = 0x00000099;

    private static GameSession InWorld()
    {
        var session = new GameSession(new LoginSettings());
        session.Inject(new PacketWriter(0x1B, 37).UInt32(Player).UInt32(0).UInt16(0x190).UInt16(1495).UInt16(1629).UInt16(0)
            .Byte(0).Zero(9).UInt16(7168).UInt16(4096).Zero(6).ToArray(), false);
        return session;
    }

    // 0x77: serial, body, x, y, z, direction, hue, flags, notoriety.
    private static byte[] Moving(uint serial, ushort body, byte flags) =>
        new PacketWriter(0x77, 17).UInt32(serial).UInt16(body).UInt16(1496).UInt16(1629).Byte(0)
            .Byte(0).UInt16(0).Byte(flags).Byte(3).ToArray();

    // 0xAF: killed, corpse, running (ModernUO's CreateDeathAnimation writes 0 there).
    private static byte[] DeathAnimation(uint killed, uint corpse, uint running = 0) =>
        new PacketWriter(0xAF, 13).UInt32(killed).UInt32(corpse).UInt32(running).ToArray();

    [Fact]
    public void WarFlagOfOtherMobilesIsRead()
    {
        GameSession session = InWorld();
        session.Inject(Moving(Orc, 753, 0x40), false);
        Assert.True(session.World.Mobiles[Orc].WarMode);
        session.Inject(Moving(Orc, 753, 0x00), false);
        Assert.False(session.World.Mobiles[Orc].WarMode);
    }

    [Fact]
    public void DeathAnimationReportsKnownOthersOnly()
    {
        GameSession session = InWorld();
        var dying = new List<(uint, uint, bool)>();
        session.MobileDying += (s, c, r) => dying.Add((s, c, r));
        Assert.Equal(13, PacketLengths.For(ClientVersion.Default)[0xAF]);

        session.Inject(Moving(Orc, 753, 0x40), false);
        session.Inject(DeathAnimation(Orc, 0x40000123), false);
        session.Inject(DeathAnimation(Orc, 0x40000124, 1), false);
        session.Inject(DeathAnimation(Player, 0x40000125), false);       // never for us (DisplayDeath.cs:23)
        session.Inject(DeathAnimation(0x00000777, 0x40000126), false);   // nor for a mobile we don't know

        Assert.Equal(new[] { (Orc, 0x40000123u, false), (Orc, 0x40000124u, true) }, dying);
    }

    /// <summary>Looks with the action sets <see cref="MobileLook.From"/> fills in, for a UOP creature and a ghost.</summary>
    private static MobileLook Look(int body, (int, int, int) peace, (int, int, int) war, (int, int) death) =>
        new MobileLook { Body = body, BodyActions = peace, WarActions = war, DeathActions = death };

    private static (int, int, int) Chosen(MobileLook look, Mobile m) =>
        MobileActions.Select(look.Mounted, look.BodyActions, look.WarActions, m.WarMode, m.IsDead);

    [Fact]
    public void WarDeathAndResurrectionPickTheRightActions()
    {
        GameSession session = InWorld();
        int falls = 0;
        session.MobileDying += (_, _, _) => falls++;

        // A UOP creature's action sets in war mode, then killed, ghosted (as a player would be) and raised.
        MobileLook living = Look(753, UopPeace, UopWar, (2, 3));
        MobileLook ghost = Look(402, HumanPeace, HumanPeace, (21, 22));

        session.Inject(Moving(Orc, 753, 0x40), false);
        Mobile m = session.World.Mobiles[Orc];
        Assert.Equal(UopWar, Chosen(living, m));

        session.Inject(DeathAnimation(Orc, 0x40000123), false);
        Assert.Equal(1, falls);
        Assert.Equal(2, living.DeathActions.First);

        // ModernUO's OnDeath: war off, ghost body. Even with the war bit, a ghost stands in peace.
        session.Inject(Moving(Orc, 402, 0x40), false);
        Assert.True(m.IsDead);
        Assert.Equal(HumanPeace, Chosen(ghost, m));

        // Resurrect: the living body back, peace stand, no fall left over.
        session.Inject(Moving(Orc, 753, 0x00), false);
        Assert.False(m.IsDead);
        Assert.Equal(25, MobileActions.Pick(Chosen(living, m), moving: false, running: false));
    }
}

/// <summary><see cref="MobileLook.From"/> against the install's mobtypes.txt and AnimationFrame*.uop.</summary>
[Collection("UO install")]
public sealed class AvatarActionInstallTests
{
    private readonly UOData _data;

    public AvatarActionInstallTests(UOInstallFixture fixture) => _data = fixture.Data;

    private static readonly (byte, int, int)[] Naked = Array.Empty<(byte, int, int)>();

    [Fact]
    public void UopCreatureLookCarriesWarAndDeathActions()
    {
        MobileLook look = MobileLook.From(_data, 753, 0, Naked); // gargoyle slave: MONSTER 10000
        Assert.Equal(753, look.Body);
        Assert.False(look.Mounted);
        Assert.Equal((22, 24, 25), look.BodyActions);
        Assert.Equal((0, 24, 1), look.WarActions);
        Assert.Equal((2, 3), look.DeathActions);
        Assert.Equal((0, 24, 1), MobileActions.Select(look.Mounted, look.BodyActions, look.WarActions, true, false));
        Assert.Equal((22, 24, 25), MobileActions.Select(look.Mounted, look.BodyActions, look.WarActions, true, true));
        // Each chosen action has frames, so nothing falls back here.
        foreach (int action in new[] { 0, 1, 2, 3, 22, 24, 25 })
            Assert.NotEmpty(_data.AnimationLibrary.Read(753, action, 1));
    }

    [Fact]
    public void MountedHumanKeepsMountedActionsInWarMode()
    {
        // A horse (mount item 0x3EA2) under a human.
        MobileLook look = MobileLook.From(_data, 400, 0x03EA, new (byte, int, int)[] { (25, 0x3EA2, 0) });
        Assert.True(look.Mounted);
        Assert.Equal((23, 24, 25), MobileActions.Select(look.Mounted, look.BodyActions, look.WarActions, true, false));
        Assert.Equal((21, 22), look.DeathActions);
    }

    [Fact]
    public void HumansKeepPeaceActionsInWarModeForNow()
    {
        // AnimationLibrary.Actions leaves people unchanged in war mode (their war stance depends on the weapon held).
        MobileLook look = MobileLook.From(_data, 400, 0, Naked);
        Assert.Equal((0, 2, 4), look.WarActions);
        Assert.Equal((21, 22), look.DeathActions);
    }
}
