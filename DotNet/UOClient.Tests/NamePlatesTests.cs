using System.Collections.Generic;
using UOClient.Unity;

namespace UOClient.Tests;

/// <summary>
/// Options batch 6: TazUO's nameplates (NameOverheadGump, NameOverHeadManager) as rules: who gets a plate, its bars,
/// layout, placement, overlap avoidance, hit testing, colours and the options that drive them.
/// </summary>
public class NamePlatesTests
{
    private static GameOptions On()
    {
        GameOptions o = GameOptions.Defaults();
        o.nameOverheadToggled = true;
        return o;
    }

    private static NamePlates.Subject Mob(int notoriety = NamePlates.NotorietyInnocent, bool human = true, int hits = 50, int max = 100) =>
        new NamePlates.Subject { Serial = 0x100, Name = "Aelora", Notoriety = notoriety, IsHuman = human, Hits = hits, HitsMax = max };

    [Fact]
    public void Defaults_MatchTazUO()
    {
        GameOptions o = GameOptions.Defaults();
        Assert.False(o.nameOverheadToggled);
        Assert.Equal(NamePlates.AllMobiles, o.nameOverheadFlags);
        Assert.True(o.namePlateHealthBar);
        Assert.False(o.namePlateUseNotorietyText);
        Assert.True(o.namePlateShowMissingHealth);
        Assert.Equal(50, o.namePlateHealthBarOpacity);
        Assert.Equal(50, o.namePlateBorderOpacity);
        Assert.Equal(75, o.namePlateOpacity);
        Assert.Equal(NamePlates.FillStatus, o.namePlateHealthBarMode);
        Assert.Equal(NamePlates.BackgroundFixed, o.namePlateBackgroundMode);
        Assert.False(o.namePlateAvoidOverlap);
    }

    [Fact]
    public void Shows_OnlyWhenToggledNamedAndNotClosed()
    {
        NamePlates.Subject m = Mob();
        Assert.False(NamePlates.Shows(GameOptions.Defaults(), m, false, null));
        Assert.True(NamePlates.Shows(On(), m, false, null));
        Assert.False(NamePlates.Shows(On(), m, false, new HashSet<uint> { m.Serial }));
        m.Name = "";
        Assert.False(NamePlates.Shows(On(), m, false, null));
    }

    [Fact]
    public void Allowed_FiltersByTypeNotorietyAndSelf()
    {
        Assert.True(NamePlates.Allowed(NamePlates.Humanoid, Mob()));
        Assert.False(NamePlates.Allowed(NamePlates.Humanoid, Mob(human: false)));
        Assert.True(NamePlates.Allowed(NamePlates.Monster, Mob(human: false)));
        Assert.True(NamePlates.Allowed(NamePlates.Murderer, Mob(NamePlates.NotorietyMurderer)));
        Assert.False(NamePlates.Allowed(NamePlates.Murderer, Mob(NamePlates.NotorietyInnocent)));

        NamePlates.Subject self = Mob();
        self.IsPlayer = true;
        Assert.True(NamePlates.Allowed(NamePlates.Self, self));
        Assert.False(NamePlates.Allowed(NamePlates.AllMobiles | NamePlates.ExcludeSelf, self)); // exclude-self wins

        NamePlates.Subject pet = Mob(human: false);
        pet.Renamable = true;
        Assert.True(NamePlates.Allowed(NamePlates.OwnFollowers, pet));
        pet.Notoriety = NamePlates.NotorietyEnemy;
        Assert.False(NamePlates.Allowed(NamePlates.OwnFollowers, pet));
    }

    [Fact]
    public void HiddenAtFullHealth_RespectsWarModeAndUnknownHealth()
    {
        Assert.False(NamePlates.HiddenAtFullHealth(false, false, false, 100, 100));
        Assert.True(NamePlates.HiddenAtFullHealth(true, false, false, 100, 100));
        Assert.False(NamePlates.HiddenAtFullHealth(true, false, false, 99, 100));
        Assert.False(NamePlates.HiddenAtFullHealth(true, true, false, 100, 100));
        Assert.True(NamePlates.HiddenAtFullHealth(true, true, true, 100, 100));
        Assert.False(NamePlates.HiddenAtFullHealth(true, false, false, 0, 0)); // no max yet: never "full"
    }

