using UOClient.Core;
using UOClient.IO;
using UOClient.Net;
using UOClient.Unity;
using UOClient.Unity.UI;

namespace UOClient.Tests;

/// <summary>
/// Our own paperdoll's TazUO equipment slots (PaperDollGump.AddEquipSlot / EquipmentSlot / ItemGumpFixed), the
/// combat and racial books (CombatBookGump at CV_7000+, RacialAbilitiesBookGump), and the weapon ability request as
/// ModernUO's EncodedCommand + WeaponAbilityPackets.SetAbility read it.
/// </summary>
public sealed class PaperdollSlotsTests
{
    private const uint Me = 0x00000042;

    [Fact]
    public void SlotsAreTazUOsColumnsInItsOrder()
    {
        // Helmet, Earrings, Neck, Ring, Bracelet, Tunic, OneHanded, TwoHanded, Talisman (Layers.cs values).
        Assert.Equal(new byte[] { 6, 18, 10, 8, 14, 17, 1, 2, 9 }, PaperdollWindow.LeftSlotLayers);
        // Robe, Gloves, Torso, Shirt, Arms, Pants, Skirt, Cloak, Waist, Shoes.
        Assert.Equal(new byte[] { 22, 7, 13, 5, 19, 4, 23, 20, 12, 3 }, PaperdollWindow.RightSlotLayers);

        PaperdollWindow.Slot[] s = PaperdollWindow.Slots;
        Assert.Equal(19, s.Length);
        for (int i = 0; i < 9; i++)
            Assert.Equal((s[i].Layer, 2, 67 + 21 * i), (PaperdollWindow.LeftSlotLayers[i], s[i].X, s[i].Y));
        // The right column starts one slot up (Position_Y_RightSlots + 21 * (count - 1)).
        for (int i = 0; i < 10; i++)
            Assert.Equal((s[9 + i].Layer, 166, 46 + 21 * i), (PaperdollWindow.RightSlotLayers[i], s[9 + i].X, s[9 + i].Y));

        // Nothing for hair, face, beard, backpack, legs or mount; no layer twice.
        Assert.Equal(19, s.Select(x => x.Layer).Distinct().Count());
        Assert.DoesNotContain(s, x => x.Layer is 0x0B or 0x0F or 0x10 or 0x15 or 0x18 or 0x19);
        // Each box sits inside the 262x324 background and clear of the button column at x 185.
        Assert.All(s, x => Assert.True(x.X >= 0 && x.X + PaperdollWindow.SlotWidth <= PaperdollWindow.ButtonX && x.Y + PaperdollWindow.SlotHeight <= 324));
    }

    [Fact]
    public void SlotChromeAndNamesFollowEquipmentSlot()
    {
        Assert.Equal((0x243A, 0x2344, 19, 20, 18),
            (PaperdollWindow.SlotBackground, PaperdollWindow.SlotBorder, PaperdollWindow.SlotWidth, PaperdollWindow.SlotHeight, PaperdollWindow.SlotItemSize));
        Assert.Equal(new[] { "Helmet", "Earrings", "Neck", "Ring", "Bracelet", "Tunic", "One Handed", "Two Handed", "Talisman" },
            PaperdollWindow.LeftSlotLayers.Select(PaperdollWindow.SlotName));
        Assert.Equal(new[] { "Robe", "Gloves", "Torso", "Shirt", "Arms", "Pants", "Skirt", "Cloak", "Waist", "Shoes" },
            PaperdollWindow.RightSlotLayers.Select(PaperdollWindow.SlotName));
    }

    [Fact]
    public void ItemArtIsSqueezedOrCentredAsItemGumpFixed()
    {
        Assert.Equal((0, 0, 18, 18), PaperdollWindow.SlotItemRect(40, 30));   // larger both ways: squeezed to 18x18
        Assert.Equal((4, 6, 10, 6), PaperdollWindow.SlotItemRect(10, 6));     // smaller: own size, (9 - 5, 9 - 3)
        Assert.Equal((0, 4, 18, 11), PaperdollWindow.SlotItemRect(25, 11));   // wide and short
        Assert.Equal((0, 0, 18, 18), PaperdollWindow.SlotItemRect(18, 18));
    }

