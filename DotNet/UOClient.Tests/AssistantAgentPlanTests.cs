using UOClient.Core;
using UOClient.Core.Assistant;

namespace UOClient.Tests;

/// <summary>The scavenger's, organizer's and dress agent's decisions: what is picked up, what is moved and how many, and the next piece to put on or take off.</summary>
public class AssistantAgentPlanTests
{
    private const byte MovableFlag = 0x20;

    private static WorldItem Ground(uint serial, ushort graphic, int x, int y, byte flags = MovableFlag, int amount = 1) =>
        new WorldItem { Serial = serial, Graphic = graphic, X = x, Y = y, Flags = flags, Amount = amount };

    private static ContainedItem In(uint serial, uint container, ushort graphic, int amount = 1, ushort hue = 0) =>
        new ContainedItem { Serial = serial, Container = container, Graphic = graphic, Amount = amount, Hue = hue };

    private static Dictionary<byte, EquippedItem> Worn(params (byte Layer, uint Serial)[] items)
    {
        var worn = new Dictionary<byte, EquippedItem>();
        foreach ((byte layer, uint serial) in items)
            worn[layer] = new EquippedItem { Serial = serial, Layer = layer, Graphic = 0x1F03 };
        return worn;
    }

    private static DressOutfit Outfit(params (int Layer, uint Serial)[] items)
    {
        var o = new DressOutfit();
        foreach ((int layer, uint serial) in items)
            o.items.Add(new DressItem { serial = serial, layer = layer });
        return o;
    }

    // ---- scavenger ----

    [Theory]
    [InlineData(100, 100, MovableFlag, true)]   // underfoot
    [InlineData(102, 98, MovableFlag, true)]    // two tiles off, diagonally
    [InlineData(103, 100, MovableFlag, false)]  // three tiles off
    [InlineData(100, 100, 0, false)]            // locked down / not movable
    public void Scavenger_TakesOnlyMovableItemsInReach(int x, int y, int flags, bool takes) =>
        Assert.Equal(takes, ScavengerRules.CanTake(Ground(1, 0x0EED, x, y, (byte)flags), 100, 100));

    [Fact]
    public void Scavenger_LeavesCorpsesAndMultisAlone()
    {
        Assert.False(ScavengerRules.CanTake(Ground(1, WorldItem.CorpseGraphic, 100, 100), 100, 100));
        var house = Ground(2, 0x0064, 100, 100);
        house.IsMulti = true;
        Assert.False(ScavengerRules.CanTake(house, 100, 100));
        Assert.False(ScavengerRules.CanTake(null, 100, 100));
    }

    [Fact]
    public void Scavenger_PicksTheNearestListedItem_SkippingTriedOnes()
    {
        var rules = new List<ItemRule> { new ItemRule { graphic = 0x0EED }, new ItemRule { graphic = 0x0F7A } };
        var ground = new[]
        {
            Ground(10, 0x0EED, 102, 100),
            Ground(11, 0x0F7A, 101, 100),
            Ground(12, 0x1234, 100, 100), // not on the list
            Ground(13, 0x0EED, 105, 100), // out of reach
        };
        (WorldItem item, ItemRule rule) = ScavengerRules.Next(ground, 100, 100, rules);
        Assert.Equal(11u, item.Serial);
        Assert.Same(rules[1], rule);

        (item, rule) = ScavengerRules.Next(ground, 100, 100, rules, skip: s => s == 11);
        Assert.Equal(10u, item.Serial);
        Assert.Same(rules[0], rule);

        (item, rule) = ScavengerRules.Next(ground, 100, 100, rules, skip: s => s == 11 || s == 10);
        Assert.Null(item);
        Assert.Null(rule);
    }

    [Fact]
    public void Scavenger_SameDistance_TakesTheLowerSerial_WhateverTheOrder()
    {
        var rules = new List<ItemRule> { new ItemRule { graphic = 0x0EED } };
        WorldItem a = Ground(20, 0x0EED, 101, 100), b = Ground(21, 0x0EED, 100, 101);
        Assert.Equal(20u, ScavengerRules.Next(new[] { a, b }, 100, 100, rules).Item.Serial);
        Assert.Equal(20u, ScavengerRules.Next(new[] { b, a }, 100, 100, rules).Item.Serial);
    }

