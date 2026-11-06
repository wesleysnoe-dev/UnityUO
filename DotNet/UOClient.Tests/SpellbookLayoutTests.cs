using UOClient.Unity.UI;

namespace UOClient.Tests;

/// <summary>
/// The classic spellbook's layout (SpellbookWindow.Layout.cs) against TazUO's SpellbookGump: GetBookInfo (:955-1054),
/// the index spreads and their topage (:309-554), the detail spreads (:556-771), the magery circle buttons (:226-307),
/// and the spell tables from SpellsMagery / SpellsNecromancy / SpellsChivalry. Bits as ModernUO's NewSpellbookContent.
/// </summary>
public sealed class SpellbookLayoutTests
{
    private static SpellbookInfo Info(int first)
    {
        Assert.True(SpellbookWindow.TryGetBookInfo(first, out SpellbookInfo info));
        return info;
    }

    [Theory]
    [InlineData(1, SpellbookKind.Magery, 0x08AC, 0x08BA, 0x08C0, 64, 8, 8, 4)]
    [InlineData(101, SpellbookKind.Necromancy, 0x2B00, 0x2B03, 0x5000, 17, 8, 4, 2)]
    [InlineData(201, SpellbookKind.Chivalry, 0x2B01, 0x2B04, 0x5100, 10, 5, 2, 1)]
    public void BookInfoIsTazUOs(int first, SpellbookKind kind, int book, int min, int icons, int max, int onPage, int dictionary, int indexPages)
    {
        SpellbookInfo info = Info(first);
        Assert.Equal((kind, first, book, min, icons, max), (info.Kind, info.First, info.BookGraphic, info.MinimizedGraphic, info.IconStart, info.MaxSpells));
        Assert.Equal((onPage, dictionary, indexPages), (info.SpellsOnPage, info.DictionaryPages, info.IndexPages));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(401)] // Bushido and later schools keep the list
    [InlineData(501)]
    [InlineData(601)]
    [InlineData(678)]
    public void OtherSchoolsAreNotClassic(int first) => Assert.False(SpellbookWindow.TryGetBookInfo(first, out _));

    [Theory]
    [InlineData(1, ulong.MaxValue, 4 + 32)]
    [InlineData(101, 0x1FFFFUL, 2 + 9)]
    [InlineData(201, 0x3FFUL, 1 + 5)]
    [InlineData(201, ulong.MaxValue, 1 + 5)] // bits past the school's last spell are not spells
    [InlineData(1, 0UL, 4)]
    [InlineData(101, 0x1UL, 2 + 1)]
    [InlineData(101, 0x3UL, 2 + 1)]
    [InlineData(101, 0x7UL, 2 + 2)]
    public void MaxPageIsIndexThenOneSpreadPerTwoSpells(int first, ulong bits, int max) =>
        Assert.Equal(max, SpellbookWindow.MaxPage(Info(first), bits));

    [Fact]
    public void KnownSpellsAreTheBitsNotContainedItems()
    {
        Assert.Equal(new[] { 0, 2, 16 }, SpellbookWindow.KnownSpells(Info(101), (1UL << 0) | (1UL << 2) | (1UL << 16) | (1UL << 17)));
        Assert.Empty(SpellbookWindow.KnownSpells(Info(201), 1UL << 10));
    }

    [Fact]
    public void MageryIndexListsEachCircleInItsHalfAndPointsAtTheDetailSpread()
    {
        // Clumsy and Feeblemind (circle 1), Cunning (circle 2), Water Elemental (circle 8).
        ulong bits = (1UL << 0) | (1UL << 2) | (1UL << 9) | (1UL << 63);
        var entries = SpellbookWindow.IndexEntries(Info(1), bits)
            .Select(e => (e.Page, e.Half, e.Spell, e.X, e.Y, e.ToPage)).ToList();
        Assert.Equal(new[]
        {
            (1, 0, 0, 62, 52, 5),
            (1, 0, 2, 62, 67, 5),
            (1, 1, 9, 225, 52, 6),
            (4, 1, 63, 225, 52, 6),
        }, entries);
    }

