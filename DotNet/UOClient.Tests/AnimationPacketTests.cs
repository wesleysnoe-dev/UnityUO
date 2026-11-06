using UOClient.Core;
using UOClient.IO;
using UOClient.IO.Formats;
using UOClient.Net;
using UOClient.Unity;

namespace UOClient.Tests;

/// <summary>
/// Server animations: 0x6E and 0xE2 as ModernUO writes them (OutgoingMobilePackets.CreateMobileAnimation /
/// CreateNewMobileAnimation), decoded as TazUO's CharacterAnimation / NewCharacterAnimation read them, and 0x2F.
/// </summary>
public sealed class AnimationPacketTests
{
    private const uint Player = 0x00000042, Orc = 0x00000099, Stranger = 0x00000777;

    private static GameSession InWorld(bool complete = true)
    {
        var session = new GameSession(new LoginSettings());
        session.StartOffline(_ => { });
        session.Inject(new PacketWriter(0x1B, 37).UInt32(Player).UInt32(0).UInt16(0x190).UInt16(1495).UInt16(1629).UInt16(0)
            .Byte(0).Zero(9).UInt16(7168).UInt16(4096).Zero(6).ToArray(), false);
        if (complete)
            session.Inject(new byte[] { 0x55 }, false);
        session.Inject(new PacketWriter(0x77, 17).UInt32(Orc).UInt16(17).UInt16(1496).UInt16(1629).Byte(0)
            .Byte(0).UInt16(0).Byte(0).Byte(3).ToArray(), false);
        return session;
    }

    // ModernUO's CreateMobileAnimation: the byte after the repeat count is !forward.
    private static byte[] MobileAnimation(uint serial, int action, int frameCount, int repeatCount, bool forward, bool repeat, int delay) =>
        new PacketWriter(0x6E, 14).UInt32(serial).UInt16(action).UInt16(frameCount).UInt16(repeatCount)
            .Bool(!forward).Bool(repeat).Byte(delay).ToArray();

    private static byte[] NewMobileAnimation(uint serial, int type, int action, int mode) =>
        new PacketWriter(0xE2, 10).UInt32(serial).UInt16(type).UInt16(action).Byte(mode).ToArray();

    [Fact]
    public void PacketLengthsMatchModernUO()
    {
        short[] lengths = PacketLengths.For(new ClientVersion(7, 0, 23, 1));
        Assert.Equal(14, lengths[0x6E]);
        Assert.Equal(10, lengths[0xE2]);
        Assert.Equal(10, lengths[0x2F]);
        Assert.Equal(14, MobileAnimation(Orc, 0, 0, 0, true, false, 0).Length);
        Assert.Equal(10, NewMobileAnimation(Orc, 0, 0, 0).Length);
    }

    [Fact]
    public void MobileAnimationIsDecodedForKnownMobiles()
    {
        GameSession session = InWorld();
        var seen = new List<ServerAnimation>();
        session.MobileAnimated += seen.Add;

        // A weapon swing as BaseWeapon.PlaySwingAnimation sends it: Animate(action, 7, 1, true, false, 0).
        session.Inject(MobileAnimation(Orc, 12, 7, 1, forward: true, repeat: false, delay: 0), false);
        // Backwards, repeating, slower, on the player.
        session.Inject(MobileAnimation(Player, 32, 5, 3, forward: false, repeat: true, delay: 2), false);
        // Not known: ignored as TazUO does.
        session.Inject(MobileAnimation(Stranger, 12, 7, 1, true, false, 0), false);

        Assert.Equal(2, seen.Count);
        ServerAnimation a = seen[0];
        Assert.Equal((Orc, false, 12, 7, 1, true, false, 0), (a.Serial, a.IsNew, a.Action, a.FrameCount, a.RepeatCount, a.Forward, a.Repeat, a.Delay));
        ServerAnimation b = seen[1];
        Assert.Equal((Player, 32, 5, 3, false, true, 2), (b.Serial, b.Action, b.FrameCount, b.RepeatCount, b.Forward, b.Repeat, b.Delay));
    }