    [Fact]
    public void PruneClosed_ForgetsCharactersOutOfViewAndAllWhenOff()
    {
        var closed = new HashSet<uint> { 1, 2 };
        NamePlates.PruneClosed(closed, true, new List<uint> { 2 });
        Assert.Equal(new HashSet<uint> { 2 }, closed);
        NamePlates.PruneClosed(closed, false, new List<uint> { 2 });
        Assert.Empty(closed);
    }

    [Fact]
    public void Bars_HealthForAll_ManaAndStaminaForSelfAndParty_OnlyWithKnownMax()
    {
        GameOptions o = On();
        NamePlates.Subject m = Mob();
        m.ManaMax = 50;
        m.StaminaMax = 50;
        Assert.Equal(new[] { NamePlates.Resource.Hits }, NamePlates.Bars(o, m));
        m.InParty = true;
        Assert.Equal(new[] { NamePlates.Resource.Hits, NamePlates.Resource.Mana, NamePlates.Resource.Stamina }, NamePlates.Bars(o, m));
        m.HitsMax = 0;
        Assert.DoesNotContain(NamePlates.Resource.Hits, NamePlates.Bars(o, m));
        o.namePlateHealthBar = false;
        Assert.Empty(NamePlates.Bars(o, m));
    }

    [Fact]
    public void PercentAndFillWidth_Clamp()
    {
        Assert.Equal(0, NamePlates.Percent(5, 0));
        Assert.Equal(1, NamePlates.Percent(150, 100));
        Assert.Equal(0.5, NamePlates.Percent(50, 100));
        Assert.Equal(50, NamePlates.FillWidth(100, 0.5));
        Assert.Equal(100, NamePlates.FillWidth(100, 2));
        Assert.Equal(0, NamePlates.FillWidth(100, double.NaN));
    }

    [Fact]
    public void Measure_MinimumWidthPaddingAndFixedWidths()
    {
        GameOptions o = On();
        NamePlates.Layout l = NamePlates.Measure(o, 20, 14, 1);
        Assert.Equal(NamePlates.MinWidth + NamePlates.HorizontalPadding, l.Width);
        Assert.Equal(NamePlates.HandlesHeight + NamePlates.VerticalPadding, l.Height);
        Assert.False(l.Split);

        o.namePlateUseFixedWidth = true;
        o.namePlateFixedWidth = 500; // clamped to 300
        Assert.Equal(NamePlates.MaxFixedWidth, NamePlates.Measure(o, 20, 14, 0).NameWidth);

        o.namePlateUseFixedWidth = false;
        o.namePlateUseFixedHealthBarWidth = true;
        o.namePlateHealthBarFixedWidth = 200;
        l = NamePlates.Measure(o, 20, 14, 1);
        Assert.Equal(200, l.BarWidth);
        Assert.Equal(200, l.Width);
    }

    [Fact]
    public void Measure_SplitPutsBarsInRowsUnderTheName()
    {
        GameOptions o = On();
        o.namePlateSplitHealthBar = true;
        NamePlates.Layout l = NamePlates.Measure(o, 40, 14, 2);
        Assert.True(l.Split);
        Assert.InRange(l.BarHeight, 4, 8);
        Assert.Equal(l.NameBandHeight + l.BarHeight * 2 + 2, l.Height);

        var plate = NamePlates.Place(100, 100, l);
        NamePlates.Box bar1 = NamePlates.BarBounds(plate, l, 1);
        Assert.Equal(plate.Y + l.NameBandHeight + 1 + l.BarHeight, bar1.Y);

        o.namePlateSplitHealthBar = false;
        Assert.False(NamePlates.Measure(o, 40, 14, 2).Split);
        Assert.False(NamePlates.Measure(On(), 40, 14, 0).Split);
    }

    [Fact]
    public void Fit_CutsLongNamesWithEllipsis()
    {
        float Measure(string t) => t.Length * 10;
        Assert.Equal("Short", NamePlates.Fit("Short", 100, Measure));
        Assert.Equal("Aelor...", NamePlates.Fit("Aelora the Brave", 80, Measure));
        Assert.Equal("...", NamePlates.Fit("Aelora", 20, Measure));
    }

