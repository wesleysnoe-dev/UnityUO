using UOClient.Core;

namespace UOClient.Tests;

public sealed class GumpTests
{
    [Fact]
    public void ParsesLayoutCommandsAndArguments()
    {
        string layout = "{ page 0 }{ resizepic 0 0 9200 300 200 }{ gumppic 10 10 5100 hue=33 }" +
                        "{ button 20 150 4005 4007 1 0 3 }{ textentry 20 60 200 20 0 7 2 }" +
                        "{ xmfhtmltok 10 10 200 40 0 0 32767 1011036 @Hello\tWorld@ }{ noclose }";
        List<GumpCommand> cmds = ServerGump.ParseLayout(layout);

        Assert.Equal(new[] { "page", "resizepic", "gumppic", "button", "textentry", "xmfhtmltok", "noclose" }, cmds.Select(c => c.Name));
        Assert.Equal(new[] { 0, 0, 9200, 300, 200 }, cmds[1].Args);
        Assert.Equal(33, cmds[2].Named("hue"));
        Assert.Equal(3, cmds[3].Arg(6));                    // button id
        Assert.Equal(7, cmds[4].Arg(5));                    // text entry id
        Assert.Equal("@Hello\tWorld@", cmds[5].Words[^1]);  // cliloc arguments kept whole
        Assert.Equal(1011036, cmds[5].Arg(7));
    }

    [Fact]
    public void ParsesPackedTextLines()
    {
        // Two lines, "Hi" and "Yo!", as uint16 length + big-endian UTF-16.
        byte[] data = { 0, 2, 0, (byte)'H', 0, (byte)'i', 0, 3, 0, (byte)'Y', 0, (byte)'o', 0, (byte)'!' };
        string[] lines = (string[])typeof(GameSession).GetMethod("ParseLines", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .Invoke(null, new object[] { data, 2 })!;
        Assert.Equal(new[] { "Hi", "Yo!" }, lines);
    }
}
