using UOClient.Core;
using UOClient.Net;

namespace UOClient.Tests;

/// <summary>0x95 hue pickers, 0x7C legacy menus and 0xA5 links, as ModernUO writes and reads them.</summary>
public sealed class MenuPacketTests
{
    private const uint Me = 0x00000042, Tub = 0x40000123, MenuSerial = 0x00000777;

    private static GameSession InWorld(List<byte[]> sent)
    {
        var session = new GameSession(new LoginSettings { AnnounceChatLinks = false });
        session.StartOffline(sent.Add);
        session.Inject(new PacketWriter(0x1B, 37).UInt32(Me).UInt32(0).UInt16(0x190).UInt16(1495).UInt16(1629).UInt16(0)
            .Byte(0).Zero(9).UInt16(7168).UInt16(4096).Zero(6).ToArray(), false);
        session.Inject(new byte[] { 0x55 }, false);
        Assert.Equal(SessionState.InWorld, session.State);
        sent.Clear();
        return session;
    }

    // ModernUO's SendDisplayHuePicker: serial, (short)0, item id (9 bytes).
    private static byte[] HuePicker(uint serial, ushort graphic) =>
        new PacketWriter(0x95, 9).UInt32(serial).UInt16(0).UInt16(graphic).ToArray();

    // ModernUO's SendDisplayItemListMenu: serial, 0, question, count, then graphic, hue, name per entry.
    private static byte[] ItemMenu(string question, params (ushort Graphic, ushort Hue, string Name)[] entries)
    {
        PacketWriter w = new PacketWriter(0x7C).UInt32(MenuSerial).UInt16(0).Byte(question.Length).Ascii(question, question.Length)
            .Byte(entries.Length);
        foreach (var (graphic, hue, name) in entries)
            w.UInt16(graphic).UInt16(hue).Byte(name.Length).Ascii(name, name.Length);
        return w.ToArray();
    }

    // SendDisplayQuestionMenu: the same, a zero uint32 in place of graphic and hue.
    private static byte[] QuestionMenu(string question, params string[] answers)
    {
        PacketWriter w = new PacketWriter(0x7C).UInt32(MenuSerial).UInt16(0).Byte(question.Length).Ascii(question, question.Length)
            .Byte(answers.Length);
        foreach (string a in answers)
            w.UInt32(0).Byte(a.Length).Ascii(a, a.Length);
        return w.ToArray();
    }

    [Fact]
    public void PacketLengthsMatchTheTable()
    {
        short[] t = PacketLengths.For(ClientVersion.Default);
        Assert.Equal(9, t[0x95]);
        Assert.Equal(-1, t[0x7C]);
        Assert.Equal(13, t[0x7D]);
        Assert.Equal(-1, t[0xA5]);
        Assert.Equal(9, GameSession.HuePickerResponse(1, 2, 3).Length);
        Assert.Equal(13, GameSession.MenuResponse(1, 0, 0, 0, 0).Length);
    }

    // ---- 0x95 ----

    [Fact]
    public void HuePickerDecodesAndOkSendsTheHue()
    {
        var sent = new List<byte[]>();
        GameSession s = InWorld(sent);
        HuePickerRequest got = null;
        s.HuePickerRequested += r => got = r;

        s.Inject(HuePicker(Tub, 0x0FAB), false);
        Assert.NotNull(got);
        Assert.Equal(Tub, got.Serial);
        Assert.Equal(0x0FAB, got.Graphic);

        Assert.True(s.AnswerHuePicker(got, 0x0021));
        byte[] reply = Assert.Single(sent);
        Assert.Equal(new byte[] { 0x95, 0x40, 0x00, 0x01, 0x23, 0x0F, 0xAB, 0x00, 0x21 }, reply);

        // Once only.
        Assert.False(s.AnswerHuePicker(got, 0x0022));
        Assert.Single(sent);
    }

    [Fact]
    public void HuePickerCancelSendsNothing()
    {
        var sent = new List<byte[]>();
        GameSession s = InWorld(sent);
        HuePickerRequest got = null;
        s.HuePickerRequested += r => got = r;
        s.Inject(HuePicker(Tub, 0x0FAB), false);

        got.Answered = true; // what the window does when closed without Okay
        Assert.False(s.AnswerHuePicker(got, 0x0021));
        Assert.Empty(sent);
    }

