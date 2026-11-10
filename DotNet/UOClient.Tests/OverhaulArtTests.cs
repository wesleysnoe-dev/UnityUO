using System.IO;
using UOClient.IO;

namespace UOClient.Tests;

/// <summary>The overhaul's HD art folders (settings overhaulArt) over the HD pack, under the shard's own art.</summary>
public class OverhaulArtTests
{
    private static string Touch(string dir, string name)
    {
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, name);
        File.WriteAllBytes(path, new byte[] { 1 });
        return path;
    }

    [Fact]
    public void Overhaul_WinsOverThePack_ExternalImagesWinOverBoth()
    {
        string root = Path.Combine(Path.GetTempPath(), "uoc-overhaul-" + System.Guid.NewGuid().ToString("N"));
        try
        {
            string client = Path.Combine(root, "Client");
            string pack = Touch(Path.Combine(root, "HDArt", "art"), "0x0100.png");
            Touch(Path.Combine(root, "HDArt", "art"), "0x0200.png");
            Touch(Path.Combine(root, "HDArt", "art"), "0x0300.png");
            string over = Touch(Path.Combine(root, "Overhaul", "art"), "0x0100.png");
            Touch(Path.Combine(root, "Overhaul", "art"), "0x0300.png");
            string shard = Touch(Path.Combine(client, "ExternalImages", "art"), "0x0300.png");

            var o = new ClientOverrides(client, includeHdPack: true, new[] { Path.Combine(root, "Overhaul"), Path.Combine(root, "Missing") });
            Assert.Equal(over, o.ArtPath(0x0100));                       // overhaul over the pack
            Assert.Equal(Path.Combine(root, "HDArt", "art", "0x0200.png"), o.ArtPath(0x0200)); // the pack where the overhaul has none
            Assert.Equal(shard, o.ArtPath(0x0300));                      // the shard's own art over both
            Assert.Single(o.OverhaulFolders);
            Assert.Contains(o.Problems, p => p.Contains("Missing"));
            Assert.NotEqual(pack, o.ArtPath(0x0100));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Theme_GumpsWinOverTheShardsOwn_OnlyWhereItHasOne()
    {
        string root = Path.Combine(Path.GetTempPath(), "uoc-theme-" + System.Guid.NewGuid().ToString("N"));
        try
        {
            string client = Path.Combine(root, "Client");
            string shardKit = Touch(Path.Combine(client, "ExternalImages", "gumps"), "0xB700.png");
            string shardGem = Touch(Path.Combine(client, "ExternalImages", "gumps"), "0xB730.png");
            string theme = Touch(Path.Combine(root, "Theme", "gumps"), "0xB700.png");
            string stockTheme = Touch(Path.Combine(root, "Theme", "gumps"), "0x2436.png"); // 9270, which the shard leaves stock

            var o = new ClientOverrides(client, includeHdPack: false);
            Assert.Null(o.ThemeGumpPath(0xB700));                   // no theme until asked
            o.UseTheme(Path.Combine(root, "Theme"));
            Assert.Equal(theme, o.ThemeGumpPath(0xB700));           // ours over the shard's kit piece
            Assert.Equal(shardKit, o.GumpPath(0xB700));             // the shard's stays known (what ours is measured against)
            Assert.Null(o.ThemeGumpPath(0xB730));                   // the shard's gem, where we have none
            Assert.Equal(shardGem, o.GumpPath(0xB730));
            Assert.Equal(stockTheme, o.ThemeGumpPath(9270));
            Assert.True(o.HasGump(9270));
            Assert.Equal(2, o.ThemeGumpCount);
            o.UseTheme(Path.Combine(root, "Missing"));              // a missing folder adds nothing and throws nothing
            Assert.Equal(2, o.ThemeGumpCount);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