    [Fact]
    public void Scavenger_WordRules_NeedTheItemsText()
    {
        var rules = new List<ItemRule> { new ItemRule { text = "slayer" } };
        var ground = new[] { Ground(30, 0x13B9, 100, 100) };
        Assert.Null(ScavengerRules.Next(ground, 100, 100, rules).Item);                          // tooltip not here yet
        Assert.Null(ScavengerRules.Next(ground, 100, 100, rules, text: _ => "a viking sword").Item);
        Assert.Equal(30u, ScavengerRules.Next(ground, 100, 100, rules, text: _ => "a viking sword\nOrc Slayer").Item.Serial);
        Assert.Null(ScavengerRules.Next(ground, 100, 100, new List<ItemRule>()).Item);
        Assert.Null(ScavengerRules.Next(null, 100, 100, rules).Item);
    }

    // ---- organizer ----

    [Fact]
    public void Organizer_AmountZero_MovesEveryMatchingItemWhole()
    {
        var rules = new List<ItemRule> { new ItemRule { graphic = 0x0F7A } };
        var source = new[] { In(1, 50, 0x0F7A, 40), In(2, 50, 0x1234, 5), In(3, 50, 0x0F7A, 25) };
        var dest = new[] { In(9, 60, 0x0F7A, 500) };
        Assert.Equal(new List<(uint, int)> { (1u, 40), (3u, 25) }, OrganizerPlan.Build(source, dest, rules));
    }

    [Fact]
    public void Organizer_Amount_CountsWhatTheDestinationHolds_AndEarlierMoves()
    {
        var rules = new List<ItemRule> { new ItemRule { graphic = 0x0F7A, max = 100 } };
        var source = new[] { In(1, 50, 0x0F7A, 40), In(2, 50, 0x0F7A, 40), In(3, 50, 0x0F7A, 40) };
        // 30 there already: 40, then 30 more, then nothing.
        var dest = new[] { In(9, 60, 0x0F7A, 30), In(8, 60, 0x0EED, 1000) };
        Assert.Equal(new List<(uint, int)> { (1u, 40), (2u, 30) }, OrganizerPlan.Build(source, dest, rules));
        // Already full: nothing to do.
        Assert.Empty(OrganizerPlan.Build(source, new[] { In(9, 60, 0x0F7A, 100) }, rules));
        // Nothing known about the destination: counts from zero.
        Assert.Equal(new List<(uint, int)> { (1u, 40), (2u, 40), (3u, 20) }, OrganizerPlan.Build(source, null, rules));
    }

    [Fact]
    public void Organizer_EachRuleKeepsItsOwnCount_AndHueNarrowsIt()
    {
        var blue = new ItemRule { graphic = 0x0F7A, hue = 5, max = 10 };
        var any = new ItemRule { graphic = 0x0F7A, max = 3 };
        var rules = new List<ItemRule> { blue, any };
        var source = new[] { In(1, 50, 0x0F7A, 20, 5), In(2, 50, 0x0F7A, 20, 0) };
        var dest = new[] { In(9, 60, 0x0F7A, 4, 5), In(8, 60, 0x0F7A, 1, 0) };
        Assert.Equal(new List<(uint, int)> { (1u, 6), (2u, 2) }, OrganizerPlan.Build(source, dest, rules));
    }

