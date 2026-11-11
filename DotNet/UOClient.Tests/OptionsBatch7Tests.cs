using System.Collections.Generic;
using System.Linq;
using UOClient.Unity;
using UOClient.Unity.UI;
using Xunit;

namespace UOClient.Tests;

/// <summary>The grid containers' tools and look (GameOptions.More.cs): search, sort, size, corpse style.</summary>
public class OptionsBatch7Tests
{
    [Fact]
    public void Defaults_KeepTheOldGrid()
    {
        GameOptions d = GameOptions.Defaults();
        Assert.Equal(GameOptions.GridSearchOnlyShow, d.gridContainerSearchMode);
        Assert.False(d.gridAutoSort);
        Assert.Equal(75, d.gridBorderAlpha);
        Assert.False(d.gridHideBorder);
        Assert.False(d.gridUseContainerHue);
        Assert.Equal((GridContainerWindow.DefaultColumns, GridContainerWindow.DefaultRows), (d.gridDefaultColumns, d.gridDefaultRows));
        Assert.Equal(GameOptions.CorpseStyleAsContainers, d.corpseContainerStyle);
    }

    [Fact]
    public void Clamp_BringsTheGridIntoRange()
    {
        var o = new GameOptions { gridDefaultColumns = 40, gridDefaultRows = 0, gridBorderAlpha = 300, gridContainerSearchMode = 9, corpseContainerStyle = -2 }.Clamp();
        Assert.Equal(GameOptions.MaxGridCells, o.gridDefaultColumns);
        Assert.Equal(GameOptions.MinGridCells, o.gridDefaultRows);
        Assert.Equal(100, o.gridBorderAlpha);
        Assert.Equal(GameOptions.GridSearchHighlight, o.gridContainerSearchMode);
        Assert.Equal(GameOptions.CorpseStyleAsContainers, o.corpseContainerStyle);
    }

    [Fact]
    public void Search_NeedsEveryWord_InTheNameOrTheTooltip()
    {
        var lines = new[] { "a katana", "Durability 80", "Fire Damage 20%" };
        Assert.True(GameOptions.GridMatches("", "katana", lines));
        Assert.True(GameOptions.GridMatches("  ", null, null));
        Assert.True(GameOptions.GridMatches("KAT", "katana", null));
        Assert.True(GameOptions.GridMatches("katana fire", "katana", lines));
        Assert.False(GameOptions.GridMatches("katana cold", "katana", lines));
        Assert.False(GameOptions.GridMatches("axe", "katana", lines));
        Assert.False(GameOptions.GridMatches("axe", null, null));
    }

    [Fact]
    public void Sort_IsByGraphic_ThenHue_ThenSerial()
    {
        var items = new List<(int G, int H, uint S)> { (5, 0, 9), (3, 7, 2), (3, 1, 8), (3, 1, 4) };
        items.Sort((a, b) => GameOptions.GridSortCompare(a.G, a.H, a.S, b.G, b.H, b.S));
        Assert.Equal(new uint[] { 4, 8, 2, 9 }, items.Select(i => i.S));
    }

    [Fact]
    public void Corpses_FollowTheirOwnStyle()
    {
        const ushort plain = 0x3C, board = 0x091A;
        // as containers: the original-view option decides
        Assert.True(GameOptions.OpensGrid(false, plain, true, GameOptions.CorpseStyleAsContainers));
        Assert.False(GameOptions.OpensGrid(true, plain, true, GameOptions.CorpseStyleAsContainers));
        // always a grid, always classic
        Assert.True(GameOptions.OpensGrid(true, plain, true, GameOptions.CorpseStyleGrid));
        Assert.False(GameOptions.OpensGrid(false, plain, true, GameOptions.CorpseStyleClassic));
        // not a corpse: the style does nothing
        Assert.False(GameOptions.OpensGrid(true, plain, false, GameOptions.CorpseStyleGrid));
        Assert.True(GameOptions.OpensGrid(false, plain, false, GameOptions.CorpseStyleClassic));
        Assert.Equal(GameOptions.OpensGrid(false, board), GameOptions.OpensGrid(false, board, false, GameOptions.CorpseStyleGrid));
    }

    [Fact]
    public void Window_GrowsWithColumnsAndRows_AndKeepsRoomForTheTools()
    {
        const int cell = 50;
        var (w, h) = GridContainerWindow.WindowSize(cell, 7, 4);
        Assert.Equal(8 + 14 + 7 * cell + 8, w);
        Assert.Equal(20 + GridContainerWindow.ToolsHeight + 8 + 4 * (cell + 1), h);
        var area = GridContainerWindow.ScrollAreaUnderTools(w, h);
        Assert.Equal((4, 24 + GridContainerWindow.ToolsHeight, w - 8, 4 * (cell + 1)), area);
        var origins = new List<(int X, int Y)>();
        GridContainerWindow.CellOrigins(28, cell, area.Width, origins);
        Assert.Equal(7, origins.Count(o => o.Y == 0));
        Assert.Equal(0, GridContainerWindow.MaxScroll(origins, cell, area.Height));
        var tools = GridContainerWindow.ToolsArea(w);
        Assert.Equal((4, 24, w - 8, GridContainerWindow.ToolsHeight), tools);
        // three small cells would be too narrow for the tools: the window keeps a least width
        Assert.Equal(GridContainerWindow.MinToolsWidth, GridContainerWindow.WindowSize(25, 3, 3).Width);
    }

    [Fact]
    public void Page_NoLongerListsWhatTheGridNowHas()
    {
        Assert.DoesNotContain(OptionsWindow.NotOffered, n => n.Page == OptionsWindow.Page.Containers && n.Text.Contains("search", System.StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(OptionsWindow.NotOffered, n => n.Text.StartsWith("Corpse style"));
        Assert.Contains(OptionsWindow.NotOffered, n => n.Page == OptionsWindow.Page.Containers && n.Text.StartsWith("Nearby loot"));
    }
}
