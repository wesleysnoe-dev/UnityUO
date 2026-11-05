using UOClient.IO;
using UOClient.IO.Formats;

namespace UOClient.Tests;

[Collection("UO install")]
public sealed class ClilocTests
{
    private readonly UOData _data;

    public ClilocTests(UOInstallFixture fixture) => _data = fixture.Data;

    [Fact]
    public void ReadsTheInstallsClilocTable()
    {
        var cliloc = new Cliloc(Path.Combine(_data.Directory, "Cliloc.enu"));
        Assert.True(cliloc.Count > 100_000, $"only {cliloc.Count} entries");
        Assert.Equal("You must wait to perform another action.", cliloc.Get(500119));
        Assert.False(string.IsNullOrEmpty(cliloc.Get(1001002))); // used by the help gump
    }
}
