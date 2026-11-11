using UOClient.IO;
using UOClient.IO.Formats;

namespace UOClient.Tests;

public sealed class SettingsTests
{
    [Fact]
    public void ItsOwnDataFolder_WinsOverTheOldClients_ButNotOverAChoice()
    {
        string root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "uoclient-own-" + System.Guid.NewGuid().ToString("N"));
        string oldData = System.IO.Path.Combine(ClientSettings.UoSmallzRoot, "UOData", "7.0.23.1", "7.0.23.1");
        string oldClient = System.IO.Path.Combine(ClientSettings.UoSmallzRoot, "Client");
        try
        {
            ClientSettings.OwnRoot = root;
            Assert.Null(ClientSettings.OwnDataPath);                 // nothing there yet: as before
            Assert.Null(ClientSettings.OwnOverridesPath);
            Assert.Equal(oldData, new ClientSettings { uoDataPath = oldData }.ResolvedDataPath);

            System.IO.Directory.CreateDirectory(System.IO.Path.Combine(root, "UOData"));
            System.IO.File.WriteAllText(System.IO.Path.Combine(root, "UOData", "tiledata.mul"), "");
            System.IO.Directory.CreateDirectory(System.IO.Path.Combine(root, "Shard", "Client"));
            string own = System.IO.Path.Combine(root, "UOData"), ownShard = System.IO.Path.Combine(root, "Shard", "Client");
            Assert.Equal(own, new ClientSettings().ResolvedDataPath);
            Assert.Equal(own, new ClientSettings { uoDataPath = oldData }.ResolvedDataPath);          // a saved old default
            Assert.Equal(own, new ClientSettings { uoDataPath = oldData.Replace('\\', '/') }.ResolvedDataPath);
            Assert.Equal(@"D:\uo", new ClientSettings { uoDataPath = @"D:\uo" }.ResolvedDataPath);    // the player's own choice stays
            Assert.Equal(ownShard, new ClientSettings().ResolvedOverridesPath);
            Assert.Equal(ownShard, new ClientSettings { overridesPath = oldClient }.ResolvedOverridesPath);
            Assert.Null(new ClientSettings { overridesPath = "none" }.ResolvedOverridesPath);
        }
        finally
        {
            ClientSettings.OwnRoot = null;
            if (System.IO.Directory.Exists(root))
                System.IO.Directory.Delete(root, true);
        }
    }

    [Fact]
    public void DefaultsToTheLiveShardAndArgumentsSwitchIt()
    {
        var s = new ClientSettings();
        Assert.Equal("uosmallz.duckdns.org:2593", s.Server); // what a fresh install connects to
        Assert.False(s.IsLocal);
        s.ApplyArguments(new[] { "Unity UO.exe", "-server", "127.0.0.1" }); // the local test server, by argument
        Assert.Equal("127.0.0.1:2593", s.Server);
        Assert.True(s.IsLocal);

        s.ApplyArguments(new[] { "Unity UO.exe", "-server", "uosmallz.duckdns.org:2593", "-uodata", @"D:\uo" });
        Assert.Equal("uosmallz.duckdns.org", s.host);
        Assert.Equal(2593, s.port);
        Assert.False(s.IsLocal);
        Assert.Equal(@"D:\uo", s.ResolvedDataPath);

        s.ApplyArguments(new[] { "--server", "10.0.0.5" }); // port defaults to 2593
        Assert.Equal("10.0.0.5:2593", s.Server);
        Assert.False(ClientSettings.TryParseServer("", out _, out _));
    }

    [Fact]
    public void OverridesFollowTheClientPatchRules()
    {
        string dir = Path.Combine(Path.GetTempPath(), "uoclient-overrides-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(dir, "TileData"));
            Directory.CreateDirectory(Path.Combine(dir, "EquipConv"));
            Directory.CreateDirectory(Path.Combine(dir, "ExternalImages", "art"));
            File.WriteAllLines(Path.Combine(dir, "TileData", "items.csv"), new[]
            {
                "# comment",
                "0x4C38,0x400000,1,22,0,1205,0,0,1,hooded shroud",   // shard block: accepted
                "0x1F03,0x400000,1,22,0,469,0,0,1,robe",             // stock id: refused
                "0xFE00,0x204040,255,0,0,0,0,8,12",                  // 9 fields: refused
            });
            File.WriteAllLines(Path.Combine(dir, "EquipConv", "robe.csv"), new[] { "400,1205,970,0,0", "400,1300,970,0,0" });
            Png.Write(Path.Combine(dir, "ExternalImages", "art", "0x4C38.png"), new Bitmap32(44, 43));

            var o = new ClientOverrides(dir, includeHdPack: false);
            Assert.Equal(1, o.TileRowCount);
            Assert.Equal(1, o.EquipConvCount);
            Assert.Equal(1, o.ArtCount);
            Assert.Equal(3, o.Problems.Count);                    // the stock row, the short row, the out-of-range anim
            Assert.True(o.TryEquipConv(400, 1205, out EquipConvEntry e));
            Assert.Equal(970, e.Anim);
            Assert.Equal(1205, e.Gump);                           // gump 0 = the item's own anim (+50000 on the paperdoll)
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void ChatLinksAreCutOffTheLine()
    {
        string text = "Bob is looking for a group for Despise (2/5) {{link join 12}}";
        Assert.Equal("join 12", UOClient.Core.GameSession.ExtractLink(ref text));
        Assert.Equal("Bob is looking for a group for Despise (2/5)", text);

        string plain = "anyone selling cloth {{link steal 3}}";   // not a verb the server uses: left alone
        Assert.Null(UOClient.Core.GameSession.ExtractLink(ref plain));
        Assert.EndsWith("}}", plain);
    }

    [Fact]
    public void HdArtIsAnExactMultipleOfTheStockSize()
    {
        Assert.Equal(2, ClientOverrides.HdScale(88, 78, 44, 39));
        Assert.Equal(1, ClientOverrides.HdScale(67, 58, 44, 39));  // any other size is a plain replacement
        Assert.Equal(1, ClientOverrides.HdScale(88, 78, 0, 0));    // no stock art to compare with

        var png = new Bitmap32(4, 2);
        png.Pixels[1] = 0xFF112233;                                // one opaque pixel in the first 2x2 block
        Bitmap32 shape = ClientOverrides.HdShape(png, 2);
        Assert.Equal(2, shape.Width);
        Assert.Equal(0xFF112233u, shape.Pixels[0]);
        Assert.Equal(0u, shape.Pixels[1]);
    }
}