    [Fact]
    public void NecromancyAndChivalryIndexHalvesHoldSpellsOnPageEach()
    {
        // Necromancy: 8 a half, so Exorcism (16) heads the second spread's left half.
        var necro = SpellbookWindow.IndexEntries(Info(101), (1UL << 7) | (1UL << 8) | (1UL << 16))
            .Select(e => (e.Page, e.Half, e.Spell, e.Y, e.ToPage)).ToList();
        Assert.Equal(new[] { (1, 0, 7, 52, 3), (1, 1, 8, 52, 3), (2, 0, 16, 52, 4) }, necro);

        // Chivalry: 5 a half on its one index spread.
        var chiv = SpellbookWindow.IndexEntries(Info(201), (1UL << 4) | (1UL << 5) | (1UL << 9))
            .Select(e => (e.Page, e.Half, e.Spell, e.X, e.Y, e.ToPage)).ToList();
        Assert.Equal(new[] { (1, 0, 4, 62, 52, 2), (1, 1, 5, 225, 52, 2), (1, 1, 9, 225, 67, 3) }, chiv);
    }

    [Fact]
    public void DetailSpreadsTakeTwoSpellsLeftThenRight()
    {
        var details = SpellbookWindow.DetailEntries(Info(201), 0b10_1100_0001UL)
            .Select(d => (d.Page, d.Right, d.Spell, d.IconX, d.TopTextX, d.IconTextX)).ToList();
        Assert.Equal(new[]
        {
            (2, false, 0, 62, 87, 112),
            (2, true, 6, 225, 224, 275),
            (3, false, 7, 62, 87, 112),
            (3, true, 9, 225, 224, 275),
        }, details);
        Assert.Equal((5, false), SpellbookWindow.DetailSlot(Info(1), 0));
        Assert.Equal((20, true), SpellbookWindow.DetailSlot(Info(1), 31));
    }

    [Fact]
    public void EveryIndexEntryOpensTheSpreadThatDescribesIt()
    {
        foreach (int first in new[] { 1, 101, 201 })
        {
            SpellbookInfo info = Info(first);
            foreach (ulong bits in new[] { ulong.MaxValue, 0x5555_5555_5555_5555UL, 0x8000_0000_0001_0201UL, 0x1_0000UL })
            {
                var detail = SpellbookWindow.DetailEntries(info, bits).ToDictionary(d => d.Spell, d => d.Page);
                var index = SpellbookWindow.IndexEntries(info, bits);
                Assert.Equal(detail.Keys.OrderBy(k => k), index.Select(e => e.Spell));
                Assert.All(index, e => Assert.Equal(detail[e.Spell], e.ToPage));
                Assert.All(detail.Values, p => Assert.InRange(p, info.IndexPages + 1, SpellbookWindow.MaxPage(info, bits)));
            }
        }
    }

    [Fact]
    public void IndexHalvesAndCircles()
    {
        Assert.Equal((106, 269), (SpellbookWindow.IndexTitleX(0), SpellbookWindow.IndexTitleX(1)));
        Assert.Equal((62, 225), (SpellbookWindow.IndexDataX(0), SpellbookWindow.IndexDataX(1)));
        Assert.Equal(new[] { 0, 1, 6, 7 }, new[] { (1, 0), (1, 1), (4, 0), (4, 1) }.Select(p => SpellbookWindow.CircleOfHalf(p.Item1, p.Item2)));
    }

    [Fact]
    public void CircleButtonsAreTazUOs()
    {
        Assert.Equal(new[]
        {
            (0x08B1, 58, 175, 1), (0x08B2, 93, 175, 1), (0x08B3, 130, 175, 2), (0x08B4, 164, 175, 2),
            (0x08B5, 227, 175, 3), (0x08B6, 260, 175, 3), (0x08B7, 297, 175, 4), (0x08B8, 332, 175, 4),
        }, SpellbookWindow.CircleButtons);
    }

    [Fact]
    public void FixedArtAndPositions()
    {
        Assert.Equal((0x08BB, 50, 0x08BC, 321, 8), (SpellbookWindow.PrevCorner, SpellbookWindow.PrevCornerX, SpellbookWindow.NextCorner, SpellbookWindow.NextCornerX, SpellbookWindow.CornerY));
        Assert.Equal((0, 98, 27, 23), (SpellbookWindow.MinimizeX, SpellbookWindow.MinimizeY, SpellbookWindow.MinimizeW, SpellbookWindow.MinimizeH));
        Assert.Equal((0x0288, 0x0033), (SpellbookWindow.InkHue, SpellbookWindow.HoverHue));
        Assert.Equal((0x0835, 88, 92, 114, 162), (SpellbookWindow.ReagentRule, SpellbookWindow.ReagentRuleY, SpellbookWindow.ReagentsTitleY, SpellbookWindow.ReagentListY, SpellbookWindow.RequiresY));
        Assert.Equal(0x08C0 + 63, SpellbookWindow.IconOf(Info(1), 63));
        Assert.Equal(0x5010, SpellbookWindow.IconOf(Info(101), 16));
        Assert.Equal(0x5109, SpellbookWindow.IconOf(Info(201), 9));
        Assert.Equal(49, SpellbookWindow.MageryWordsY(18)); // one line: 31 + height
        Assert.Equal(56, SpellbookWindow.MageryWordsY(30)); // wrapped: 26 + height
    }

