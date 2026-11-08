using UOClient.Core;

namespace UOClient.Tests;

/// <summary>A clicked map static is named from tiledata in the singular (TileNames.Singular).</summary>
public class TileNamesTests
{
    [Theory]
    [InlineData("tree", "tree")]
    [InlineData("bottle%s%", "bottle")]
    [InlineData("loa%ves/f%", "loaf")]
    [InlineData("stone wall%s%", "stone wall")]
    [InlineData("  wooden sign  ", "wooden sign")]
    [InlineData("%s%", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    [InlineData("bad %marker", "bad %marker")]
    public void Singular(string raw, string expected) => Assert.Equal(expected, TileNames.Singular(raw));
}
