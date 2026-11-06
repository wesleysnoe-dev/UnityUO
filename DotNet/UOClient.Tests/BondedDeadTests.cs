using UOClient.Core;
using UOClient.IO;
using UOClient.Net;
using UOClient.Unity;

namespace UOClient.Tests;

/// <summary>
/// Dead bonded pets: ModernUO's CreateBondedStatus (OutgoingMobilePackets.cs:44-58), 0xBF length 11, sub 0x19, version 0,
/// serial, bool; sent after the pet's 0x78 while it is dead (Mobile.cs:2947-2955) and with false on resurrection
/// (BaseCreature.cs:3934-3938). Kept as TazUO's <c>Mobile._isDead</c> (ExtendedCommand.cs:311-320).
/// </summary>
public sealed class BondedDeadTests
{
    private const uint Me = 0x00000042, Pet = 0x00000777;

    private static GameSession InWorld()
    {
        var session = new GameSession(new LoginSettings { AnnounceChatLinks = false });
        session.StartOffline(_ => { });
        session.Inject(new PacketWriter(0x1B, 37).UInt32(Me).UInt32(0).UInt16(0x190).UInt16(1495).UInt16(1629).UInt16(0)
            .Byte(0).Zero(9).UInt16(7168).UInt16(4096).Zero(6).ToArray(), false);
        session.Inject(new byte[] { 0x55 }, false);
        return session;
    }

    // A horse (0xCC) at our feet: MobileIncoming 0x78 with no equipment.
    private static void IncomingPet(GameSession s, ushort body = 0xCC) =>
        s.Inject(new PacketWriter(0x78).UInt32(Pet).UInt16(body).UInt16(1496).UInt16(1629).Byte(0).Byte(2).UInt16(0)
            .Byte(0).Byte(3).UInt32(0).ToArray(), true);

    // Byte for byte what ModernUO's CreateBondedStatus writes.
    private static byte[] BondedStatus(uint serial, bool dead) =>
        new byte[] { 0xBF, 0x00, 0x0B, 0x00, 0x19, 0x00, (byte)(serial >> 24), (byte)(serial >> 16), (byte)(serial >> 8), (byte)serial, (byte)(dead ? 1 : 0) };

    [Fact]
    public void TheServerLayoutIsElevenBytes()
    {
        byte[] p = BondedStatus(Pet, true);
        Assert.Equal(11, p.Length);
        Assert.Equal(p.Length, (p[1] << 8) | p[2]);
    }

    [Fact]
    public void DeathAndResurrectionAreKeptAndAnnounced()
    {
        GameSession s = InWorld();
        IncomingPet(s);
        var events = new List<(uint, bool)>();
        int updates = 0;
        s.BondedDeadChanged += (serial, dead) => events.Add((serial, dead));
        s.World.MobileUpdated += m => { if (m.Serial == Pet) updates++; };
        Mobile pet = s.World.Mobiles[Pet];
        int look = pet.AppearanceVersion;

        s.Inject(BondedStatus(Pet, true), true);
        Assert.True(pet.BondedDead);
        Assert.True(pet.IsDead);
        Assert.Equal(0xCC, pet.Body); // the body is kept; only the flag says dead
        Assert.NotEqual(look, pet.AppearanceVersion);

        // Repeated (it comes again with every 0x78 while dead): no new event.
        s.Inject(BondedStatus(Pet, true), true);

        s.Inject(BondedStatus(Pet, false), true);
        Assert.False(pet.BondedDead);
        Assert.False(pet.IsDead);
        Assert.Equal(new[] { (Pet, true), (Pet, false) }, events);
        Assert.Equal(2, updates);
    }

    [Fact]
    public void AnUnknownSerialIsIgnoredAndNotCreated()
    {
        GameSession s = InWorld();
        s.Inject(BondedStatus(0x999, true), true);
        Assert.False(s.World.Mobiles.ContainsKey(0x999));
    }

