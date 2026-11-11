using UOClient.Core;
using UOClient.Core.Assistant;

namespace UOClient.Tests;

/// <summary>Auto buy and auto sell: which lines of a vendor's list are ordered, and how many.</summary>
public class AssistantShopPlannerTests
{
    private const int Pearl = 0x0F7A, Ginseng = 0x0F85, Arrow = 0x0F3F;

    private static ShopList Shop(bool selling, params (uint Serial, int Graphic, int Hue, int Amount, int Price, string Name)[] lines)
    {
        var list = new ShopList { Vendor = 0x99, Selling = selling };
        foreach (var l in lines)
            list.Items.Add(new ShopItem { Serial = l.Serial, Graphic = (ushort)l.Graphic, Hue = (ushort)l.Hue, Amount = l.Amount, Price = l.Price, Name = l.Name });
        return list;
    }

    private static ShopList Mage() => Shop(false,
        (1, Pearl, 0, 20, 5, "Black pearl"),
        (2, Ginseng, 0, 20, 3, "Ginseng"),
        (3, Arrow, 0, 500, 2, "Arrow"));

    private static ShopOrder Buy(ShopList list, ItemRule[] rules, int[] restock = null, int[] maxPrice = null, int held = 0,
        int maxItems = 0, int maxKinds = 0, long gold = -1) =>
        ShopPlanner.Buy(list, rules, restock, maxPrice, _ => held, maxItems, maxKinds, gold);

    [Fact]
    public void Buy_TakesOnlyListedLines_UpToEachRulesMax()
    {
        ShopOrder order = Buy(Mage(), new[] { new ItemRule { graphic = Pearl, max = 12 }, new ItemRule { graphic = Arrow } });
        Assert.Equal(new[] { (1u, 12), (3u, 500) }, order.Reply());
        Assert.Equal(512, order.Count);
        Assert.Equal(12 * 5 + 500 * 2, order.Gold);
    }

    [Fact]
    public void Buy_NothingListed_IsAnEmptyOrder()
    {
        Assert.Equal(0, Buy(Mage(), new[] { new ItemRule { graphic = 0x1234 } }).Count);
        Assert.Equal(0, Buy(Mage(), new[] { new ItemRule { graphic = Pearl, enabled = false } }).Count);
        Assert.Equal(0, Buy(Mage(), new ItemRule[0]).Count);
        Assert.Equal(0, ShopPlanner.Buy(null, null, null, null, null, 0, 0, -1).Count);
    }

    [Theory]
    [InlineData(100, 0, 0, 20)]    // nothing held: all 20 in stock (the level is far off)
    [InlineData(100, 90, 0, 10)]   // ten short of the level
    [InlineData(100, 100, 0, 0)]   // at the level
    [InlineData(100, 130, 0, 0)]   // over it
    [InlineData(100, 90, 4, 4)]    // the visit's max is under what is missing
    [InlineData(0, 500, 6, 6)]     // no level: what is held does not matter
    public void Buy_RestocksUpToTheLevel(int level, int held, int max, int bought)
    {
        ShopOrder order = Buy(Mage(), new[] { new ItemRule { graphic = Pearl, max = max } }, restock: new[] { level }, held: held);
        Assert.Equal(bought, order.Count);
    }

    [Fact]
    public void Buy_PassesOverLinesDearerThanTheRulesPrice()
    {
        ShopList list = Shop(false, (1, Pearl, 0, 20, 9, "Black pearl"), (2, Pearl, 0, 20, 5, "Black pearl"));
        ShopOrder order = Buy(list, new[] { new ItemRule { graphic = Pearl, max = 30 } }, maxPrice: new[] { 6 });
        Assert.Equal(new[] { (2u, 20) }, order.Reply());
    }

    [Fact]
    public void Buy_StopsAtTheGoldAvailable()
    {
        var rules = new[] { new ItemRule { graphic = Pearl }, new ItemRule { graphic = Ginseng } };
        ShopOrder order = Buy(Mage(), rules, gold: 107); // 20 pearl = 100, then 2 ginseng = 6
        Assert.Equal(new[] { (1u, 20), (2u, 2) }, order.Reply());
        Assert.Equal(106, order.Gold);
        Assert.Equal(0, Buy(Mage(), rules, gold: 2).Count);
        Assert.Equal(40, Buy(Mage(), rules, gold: -1).Count); // gold not looked at
    }

    [Fact]
    public void Buy_CapsTheWholeOrder_ByItemsAndByEntries()
    {
        var rules = new[] { new ItemRule { graphic = Pearl }, new ItemRule { graphic = Ginseng }, new ItemRule { graphic = Arrow } };
        Assert.Equal(new[] { (1u, 20), (2u, 5) }, Buy(Mage(), rules, maxItems: 25).Reply());
        Assert.Equal(new[] { (1u, 20), (2u, 20) }, Buy(Mage(), rules, maxKinds: 2).Reply());
    }

    [Fact]
    public void Buy_TwoRulesMeaningOneLine_NeverOrderMoreThanItsStock_AndReplyOnce()
    {
        var rules = new[] { new ItemRule { graphic = Pearl, max = 15 }, new ItemRule { text = "pearl" } };
        ShopOrder order = Buy(Mage(), rules);
        Assert.Equal(new[] { (1u, 20) }, order.Reply());
        Assert.Equal(100, order.Gold);
    }

    [Fact]
    public void Buy_MatchesByHueAndByTheVendorsName()
    {
        ShopList list = Shop(false, (1, 0x1F03, 0, 5, 30, "Robe"), (2, 0x1F03, 33, 5, 40, "Robe"), (3, 0x0E21, 0, 50, 4, "Clean bandage"));
        Assert.Equal(new[] { (2u, 5) }, Buy(list, new[] { new ItemRule { graphic = 0x1F03, hue = 33 } }).Reply());
        Assert.Equal(new[] { (3u, 50) }, Buy(list, new[] { new ItemRule { text = "BANDAGE" } }).Reply());
    }