    [Theory]
    [InlineData(0, 6, 1)]
    [InlineData(7, 6, 6)]
    [InlineData(3, 6, 3)]
    [InlineData(2, 1, 1)]
    public void PagesClampToTheBook(int page, int max, int expected) => Assert.Equal(expected, SpellbookWindow.ClampPage(page, max));

    [Fact]
    public void SpellTablesAreTazUOs()
    {
        SpellbookSpells.Spell clumsy = SpellbookSpells.Get(SpellbookKind.Magery, 0)!.Value;
        Assert.Equal(("Clumsy", 3002011, "Uus Jux"), (clumsy.Name, clumsy.Cliloc, clumsy.Words));
        Assert.Equal(new[] { "Bloodmoss", "Nightshade" }, clumsy.Reagents.Select(r => r.Name));
        Assert.Equal("U J", SpellbookSpells.Abbreviate(clumsy.Words));
        Assert.Equal("K V X A F", SpellbookSpells.Abbreviate("Kal Vas Xen An Flam"));

        SpellbookSpells.Spell water = SpellbookSpells.Get(SpellbookKind.Magery, 63)!.Value;
        Assert.Equal(("Water Elemental", 3002074), (water.Name, water.Cliloc));
        Assert.Equal(new[] { 1044354, 1044357, 1044360 }, water.Reagents.Select(r => r.Cliloc));

        SpellbookSpells.Spell exorcism = SpellbookSpells.Get(SpellbookKind.Necromancy, 16)!.Value;
        Assert.Equal(("Exorcism", 1060525, "Ort Corp Grav", 40, 80), (exorcism.Name, exorcism.Cliloc, exorcism.Words, exorcism.Mana, exorcism.MinSkill));
        Assert.Equal(new[] { "Nox Crystal", "Grave Dust" }, exorcism.Reagents.Select(r => r.Name));

        SpellbookSpells.Spell journey = SpellbookSpells.Get(SpellbookKind.Chivalry, 9)!.Value;
        Assert.Equal(("Sacred Journey", 1060502, "Sanctum Viatas", 20, 5), (journey.Name, journey.Cliloc, journey.Words, journey.Mana, journey.MinSkill));
        Assert.Empty(journey.Reagents);
        Assert.Equal("Mana cost: 20\nMin. Skill: 5", SpellbookSpells.Requires(journey.Mana, journey.MinSkill));

        Assert.Null(SpellbookSpells.Get(SpellbookKind.Magery, 64));
        Assert.Null(SpellbookSpells.Get(SpellbookKind.Necromancy, 17));
        Assert.Null(SpellbookSpells.Get(SpellbookKind.Chivalry, -1));
    }

    [Fact]
    public void EverySpellOfTheThreeSchoolsHasAnEntry()
    {
        foreach ((SpellbookKind kind, int count) in new[] { (SpellbookKind.Magery, 64), (SpellbookKind.Necromancy, 17), (SpellbookKind.Chivalry, 10) })
            for (int i = 0; i < count; i++)
            {
                SpellbookSpells.Spell s = SpellbookSpells.Get(kind, i)!.Value;
                Assert.False(string.IsNullOrEmpty(s.Name));
                Assert.False(string.IsNullOrEmpty(s.Words));
                Assert.True(kind == SpellbookKind.Chivalry ? s.Reagents.Length == 0 : s.Reagents.Length > 0);
            }
        Assert.Equal(64, SpellbookSpells.MageryReagents.Length);
        Assert.Equal(17, SpellbookSpells.NecromancyRequires.Length);
        Assert.Equal(10, SpellbookSpells.ChivalryRequires.Length);
    }
}