    [Fact]
    public void Organizer_DisabledOrMissingRules_MoveNothing_AndSingleItemsCountAsOne()
    {
        var source = new[] { In(1, 50, 0x13B9, 0), In(2, 50, 0x13B9, 0) }; // unstackable things arrive with amount 0
        Assert.Empty(OrganizerPlan.Build(source, null, new List<ItemRule> { new ItemRule { graphic = 0x13B9, enabled = false } }));
        Assert.Empty(OrganizerPlan.Build(source, null, new List<ItemRule>()));
        Assert.Empty(OrganizerPlan.Build(null, null, new List<ItemRule> { new ItemRule { graphic = 0x13B9 } }));
        Assert.Equal(new List<(uint, int)> { (1u, 1) }, OrganizerPlan.Build(source, null, new List<ItemRule> { new ItemRule { graphic = 0x13B9, max = 1 } }));
    }

    [Fact]
    public void Organizer_WordRules_UseTheItemsText()
    {
        var rules = new List<ItemRule> { new ItemRule { text = "slayer" } };
        var source = new[] { In(1, 50, 0x13B9), In(2, 50, 0x13B9) };
        Assert.Empty(OrganizerPlan.Build(source, null, rules));
        Assert.Equal(new List<(uint, int)> { (2u, 1) }, OrganizerPlan.Build(source, null, rules, i => i.Serial == 2 ? "Orc Slayer" : "plain"));
    }

    // ---- dress ----

    [Theory]
    [InlineData(1, true)]    // right hand
    [InlineData(2, true)]    // left hand
    [InlineData(6, true)]    // helm
    [InlineData(24, true)]   // inner legs
    [InlineData(0, false)]
    [InlineData(11, false)]  // hair
    [InlineData(15, false)]  // face
    [InlineData(16, false)]  // beard
    [InlineData(21, false)]  // backpack
    [InlineData(25, false)]  // mount
    [InlineData(26, false)]  // shop
    [InlineData(29, false)]  // bank
    public void Dress_WhichLayersAreClothing(int layer, bool clothing) =>
        Assert.Equal(clothing, DressPlan.IsClothingLayer(layer));

    [Fact]
    public void Dress_Capture_TakesClothingOnly_InLayerOrder()
    {
        List<DressItem> items = DressPlan.Capture(Worn((21, 900), (6, 106), (11, 911), (1, 101), (25, 925), (16, 916), (29, 929), (3, 103)));
        Assert.Equal(new[] { 1, 3, 6 }, items.Select(i => i.layer));
        Assert.Equal(new uint[] { 101, 103, 106 }, items.Select(i => i.serial));
        Assert.Empty(DressPlan.Capture(null));
    }

    /// <summary>Plays a run to its end as the agent does, the server granting every step at once.</summary>
    private static List<DressStep> Play(DressOutfit outfit, Dictionary<byte, EquippedItem> worn, bool dress)
    {
        var done = new HashSet<uint>();
        var steps = new List<DressStep>();
        for (int i = 0; i < 50; i++)
        {
            DressStep step = dress ? DressPlan.NextDress(outfit, worn, done) : DressPlan.NextUndress(outfit, worn, done);
            if (step.Kind == DressStepKind.None)
                break;
            steps.Add(step);
            done.Add(step.Serial);
            if (step.Kind == DressStepKind.TakeOff)
                worn.Remove(step.Layer);
            else
                worn[step.Layer] = new EquippedItem { Serial = step.Serial, Layer = step.Layer };
        }
        return steps;
    }

    [Fact]
    public void Dress_TakesOffWhatIsInTheWay_ThenPutsEachPieceOn()
    {
        DressOutfit outfit = Outfit((6, 206), (3, 203), (20, 220));
        // Another helm on, the outfit's shoes on already, no cloak; a ring that is none of the outfit's business.
        Dictionary<byte, EquippedItem> worn = Worn((6, 106), (3, 203), (8, 108), (21, 900));
        List<DressStep> steps = Play(outfit, worn, dress: true);
        Assert.Equal(new[] { (DressStepKind.TakeOff, 106u, (byte)6), (DressStepKind.PutOn, 206u, (byte)6), (DressStepKind.PutOn, 220u, (byte)20) },
            steps.Select(s => (s.Kind, s.Serial, s.Layer)));
        Assert.Equal(108u, worn[8].Serial);
        Assert.Equal(900u, worn[21].Serial);
    }