    [Fact]
    public void Buy_OneLineNeverCarriesMoreThanAReplyCan()
    {
        ShopList list = Shop(false, (1, Arrow, 0, 100000, 0, "Arrow"));
        Assert.Equal(new[] { (1u, ShopPlanner.LineMost) }, Buy(list, new[] { new ItemRule { graphic = Arrow } }).Reply());
    }

    private static ShopList Pack() => Shop(true,
        (10, 0x1BF2, 0, 60, 4, "Iron ingot"),
        (11, 0x1BF2, 0, 30, 4, "Iron ingot"),
        (12, 0x13B9, 0, 1, 27, "Viking sword"));

    private static ShopOrder Sell(ShopList list, ItemRule[] rules, int[] keep = null, int held = 0, int maxItems = 0, int maxKinds = 0) =>
        ShopPlanner.Sell(list, rules, keep, _ => held, maxItems, maxKinds);

    [Fact]
    public void Sell_TakesOnlyListedLines_UpToEachRulesMax_AcrossStacks()
    {
        Assert.Equal(new[] { (10u, 60), (11u, 30) }, Sell(Pack(), new[] { new ItemRule { graphic = 0x1BF2 } }).Reply());
        ShopOrder order = Sell(Pack(), new[] { new ItemRule { graphic = 0x1BF2, max = 70 } });
        Assert.Equal(new[] { (10u, 60), (11u, 10) }, order.Reply());
        Assert.Equal(280, order.Gold);
        Assert.Equal(0, Sell(Pack(), new[] { new ItemRule { graphic = 0x0EED } }).Count);
    }

    [Theory]
    [InlineData(50, 90, 0, 40)]   // 90 held, keep 50
    [InlineData(50, 90, 25, 25)]  // the visit's max is under what can be spared
    [InlineData(90, 90, 0, 0)]    // exactly what is kept
    [InlineData(200, 90, 0, 0)]
    [InlineData(0, 90, 0, 90)]    // nothing kept
    public void Sell_LeavesWhatIsKept(int keep, int held, int max, int sold)
    {
        ShopOrder order = Sell(Pack(), new[] { new ItemRule { graphic = 0x1BF2, max = max } }, keep: new[] { keep }, held: held);
        Assert.Equal(sold, order.Count);
    }

    [Fact]
    public void Sell_CapsTheWholeOrder_ByItemsAndByEntries()
    {
        var rules = new[] { new ItemRule { graphic = 0x1BF2 }, new ItemRule { graphic = 0x13B9 } };
        Assert.Equal(new[] { (10u, 60), (11u, 5) }, Sell(Pack(), rules, maxItems: 65).Reply());
        Assert.Equal(new[] { (10u, 60), (11u, 30) }, Sell(Pack(), rules, maxKinds: 1).Reply());
        Assert.Equal(91, Sell(Pack(), rules).Count);
    }

    [Fact]
    public void Describe_AddsLikeNamesUp_AndCutsALongOrderShort()
    {
        ShopOrder order = Sell(Pack(), new[] { new ItemRule { graphic = 0x1BF2 }, new ItemRule { graphic = 0x13B9 } });
        Assert.Equal("90 Iron ingot, 1 Viking sword", order.Describe());
        Assert.Equal("90 Iron ingot and 1 more kind", order.Describe(1));
    }

    [Fact]
    public void Held_CountsStacksByAmount_AndBagsOnlyWhenAsked()
    {
        var items = new[]
        {
            new ContainedItem { Serial = 1, Container = 100, Graphic = Pearl, Amount = 30 },
            new ContainedItem { Serial = 2, Container = 100, Graphic = 0x0E75, Amount = 1 },        // a bag in the pack
            new ContainedItem { Serial = 3, Container = 2, Graphic = Pearl, Amount = 12 },
            new ContainedItem { Serial = 4, Container = 2, Graphic = 0x0E76, Amount = 0 },          // a bag in the bag
            new ContainedItem { Serial = 5, Container = 4, Graphic = Pearl, Hue = 7, Amount = 5 },
            new ContainedItem { Serial = 6, Container = 200, Graphic = Pearl, Amount = 99 },        // someone else's
            new ContainedItem { Serial = 7, Container = 100, Graphic = Ginseng, Amount = 0 }        // a single item
        };
        Assert.Equal(30, ShopPlanner.Held(items, 100, Pearl, -1, false));
        Assert.Equal(47, ShopPlanner.Held(items, 100, Pearl, -1, true));
        Assert.Equal(42, ShopPlanner.Held(items, 100, Pearl, 0, true));
        Assert.Equal(1, ShopPlanner.Held(items, 100, Ginseng, -1, false));
        Assert.Equal(0, ShopPlanner.Held(items, 0, Pearl, -1, true));
        Assert.Equal(0, ShopPlanner.Held(items, 100, -1, -1, true)); // a rule without a graphic counts nothing
    }

    [Fact]
    public void Align_KeepsEachRulesNumber_WhenARowGoesOrComes()
    {
        ItemRule a = new ItemRule { name = "a" }, b = new ItemRule { name = "b" }, c = new ItemRule { name = "c" }, d = new ItemRule { name = "d" };
        var numbers = new List<int> { 10, 20, 30 };
        ShopPlanner.Align(numbers, new[] { a, b, c }, new[] { a, c, d });
        Assert.Equal(new[] { 10, 30, 0 }, numbers);
    }
}
