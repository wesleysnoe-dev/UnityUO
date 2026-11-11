using System;
using System.Collections.Generic;
using System.Linq;
using UOClient.Core;
using UOClient.Unity;
using Xunit;

namespace UOClient.Tests;

public class PathFinderTests
{
    /// <summary>A flat map from rows of text: '#' a wall, anything else floor; outside the rows is wall.</summary>
    private static Func<int, int, int, int?> Map(params string[] rows) => (x, y, z) =>
        y >= 0 && y < rows.Length && x >= 0 && x < rows[y].Length && rows[y][x] != '#' ? 0 : (int?)null;

    private static void AssertWalkable(List<PathFinder.Node> path, Func<int, int, int, int?> step)
    {
        for (int i = 1; i < path.Count; i++)
        {
            int dx = path[i].X - path[i - 1].X, dy = path[i].Y - path[i - 1].Y;
            Assert.True(PathFinder.DirectionOf(dx, dy) >= 0, "each step is to a neighbour");
            Assert.NotNull(step(path[i].X, path[i].Y, path[i - 1].Z));
            if (dx != 0 && dy != 0)
            {
                Assert.NotNull(step(path[i - 1].X + dx, path[i - 1].Y, path[i - 1].Z)); // no cutting corners
                Assert.NotNull(step(path[i - 1].X, path[i - 1].Y + dy, path[i - 1].Z));
            }
        }
    }

    [Fact]
    public void Directions_AreTheEightNeighbours()
    {
        for (int d = 0; d < 8; d++)
            Assert.Equal(d, PathFinder.DirectionOf(PathFinder.Delta(d).Dx, PathFinder.Delta(d).Dy));
        Assert.Equal((0, -1), PathFinder.Delta(0));   // north
        Assert.Equal((1, 0), PathFinder.Delta(2));    // east
        Assert.Equal(-1, PathFinder.DirectionOf(0, 0));
        Assert.Equal(-1, PathFinder.DirectionOf(2, 0));
    }

    [Fact]
    public void OpenGround_GoesStraight_DiagonalsCountOne()
    {
        var step = Map(".......", ".......", ".......", ".......");
        List<PathFinder.Node> path = PathFinder.Find(0, 0, 0, 6, 3, step);
        Assert.Equal(7, path.Count);                       // 6 steps: Chebyshev distance
        Assert.Equal((0, 0), (path[0].X, path[0].Y));
        Assert.Equal((6, 3), (path[^1].X, path[^1].Y));
        AssertWalkable(path, step);
        Assert.Single(PathFinder.Find(2, 2, 0, 2, 2, step)); // already there
    }

    [Fact]
    public void AWall_IsWalkedRound_WithoutCuttingItsCorner()
    {
        var step = Map(
            ".....",
            ".###.",
            ".#...",
            ".#.#.",
            "...#.");
        List<PathFinder.Node> path = PathFinder.Find(0, 0, 0, 2, 3, step);
        Assert.Equal((2, 3), (path[^1].X, path[^1].Y));
        AssertWalkable(path, step);
        Assert.DoesNotContain(path, n => step(n.X, n.Y, 0) == null);
    }

    [Fact]
    public void NoWayIn_GivesTheNearestTile_OrNothing()
    {
        var step = Map(
            ".......",
            ".###...",
            ".#.#...",
            ".###...",
            ".......");
        Assert.Null(PathFinder.Find(6, 4, 0, 2, 2, step, nearest: false));
        Assert.Null(PathFinder.Find(0, 0, 0, 2, 2, step));  // already as near as anyone can get
        List<PathFinder.Node> near = PathFinder.Find(6, 4, 0, 2, 2, step);
        Assert.NotNull(near);
        Assert.Equal(2, PathFinder.Distance(near[^1].X, near[^1].Y, 2, 2)); // up against the wall round it
        AssertWalkable(near, step);
        // Walled in at the start: nothing is nearer than where you stand.
        Assert.Null(PathFinder.Find(2, 2, 0, 0, 0, step));
        Assert.Null(PathFinder.Find(0, 0, 0, 0, PathFinder.MaxDistance + 1, step)); // too far to try
    }

    [Fact]
    public void Height_IsCarriedAlong_SoStairsWorkAndCliffsDoNot()
    {
        // x is the height; a step may climb 2 at most.
        Func<int, int, int, int?> stairs = (x, y, z) => y == 0 && x >= 0 && x <= 5 && Math.Abs(x * 2 - z) <= 2 ? x * 2 : (int?)null;
        List<PathFinder.Node> up = PathFinder.Find(0, 0, 0, 5, 0, stairs);
        Assert.Equal(new[] { 0, 2, 4, 6, 8, 10 }, up.Select(n => n.Z));
        Func<int, int, int, int?> cliff = (x, y, z) => y == 0 && x >= 0 && x <= 5 && Math.Abs((x >= 3 ? 20 : 0) - z) <= 2 ? (x >= 3 ? 20 : 0) : (int?)null;
        List<PathFinder.Node> stopped = PathFinder.Find(0, 0, 0, 5, 0, cliff);
        Assert.Equal((2, 0), (stopped[^1].X, stopped[^1].Y)); // the foot of the cliff
    }

    [Fact]
    public void TheClick_NeedsTheOptions()
    {
        Assert.True(GameOptions.Defaults().enablePathfind);
        Assert.False(GameOptions.PathfindClick(false, false, true, true, true));
        Assert.True(GameOptions.PathfindClick(true, false, false, false, true));    // a double-click
        Assert.False(GameOptions.PathfindClick(true, false, false, false, false));  // one click is not enough
        Assert.True(GameOptions.PathfindClick(true, false, true, false, false));    // unless asked
        Assert.False(GameOptions.PathfindClick(true, true, true, false, true));     // Shift asked for, not held
        Assert.True(GameOptions.PathfindClick(true, true, false, true, true));
    }
}