    [Fact]
    public void ArtBoundsKeepTazUOsMaxMinusMin()
    {
        var px = new uint[8 * 6];
        px[1 * 8 + 2] = 0xFF000000; // (2, 1)
        px[4 * 8 + 6] = 0xFF000000; // (6, 4)
        Assert.Equal((2, 1, 4, 3), PaperdollWindow.ArtBounds(px, 8, 6)); // max - min, as Art.cs:330
        Assert.Equal((0, 0, 0, 0), PaperdollWindow.ArtBounds(new uint[4], 2, 2));
    }

    [Theory]
    [InlineData(1f)]
    [InlineData(1.08f)]
    [InlineData(1.44f)]
    [InlineData(2f)]
    public void SlotPickIsTheWholeBoxByTheShownPixel(float scale)
    {
        PaperdollWindow.Slot[] s = PaperdollWindow.Slots;
        int At(float x, float y) => PaperdollWindow.SlotAt(s.Length, i => s[i], x, y, scale);
        float half = 0.5f / scale; // a display pixel's top-left shows the pixel its centre samples
        for (int i = 0; i < s.Length; i++)
        {
            Assert.Equal(i, At(s[i].X + 9.5f, s[i].Y + 10f));
            Assert.Equal(i, At(s[i].X - half, s[i].Y - half));                     // first pixel of the box
            Assert.Equal(i, At(s[i].X + 19 - half - 0.01f, s[i].Y + 20 - half - 0.01f)); // last pixel
            Assert.NotEqual(i, At(s[i].X + 19 - half, s[i].Y + 10));                 // one pixel past the right edge
        }
        // Between the boxes (left column x 21..) nothing is hit; a slot's step is 21 and it is 20 high: a gap row.
        Assert.Equal(-1, At(40, 100));
        Assert.Equal(-1, At(10, 67 + 20 - half));
    }

    [Fact]
    public void BooksAndTheProfileShift()
    {
        Assert.Equal((0x2B34, 150, 198), (PaperdollWindow.CombatBookIcon, PaperdollWindow.CombatBookX, PaperdollWindow.CombatBookY));
        Assert.Equal((0x2B28, 23, 200, 14), (PaperdollWindow.RacialBookIcon, PaperdollWindow.RacialBookX, PaperdollWindow.RacialBookY, PaperdollWindow.RacialBookWidth));
        Assert.Equal(25, PaperdollWindow.ProfileXFor(false));
        Assert.Equal(39, PaperdollWindow.ProfileXFor(true));
        Assert.Equal(25 + 30 - 4, PaperdollWindow.PartyX(30));
        Assert.Equal(39 + 30 - 4, PaperdollWindow.PartyX(30, racialBook: true));
        // The racial book is clear of the left slot column.
        Assert.True(PaperdollWindow.RacialBookX >= PaperdollWindow.LeftSlotsX + PaperdollWindow.SlotWidth);
    }

    // ---- weapon abilities ----

    [Fact]
    public void WeaponAbilitiesFollowTazUOsMap()
    {
        Assert.Equal((11, 5), WeaponAbilities.Of(0, null));                     // bare hands: Paralyzing Blow, Disarm
        Assert.Equal((7, 1), WeaponAbilities.Of(0x13FF, null));                 // katana: Double Strike, Armor Ignore
        Assert.Equal((3, 9), WeaponAbilities.Of(0x0F4F, null));                 // crossbow: Concussion Blow, Mortal Strike
        Assert.Equal((11, 5), WeaponAbilities.Of(0x1234, _ => 0));               // unknown: the default
        // A graphic not in the map takes the one beside it with the same AnimId (graphic - 1 first).
        Assert.Equal((7, 1), WeaponAbilities.Of(0x1400, g => g == 0x13FF || g == 0x1400 ? 0x22 : 0));
        Assert.Equal(31, WeaponAbilities.Count);
        Assert.Equal("Mystic Arc", WeaponAbilities.Names[31]);
        Assert.Equal(0x521E, WeaponAbilities.Icon(31));
        Assert.All(WeaponAbilities.Map.Values, a => Assert.True(a.Item1 is >= 1 and <= 31 && a.Item2 is >= 1 and <= 31));
    }