    [Fact]
    public void FrameCountIsTakenAsAByte()
    {
        // SetAnimation(..., (byte)frame_count, ...).
        GameSession session = InWorld();
        ServerAnimation got = null;
        session.MobileAnimated += a => got = a;
        session.Inject(MobileAnimation(Orc, 4, 0x0105, 1, true, false, 0), false);
        Assert.Equal(5, got.FrameCount);
    }

    [Fact]
    public void NewMobileAnimationIsDecoded()
    {
        GameSession session = InWorld();
        var seen = new List<ServerAnimation>();
        session.MobileAnimated += seen.Add;

        session.Inject(NewMobileAnimation(Orc, 0, 3, 1), false);
        session.Inject(NewMobileAnimation(Stranger, 0, 3, 1), false);

        ServerAnimation a = Assert.Single(seen);
        Assert.Equal((Orc, true, 0, 3, 1), (a.Serial, a.IsNew, a.Type, a.Action, a.Mode));
        Assert.Equal((1, false, true), (a.RepeatCount, a.Repeat, a.Forward));
    }

    [Fact]
    public void NewAnimationRepeatsOnlyABlockOnBody0x15()
    {
        GameSession session = InWorld();
        session.Inject(new PacketWriter(0x77, 17).UInt32(0x123).UInt16(0x15).UInt16(1497).UInt16(1629).Byte(0)
            .Byte(0).UInt16(0).Byte(0).Byte(3).ToArray(), false);
        var seen = new List<ServerAnimation>();
        session.MobileAnimated += seen.Add;
        session.Inject(NewMobileAnimation(0x123, 1, 0, 0), false);
        session.Inject(NewMobileAnimation(0x123, 2, 0, 0), false);
        session.Inject(NewMobileAnimation(0x123, 0, 0, 0), false);
        session.Inject(NewMobileAnimation(Orc, 1, 0, 0), false);
        Assert.Equal(new[] { true, true, false, false }, seen.Select(s => s.Repeat));
    }

    [Fact]
    public void NewMobileAnimationWaitsForThePlayer()
    {
        var session = new GameSession(new LoginSettings());
        int count = 0;
        session.MobileAnimated += _ => count++;
        session.Inject(new PacketWriter(0x77, 17).UInt32(Orc).UInt16(17).UInt16(1496).UInt16(1629).Byte(0)
            .Byte(0).UInt16(0).Byte(0).Byte(3).ToArray(), false);
        session.Inject(NewMobileAnimation(Orc, 0, 0, 0), false);
        Assert.Equal(0, count);
    }

    [Theory]
    [InlineData(new byte[] { 0x6E })]
    [InlineData(new byte[] { 0x6E, 0, 0, 0, 0x99 })]
    [InlineData(new byte[] { 0x6E, 0, 0, 0, 0x99, 0, 12, 0, 7, 0, 1, 0, 0 })]   // one byte short
    [InlineData(new byte[] { 0xE2, 0, 0, 0, 0x99, 0, 0, 0, 3 })]                // one byte short
    [InlineData(new byte[] { 0x2F, 0, 0, 0, 0, 0x42 })]
    public void ShortPacketsAreIgnored(byte[] packet)
    {
        GameSession session = InWorld();
        int animated = 0, swung = 0;
        session.MobileAnimated += _ => animated++;
        session.SwungAtLastAttack += _ => swung++;
        session.Inject(packet, false);
        Assert.Equal(0, animated);
        Assert.Equal(0, swung);
    }

    private static byte[] Swing(uint attacker, uint defender) =>
        new PacketWriter(0x2F, 10).Byte(0).UInt32(attacker).UInt32(defender).ToArray();

    [Fact]
    public void SwingIsReportedOnlyForThePlayersWarAgainstItsLastAttack()
    {
        GameSession session = InWorld();
        var swung = new List<uint>();
        int animated = 0;
        session.SwungAtLastAttack += swung.Add;
        session.MobileAnimated += _ => animated++;

        session.Inject(Swing(Player, Orc), false);                           // no last attack, no war
        session.Inject(new PacketWriter(0xAA, 5).UInt32(Orc).ToArray(), false); // combatant -> last attack
        session.Inject(Swing(Player, Orc), false);                           // not in war mode yet
        session.Inject(new byte[] { 0x72, 1, 0, 0x32, 0 }, false);
        session.Inject(Swing(Player, Orc), false);                           // reported
        session.Inject(Swing(Orc, Player), false);                           // someone else's swing
        session.Inject(Swing(Player, Stranger), false);                      // not the last attack

        Assert.Equal(new[] { Orc }, swung);
        Assert.Equal(0, animated); // 0x2F never plays an animation itself
    }
}

