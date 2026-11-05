using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using UOClient.Core;
using UOClient.Net;
using UOClient.Unity.UI;

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
    public void ParsesModernUOLayoutBuilderCommands()
    {
        // As GumpLayoutBuilder writes them: AddSpriteImage, AddImageTiledButton + its tooltip with arguments,
        // AddTextEntryLimited (StValentinesBears' 25-character lines, and the default size 0), AddGumpIdOverride.
        string layout = "{ picinpic 10 20 9200 30 40 5 6 }" +
                        "{ buttontileart 0 0 4005 4007 1 0 9 3823 0 18 12 }{ tooltip 1062547 @Wes\t#1011036@ }" +
                        "{ tooltip 1011036 }" +
                        "{ textentrylimited 15 152 390 20 0 0 1 25 }{ textentrylimited 15 202 390 20 0 1 2 0 }" +
                        "{ textentrylimited 0 0 10 20 0 2 3 500 }{ textentry 0 0 10 20 0 3 4 }{ mastergump 2000 }";
        List<GumpCommand> c = ServerGump.ParseLayout(layout);

        Assert.Equal(new[] { 10, 20, 9200, 30, 40, 5, 6 }, c[0].Args);
        Assert.Equal(9, c[1].Arg(6));                       // tile-art button reply id
        Assert.Equal(1062547, c[2].Arg(0));
        Assert.Equal("Wes\t#1011036", c[2].ClilocArgs);
        Assert.Null(c[3].ClilocArgs);
        Assert.Equal(25, c[4].TextLimit);
        Assert.Equal(GumpCommand.MaxReplyText, c[5].TextLimit); // size 0: no limit of its own, not one character
        Assert.Equal(GumpCommand.MaxReplyText, c[6].TextLimit); // never more than ModernUO accepts back
        Assert.Equal(GumpCommand.MaxReplyText, c[7].TextLimit);
        Assert.Equal("mastergump", c[8].Name);
    }

    [Fact]
    public void XmfHtmlTokArgumentsComeFromTheAtBlock()
    {
        GumpCommand c = ServerGump.ParseLayout("{ xmfhtmltok 10 10 200 40 0 0 32767 1011036 @a b\tc@ }")[0];
        Assert.Equal(1011036, c.Arg(7));
        Assert.Equal("a b\tc", c.ClilocArgs);
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

    [Fact]
    public void UncompressedGumpFromTheGmPanelOpensWithPaddedTextIndices()
    {
        // CustomBots' UncompressedGump.ToUncompressed: the 0xDD inflated into 0xB0 (u16 layout length, the layout
        // with its 0, u16 line count, [u16 n][UTF-16BE] lines). StaticGump placeholders pad text indices to 5 digits.
        string layout = "{ page 0 }{ resizepic 0 0 9270 560 470 }{ text 48 73 1153 00001 }{ htmlgump 24 20 512 22 00000 0 0 }\0";
        var w = new PacketWriter(0xB0).UInt32(0x40000002).UInt32(0x77).Int32(120).Int32(70).UInt16(layout.Length);
        foreach (byte b in Encoding.Latin1.GetBytes(layout))
            w.Byte(b);
        string[] lines = { "<B>Chat</B>", "Global" };
        w.UInt16(lines.Length);
        foreach (string line in lines)
        {
            w.UInt16(line.Length);
            foreach (byte b in Encoding.BigEndianUnicode.GetBytes(line))
                w.Byte(b);
        }
        var session = new GameSession(new LoginSettings());
        ServerGump? opened = null;
        session.World.GumpOpened += g => opened = g;

        session.Inject(w.ToArray(), true);

        Assert.NotNull(opened);
        Assert.Equal((0x77u, 120, 70), (opened!.TypeId, opened.X, opened.Y));
        Assert.Equal(new[] { "page", "resizepic", "text", "htmlgump" }, opened.Commands.Select(c => c.Name));
        Assert.Equal("Global", opened.Line(opened.Commands[2].Arg(3)));
        Assert.Equal("<B>Chat</B>", opened.Line(opened.Commands[3].Arg(4)));
    }

    [Fact]
    public void GumpTextAndPictureHuesAreShiftedAsTazUOShiftsThem()
    {
        // Label, CroppedText and StbTextBox add one: ChatSettingsGump's 1153 labels and its "Hue N" swatches.
        Assert.Equal(1154, ServerGumpWindow.TextHue(1153));
        Assert.Equal(1, ServerGumpWindow.TextHue(0));
        // GumpPic adds one, then TransformHue leaves 2 and below unhued.
        Assert.Equal(0, ServerGumpWindow.PicHue(0));
        Assert.Equal(0, ServerGumpWindow.PicHue(1));
        Assert.Equal(3, ServerGumpWindow.PicHue(2));
        Assert.Equal(34, ServerGumpWindow.PicHue(33));
    }

    [Fact]
    public void RadioGroupsCountGroupCommandsAsTazUODoes()
    {
        // ChatSettingsGump: a checkbox, AddGroup(1) + the bot-talk radios, AddGroup(2) + the hue radios.
        List<GumpCommand> chat = ServerGump.ParseLayout(
            "{ page 0 }{ checkbox 24 72 210 211 1 100 }{ group 1 }{ radio 24 126 208 209 0 200 }{ radio 134 126 208 209 1 201 }" +
            "{ group 2 }{ radio 24 200 208 209 1 300 }{ radio 128 200 208 209 0 301 }");
        Dictionary<int, int> g = ServerGumpWindow.RadioGroups(chat); // by command index
        Assert.Equal(g[3], g[4]);
        Assert.Equal(g[6], g[7]);
        Assert.NotEqual(g[3], g[6]);
        Assert.False(g.ContainsKey(1));

        // AuctionSellGump's AddGroup(0): its radios are a group apart from one laid before it, though both say 0.
        g = ServerGumpWindow.RadioGroups(ServerGump.ParseLayout(
            "{ radio 0 0 208 209 1 5 }{ group 0 }{ radio 0 20 208 209 1 6 }{ radio 0 40 208 209 0 7 }"));
        Assert.NotEqual(g[0], g[2]);
        Assert.Equal(g[2], g[3]);
    }

    [Fact]
    public void ChatSettingsReplyCarriesWhatIsCheckedInLayoutOrder()
    {
        // ChatSettingsGump (legacy, 0xB0): channel checkboxes, AddGroup(1) bot radios, AddGroup(2) hue radios, sound.
        var gump = Gump(
            "{ page 0 }{ checkbox 24 72 210 211 1 100 }{ checkbox 134 72 210 211 0 101 }" +
            "{ group 1 }{ radio 24 126 208 209 0 200 }{ radio 134 126 208 209 1 201 }{ radio 244 126 208 209 0 202 }" +
            "{ group 2 }{ radio 24 200 208 209 1 300 }{ radio 128 200 208 209 0 301 }" +
            "{ checkbox 24 300 210 211 1 400 }{ button 340 428 4005 4007 1 0 2 }");
        var controls = new GumpControls(gump);
        Assert.Equal(new[] { 100, 201, 300, 400 }, controls.Switches());

        controls.Click(2);  // channel 101 on
        controls.Click(1);  // channel 100 off
        controls.Click(6);  // bot radio 202: 201 goes off, the hue group is untouched
        Assert.Equal(new[] { 101, 202, 300, 400 }, controls.Switches());

        controls.Click(9);  // hue radio 301
        Assert.Equal(new[] { 101, 202, 301, 400 }, controls.Switches());

        // TazUO's Checkbox.OnMouseUp flips a radio like a checkbox: clicking the checked one turns it off.
        controls.Click(9);
        Assert.Equal(new[] { 101, 202, 400 }, controls.Switches());
        Assert.Empty(controls.Entries());

        // The save button replies id 2 (its last argument), not the param.
        Assert.Equal((false, 2), ServerGumpWindow.ButtonPress(gump.Commands[11]));
    }

    [Fact]
    public void SwitchesStartOnOnlyForOneAndTwoOnInAGroupStayOn()
    {
        // Checkbox(parts): IsChecked = parts[5] == "1"; set before the radio has a group to clear.
        var controls = new GumpControls(Gump("{ group 1 }{ radio 0 0 208 209 1 1 }{ radio 0 20 208 209 1 2 }{ radio 0 40 208 209 0 3 }{ checkbox 0 60 210 211 2 4 }"));
        Assert.Equal(new[] { 1, 2 }, controls.Switches());
        controls.Click(3);
        Assert.Equal(new[] { 3 }, controls.Switches());
    }

    [Fact]
    public void AuctionReplyKeepsEveryPagesEntriesAndSwitches()
    {
        // AuctionOrderGump: amount + price entries, AddGroup(0) duration radios; the reply carries them in layout
        // order wherever they are, and a page-1 switch counts while page 2 shows.
        var gump = Gump(
            "{ page 0 }{ resizepic 0 0 9270 420 260 }{ textentry 136 111 90 20 0 2 0 }" +
            "{ page 1 }{ textentry 136 145 150 20 0 3 1 }{ group 0 }{ radio 110 180 208 209 0 10 }{ radio 210 180 208 209 1 11 }" +
            "{ button 300 220 4005 4007 0 2 0 }" +
            "{ page 2 }{ checkbox 20 20 210 211 1 20 }{ button 300 220 4023 4025 1 0 5 }",
            "3", "1500");
        var controls = new GumpControls(gump);

        controls.SetText(2, "12");
        Assert.Equal(new List<(int, string)> { (2, "12"), (3, "1500") }, controls.Entries());
        Assert.Equal(new[] { 11, 20 }, controls.Switches());
        Assert.Equal((true, 2), ServerGumpWindow.ButtonPress(gump.Commands[8]));
        Assert.Equal((false, 5), ServerGumpWindow.ButtonPress(gump.Commands[11]));

        // Through the packet, as DisplayGumpResponse reads it: counts within what the gump declared.
        byte[] p = GameSession.GumpReply(gump.Serial, gump.TypeId, 5, controls.Switches(), controls.Entries());
        (int button, int[] switches, List<(int, string)> texts) = ReadReply(p);
        Assert.Equal(5, button);
        Assert.Equal(new[] { 11, 20 }, switches);
        Assert.True(switches.Length <= 3, "a gump with 3 switch controls");
        Assert.Equal(new List<(int, string)> { (2, "12"), (3, "1500") }, texts);
    }

    [Fact]
    public void TextEntriesWithTheSameIdAreSeparateBoxes()
    {
        // TazUO keeps one StbTextBox per command and sends each; ModernUO's GetTextEntry takes the first.
        var controls = new GumpControls(Gump("{ textentry 0 0 100 20 0 1 0 }{ textentry 0 30 100 20 0 1 1 }", "first", "second"));
        controls.SetText(1, "edited");
        Assert.Equal(new List<(int, string)> { (1, "first"), (1, "edited") }, controls.Entries());
    }

    [Fact]
    public void StartingTextIsCutToTheEntrysLimit()
    {
        string longText = new string('a', 300);
        var controls = new GumpControls(Gump(
            "{ textentrylimited 0 0 100 20 0 1 0 25 }{ textentrylimited 0 30 100 20 0 2 0 0 }{ textentry 0 60 100 20 0 3 0 }{ textentry 0 90 100 20 0 4 9 }",
            longText));
        List<(int Id, string Text)> e = controls.Entries();
        Assert.Equal(25, e[0].Text.Length);   // StbTextBox.SetText cuts to the size
        Assert.Equal(239, e[1].Text.Length);  // size 0: no limit of its own; ModernUO takes 239
        Assert.Equal(239, e[2].Text.Length);
        Assert.Equal("", e[3].Text);          // a missing line starts empty
    }

    [Fact]
    public void PageButtonToPageZeroShowsPageOne()
    {
        // Gump.Update: ActivePage 0 becomes 1.
        Assert.Equal(1, ServerGumpWindow.PageShown(0));
        Assert.Equal((true, 1), ServerGumpWindow.ButtonPress(ServerGump.ParseLayout("{ button 0 0 4005 4007 0 0 0 }")[0]));
        Assert.Equal((false, 9), ServerGumpWindow.ButtonPress(ServerGump.ParseLayout("{ buttontileart 0 0 4005 4007 1 0 9 3823 0 18 12 }")[0]));
    }

    [Fact]
    public void ShardHtmlShowsWithBalancedRichText()
    {
        // AuctionGump's title: color + size + bold, all closed.
        Assert.Equal("<size=16><color=#F4F4F4><b>Auction House</b></color></size>",
            GumpHtml.ToRichText("<BASEFONT COLOR=#F4F4F4 SIZE=4><B>Auction House</B></BASEFONT>", out _));

        // A <B> never closed (AdminTools' "<B>{text}" lines were closed, but a cut-off one is not) stays bold to the
        // end as in TazUO, and the rich text stays balanced.
        Assert.Equal("<b>Spawn</b><b> here</b>", GumpHtml.ToRichText("<B>Spawn<BR> here", out _).Replace("\n", ""));

        // Misnested: TazUO closes the last tag of each kind; no stray </b> or </color> is left.
        Assert.Equal("<color=#FF0000><b>a</b></color><color=#FF0000>b</color>c",
            GumpHtml.ToRichText("<BASEFONT COLOR=#FF0000><B>a</B>b</BASEFONT>c", out _));
        Assert.Equal("<color=#FF0000><b>a</b></color><color=#FF0000>b</color>",
            GumpHtml.ToRichText("<B><BASEFONT COLOR=#FF0000>a</B>b</BASEFONT>", out _));
    }

    [Fact]
    public void EscapedPlayerTextStaysText()
    {
        // ChatGumps / GroupFinder write a player's words through ModernUO's EscapeHtml.
        string rich = GumpHtml.ToRichText("<BASEFONT COLOR=#FFD24A>&lt;color=red&gt;hi&lt;/color&gt; &amp; bye</BASEFONT>", out _);
        Assert.Equal("<color=#FFD24A>‹color=red›hi‹/color› & bye</color>", rich);
    }

    [Fact]
    public void CenteredHtmlIsCentered()
    {
        GumpHtml.ToRichText("<CENTER>Group Finder</CENTER>", out bool centered);
        Assert.True(centered);
        GumpHtml.ToRichText("<DIV ALIGN=CENTER>x</DIV>", out centered);
        Assert.True(centered);
        GumpHtml.ToRichText("x", out centered);
        Assert.False(centered);
    }

    private static ServerGump Gump(string layout, params string[] lines) => new ServerGump
    {
        Serial = 0x40000010, TypeId = 0x1234, Commands = ServerGump.ParseLayout(layout), Lines = lines
    };

    /// <summary>The 0xB1 body as GumpSystem.DisplayGumpResponse reads it.</summary>
    private static (int Button, int[] Switches, List<(int, string)> Texts) ReadReply(byte[] p)
    {
        Assert.Equal(p.Length, BinaryPrimitives.ReadUInt16BigEndian(p.AsSpan(1)));
        int pos = 11;
        int Int() { int v = BinaryPrimitives.ReadInt32BigEndian(p.AsSpan(pos)); pos += 4; return v; }
        int UShort() { int v = BinaryPrimitives.ReadUInt16BigEndian(p.AsSpan(pos)); pos += 2; return v; }
        int button = Int();
        int[] switches = new int[Int()];
        for (int i = 0; i < switches.Length; i++)
            switches[i] = Int();
        var texts = new List<(int, string)>();
        for (int n = Int(), i = 0; i < n; i++)
        {
            int id = UShort(), length = UShort();
            texts.Add((id, Encoding.BigEndianUnicode.GetString(p, pos, length * 2)));
            pos += length * 2;
        }
        Assert.Equal(p.Length, pos);
        return (button, switches, texts);
    }

    [Fact]
    public void AlphaRegionFadesOnlyWhatIsAlreadyUnderIt()
    {
        // ChatSettingsGump's head: background, AddAlphaRegion over it, then the title and labels laid on top.
        List<GumpCommand> c = ServerGump.ParseLayout(
            "{ page 0 }{ resizepic 0 0 9270 560 470 }{ checkertrans 12 12 536 446 }" +
            "{ htmlgump 24 20 512 22 0 0 0 }{ text 48 73 1153 1 }{ button 340 428 4005 4007 1 0 2 }");
        Assert.Equal(new[] { 1 }, ServerGumpWindow.CheckerFaded(c, Box).OrderBy(i => i));

        // BotPanelGump-style pages: a region on page 2 reaches page 0 and page 2, not page 1, and not what lies beside it.
        c = ServerGump.ParseLayout(
            "{ page 0 }{ resizepic 0 0 9270 100 100 }{ gumppictiled 200 0 50 50 2624 }{ page 1 }{ gumppictiled 0 0 50 50 2624 }" +
            "{ page 2 }{ gumppictiled 0 0 50 50 2624 }{ checkertrans 0 0 100 100 }");
        Assert.Equal(new[] { 1, 6 },ServerGumpWindow.CheckerFaded(c, Box).OrderBy(i => i));

        static (int, int, int, int) Box(GumpCommand g) => g.Name switch
        {
            "resizepic" => (g.Arg(0), g.Arg(1), g.Arg(3), g.Arg(4)),
            "text" => (g.Arg(0), g.Arg(1), 80, 20),
            "button" => (g.Arg(0), g.Arg(1), 60, 22),
            _ => (g.Arg(0), g.Arg(1), g.Arg(2), g.Arg(3)),
        };
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