    [Fact]
    public void CombatAbilityRequestIsWhatModernUOReads()
    {
        byte[] p = GameSession.CombatAbilityRequest(Me, 11);
        Assert.Equal(new byte[] { 0xD7, 0, 15, 0, 0, 0, 0x42, 0, 0x19, 0, 0, 0, 0, 11, 0x0A }, p);

        // IncomingPlayerPackets.EncodedCommand: serial, packet id; then EncodedReader.ReadInt32: a type byte that must
        // be 0, then the big-endian int32 SetAbility takes as the index (1-31 sets, else clears).
        var r = new PacketReader(p, true);
        Assert.Equal(Me, r.ReadUInt32());
        Assert.Equal(0x19, r.ReadUInt16());
        Assert.Equal(0, r.ReadByte());
        Assert.Equal(11, r.ReadInt32());
        Assert.Equal(0, new PacketReader(GameSession.CombatAbilityRequest(Me, 0), true) is var c && c.ReadUInt32() == Me && c.ReadUInt16() == 0x19 && c.ReadByte() == 0 ? c.ReadInt32() : -1);
    }

    private static (GameSession, List<byte[]>) InWorld(uint features)
    {
        var sent = new List<byte[]>();
        var session = new GameSession(new LoginSettings { AnnounceChatLinks = false });
        session.StartOffline(sent.Add);
        session.Inject(new PacketWriter(0xB9, 5).UInt32(features).ToArray(), false);
        session.Inject(new PacketWriter(0x1B, 37).UInt32(Me).UInt32(0).UInt16(0x190).UInt16(1495).UInt16(1629).UInt16(0)
            .Byte(0).Zero(9).UInt16(7168).UInt16(4096).Zero(6).ToArray(), false);
        session.Inject(new byte[] { 0x55 }, false);
        sent.Clear();
        return (session, sent);
    }

    private static void Wear(GameSession s, uint serial, ushort graphic, byte layer) =>
        s.Inject(new PacketWriter(0x2E, 15).UInt32(serial).UInt16(graphic).Byte(0).Byte(layer).UInt32(Me).UInt16(0).ToArray(), false);

    [Fact]
    public void UsingAnAbilityTogglesItAsTazUO()
    {
        // ModernUO's ExpansionAOS feature flags: LBR | AOS | LiveAccount.
        var (s, sent) = InWorld(0x08 | 0x10 | 0x80);
        Assert.True(s.AosFeatures);
        Assert.Null(s.WornWeapon());

        Assert.True(s.UseWeaponAbility(false, 11));                    // primary on
        Assert.True(s.IsWeaponAbilityOn(false));
        Assert.False(s.IsWeaponAbilityOn(true));
        Assert.True(s.UseWeaponAbility(false, 11));                    // again: off, index 0
        Assert.False(s.IsWeaponAbilityOn(false));
        Assert.True(s.UseWeaponAbility(true, 5));                      // secondary on
        Assert.True(s.UseWeaponAbility(false, 11));                    // primary on: the secondary goes off
        Assert.True(s.IsWeaponAbilityOn(false));
        Assert.False(s.IsWeaponAbilityOn(true));
        Assert.Equal(new byte[] { 11, 0, 5, 11 }, sent.Select(p => p[13]));
        Assert.All(sent, p => Assert.Equal((0xD7, 0x19), (p[0], p[7] << 8 | p[8])));

        // ModernUO's SendClearWeaponAbility after the swing: 0xBF 0x21.
        s.Inject(new byte[] { 0xBF, 0, 5, 0, 0x21 }, true);
        Assert.False(s.IsWeaponAbilityOn(false));
    }

    [Fact]
    public void ChangingWeaponTurnsTheAbilityOff()
    {
        var (s, _) = InWorld(0x10);
        Wear(s, 0x40000001, 0x13FF, 1);
        Assert.Equal(0x13FFu, s.WornWeapon()!.Value.Graphic);
        s.UseWeaponAbility(false, 7);
        Assert.True(s.IsWeaponAbilityOn(false));
        Wear(s, 0x40000002, 0x0F5E, 1); // UpdateAbilities resets the flags with the new weapon's pair
        Assert.False(s.IsWeaponAbilityOn(false));
    }

