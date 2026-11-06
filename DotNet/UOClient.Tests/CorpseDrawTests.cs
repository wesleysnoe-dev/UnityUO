using UOClient.Core;
using UOClient.IO;
using UOClient.IO.Formats;
using UOClient.Net;
using UOClient.Unity;

namespace UOClient.Tests;

/// <summary>
/// Corpses as TazUO draws them (ItemView.DrawCorpse, Item.CheckGraphicChange, CorpseManager, CorpseEquipment): the facing
/// and fall read off the world item packet, hidden while the owner falls, then turned to the owner's facing and drawn as
/// the body's last death frame through <see cref="CorpseDraw.Select{T}"/>, the choice ItemView makes.
/// </summary>
public sealed class CorpseDrawTests
{
    private const uint Player = 0x00000042, Man = 0x00000099, Corpse = 0x40000123;
    private const uint Shirt = 0x40000200, Hair = 0x40000201, Pack = 0x40000202, Sword = 0x40000203;

    internal static GameSession InWorld()
    {
        var session = new GameSession(new LoginSettings());
        session.Inject(new PacketWriter(0x1B, 37).UInt32(Player).UInt32(0).UInt16(0x190).UInt16(1495).UInt16(1629).UInt16(0)
            .Byte(0).Zero(9).UInt16(7168).UInt16(4096).Zero(6).ToArray(), false);
        return session;
    }

    // 0x77: serial, body, x, y, z, direction, hue, flags, notoriety.
    internal static byte[] Moving(uint serial, ushort body, byte direction) =>
        new PacketWriter(0x77, 17).UInt32(serial).UInt16(body).UInt16(1496).UInt16(1629).Byte(0)
            .Byte(direction).UInt16(0x83EA).Byte(0).Byte(3).ToArray();

    // 0xF3 as ModernUO's CreateWorldEntity writes it (OutgoingEntityPackets.cs:88-152): the byte after z is Item.Light,
    // the same field as Item.Direction, which a corpse takes from its owner.
    internal static byte[] GroundItem(uint serial, ushort graphic, ushort amount, byte direction, ushort hue = 0) =>
        new PacketWriter(0xF3, 26).UInt16(1).Byte(0).UInt32(serial).UInt16(graphic).Byte(0).UInt16(amount).UInt16(amount)
            .UInt16(1496).UInt16(1629).Byte(0).Byte(direction).UInt16(hue).Byte(0).UInt16(0).ToArray();

    // 0xAF: killed, corpse, running (ModernUO's CreateDeathAnimation writes 0 there).
    internal static byte[] DeathAnimation(uint killed, uint corpse, uint running = 0) =>
        new PacketWriter(0xAF, 13).UInt32(killed).UInt32(corpse).UInt32(running).ToArray();

    // 0x89 as CorpsePackets.SendCorpseEquip: corpse, (layer + 1, serial)..., 0.
    internal static byte[] CorpseEquip(uint corpse, params (byte Layer, uint Serial)[] worn)
    {
        var w = new PacketWriter(0x89).UInt32(corpse);
        foreach (var (layer, serial) in worn)
            w.Byte(layer + 1).UInt32(serial);
        return w.Byte(0).ToArray();
    }

    // 0x3C as CorpsePackets.SendCorpseContent (grid byte included).
    internal static byte[] CorpseContent(uint corpse, params (uint Serial, ushort Graphic, ushort Hue)[] items)
    {
        var w = new PacketWriter(0x3C).UInt16(items.Length);
        foreach (var (serial, graphic, hue) in items)
            w.UInt32(serial).UInt16(graphic).Byte(0).UInt16(1).UInt16(0).UInt16(0).Byte(0).UInt32(corpse).UInt16(hue);
        return w.ToArray();
    }

    [Fact]
    public void CorpseReadsFacingAndSecondFallOffTheDirectionByte()
    {
        GameSession session = InWorld();
        session.Inject(GroundItem(Corpse, 0x2006, 400, 0x83, 0x83EA), false);
        WorldItem c = session.World.Items[Corpse];
        Assert.True(c.IsCorpse);
        Assert.Equal(400, c.Amount);         // the body (Corpse.cs:211)
        Assert.Equal(3, c.Layer);
        Assert.True(c.UsedLayer);            // run bit: the second fall (Item.cs:434-441)

        session.Inject(GroundItem(Corpse, 0x2006, 400, 0x05), false);
        Assert.Equal(5, c.Layer);
        Assert.False(c.UsedLayer);

        // Any other item keeps the byte as it is (a light source's light) and has no corpse facing.
        session.Inject(GroundItem(0x40000300, 0x0A0F, 1, 0x1D), false);
        WorldItem lamp = session.World.Items[0x40000300];
        Assert.False(lamp.IsCorpse);
        Assert.Equal(0x1D, lamp.Direction);
        Assert.Equal(0, lamp.Layer);
    }

