using UOClient.Core;

namespace UOClient.Tests;

/// <summary>The public moongate's gump read into facets and towns (MoongateMenu), as ModernUO's MoongateGump lays it out.</summary>
public class MoongateMenuTests
{
    // Two facets: Felucca (page 1, two towns) and Malas (page 2, one town); tabs on page 0, each page repeating its own tab.
    private const string Layout =
        "{page 0}{resizepic 0 0 5054 380 280}" +
        "{button 10 210 4005 4007 1 0 1}{xmfhtmlgump 45 210 140 25 1011036 0 0}" +
        "{button 10 235 4005 4007 1 0 0}{xmfhtmlgump 45 235 140 25 1011012 0 0}" +
        "{xmfhtmlgump 5 5 200 20 1012011 0 0}" +
        "{button 10 35 2117 2118 0 1 0}{xmfhtmlgump 30 35 150 20 1012001 0 0}" +
        "{button 10 60 2117 2118 0 2 0}{xmfhtmlgump 30 60 150 20 1060643 0 0}" +
        "{page 1}{button 10 35 2117 2118 0 1 0}{xmfhtmlgump 30 35 150 20 1012001 0 0}" +
        "{radio 200 35 210 211 0 0}{xmfhtmlgump 225 35 150 20 1012003 0 0}" +
        "{radio 200 60 210 211 0 1}{xmfhtmlgump 225 60 150 20 1012004 0 0}" +
        "{page 2}{button 10 60 2117 2118 0 2 0}{xmfhtmlgump 30 60 150 20 1060643 0 0}" +
        "{radio 200 35 210 211 0 100}{xmfhtmlgump 225 35 150 20 1060641 0 0}";

    [Fact]
    public void Reads_FacetsAndTheirTowns()
    {
        MoongateMenu menu = MoongateMenu.TryRead(ServerGump.ParseLayout(Layout));
        Assert.NotNull(menu);
        Assert.Equal(2, menu.Facets.Count);
        Assert.Equal((1, 1012001), (menu.Facets[0].Page, menu.Facets[0].NameCliloc));
        Assert.Equal((2, 1060643), (menu.Facets[1].Page, menu.Facets[1].NameCliloc));
        Assert.Equal(new[] { (0, 1012003), (1, 1012004) }, menu.Facets[0].Destinations.ConvertAll(d => (d.Switch, d.NameCliloc)));
        Assert.Equal(new[] { (100, 1060641) }, menu.Facets[1].Destinations.ConvertAll(d => (d.Switch, d.NameCliloc)));
        Assert.Same(menu.Facets[0], menu.First);
    }

    [Theory]
    [InlineData("{page 0}{resizepic 0 0 9200 300 200}{button 10 10 4005 4007 1 0 1}{xmfhtmlgump 5 5 200 20 1011036 0 0}")] // another gump
    [InlineData("{page 0}{xmfhtmlgump 5 5 200 20 1012011 0 0}{button 10 210 4005 4007 1 0 1}")]                           // no facets
    public void OtherGumps_AreNotTheMenu(string layout) => Assert.Null(MoongateMenu.TryRead(ServerGump.ParseLayout(layout)));

    [Fact]
    public void AnUnknownControl_LeavesTheGumpAsSent()
    {
        Assert.Null(MoongateMenu.TryRead(ServerGump.ParseLayout(Layout + "{textentry 10 10 100 20 0 5 0}")));
        Assert.Null(MoongateMenu.TryRead(ServerGump.ParseLayout(Layout + "{button 10 260 4005 4007 1 0 7}")));
    }
}