/// <summary>The mapping of server action numbers to the action a body draws (TazUO MobileAnimation.cs).</summary>
public sealed class ServerAnimationGroupTests
{
    private static ServerAnimationGroups.Body Body(MobType type, uint flags = 0, Func<int, bool> exists = null,
        bool mounted = false, bool war = false, MobType? original = null, bool listed = true) => new()
    {
        OriginalType = original ?? type,
        Type = type,
        Flags = flags | (listed ? ServerAnimationGroups.Found : 0),
        Exists = exists ?? (_ => true),
        Mounted = mounted,
        WarMode = war
    };

    private static readonly List<(int, int)> NoReplaces = new();

    [Fact]
    public void ReplacedObjectAnimationUsesTheDefsAndWraps()
    {
        var low = new List<(int, int)> { (5, 6), (4, 0xFF) };
        var people = new List<(int, int)> { (9, 10), (30, 0xFF) };
        Assert.Equal(6, ServerAnimationGroups.Replaced(MobType.Animal, 5, low, people));
        Assert.Equal(0, ServerAnimationGroups.Replaced(MobType.Animal, 4, low, people));      // -1: the walk
        Assert.Equal(1, ServerAnimationGroups.Replaced(MobType.Animal, 14, low, people));     // 14 % 13
        Assert.Equal(10, ServerAnimationGroups.Replaced(MobType.Human, 9, low, people));
        Assert.Equal(10, ServerAnimationGroups.Replaced(MobType.Equipment, 9, low, people));
        Assert.Equal(0, ServerAnimationGroups.Replaced(MobType.Human, 30, low, people));
        Assert.Equal(1, ServerAnimationGroups.Replaced(MobType.Human, 36, low, people));      // 36 % 35
        Assert.Equal(3, ServerAnimationGroups.Replaced(MobType.Monster, 25, low, people));    // 25 % 22
    }

    [Theory]
    // A human swinging: the action stands (ModernUO's weapon animations are already people actions).
    [InlineData(9, 9)]
    [InlineData(12, 12)]
    [InlineData(18, 18)]
    [InlineData(32, 32)]
    public void HumansPlayTheActionAsSent(int sent, int drawn) =>
        Assert.Equal(drawn, ServerAnimationGroups.Group(sent, Body(MobType.Human), NoReplaces, NoReplaces));

    [Fact]
    public void MonstersFallBackToStandingForMissingOrOutOfRangeActions()
    {
        // 12 (cast) on a monster without Unknown1000 is attack 4; actions it lacks stand (1).
        Assert.Equal(4, ServerAnimationGroups.Group(12, Body(MobType.Monster), NoReplaces, NoReplaces));
        Assert.Equal(1, ServerAnimationGroups.Group(5, Body(MobType.Monster, exists: a => a != 5), NoReplaces, NoReplaces));
        Assert.Equal(1, ServerAnimationGroups.Group(25, Body(MobType.Monster), NoReplaces, NoReplaces));
        // UOP monsters keep actions over 21.
        Assert.Equal(25, ServerAnimationGroups.Group(25, Body(MobType.Monster, ServerAnimationGroups.UseUop), NoReplaces, NoReplaces));
    }

    [Fact]
    public void AnimalsMapPeopleActionsAboveTwelve()
    {
        Assert.Equal(0, ServerAnimationGroups.Group(23, Body(MobType.Animal), NoReplaces, NoReplaces));
        Assert.Equal(1, ServerAnimationGroups.Group(24, Body(MobType.Animal), NoReplaces, NoReplaces));
        Assert.Equal(26, ServerAnimationGroups.Group(26, Body(MobType.Animal), NoReplaces, NoReplaces));
        Assert.Equal(9, ServerAnimationGroups.Group(26, Body(MobType.Animal, war: true), NoReplaces, NoReplaces));
        Assert.Equal(9, ServerAnimationGroups.Group(26, Body(MobType.Animal, exists: a => a != 26), NoReplaces, NoReplaces));
        Assert.Equal(10, ServerAnimationGroups.Group(28, Body(MobType.Animal), NoReplaces, NoReplaces));
        Assert.Equal(5, ServerAnimationGroups.Group(28, Body(MobType.Animal, exists: a => a != 10), NoReplaces, NoReplaces));
        Assert.Equal(2, ServerAnimationGroups.Group(17, Body(MobType.Animal), NoReplaces, NoReplaces));
        // 12 (cast) on an animal is attack 5.
        Assert.Equal(5, ServerAnimationGroups.Group(12, Body(MobType.Animal), NoReplaces, NoReplaces));
    }