    [Fact]
    public void Dress_AHandPiece_ClearsTheOtherHandToo_UnlessItIsTheOutfits()
    {
        // A two-handed weapon in the outfit: the sword and the shield both come off first.
        List<DressStep> steps = Play(Outfit((2, 202)), Worn((1, 101), (2, 102)), dress: true);
        Assert.Equal(new[] { (DressStepKind.TakeOff, 102u), (DressStepKind.TakeOff, 101u), (DressStepKind.PutOn, 202u) }, steps.Select(s => (s.Kind, s.Serial)));

        // Sword and shield both in the outfit, the shield on already: it stays.
        steps = Play(Outfit((1, 201), (2, 202)), Worn((2, 202)), dress: true);
        Assert.Equal(new[] { (DressStepKind.PutOn, 201u) }, steps.Select(s => (s.Kind, s.Serial)));
    }

    [Fact]
    public void Dress_NothingToDo_WhenTheOutfitIsOn_AndForbiddenLayersAreIgnored()
    {
        Assert.Equal(DressStepKind.None, DressPlan.NextDress(Outfit((6, 206)), Worn((6, 206)), new HashSet<uint>()).Kind);
        // A saved outfit that somehow lists the backpack or hair never moves them.
        DressOutfit bad = Outfit((21, 900), (11, 911), (25, 925));
        Assert.Equal(DressStepKind.None, DressPlan.NextDress(bad, Worn((21, 901), (11, 912)), new HashSet<uint>()).Kind);
        Assert.Equal(DressStepKind.None, DressPlan.NextUndress(bad, Worn((21, 900), (11, 911), (25, 925)), new HashSet<uint>()).Kind);
        Assert.Equal(DressStepKind.None, DressPlan.NextDress(null, Worn(), null).Kind);
        Assert.Equal(DressStepKind.None, DressPlan.NextUndress(Outfit((6, 206)), null, null).Kind);
    }

    [Fact]
    public void Dress_ARefusedItem_IsNotAskedForAgain()
    {
        DressOutfit outfit = Outfit((6, 206), (3, 203));
        Dictionary<byte, EquippedItem> worn = Worn((6, 106));
        var done = new HashSet<uint>();
        // The server never takes the old helm off: it is asked once, then the run goes on to the rest.
        DressStep step = DressPlan.NextDress(outfit, worn, done);
        Assert.Equal((DressStepKind.TakeOff, 106u), (step.Kind, step.Serial));
        done.Add(step.Serial);
        step = DressPlan.NextDress(outfit, worn, done);
        Assert.Equal((DressStepKind.PutOn, 206u), (step.Kind, step.Serial));
        done.Add(step.Serial);
        step = DressPlan.NextDress(outfit, worn, done);
        Assert.Equal((DressStepKind.PutOn, 203u), (step.Kind, step.Serial));
        done.Add(step.Serial);
        Assert.Equal(DressStepKind.None, DressPlan.NextDress(outfit, worn, done).Kind);
    }

    [Fact]
    public void Undress_TakesOffOnlyTheOutfitsWornPieces()
    {
        DressOutfit outfit = Outfit((6, 206), (3, 203), (20, 220));
        Dictionary<byte, EquippedItem> worn = Worn((6, 206), (3, 103), (20, 220), (8, 108));
        List<DressStep> steps = Play(outfit, worn, dress: false);
        Assert.Equal(new[] { (DressStepKind.TakeOff, 206u, (byte)6), (DressStepKind.TakeOff, 220u, (byte)20) }, steps.Select(s => (s.Kind, s.Serial, s.Layer)));
        Assert.Equal(new byte[] { 3, 8 }, worn.Keys.OrderBy(k => k));
    }

    [Fact]
    public void LayerNames_AreReadable()
    {
        Assert.Equal("Right hand", DressPlan.LayerName(1));
        Assert.Equal("Inner legs", DressPlan.LayerName(24));
        Assert.Equal("Layer 40", DressPlan.LayerName(40));
    }
}