    [Fact]
    public void NoAosFeaturesOrNotInWorldSendsNothing()
    {
        var (s, sent) = InWorld(0x08); // pre-AoS: TazUO's stun / disarm requests, which we do not send
        Assert.False(s.UseWeaponAbility(false, 11));
        Assert.False(s.IsWeaponAbilityOn(false));
        Assert.Empty(sent);

        var out_ = new GameSession(new LoginSettings { AnnounceChatLinks = false });
        Assert.False(out_.UseWeaponAbility(false, 11));
        var (s2, sent2) = InWorld(0x10);
        Assert.False(s2.UseWeaponAbility(false, 0));
        Assert.False(s2.UseWeaponAbility(false, 32));
        Assert.Empty(sent2);
    }

    [Fact]
    public void Old2ByteSupportedFeaturesReads()
    {
        var session = new GameSession(new LoginSettings { AnnounceChatLinks = false, Version = new ClientVersion(5, 0, 9, 1) });
        session.Inject(new PacketWriter(0xB9, 3).UInt16(0x801B).ToArray(), false);
        Assert.Equal(0x801Bu, session.SupportedFeatures);
        Assert.True(session.AosFeatures);
    }

    // ---- books ----

    [Fact]
    public void CombatBookIsTazUOsCv7000Book()
    {
        Assert.Equal((0x2B02, 3, 34), (CombatBookWindow.Background, CombatBookWindow.IndexPages, CombatBookWindow.MaxPage));
        CombatBookWindow.IndexEntry[] ix = CombatBookWindow.Index;
        Assert.Equal(Enumerable.Range(1, 31), ix.Select(e => e.Ability));
        // Page 1: nine on the left from (52, 42) down 15, four on the right at x 215; then page 2 the same; page 3 the rest.
        Assert.Equal(Enumerable.Range(0, 9).Select(i => (1, 52, 42 + 15 * i)), ix.Take(9).Select(e => (e.Page, e.X, e.Y)));
        Assert.Equal(Enumerable.Range(0, 4).Select(i => (1, 215, 42 + 15 * i)), ix.Skip(9).Take(4).Select(e => (e.Page, e.X, e.Y)));
        Assert.Equal((2, 52, 42), (ix[13].Page, ix[13].X, ix[13].Y));
        Assert.Equal((3, 52, 42 + 15 * 4), (ix[30].Page, ix[30].X, ix[30].Y));
        Assert.Equal(Enumerable.Range(4, 31), ix.Select(e => e.ToPage));
        Assert.True(CombatBookWindow.ShowsAbilityIcons(3));
        Assert.False(CombatBookWindow.ShowsAbilityIcons(4));
        Assert.Equal((62, 98), CombatBookWindow.ItemLine(0));
        Assert.Equal((62, 178), CombatBookWindow.ItemLine(5));
        Assert.Equal((215, 34), CombatBookWindow.ItemLine(6));
        Assert.Equal(31, CombatBookWindow.Items.Length);
        Assert.Equal((1061693, 1028838), (CombatBookWindow.NameCliloc(1), CombatBookWindow.IconCliloc(1)));
        Assert.Equal("Two Handed Axe", CombatBookWindow.CapitalizeAllWords("two handed axe"));
    }