    [Fact]
    public void ShortPacketsAreIgnored()
    {
        GameSession s = InWorld();
        IncomingPet(s);
        // No bool, and no serial at all (lengths 10 and 6).
        s.Inject(new byte[] { 0xBF, 0x00, 0x0A, 0x00, 0x19, 0x00, 0x00, 0x00, 0x07, 0x77 }, true);
        s.Inject(new byte[] { 0xBF, 0x00, 0x06, 0x00, 0x19, 0x00 }, true);
        Assert.False(s.World.Mobiles[Pet].BondedDead);
        Assert.Equal(SessionState.InWorld, s.State);
    }

    [Fact]
    public void Version2StatLocksStillApply()
    {
        GameSession s = InWorld();
        s.Inject(new byte[] { 0xBF, 0x00, 0x0C, 0x00, 0x19, 0x02, 0x00, 0x00, 0x00, 0x42, 0x00, (2 << 4) | (1 << 2) | 0 }, true);
        Mobile me = s.World.Player;
        Assert.Equal(2, me.StrLock);
        Assert.Equal(1, me.DexLock);
        Assert.Equal(0, me.IntLock);
        Assert.False(me.BondedDead);
    }

    [Fact]
    public void LeavingViewForgetsIt()
    {
        // A new 0x78 after the pet left is a new mobile; the server sends the status again if it is still dead.
        GameSession s = InWorld();
        IncomingPet(s);
        s.Inject(BondedStatus(Pet, true), true);
        s.Inject(new PacketWriter(0x1D, 5).UInt32(Pet).ToArray(), false);
        IncomingPet(s);
        Assert.False(s.World.Mobiles[Pet].BondedDead);
    }

    [Theory]
    [InlineData(400, true)]
    [InlineData(403, true)]
    [InlineData(0x00B7, true)] // savage
    [InlineData(605, true)]
    [InlineData(694, true)]
    [InlineData(0x03DB, true)]
    [InlineData(0x04E5, true)]
    [InlineData(0x00CC, false)] // horse
    [InlineData(0x00DC, false)] // llama
    [InlineData(0x0317, false)] // beetle
    [InlineData(970, false)]
    public void HumanGraphicIsTazUOsTable(int body, bool human) => Assert.Equal(human, MobileLook.IsHumanGraphic(body));
}

/// <summary>The dead pet's look against the installed data.</summary>
[Collection("UO install")]
public sealed class BondedDeadInstallTests
{
    private readonly UOData _data;

    public BondedDeadInstallTests(UOInstallFixture fixture) => _data = fixture.Data;

    [Theory]
    [InlineData(0x00CC, 0)]      // horse
    [InlineData(0x0317, 0x0481)] // beetle, hued
    [InlineData(0x00DC, 0x8021)] // llama, hue with the partial bit
    public void ADeadPetDrawsGrey(int body, int hue)
    {
        MobileLook alive = MobileLook.From(_data, body, hue, Array.Empty<(byte, int, int)>());
        MobileLook dead = MobileLook.From(_data, body, hue, Array.Empty<(byte, int, int)>(), bondedDead: true);
        Assert.Single(dead.Layers);
        Assert.Equal(MobileLook.DeadHue, dead.Layers[0].Hue);
        Assert.False(dead.Layers[0].Partial);
        Assert.Equal(alive.Layers[0].Body, dead.Layers[0].Body); // same art, same body
        Assert.Equal(body, dead.Body);
        Assert.NotEqual(MobileLook.DeadHue, alive.Layers[0].Hue);
    }

    [Fact]
    public void HumansAndGhostsKeepTheirHues()
    {
        var robe = new (byte, int, int)[] { (22, 0x204E, 0) };
        // A human flagged dead keeps its hue (TazUO: isDead && !isHuman only); a ghost body keeps its hue as before.
        Assert.Equal(0x03EA, MobileLook.From(_data, 400, 0x83EA, robe, bondedDead: true).Layers[0].Hue);
        Assert.Equal(0x03EA, MobileLook.From(_data, 402, 0x83EA, robe).Layers[0].Hue);
        // 970 is not touched by the flag-less path.
        int h970 = MobileLook.From(_data, 970, 0, Array.Empty<(byte, int, int)>()).Layers[0].Hue;
        Assert.NotEqual(MobileLook.DeadHue, h970);
    }
}