    [Fact]
    public void SeaMonstersStandAboveEight() =>
        Assert.Equal(2, ServerAnimationGroups.Group(9, Body(MobType.SeaMonster), NoReplaces, NoReplaces));

    [Fact]
    public void AMonsterDrawnAsAHumanByBodyDefTakesThePeopleAction()
    {
        var b = Body(MobType.Human, original: MobType.Monster);
        Assert.Equal(9, ServerAnimationGroups.Group(4, b, NoReplaces, NoReplaces));    // attack -> one-handed attack
        Assert.Equal(21, ServerAnimationGroups.Group(2, b, NoReplaces, NoReplaces));   // die
        Assert.Equal(4, ServerAnimationGroups.Group(1, b, NoReplaces, NoReplaces));    // stand
    }

    [Fact]
    public void AnAnimalDrawnAsAMonsterTakesTheHighAction()
    {
        var b = Body(MobType.Monster, original: MobType.Animal);
        Assert.Equal(4, ServerAnimationGroups.Group(5, b, NoReplaces, NoReplaces));
        Assert.Equal(2, ServerAnimationGroups.Group(8, b, NoReplaces, NoReplaces));
        Assert.Equal(1, ServerAnimationGroups.Group(2, b, NoReplaces, NoReplaces));
    }

    [Fact]
    public void LabelRemapsFollowTheFlags()
    {
        // LABEL_222: extended low group.
        Assert.Equal(19, ServerAnimationGroups.Label222(ServerAnimationGroups.LowGroupExtended, 1));
        Assert.Equal(6, ServerAnimationGroups.Label222(ServerAnimationGroups.LowGroupExtended, 5, coin: 0));
        Assert.Equal(5, ServerAnimationGroups.Label222(ServerAnimationGroups.LowGroupExtended, 5, coin: 1));
        Assert.Equal(4, ServerAnimationGroups.Label222(ServerAnimationGroups.LowGroupExtended | ServerAnimationGroups.IdleAt8Frame, 6));
        Assert.Equal(17, ServerAnimationGroups.Label222(ServerAnimationGroups.LowGroupExtended | ServerAnimationGroups.IdleAt8Frame, 10));
        // By low group.
        Assert.Equal(5, ServerAnimationGroups.Label222(ServerAnimationGroups.ByLowGroup, 4));
        Assert.Equal(2, ServerAnimationGroups.Label222(ServerAnimationGroups.ByLowGroup, 1));
        // LABEL_190: 0x80 turns 4 into 5; 0x100; people group; 0x200 over 16.
        Assert.Equal(5, ServerAnimationGroups.Label190(ServerAnimationGroups.Unknown80, 4));
        Assert.Equal(1, ServerAnimationGroups.Label190(ServerAnimationGroups.Unknown100, 15));
        Assert.Equal(17, ServerAnimationGroups.Label190(ServerAnimationGroups.Unknown100, 11));
        Assert.Equal(9, ServerAnimationGroups.Label190(ServerAnimationGroups.ByPeopleGroup, 4));
        Assert.Equal(3, ServerAnimationGroups.Label190(ServerAnimationGroups.ByPeopleGroup, 11));
        Assert.Equal(0, ServerAnimationGroups.Label190(ServerAnimationGroups.Unknown200, 19));
        Assert.Equal(1, ServerAnimationGroups.Label190(ServerAnimationGroups.Unknown200, 20));
        Assert.Equal(4, ServerAnimationGroups.Label190(ServerAnimationGroups.Unknown200, 10));
        Assert.Equal(10, ServerAnimationGroups.Label190(ServerAnimationGroups.Unknown1, 21));
        // No flags: as is, kept to 7 bits.
        Assert.Equal(9, ServerAnimationGroups.Label190(0, 9));
    }

