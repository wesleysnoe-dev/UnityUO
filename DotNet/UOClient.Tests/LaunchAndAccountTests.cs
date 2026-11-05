using UOClient.Unity;

namespace UOClient.Tests;

public sealed class LaunchAndAccountTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "uoclient-project");
    private const string AccountFile = "UserSettings/uoclient-account.json";

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("localhost")]
    [InlineData("::1")]
    public void TheLocalServerUsesTheTestAccountFile(string host)
    {
        Assert.Equal(Path.Combine(Root, AccountFile), AccountStore.PathFor(AccountFile, Root, host));
    }

    [Fact]
    public void AnyOtherServerGetsItsOwnFileNextToIt()
    {
        string dir = Path.GetDirectoryName(Path.Combine(Root, AccountFile));
        Assert.Equal(Path.Combine(dir, "uoclient-account-uosmallz.duckdns.org.json"),
                     AccountStore.PathFor(AccountFile, Root, "uosmallz.duckdns.org"));
        Assert.Equal(Path.Combine(dir, "uoclient-account-10.0.0.5.json"), AccountStore.PathFor(AccountFile, Root, "10.0.0.5"));
        // Anything that is not a letter, digit, '.' or '-' becomes '_', so a host can't climb out of the folder.
        Assert.Equal(Path.Combine(dir, "uoclient-account-.._.._evil_host.json"), AccountStore.PathFor(AccountFile, Root, "../../evil:host"));
        // The match is exact: only those three spellings count as local.
        Assert.Equal(Path.Combine(dir, "uoclient-account-LOCALHOST.json"), AccountStore.PathFor(AccountFile, Root, "LOCALHOST"));
        Assert.Equal(Path.Combine(dir, "uoclient-account-127.0.0.2.json"), AccountStore.PathFor(AccountFile, Root, "127.0.0.2"));
        Assert.NotEqual(Path.Combine(Root, AccountFile), AccountStore.PathFor(AccountFile, Root, ""));
    }

    [Fact]
    public void ARootedAccountFileIsUsedAsIs()
    {
        string rooted = Path.Combine(Path.GetTempPath(), "elsewhere", "test-account.json");
        Assert.Equal(rooted, AccountStore.PathFor(rooted, Root, "localhost"));
        Assert.Equal(Path.Combine(Path.GetTempPath(), "elsewhere", "uoclient-account-example.com.json"),
                     AccountStore.PathFor(rooted, Root, "example.com"));
    }

    [Fact]
    public void LaunchOptionsReadTheCommandLine()
    {
        LaunchOptions none = LaunchOptions.Parse(new[] { "Unity UO.exe" });
        Assert.False(none.CaptureFromCommandLine);
        Assert.False(none.AutoLogin);
        Assert.False(none.ServerFromArguments);
        Assert.Null(none.OfflineAt);

        LaunchOptions all = LaunchOptions.Parse(new[] { "Unity UO.exe", "-uoclient-capture", "-uoclient-autologin", "--SERVER", "10.0.0.5", "-offline", "1", "1467", "1671" });
        Assert.True(all.CaptureFromCommandLine);
        Assert.True(all.AutoLogin);
        Assert.True(all.ServerFromArguments);
        Assert.Equal((1, 1467, 1671), all.OfflineAt);

        Assert.Null(LaunchOptions.Parse(new[] { "x", "-offline", "1", "1467" }).OfflineAt);
        Assert.Null(LaunchOptions.Parse(new[] { "x", "-offline", "1", "a", "1671" }).OfflineAt);
    }
}