    [Fact]
    public void OldWorldItemPacketCarriesTheDirectionWhenFlagged()
    {
        GameSession session = InWorld();
        // 0x1A: serial|0x80000000 (amount follows), graphic, amount, x|0x8000 (direction follows), y, direction, z.
        session.Inject(new PacketWriter(0x1A).UInt32(Corpse | 0x80000000).UInt16(0x2006).UInt16(0x0190)
            .UInt16(1496 | 0x8000).UInt16(1629).Byte(0x82).Byte(0).ToArray(), true);
        WorldItem c = session.World.Items[Corpse];
        Assert.True(c.IsCorpse);
        Assert.Equal(2, c.Layer);
        Assert.True(c.UsedLayer);
    }

    [Fact]
    public void FallHidesTheCorpseThenTurnsItToTheOwnersFacing()
    {
        GameSession session = InWorld();
        session.Inject(Moving(Man, 400, 6), false);
        session.Inject(GroundItem(Corpse, 0x2006, 400, 0x02), false);
        session.Inject(DeathAnimation(Man, Corpse), false);

        CorpseFalls falls = session.World.CorpseFalls;
        Assert.True(falls.Exists(Corpse, 0));      // ItemView.cs:183
        Assert.True(falls.Exists(0, Man));
        Assert.Equal(2, session.World.Items[Corpse].Layer);

        falls.Finish(Man);                         // the fall played through (Mobile.cs:706-714)
        Assert.False(falls.Exists(Corpse, 0));
        Assert.Equal(6, session.World.Items[Corpse].Layer); // CorpseManager.cs:72, 0xAF's running 0
        // The corpse stays an ordinary item for use, pick-up and loot.
        Assert.Equal(WorldItem.CorpseGraphic, session.World.Items[Corpse].Graphic);
    }

    [Fact]
    public void NoCorpseOrUnknownOwnerAddsNoFall()
    {
        GameSession session = InWorld();
        session.Inject(Moving(Man, 400, 6), false);
        session.Inject(DeathAnimation(Man, 0), false);
        session.Inject(DeathAnimation(0x00000777, Corpse), false);
        Assert.Equal(0, session.World.CorpseFalls.Count);
    }

    [Fact]
    public void CorpseEquipNamesWhatTheCorpseWearsUntilLooted()
    {
        GameSession session = InWorld();
        session.Inject(GroundItem(Corpse, 0x2006, 400, 0x03, 0x83EA), false);
        int updates = 0;
        session.World.ItemUpdated += i => { if (i.Serial == Corpse) updates++; };

        session.Inject(CorpseContent(Corpse, (Shirt, 0x1517, 0x0021), (Hair, 0x203B, 0x044E), (Sword, 0x13B9, 0)), true);
        session.Inject(CorpseEquip(Corpse, (5, Shirt), (11, Hair), (21, Pack), (1, Sword)), true);
        Assert.Equal(1, updates);

        WorldItem c = session.World.Items[Corpse];
        Assert.DoesNotContain((byte)21, c.CorpseEquipment.Keys);   // the backpack is never drawn (CorpseEquipment.cs:31)
        var worn = session.World.CorpseWears(c).OrderBy(e => e.Layer).ToArray();
        Assert.Equal(new (byte, int, int)[] { (1, 0x13B9, 0), (5, 0x1517, 0x21), (11, 0x203B, 0x44E) }, worn);

        // Looted: the sword goes to our pack and drops out of the corpse's outfit.
        session.Inject(new PacketWriter(0x25, 21).UInt32(Sword).UInt16(0x13B9).Byte(0).UInt16(1).UInt16(50).UInt16(60).Byte(0)
            .UInt32(0x40000999).UInt16(0).ToArray(), false);
        Assert.DoesNotContain(session.World.CorpseWears(c), e => e.Layer == 1);
    }

    [Fact]
    public void CorpseEquipForSomethingElseIsIgnored()
    {
        GameSession session = InWorld();
        session.Inject(GroundItem(0x40000300, 0x0E75, 1, 0), false);
        session.Inject(CorpseEquip(0x40000300, (5, Shirt)), true);
        Assert.Null(session.World.Items[0x40000300].CorpseEquipment);
        session.Inject(CorpseEquip(0x40000301, (5, Shirt)), true);   // unknown: nothing to attach it to
        Assert.False(session.World.Items.ContainsKey(0x40000301));
    }

    /// <summary>Frames per (action, file direction): null while building, empty for none, as MobileArt.Get answers.</summary>
    private static Func<int, int, int[]> Built(List<(int, int)> asked, params (int Action, int Dir, int[] Frames)[] built)
    {
        var map = built.ToDictionary(b => (b.Action, b.Dir), b => b.Frames);
        return (a, d) =>
        {
            asked.Add((a, d));
            return map.TryGetValue((a, d), out int[] f) ? f : null;
        };
    }