    [Fact]
    public void NewAnimationsByType()
    {
        var human = Body(MobType.Human);
        var rider = Body(MobType.Human, mounted: true);
        var monster = Body(MobType.Monster);
        var animal = Body(MobType.Animal);

        // Attack (type 0): people by action, riders their mounted attacks, monsters by mode.
        Assert.Equal(18, ServerAnimationGroups.New(0, 1, 0, human));
        Assert.Equal(31, ServerAnimationGroups.New(0, 0, 0, human));
        Assert.Equal(29, ServerAnimationGroups.New(0, 0, 0, rider));
        Assert.Equal(27, ServerAnimationGroups.New(0, 1, 0, rider));
        Assert.Equal(4, ServerAnimationGroups.New(0, 0, 0, monster));
        Assert.Equal(5, ServerAnimationGroups.New(0, 0, 1, monster));
        Assert.Equal(6, ServerAnimationGroups.New(0, 0, 1, animal));
        Assert.Equal(0, ServerAnimationGroups.New(0, 11, 0, human));
        // An unlisted body counts as a monster.
        Assert.Equal(4, ServerAnimationGroups.New(0, 1, 0, Body(MobType.Human, listed: false)));

        // Block (1-2): people 30, riders and animals keep their action.
        Assert.Equal(30, ServerAnimationGroups.New(1, 0, 0, human));
        Assert.Equal(ServerAnimationGroups.None, ServerAnimationGroups.New(1, 0, 0, rider));
        Assert.Equal(ServerAnimationGroups.None, ServerAnimationGroups.New(2, 0, 0, animal));
        Assert.Equal(15, ServerAnimationGroups.New(2, 0, 1, monster));

        // Spell (11), emote (7), get hit (4).
        Assert.Equal(16, ServerAnimationGroups.New(11, 0, 0, human));
        Assert.Equal(17, ServerAnimationGroups.New(11, 1, 0, human));
        Assert.Equal(12, ServerAnimationGroups.New(11, 0, 0, monster));
        Assert.Equal(32, ServerAnimationGroups.New(7, 0, 0, human));
        Assert.Equal(33, ServerAnimationGroups.New(7, 1, 0, human));
        Assert.Equal(20, ServerAnimationGroups.New(4, 0, 0, human));
        Assert.Equal(7, ServerAnimationGroups.New(4, 0, 0, animal));
        Assert.Equal(10, ServerAnimationGroups.New(4, 0, 0, monster));
    }
}

/// <summary>How a server animation plays on an avatar (TazUO Mobile.ProcessAnimation's server branch).</summary>
public sealed class ServerPlaybackTests
{
    [Fact]
    public void FramesLastLongerWithTheDelay()
    {
        Assert.Equal(0.16f, MobileActions.ServerFrameSeconds(0), 4);
        Assert.Equal(0.32f, MobileActions.ServerFrameSeconds(2), 4);
    }

    /// <summary>The frames shown, one per step, until it ends (or <paramref name="max"/> steps).</summary>
    private static List<int> Steps(ServerPlayback p, int frames, int max = 40)
    {
        var shown = new List<int> { p.Frame(frames) };
        for (int i = 0; i < max && !p.Done; i++)
        {
            p.Step(frames);
            if (!p.Done)
                shown.Add(p.Frame(frames));
        }
        return shown;
    }

    [Fact]
    public void OncePlaysThroughThenEnds()
    {
        var p = new ServerPlayback(9, 0, 0, 1, false, true);
        Assert.Equal(new[] { 0, 1, 2, 3, 4 }, Steps(p, 5));
        Assert.True(p.Done);
    }

    [Fact]
    public void ForwardsPlaysEveryFrameWhateverTheCount()
    {
        // SetAnimation: AnimationFrameCount = forward ? 0 : frameCount, so a forward pass takes the body's own count.
        var p = new ServerPlayback(9, 0, 3, 2, false, true);
        Assert.Equal(new[] { 0, 1, 2, 3, 0, 1, 2, 3 }, Steps(p, 4));
        Assert.True(p.Done);
    }