    [Fact]
    public void DyeSwatchesCoverTheDyeableRange()
    {
        // TazUO's ColorPickerBox at its starting graduation: 3, 8, 13, ...
        Assert.Equal(3, GameSession.DyeHue(GameSession.DyeDefaultGraduation, 0));
        Assert.Equal(8, GameSession.DyeHue(GameSession.DyeDefaultGraduation, 1));
        Assert.Equal(2, GameSession.DyeHue(0, 0));
        Assert.Equal(1001, GameSession.DyeHue(4, 199));
        Assert.Equal(1001, GameSession.DyeHue(99, 999)); // clamped
        // ModernUO's ClipDyedHue range is kept even for a bad hue.
        Assert.Equal(new byte[] { 0x00, 0x02 }, GameSession.HuePickerResponse(1, 0, 0)[7..]);
        Assert.Equal(new byte[] { 0x03, 0xE9 }, GameSession.HuePickerResponse(1, 0, 0x7FFF)[7..]);
    }

    // ---- 0x7C ----

    [Fact]
    public void ItemMenuDecodesAndChoosingSendsIndexGraphicHue()
    {
        var sent = new List<byte[]>();
        GameSession s = InWorld(sent);
        LegacyMenu menu = null;
        s.MenuOpened += m => menu = m;

        s.Inject(ItemMenu("What to make?", (0x0F51, 0, "dagger"), (0x13B9, 0x0481, "sword")), true);
        Assert.NotNull(menu);
        Assert.False(menu.IsQuestion);
        Assert.Equal(MenuSerial, menu.Serial);
        Assert.Equal("What to make?", menu.Question);
        Assert.Equal(2, menu.Entries.Count);
        Assert.Equal((2, (ushort)0x13B9, (ushort)0x0481, "sword"),
            (menu.Entries[1].Index, menu.Entries[1].Graphic, menu.Entries[1].Hue, menu.Entries[1].Text));

        Assert.True(s.AnswerMenu(menu, menu.Entries[1]));
        Assert.Equal(new byte[] { 0x7D, 0x00, 0x00, 0x07, 0x77, 0x00, 0x00, 0x00, 0x02, 0x13, 0xB9, 0x04, 0x81 }, Assert.Single(sent));

        // Answered once: the window's close afterwards sends no cancel.
        Assert.False(s.AnswerMenu(menu, null));
        Assert.Single(sent);
    }

    [Fact]
    public void QuestionMenuSendsIndexOnly()
    {
        var sent = new List<byte[]>();
        GameSession s = InWorld(sent);
        LegacyMenu menu = null;
        s.MenuOpened += m => menu = m;

        s.Inject(QuestionMenu("Which?", "Yes", "No", "Maybe"), true);
        Assert.True(menu.IsQuestion);
        Assert.Equal(new[] { "Yes", "No", "Maybe" }, menu.Entries.Select(e => e.Text));
        Assert.Equal(new[] { 1, 2, 3 }, menu.Entries.Select(e => e.Index));

        s.AnswerMenu(menu, menu.Entries[2]);
        Assert.Equal(new byte[] { 0x7D, 0x00, 0x00, 0x07, 0x77, 0x00, 0x00, 0x00, 0x03, 0x00, 0x00, 0x00, 0x00 }, Assert.Single(sent));
    }

    [Fact]
    public void MenuCancelSendsIndexZero()
    {
        var sent = new List<byte[]>();
        GameSession s = InWorld(sent);
        LegacyMenu menu = null;
        s.MenuOpened += m => menu = m;
        s.Inject(ItemMenu("Pick", (0x0F51, 0, "dagger")), true);

        Assert.True(s.AnswerMenu(menu, null));
        Assert.Equal(new byte[] { 0x7D, 0x00, 0x00, 0x07, 0x77, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 }, Assert.Single(sent));
        Assert.False(s.AnswerMenu(menu, menu.Entries[0]));
        Assert.Single(sent);
    }

    [Fact]
    public void AnEntryFromAnotherMenuCancels()
    {
        var sent = new List<byte[]>();
        GameSession s = InWorld(sent);
        LegacyMenu menu = null;
        s.MenuOpened += m => menu = m;
        s.Inject(ItemMenu("Pick", (0x0F51, 0, "dagger")), true);

        s.AnswerMenu(menu, new LegacyMenuEntry { Index = 9, Graphic = 0x1234 });
        Assert.Equal(0, Assert.Single(sent)[8]);
    }