    [Fact]
    public void PlaceAndPlateBottom_CentreOverTheHead()
    {
        NamePlates.Layout l = NamePlates.Measure(On(), 20, 14, 0);
        NamePlates.Box b = NamePlates.Place(200, 100, l);
        Assert.Equal(200 - (l.Width >> 1), b.X);
        Assert.Equal(100, b.Bottom);
        Assert.Equal(100 - NamePlates.PlainNameBottom, NamePlates.PlateBottom(100, false));
        Assert.Equal(100 - NamePlates.PlainNameBottom - NamePlates.PlainNameHeight, NamePlates.PlateBottom(100, true));
    }

    [Fact]
    public void OnScreen_RequiresTheWholePlate()
    {
        Assert.True(NamePlates.OnScreen(new NamePlates.Box(0, 0, 64, 22), 800, 600));
        Assert.False(NamePlates.OnScreen(new NamePlates.Box(-1, 0, 64, 22), 800, 600));
        Assert.False(NamePlates.OnScreen(new NamePlates.Box(750, 0, 64, 22), 800, 600));
    }

    [Fact]
    public void AvoidOverlap_MovesDownPastEarlierPlates()
    {
        var placed = new List<NamePlates.Box> { new NamePlates.Box(0, 0, 64, 22) };
        NamePlates.Box moved = NamePlates.AvoidOverlap(new NamePlates.Box(10, 10, 64, 22), placed);
        Assert.Equal(10, moved.X);
        Assert.Equal(22 + NamePlates.CollisionSpacing, moved.Y);
        NamePlates.Box clear = new NamePlates.Box(100, 0, 64, 22);
        Assert.Equal(clear.Y, NamePlates.AvoidOverlap(clear, placed).Y);
    }

    [Fact]
    public void HitTest_TopmostPlateWins()
    {
        var plates = new List<(uint, NamePlates.Box)>
        {
            (1, new NamePlates.Box(0, 0, 64, 22)),
            (2, new NamePlates.Box(10, 10, 64, 22)),
        };
        Assert.Equal(2u, NamePlates.HitTest(plates, 20, 15));
        Assert.Equal(1u, NamePlates.HitTest(plates, 2, 2));
        Assert.Equal(0u, NamePlates.HitTest(plates, 500, 500));
    }

    [Fact]
    public void Alphas_AndContrastText()
    {
        Assert.Equal(0.5f, NamePlates.Alpha(50));
        Assert.Equal(1f, NamePlates.Alpha(150));
        Assert.Equal(0f, NamePlates.Alpha(-5));
        Assert.True(NamePlates.BlackText(new NamePlates.Rgb(255, 255, 255)));
        Assert.False(NamePlates.BlackText(new NamePlates.Rgb(0, 0, 0)));
        Assert.Null(NamePlates.FixedFill(NamePlates.FillStatus));
        Assert.NotNull(NamePlates.FixedFill(1));
    }

    [Fact]
    public void Clamp_KeepsNamePlateValuesInRange()
    {
        GameOptions o = On();
        o.namePlateOpacity = 300;
        o.namePlateBorderOpacity = -4;
        o.namePlateFixedWidth = 5;
        o.nameOverheadFlags = -1;
        o.Clamp();
        Assert.Equal(100, o.namePlateOpacity);
        Assert.Equal(0, o.namePlateBorderOpacity);
        Assert.Equal(NamePlates.MinFixedWidth, o.namePlateFixedWidth);
        Assert.Equal(NamePlates.MobileFlags, o.nameOverheadFlags);
    }

    [Fact]
    public void CopyNamePlates_CopiesEveryPlateOption()
    {
        GameOptions from = On();
        from.namePlateAvoidOverlap = true;
        from.namePlateBackgroundR = 40;
        from.namePlateHeight = 30;
        GameOptions to = GameOptions.Defaults();
        GameOptions.CopyNamePlates(from, to);
        Assert.True(to.nameOverheadToggled);
        Assert.True(to.namePlateAvoidOverlap);
        Assert.Equal(40, to.namePlateBackgroundR);
        Assert.Equal(30, to.namePlateHeight);
    }
}