    [Fact]
    public void BackwardsCountsDownFromTheFrameCount()
    {
        var p = new ServerPlayback(9, 0, 4, 1, false, false);
        Assert.Equal(new[] { 4 % 6, 3, 2, 1, 0 }, Steps(p, 6));
        Assert.True(p.Done);
    }

    [Fact]
    public void ZeroRepeatsOrTheRepeatFlagPlayForEver()
    {
        var forever = new ServerPlayback(9, 0, 0, 0, false, true);
        Steps(forever, 3, max: 30);
        Assert.False(forever.Done);
        var repeating = new ServerPlayback(9, 0, 0, 2, true, true);
        Steps(repeating, 3, max: 30);
        Assert.False(repeating.Done);
    }

    [Fact]
    public void AdvanceStepsByTheFrameTime()
    {
        var p = new ServerPlayback(9, 1, 0, 1, false, true);    // 0.24 s a frame
        p.Advance(0.01f, 4);                                    // first step at once
        Assert.Equal(1, p.Index);
        p.Advance(0.2f, 4);
        Assert.Equal(1, p.Index);
        p.Advance(0.05f, 4);
        Assert.Equal(2, p.Index);
        p.Advance(0.24f * 2, 4);                                // 3, then wrap: done
        Assert.True(p.Done);
        // No frames: nothing moves.
        var none = new ServerPlayback(9, 0, 0, 1, false, true);
        none.Advance(5f, 0);
        Assert.Equal((0, false), (none.Index, none.Done));
    }

    [Fact]
    public void ALongStallDoesNotRunAway()
    {
        var p = new ServerPlayback(9, 0, 0, 0, false, true);
        p.Advance(10_000f, 5);
        Assert.False(p.Done);
        int before = p.Index;
        p.Advance(0.01f, 5);                                    // the next tick takes one step, no backlog
        Assert.Equal((before + 1) % 5, p.Index);
        p.Advance(0.01f, 5);
        Assert.Equal((before + 1) % 5, p.Index);
    }
}

/// <summary><see cref="AnimationLibrary.ServerAction"/> against the install's art.</summary>
[Collection("UO install")]
public sealed class ServerAnimationInstallTests
{
    private readonly UOData _data;

    public ServerAnimationInstallTests(UOInstallFixture fixture) => _data = fixture.Data;

    [Fact]
    public void HumanAttackAndCastActionsHaveFrames()
    {
        AnimationLibrary anims = _data.AnimationLibrary;
        // ModernUO weapon swings (9 one-handed, 11 / 12 / 13 / 14 two-handed, 18 / 19 ranged) and casts (16, 17).
        foreach (int action in new[] { 9, 10, 11, 12, 13, 14, 16, 17, 18, 19, 20, 32, 33 })
        {
            int drawn = anims.ServerAction(400, action, mounted: false, warMode: true);
            Assert.True(anims.AnimationExists(400, drawn), $"action {action} -> {drawn}");
        }
        // A rider's attack: 26.
        Assert.Equal(26, anims.ServerAction(400, 26, mounted: true, warMode: true));
    }

    [Fact]
    public void MonsterAttackIsKeptAndMissingActionsStand()
    {
        AnimationLibrary anims = _data.AnimationLibrary;
        // An orc (17): attack 4 exists.
        Assert.True(anims.AnimationExists(17, 4));
        Assert.Equal(4, anims.ServerAction(17, 4, false, true));
        // Whatever it draws, it has frames for (else it stands).
        for (int action = 0; action < 22; action++)
        {
            int drawn = anims.ServerAction(17, action, false, true);
            Assert.True(drawn == 1 || anims.AnimationExists(17, drawn) || (anims.AnimFlags(17) & ~ServerAnimationGroups.Found) != 0,
                $"action {action} -> {drawn}");
        }
    }

    [Fact]
    public void NewAttackOnAHumanIsAPeopleAttack()
    {
        AnimationLibrary anims = _data.AnimationLibrary;
        int drawn = anims.NewServerAction(400, 0, 1, 0, mounted: false, warMode: true);
        Assert.NotEqual(ServerAnimationGroups.None, drawn);
        Assert.True(anims.AnimationExists(400, drawn));
    }
}
