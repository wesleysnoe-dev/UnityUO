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
}