    [Fact]
    public void HandoffDrawsTheLastDeathFrameAtTheOwnersFacing()
    {
        GameSession session = InWorld();
        session.Inject(Moving(Man, 400, 6), false);
        session.Inject(GroundItem(Corpse, 0x2006, 400, 0x02), false);
        session.Inject(DeathAnimation(Man, Corpse), false);
        WorldItem c = session.World.Items[Corpse];
        CorpseFalls falls = session.World.CorpseFalls;

        // Facing 6 is stored as file direction 3, unmirrored; facing 2 as 1, mirrored (AnimationFile.FileDirection).
        var asked = new List<(int, int)>();
        var get = Built(asked, (21, 3, new[] { 10, 11, 12, 13, 14, 15 }), (21, 1, new[] { 20, 21 }));

        // While falling: hidden, but the frames are already asked for.
        CorpseDraw.Pick pick = CorpseDraw.Select(c, 21, falls.Exists(Corpse, 0), get, out _);
        Assert.Equal(CorpseDraw.Art.Hidden, pick.Art);
        Assert.Equal((21, 1), asked[^1]);

        falls.Finish(Man);
        pick = CorpseDraw.Select(c, 21, falls.Exists(Corpse, 0), get, out int[] frames);
        Assert.Equal(CorpseDraw.Art.Body, pick.Art);    // never the 0x2006 art when frames exist
        Assert.Equal(400, pick.Body);
        Assert.Equal(21, pick.Action);
        Assert.Equal(6, pick.Direction);
        Assert.Equal(3, pick.FileDir);
        Assert.False(pick.Mirror);
        Assert.Equal(5, pick.Frame);                    // the last: AnimIndex 99 clamped (Item.cs:622-630)
        Assert.Equal(15, frames[pick.Frame]);
    }

    [Fact]
    public void MissingDeathFramesFallBackToTheCorpseArtAndBuildingWaits()
    {
        GameSession session = InWorld();
        session.Inject(GroundItem(Corpse, 0x2006, 0x0123, 0x04), false);
        WorldItem c = session.World.Items[Corpse];
        var asked = new List<(int, int)>();

        Assert.Equal(CorpseDraw.Art.Building, CorpseDraw.Select(c, 2, false, Built(asked), out _).Art);
        Assert.Equal(CorpseDraw.Art.StaticArt, CorpseDraw.Select(c, 2, false, Built(asked, (2, 1, Array.Empty<int>())), out _).Art);
        Assert.Equal(CorpseDraw.Art.Body, CorpseDraw.Select(c, 2, false, Built(asked, (2, 1, new[] { 7 })), out _).Art);
    }
}

/// <summary><see cref="CorpseDraw"/> against the install: the corpse's look and death action match the fall's.</summary>
[Collection("UO install")]
public sealed class CorpseDrawInstallTests
{
    private readonly UOData _data;

    public CorpseDrawInstallTests(UOInstallFixture fixture) => _data = fixture.Data;

    private const uint Man = 0x00000099, Corpse = 0x40000123, Shirt = 0x40000200, Horse = 0x40000204;

    [Fact]
    public void HumanCorpseWearsWhatItsOwnerWoreAndLiesInTheSameFall()
    {
        GameSession session = CorpseDrawTests.InWorld();
        session.Inject(CorpseDrawTests.GroundItem(Corpse, 0x2006, 400, 0x03, 0x83EA), false);
        session.Inject(CorpseDrawTests.CorpseContent(Corpse, (Shirt, 0x1517, 0x0021), (Horse, 0x3EA2, 0)), true);
        session.Inject(CorpseDrawTests.CorpseEquip(Corpse, (5, Shirt), (25, Horse)), true);
        WorldItem c = session.World.Items[Corpse];

        MobileLook corpse = CorpseDraw.Look(_data, session.World, c);
        MobileLook owner = MobileLook.From(_data, 400, 0x83EA, new (byte, int, int)[] { (5, 0x1517, 0x0021) });
        Assert.False(corpse.Mounted);                // a corpse never lies on its mount
        Assert.Equal(owner.Key, corpse.Key);         // so the fall's frames are the corpse's frames
        Assert.Equal(owner.DeathActions.First, CorpseDraw.Action(_data.AnimationLibrary, c));
    }

    [Theory]
    [InlineData(400, false, 21)]   // human, first fall
    [InlineData(400, true, 22)]    // human, second fall (run bit)
    [InlineData(753, false, 2)]    // UOP gargoyle slave
    public void CorpseActionIsTheFallsActionAndItsLastFrameIsDrawn(int body, bool second, int expected)
    {
        GameSession session = CorpseDrawTests.InWorld();
        session.Inject(CorpseDrawTests.GroundItem(Corpse, 0x2006, (ushort)body, (byte)(3 | (second ? 0x80 : 0))), false);
        WorldItem c = session.World.Items[Corpse];

        int action = CorpseDraw.Action(_data.AnimationLibrary, c);
        Assert.Equal(expected, action);
        MobileLook look = MobileLook.From(_data, body, 0, Array.Empty<(byte, int, int)>());
        Assert.Equal(action, second ? look.DeathActions.Second : look.DeathActions.First);

        CorpseDraw.Pick pick = CorpseDraw.Select(c, action, false,
            (a, d) => _data.AnimationLibrary.Read(body, a, d), out AnimationFrame[] frames);
        Assert.Equal(CorpseDraw.Art.Body, pick.Art);
        Assert.Equal(frames.Length - 1, pick.Frame);
    }
}