    [Fact]
    public void RacialBookIsTazUOsPassiveList()
    {
        Assert.Equal(RacialAbilitiesWindow.Race.Human, RacialAbilitiesWindow.RaceOf(400));
        Assert.Equal(RacialAbilitiesWindow.Race.Elf, RacialAbilitiesWindow.RaceOf(606));
        Assert.Equal(RacialAbilitiesWindow.Race.Gargoyle, RacialAbilitiesWindow.RaceOf(667));

        RacialAbilitiesWindow.Book human = RacialAbilitiesWindow.BookOf(RacialAbilitiesWindow.Race.Human);
        Assert.Equal(new[] { "Strong Back", "Tough", "Workhorse", "Jack of All Trades" }, human.Names);
        Assert.Equal((0x5DD0, 1112198, 4), (human.FirstIcon, human.FirstCliloc, human.MaxPage));
        Assert.Equal((0x5DD4, 1112202, 5), (RacialAbilitiesWindow.BookOf(RacialAbilitiesWindow.Race.Elf).FirstIcon,
            RacialAbilitiesWindow.BookOf(RacialAbilitiesWindow.Race.Elf).FirstCliloc, RacialAbilitiesWindow.BookOf(RacialAbilitiesWindow.Race.Elf).MaxPage));
        Assert.Equal((1, 62, 52), RacialAbilitiesWindow.IndexLine(0));
        Assert.Equal((1, 225, 52), RacialAbilitiesWindow.IndexLine(3));
        Assert.Equal((2, 62, 52), RacialAbilitiesWindow.IndexLine(6));
        Assert.Equal(new[] { 2, 2, 3, 3 }, Enumerable.Range(0, 4).Select(RacialAbilitiesWindow.PageOf));
        Assert.Equal((62, 112), RacialAbilitiesWindow.DetailX(0));
        Assert.Equal((225, 275), RacialAbilitiesWindow.DetailX(1));
        Assert.All(Enumerable.Range(0, 6), i => Assert.True(RacialAbilitiesWindow.IsPassive(RacialAbilitiesWindow.Race.Elf, i)));
        Assert.False(RacialAbilitiesWindow.IsPassive(RacialAbilitiesWindow.Race.Gargoyle, 0)); // Flying
    }
}

/// <summary>The slot, book and ability art the paperdoll and its books draw, in the 7.0.23.1 install.</summary>
[Collection("UO install")]
public sealed class PaperdollSlotsInstallTests
{
    private readonly UOData _data;

    public PaperdollSlotsInstallTests(UOInstallFixture fixture) => _data = fixture.Data;

    [Fact]
    public void TheArtExists()
    {
        Assert.Equal((19, 20), Size(PaperdollWindow.SlotBorder));
        Assert.NotNull(_data.GetGump(PaperdollWindow.SlotBackground));
        Assert.Equal((15, 47), Size(PaperdollWindow.CombatBookIcon));
        Assert.Equal((15, 47), Size(PaperdollWindow.RacialBookIcon));
        Assert.Equal((406, 229), Size(CombatBookWindow.Background));
        Assert.Equal((406, 229), Size(RacialAbilitiesWindow.Background));
        foreach (int g in new[] { 0x08BB, 0x08BC, CombatBookWindow.Rule })
            Assert.NotNull(_data.GetGump(g));
        for (int a = 1; a <= WeaponAbilities.Count; a++)
            Assert.Equal((44, 44), Size(WeaponAbilities.Icon(a)));
        Assert.Null(_data.GetGump(0x521F)); // TazUO's 32nd (Disrobe): no icon here, so not listed
        foreach (RacialAbilitiesWindow.Race race in Enum.GetValues<RacialAbilitiesWindow.Race>())
        {
            RacialAbilitiesWindow.Book b = RacialAbilitiesWindow.BookOf(race);
            for (int i = 0; i < b.Count; i++)
                Assert.NotNull(_data.GetGump(b.FirstIcon + i));
        }
    }

    [Fact]
    public void EveryListedWeaponHasATiledataName()
    {
        foreach (int[] list in CombatBookWindow.Items)
            Assert.All(list, g => Assert.False(string.IsNullOrWhiteSpace(_data.TileData.GetStatic(g).Name), $"0x{g:X4}"));
    }

    [Fact]
    public void RealItemsFitTheirSlot()
    {
        // A ring (0x108A) is smaller than the box and centred; a katana (0x13FF) is squeezed to 18x18.
        foreach (int g in new[] { 0x108A, 0x13FF, 0x1F03 })
        {
            var art = _data.GetStatic(g)!;
            var (_, _, w, h) = PaperdollWindow.ArtBounds(art.Pixels, art.Width, art.Height);
            var (x, y, rw, rh) = PaperdollWindow.SlotItemRect(w, h);
            Assert.True(rw <= 18 && rh <= 18 && x >= 0 && y >= 0 && x + rw <= 18 && y + rh <= 18, $"0x{g:X4}: {w}x{h} -> {x},{y} {rw}x{rh}");
        }
    }

    private (int, int) Size(int gump)
    {
        var b = _data.GetGump(gump);
        Assert.NotNull(b);
        return (b!.Width, b.Height);
    }
}
