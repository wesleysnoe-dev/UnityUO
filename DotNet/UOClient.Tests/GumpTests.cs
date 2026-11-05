using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using UOClient.Core;
using UOClient.Net;

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

    [Fact]
    public void ReplyCarriesButtonSwitchesAndTextEntries()
    {
        byte[] p = GameSession.GumpReply(0x01020304, 0x0A0B0C0D, 3, new[] { 5 }, new List<(int, string)> { (7, "hi") });

        Assert.Equal(new byte[]
        {
            0xB1, 0x00, 0x23,           // id, length 35
            0x01, 0x02, 0x03, 0x04,     // gump serial
            0x0A, 0x0B, 0x0C, 0x0D,     // type id
            0x00, 0x00, 0x00, 0x03,     // button
            0x00, 0x00, 0x00, 0x01,     // one switch on
            0x00, 0x00, 0x00, 0x05,     //   switch id 5
            0x00, 0x00, 0x00, 0x01,     // one text entry
            0x00, 0x07, 0x00, 0x02,     //   entry id 7, 2 characters
            0x00, (byte)'h', 0x00, (byte)'i'
        }, p);
    }

    [Fact]
    public void ModernUOReadsTheReplyBack()
    {
        string longText = new string('x', 300) + "é";
        var texts = new List<(int, string)> { (1, "Wes Émile"), (2, ""), (9, longText) };
        byte[] p = GameSession.GumpReply(0x40000001, 0x1234, 12, new[] { 3, 70000 }, texts);

        // Mirrors GumpSystem.DisplayGumpResponse.
        int pos = 3;
        int Int() { int v = BinaryPrimitives.ReadInt32BigEndian(p.AsSpan(pos)); pos += 4; return v; }
        int UShort() { int v = BinaryPrimitives.ReadUInt16BigEndian(p.AsSpan(pos)); pos += 2; return v; }
        Assert.Equal(0x40000001, Int());
        Assert.Equal(0x1234, Int());
        Assert.Equal(12, Int());
        int switches = Int();
        Assert.Equal(new[] { 3, 70000 }, Enumerable.Range(0, switches).Select(_ => Int()).ToArray());
        int count = Int();
        var read = new List<(int, string)>();
        for (int i = 0; i < count; i++)
        {
            int id = UShort(), length = UShort();
            Assert.True(length <= 239, "ModernUO drops a response with a longer text");
            read.Add((id, Encoding.BigEndianUnicode.GetString(p, pos, length * 2)));
            pos += length * 2;
        }
        Assert.Equal(p.Length, pos);
        Assert.Equal(new List<(int, string)> { (1, "Wes Émile"), (2, ""), (9, new string('x', 239)) }, read);
    }

    [Fact]
    public void CompressedGumpOpensWithLatin1LayoutAndLines()
    {
        var session = new GameSession(new LoginSettings());
        ServerGump? opened = null;
        session.World.GumpOpened += g => opened = g;

        session.Inject(CompressedGump(0x40000001, 0x99, "{ page 0 }{ xmfhtmltok 0 0 100 20 0 0 0 1011036 @André@ }{ htmlgump 0 20 100 40 0 1 0 }{ textentry 0 70 100 20 0 4 1 }",
            "<BASEFONT COLOR=#FFFFFF>Hello</BASEFONT>", "name"), true);

        Assert.NotNull(opened);
        Assert.Equal(0x40000001u, opened!.Serial);
        Assert.Equal(0x99u, opened.TypeId);
        Assert.Equal("@André@", opened.Commands[1].Words[^1]);
        Assert.Equal("<BASEFONT COLOR=#FFFFFF>Hello</BASEFONT>", opened.Line(opened.Commands[2].Arg(4)));
        Assert.Equal("name", opened.Line(opened.Commands[3].Arg(6)));
    }

    [Fact]
    public void ServerCloseTakesOnlyTheOldestGumpOfThatType()
    {
        var session = new GameSession(new LoginSettings());
        var closed = new List<ServerGump>();
        session.World.GumpClosed += closed.Add;
        session.Inject(CompressedGump(1, 0x99, "{ page 0 }", "a"), true);
        session.Inject(CompressedGump(2, 0x99, "{ page 0 }", "b"), true);
        session.Inject(CompressedGump(3, 0x55, "{ page 0 }", "c"), true);

        // NetStateGumps.Close: 0xBF, length 13, sub 0x04, type id, button 0.
        session.Inject(new PacketWriter(0xBF).UInt16(0x04).UInt32(0x99).UInt32(0).ToArray(), true);

        Assert.Equal(new uint[] { 1 }, closed.Select(g => g.Serial));
        Assert.Equal(new uint[] { 2, 3 }, session.World.Gumps.Select(g => g.Serial));
    }

    [Fact]
    public void SingletonResendReplacesTheGumpWithTheSameSerial()
    {
        var session = new GameSession(new LoginSettings());
        var opened = new List<ServerGump>();
        session.World.GumpOpened += opened.Add;
        session.Inject(CompressedGump(7, 0x99, "{ page 0 }", "old"), true);

        // A singleton re-send: close by type, then the same gump instance (same serial) again.
        session.Inject(new PacketWriter(0xBF).UInt16(0x04).UInt32(0x99).UInt32(0).ToArray(), true);
        session.Inject(CompressedGump(7, 0x99, "{ page 0 }", "new"), true);

        Assert.Equal(2, opened.Count);
        ServerGump live = Assert.Single(session.World.Gumps);
        Assert.Same(opened[1], live);
        Assert.Equal("new", live.Line(0));
    }

    /// <summary>0xDD as ModernUO's DynamicGump.SendTo writes it.</summary>
    private static byte[] CompressedGump(uint serial, uint typeId, string layout, params string[] lines)
    {
        var w = new PacketWriter(0xDD).UInt32(serial).UInt32(typeId).Int32(40).Int32(50);
        Packed(w, Encoding.Latin1.GetBytes(layout + "\0"));
        var text = new List<byte>();
        foreach (string line in lines)
        {
            text.Add((byte)(line.Length >> 8));
            text.Add((byte)line.Length);
            text.AddRange(Encoding.BigEndianUnicode.GetBytes(line));
        }
        w.Int32(lines.Length);
        Packed(w, text.ToArray());
        return w.ToArray();
    }

    private static void Packed(PacketWriter w, byte[] data)
    {
        var ms = new MemoryStream();
        using (var z = new ZLibStream(ms, CompressionLevel.Optimal, leaveOpen: true))
            z.Write(data);
        byte[] packed = ms.ToArray();
        w.Int32(4 + packed.Length).Int32(data.Length);
        foreach (byte b in packed)
            w.Byte(b);
    }
}