    [Fact]
    public void EmptyAndMalformedMenusAreSafe()
    {
        var sent = new List<byte[]>();
        GameSession s = InWorld(sent);
        var menus = new List<LegacyMenu>();
        s.MenuOpened += menus.Add;

        // No entries: still opens (as a question), so it can be cancelled.
        s.Inject(QuestionMenu("Nothing here"), true);
        Assert.Empty(menus[0].Entries);
        Assert.True(menus[0].IsQuestion);

        // Says three entries, carries one and a half: the cut one is dropped.
        byte[] full = ItemMenu("Pick", (0x0F51, 0, "dagger"), (0x13B9, 0, "sword"));
        byte[] cut = full[..^3];
        s.Inject(Patch(cut, countAt: 3 + 4 + 2 + 1 + 4, count: 3), true);
        Assert.Equal("dagger", Assert.Single(menus[1].Entries).Text);

        // Only the header, then nothing at all.
        s.Inject(new byte[] { 0x7C, 0x00, 0x08, 0x00, 0x00, 0x07, 0x77, 0x00 }, true);
        s.Inject(new byte[] { 0x7C, 0x00, 0x03 }, true);
        Assert.Equal(4, menus.Count);
        Assert.All(menus.Skip(2), m => Assert.Empty(m.Entries));
        Assert.Empty(sent);
    }

    private static byte[] Patch(byte[] packet, int countAt, byte count)
    {
        byte[] copy = (byte[])packet.Clone();
        copy[countAt] = count;
        copy[1] = (byte)(copy.Length >> 8);
        copy[2] = (byte)copy.Length;
        return copy;
    }

    [Fact]
    public void MenusOutsideTheWorldAreIgnored()
    {
        var session = new GameSession(new LoginSettings());
        LegacyMenu menu = null;
        session.MenuOpened += m => menu = m;
        session.Inject(ItemMenu("Pick", (0x0F51, 0, "dagger")), true);
        Assert.Null(menu);
    }

    // ---- 0xA5 ----

    [Fact]
    public void OpenUrlIsOnlyReportedNeverOpened()
    {
        var sent = new List<byte[]>();
        GameSession s = InWorld(sent);
        var urls = new List<string>();
        s.OpenUrlRequested += urls.Add;

        // ModernUO's SendLaunchBrowser: length, the URL null-terminated.
        s.Inject(new PacketWriter(0xA5).Ascii("https://www.uo.com/", 19).Byte(0).ToArray(), true);
        Assert.Equal("https://www.uo.com/", Assert.Single(urls));
        Assert.Empty(sent);
    }

    [Theory]
    [InlineData("https://www.uo.com/")]
    [InlineData("http://example.org/page?a=1&b=2#top")]
    [InlineData("  HTTPS://Example.com/path  ")]
    public void SafeUrlsAreHttpAndHttps(string url)
    {
        Assert.True(GameSession.TryParseSafeUrl(url, out Uri uri));
        Assert.True(uri.Scheme == "http" || uri.Scheme == "https");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("file:///C:/Windows/System32/calc.exe")]
    [InlineData("file://server/share/evil.exe")]
    [InlineData("javascript:alert(1)")]
    [InlineData("JavaScript:alert(1)")]
    [InlineData("data:text/html,<script>alert(1)</script>")]
    [InlineData("vbscript:msgbox(1)")]
    [InlineData("mailto:someone@example.com")]
    [InlineData("ftp://example.com/x")]
    [InlineData("ms-settings:")]
    [InlineData("search-ms:query=x")]
    [InlineData("calc.exe")]
    [InlineData("C:\\Windows\\System32\\cmd.exe")]
    [InlineData("\\\\server\\share\\x.exe")]
    [InlineData("/etc/passwd")]
    [InlineData("www.uo.com")]
    [InlineData("cmd /c del *")]
    [InlineData("https://")]
    [InlineData("https://good.com@evil.com/")]
    [InlineData("https://user:pass@example.com/")]
    [InlineData("https://example.com/a b")]
    [InlineData("https://example.com/\u0000.exe")]
    [InlineData("https://example.com/\r\nx")]
    [InlineData("https://exämple.com/")]
    public void UnsafeUrlsAreRefused(string? url)
    {
        Assert.False(GameSession.TryParseSafeUrl(url, out Uri uri));
        Assert.Null(uri);
    }

    [Fact]
    public void OverlongUrlsAreRefused()
    {
        Assert.False(GameSession.TryParseSafeUrl("https://example.com/" + new string('a', GameSession.MaxUrlLength), out _));
    }
}
