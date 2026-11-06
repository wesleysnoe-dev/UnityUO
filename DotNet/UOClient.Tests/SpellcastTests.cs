using UOClient.Core;
using UOClient.Net;
using UOClient.Unity.UI;

namespace UOClient.Tests;

/// <summary>
/// Casting a spell (TazUO's Send_CastSpell / Send_CastSpellFromBook, OutgoingPackets.cs:1056-1135) from an offline
/// session at our client version, checked byte for byte against ModernUO's readers: IncomingExtendedCommandPackets.CastSpell
/// (0xBF 0x1C) and IncomingPlayerPackets TextCommand 0x27 / 0x56. No server, no Unity.
/// </summary>
public sealed class SpellcastTests
{
    private const uint Me = 0x00000042, Book = 0x40001234;

    private static GameSession InWorld(List<byte[]> sent, ClientVersion version)
    {
        var session = new GameSession(new LoginSettings { AnnounceChatLinks = false, Version = version });
        session.StartOffline(sent.Add);
        session.Inject(new PacketWriter(0x1B, 37).UInt32(Me).UInt32(0).UInt16(0x190).UInt16(1495).UInt16(1629).UInt16(0)
            .Byte(0).Zero(9).UInt16(7168).UInt16(4096).Zero(6).ToArray(), false);
        session.Inject(new byte[] { 0x55 }, false);
        Assert.Equal(SessionState.InWorld, session.State);
        session.Update(1.0); // the first update pings; get it out of the way
        sent.Clear();
        return session;
    }

    private static List<byte[]> NoPings(List<byte[]> sent) => sent.Where(p => p[0] != 0x73).ToList();

    // 0xBF, length 9, sub 0x1C, 0x0002 (no book: ModernUO reads a serial only after 1), the 1-based spell number.
    private static byte[] Extended(int spell) =>
        new byte[] { 0xBF, 0x00, 0x09, 0x00, 0x1C, 0x00, 0x02, (byte)(spell >> 8), (byte)spell };

    [Theory]
    [InlineData(1)]   // Clumsy
    [InlineData(4)]   // Heal
    [InlineData(29)]  // Greater Heal
    [InlineData(101)] // Animate Dead, the first necromancy spell
    [InlineData(202)] // Close Wounds
    public void CastSpellSendsTheExtendedCastAtOurVersion(int spell)
    {
        var sent = new List<byte[]>();
        GameSession session = InWorld(sent, ClientVersion.Default);

        session.CastSpell(spell);

        Assert.Equal(Extended(spell), Assert.Single(NoPings(sent)));
        Assert.Equal(Extended(spell), GameSession.CastSpellRequest(spell, new ClientVersion(7, 0, 23, 1)));
    }

    [Fact]
    public void OlderClientsCastByTheMacroTextCommand()
    {
        // Below CV_60142 (ClientVersion.cs:36): 0x12, length, 0x56, ASCII spell number, null.
        byte[] old = { 0x12, 0x00, 0x07, 0x56, (byte)'2', (byte)'9', 0x00 };
        Assert.Equal(old, GameSession.CastSpellRequest(29, new ClientVersion(6, 0, 14, 1)));
        Assert.Equal(Extended(29), GameSession.CastSpellRequest(29, new ClientVersion(6, 0, 14, 2)));

        var sent = new List<byte[]>();
        GameSession session = InWorld(sent, new ClientVersion(5, 0, 9, 1));
        session.CastSpell(29);
        Assert.Equal(old, Assert.Single(NoPings(sent)));
    }

    [Fact]
    public void CastSpellFromBookSendsTheSpellAndTheBookSerial()
    {
        // ModernUO tokenizes "spell serial" and parses the serial with Utility.ToUInt32 (decimal, as TazUO writes it).
        byte[] p = GameSession.CastSpellFromBookRequest(4, Book);
        string text = $"4 {Book}";
        Assert.Equal(new byte[] { 0x12, 0x00, (byte)(4 + text.Length + 1), 0x27 }, p[..4]);
        Assert.Equal(text, System.Text.Encoding.ASCII.GetString(p, 4, text.Length));
        Assert.Equal(0, p[^1]);
    }

    [Fact]
    public void ClickingAListRowCastsFromThatBook()
    {
        var sent = new List<byte[]>();
        GameSession session = InWorld(sent, ClientVersion.Default);
        var opened = new List<(uint Book, int First, ulong Bits)>();
        session.SpellbookContent += (b, f, bits) => opened.Add((b, f, bits));

        // ModernUO's NewSpellbookContent (0xBF 0x1B): 1, book serial, graphic, first spell id, 64 bits low byte first.
        session.Inject(new PacketWriter(0xBF).UInt16(0x1B).UInt16(0x01).UInt32(Book).UInt16(0x2253).UInt16(101)
            .Byte(0x05).Zero(7).ToArray(), true);
        (uint book, int first, ulong bits) = Assert.Single(opened);
        Assert.Equal((Book, 101, 0x05UL), (book, first, bits));

        // The plain list (the schools not drawn classic) keeps the book-serial cast; spell 103 is its third row.
        SpellbookWindow.CastFromBook(session, book, first, 2);

        Assert.Equal(GameSession.CastSpellFromBookRequest(103, Book), Assert.Single(NoPings(sent)));
    }

    [Fact]
    public void TheClassicBooksIconDoubleClickCastsWithoutTheBook()
    {
        var sent = new List<byte[]>();
        GameSession session = InWorld(sent, ClientVersion.Default);
        var opened = new List<(uint Book, int First, ulong Bits)>();
        session.SpellbookContent += (b, f, bits) => opened.Add((b, f, bits));

        // A paladin book (0x2252) holding Close Wounds (bit 1) and Dispel Evil (bit 3).
        session.Inject(new PacketWriter(0xBF).UInt16(0x1B).UInt16(0x01).UInt32(Book).UInt16(0x2252).UInt16(201)
            .Byte(0x0A).Zero(7).ToArray(), true);
        (uint book, int first, ulong bits) = Assert.Single(opened);
        Assert.True(SpellbookWindow.TryGetBookInfo(first, out SpellbookInfo info));

        // The first detail spread's right page is Dispel Evil; its icon's double-click is GameActions.CastSpell(204).
        SpellbookWindow.DetailEntry right = SpellbookWindow.DetailEntries(info, bits)[1];
        Assert.Equal((2, true, 3), (right.Page, right.Right, right.Spell));
        var b = new BookDriver(session, new SpellbookState(book, first, bits));
        b.Press(BookTarget.Icon, right.Spell, 0, 240, 60, 0.00);
        b.Release(240, 60);
        b.Press(BookTarget.Icon, right.Spell, 0, 240, 60, 0.20);
        b.Release(240, 60);

        Assert.Equal(Extended(204), Assert.Single(NoPings(sent)));
    }

    /// <summary>
    /// SpellbookWindow.Pointer and Draw minus IMGUI: each event goes through SpellbookClicks.Route, the call Pointer
    /// makes; a Cast sends CastBookSpell for Clicks.Spell, a DragOut counts a floating icon, a Tick's page opens. Used
    /// records whether Pointer would e.Use() the event (a used press is one GUI.DragWindow does not see), Hot whether
    /// the window holds hotControl after it. That IMGUI delivers these events as assumed needs play mode.
    /// </summary>
    private sealed class BookDriver
    {
        private readonly GameSession _session;
        public readonly SpellbookState State;
        public readonly SpellbookClicks Clicks = new SpellbookClicks();
        public int DraggedOut;
        public bool Used, Hot;

        public BookDriver(GameSession session, SpellbookState state)
        {
            _session = session;
            State = state;
        }

        private void Route(PointerEvent kind, BookTarget target, int spell, int toPage, float x, float y, double now)
        {
            (BookAction action, Used) = Clicks.Route(kind, target, spell, toPage, x, y, now);
            Hot = Clicks.HoldingIcon;
            if (action == BookAction.Cast)
                SpellbookWindow.CastBookSpell(_session, State.Info.First, Clicks.Spell);
            else if (action == BookAction.DragOut)
                DraggedOut++;
        }

        public void Press(BookTarget target, int spell, int toPage, float x, float y, double now) =>
            Route(PointerEvent.Down, target, spell, toPage, x, y, now);

        public void Drag(float x, float y) => Route(PointerEvent.Drag, BookTarget.None, -1, 0, x, y, 0);

        public void Release(float x, float y) => Route(PointerEvent.Up, BookTarget.None, -1, 0, x, y, 0);

        // Pointer's guard: a Repaint with the button up while an icon is held.
        public void LoseRelease()
        {
            Clicks.Cancel();
            Hot = Clicks.HoldingIcon;
        }

        public void Tick(double now)
        {
            int page = Clicks.Tick(now);
            if (page >= 0)
                State.GoTo(page);
        }
    }

    // A full magery book: Heal (index 3) is on index spread 1, and its name opens detail spread 6.
    private static BookDriver Magery(List<byte[]> sent, out SpellbookWindow.IndexEntry heal)
    {
        GameSession session = InWorld(sent, ClientVersion.Default);
        var driver = new BookDriver(session, new SpellbookState(Book, 1, ulong.MaxValue));
        heal = driver.State.Index.Single(i => i.Spell == 3);
        Assert.Equal((1, 6), (heal.Page, heal.ToPage));
        return driver;
    }

    [Fact]
    public void AnIconsSingleClickSendsNothing()
    {
        var sent = new List<byte[]>();
        BookDriver b = Magery(sent, out _);
        b.State.GoTo(6);

        b.Press(BookTarget.Icon, 3, 0, 80, 60, 0.00);
        b.Release(80, 60);
        b.Tick(1.00);
        b.Press(BookTarget.Icon, 3, 0, 80, 60, 2.00); // long after: a fresh single click, not a double
        b.Release(80, 60);
        b.Tick(3.00);

        Assert.Empty(NoPings(sent));
        Assert.Equal(6, b.State.Page);
    }

    [Fact]
    public void AnIconsDoubleClickIsTheExtendedCastOnce()
    {
        var sent = new List<byte[]>();
        BookDriver b = Magery(sent, out _);
        b.State.GoTo(6);

        b.Press(BookTarget.Icon, 3, 0, 80, 60, 0.00);
        b.Release(80, 60);
        b.Press(BookTarget.Icon, 3, 0, 80, 60, 0.35); // MOUSE_DELAY_DOUBLE_CLICK: lastClickTime + 350 >= ticks
        b.Release(80, 60);
        b.Press(BookTarget.Icon, 3, 0, 80, 60, 0.40); // a third press starts over
        b.Release(80, 60);

        Assert.Equal(Extended(4), Assert.Single(NoPings(sent)));
        Assert.DoesNotContain(NoPings(sent), p => p[0] == 0x12);
    }

    [Fact]
    public void ASecondPressPastTheDoubleClickTimeIsNotADoubleClick()
    {
        var sent = new List<byte[]>();
        BookDriver b = Magery(sent, out _);
        b.State.GoTo(6);

        b.Press(BookTarget.Icon, 3, 0, 80, 60, 0.00);
        b.Release(80, 60);
        b.Press(BookTarget.Icon, 3, 0, 80, 60, 0.36);
        b.Release(80, 60);

        Assert.Empty(NoPings(sent));
    }

    [Fact]
    public void AnIndexNamesClickOpensItsSpreadAfterTheDoubleClickTime()
    {
        var sent = new List<byte[]>();
        BookDriver b = Magery(sent, out SpellbookWindow.IndexEntry heal);

        b.Press(BookTarget.Index, heal.Spell, heal.ToPage, heal.X + 400, heal.Y + 100, 10.00);
        b.Release(heal.X + 400, heal.Y + 100);
        Assert.Equal(heal.ToPage, b.Clicks.EnqueuedPage);
        b.Tick(10.20);
        Assert.Equal(1, b.State.Page); // still waiting for a possible second click
        b.Tick(10.36);

        Assert.Equal(heal.ToPage, b.State.Page);
        Assert.Equal(-1, b.Clicks.EnqueuedPage);
        Assert.Empty(NoPings(sent));
    }

    [Fact]
    public void AnIndexNamesDoubleClickCastsAndKeepsTheSpread()
    {
        var sent = new List<byte[]>();
        BookDriver b = Magery(sent, out SpellbookWindow.IndexEntry heal);

        b.Press(BookTarget.Index, heal.Spell, heal.ToPage, 470, 160, 10.00);
        b.Release(470, 160); // queues the page
        b.Press(BookTarget.Index, heal.Spell, heal.ToPage, 470, 160, 10.15);
        Assert.Equal(-1, b.Clicks.EnqueuedPage);
        b.Release(470, 160); // the double-click's release is swallowed: no page queued again
        b.Tick(11.00);

        Assert.Equal(Extended(4), Assert.Single(NoPings(sent)));
        Assert.Equal(1, b.State.Page);
    }

    [Fact]
    public void AnIndexNamePressThatDragsTheBookQueuesNothing()
    {
        var sent = new List<byte[]>();
        BookDriver b = Magery(sent, out SpellbookWindow.IndexEntry heal);

        // The name is CanMove: the press drags the book, so the release is somewhere else on screen.
        b.Press(BookTarget.Index, heal.Spell, heal.ToPage, 470, 160, 10.00);
        b.Drag(480, 160);
        b.Release(510, 175);
        b.Tick(11.00);

        Assert.Equal(-1, b.Clicks.EnqueuedPage);
        Assert.Equal(1, b.State.Page);
        Assert.Equal(0, b.DraggedOut); // only an icon drags out
        Assert.Empty(NoPings(sent));
    }

    [Fact]
    public void DraggingAnIconOutCastsNothing()
    {
        var sent = new List<byte[]>();
        BookDriver b = Magery(sent, out _);
        b.State.GoTo(6);

        b.Press(BookTarget.Icon, 3, 0, 80, 60, 0.00);
        b.Drag(83, 57);
        Assert.Equal(0, b.DraggedOut); // under 5 px
        b.Drag(85, 60);
        b.Drag(140, 90);
        b.Release(140, 90);
        b.Tick(1.00);

        Assert.Equal(1, b.DraggedOut); // once per press
        Assert.Equal(3, b.Clicks.Spell);
        Assert.Empty(NoPings(sent));

        // A press on nothing (the book's art) is counted but never casts, alone or as a double-click.
        b.Press(BookTarget.None, -1, 0, 10, 10, 5.00);
        b.Release(10, 10);
        b.Press(BookTarget.None, -1, 0, 10, 10, 5.10);
        b.Release(10, 10);
        Assert.Empty(NoPings(sent));
    }

    [Fact]
    public void TheFloatingIconCastsOnlyOnItsDoubleClick()
    {
        var sent = new List<byte[]>();
        GameSession session = InWorld(sent, ClientVersion.Default);
        var clicks = new SpellbookClicks(); // SpellIconWindow's clock: SingleClickIconUse off

        void Press(double now)
        {
            if (clicks.Press(BookTarget.Icon, 202, 0, 20, 20, now) == BookAction.Cast)
                SpellbookWindow.CastIcon(session, 202);
            clicks.Release(20, 20);
        }

        Press(0.00);
        Assert.Empty(NoPings(sent));
        Press(0.25);
        Assert.Equal(Extended(202), Assert.Single(NoPings(sent)));
        Press(3.00);
        Assert.Single(NoPings(sent));
    }

    [Fact]
    public void AnIconPressHoldsThePointerUntilItsReleaseAndTheBookStaysPut()
    {
        var sent = new List<byte[]>();
        BookDriver b = Magery(sent, out _);
        b.State.GoTo(6);

        b.Press(BookTarget.Icon, 3, 0, 80, 60, 0.00);
        Assert.True(b.Used && b.Hot); // GUI.DragWindow never sees it
        b.Drag(82, 61);
        Assert.True(b.Used && b.Hot);
        b.Release(82, 61);
        Assert.True(b.Used);
        Assert.False(b.Hot);

        // The double-click's press is used (the cast) but not held; its release is swallowed and left alone.
        b.Press(BookTarget.Icon, 3, 0, 82, 61, 0.20);
        Assert.True(b.Used);
        Assert.False(b.Hot);
        b.Release(82, 61);
        Assert.False(b.Used || b.Hot);
        Assert.Equal(Extended(4), Assert.Single(NoPings(sent)));
    }

    [Fact]
    public void AnIndexPressIsLeftToTheBookDragAndItsDoubleClickIsUsed()
    {
        var sent = new List<byte[]>();
        BookDriver b = Magery(sent, out SpellbookWindow.IndexEntry heal);

        b.Press(BookTarget.Index, heal.Spell, heal.ToPage, 470, 160, 10.00);
        Assert.False(b.Used || b.Hot); // GUI.DragWindow takes it, as TazUO's CanMove label
        b.Drag(480, 160);
        Assert.False(b.Used || b.Hot);
        b.Release(480, 160);
        Assert.False(b.Used);
        Assert.Equal(-1, b.Clicks.EnqueuedPage); // the book moved

        b.Press(BookTarget.Index, heal.Spell, heal.ToPage, 480, 160, 10.10);
        Assert.True(b.Used); // the cast: the book does not also start a move
        Assert.False(b.Hot);
        Assert.Equal(Extended(4), Assert.Single(NoPings(sent)));

        // A press on the book's art is the book's own drag too.
        b.Press(BookTarget.None, -1, 0, 10, 10, 20.00);
        Assert.False(b.Used || b.Hot);
    }

    [Fact]
    public void AfterTheDragOutTheBookLetsGoAndNothingCasts()
    {
        var sent = new List<byte[]>();
        BookDriver b = Magery(sent, out _);
        b.State.GoTo(6);

        b.Press(BookTarget.Icon, 3, 0, 80, 60, 0.00);
        b.Drag(80, 65);
        Assert.Equal(1, b.DraggedOut);
        Assert.True(b.Used); // that drag event is the book's
        Assert.False(b.Hot); // hotControl handed back; UiManager.BeginWindowDrag carries the icon
        b.Drag(120, 90);
        Assert.False(b.Used || b.Hot); // later drags are no longer ours
        Assert.Equal(1, b.DraggedOut);
        b.Release(120, 90);
        Assert.False(b.Used || b.Hot);
        b.Tick(1.00);

        Assert.Empty(NoPings(sent));
        Assert.Equal(6, b.State.Page);
    }

    [Fact]
    public void AnIconPressWhoseReleaseIsLostLetsGoAndCastsNothing()
    {
        var sent = new List<byte[]>();
        BookDriver b = Magery(sent, out _);
        b.State.GoTo(6);

        b.Press(BookTarget.Icon, 3, 0, 80, 60, 0.00);
        Assert.True(b.Hot);
        b.LoseRelease(); // no MouseUp reached the book; the button is up
        Assert.False(b.Hot);
        b.Drag(140, 90);
        Assert.Equal(0, b.DraggedOut); // a spent press does not drag out
        b.Release(140, 90);
        Assert.False(b.Used);
        Assert.Empty(NoPings(sent));

        // The next press past the double-click time is a fresh single click.
        b.Press(BookTarget.Icon, 3, 0, 80, 60, 1.00);
        Assert.True(b.Hot);
        Assert.Empty(NoPings(sent));
    }

    // ModernUO's SendNewSpellbookContent (OutgoingContainerPackets.cs:46-69) byte for byte: 0xBF, length 23, sub 0x1B,
    // command 1, book serial, graphic, offset (Spellbook.BookOffset + 1), the 64 content bits low byte first.
    private static byte[] NewSpellbookContent(uint book, ushort graphic, ushort offset, ulong content)
    {
        var p = new byte[23];
        p[0] = 0xBF;
        p[2] = 23;
        p[4] = 0x1B;
        p[6] = 0x01;
        p[7] = (byte)(book >> 24);
        p[8] = (byte)(book >> 16);
        p[9] = (byte)(book >> 8);
        p[10] = (byte)book;
        p[11] = (byte)(graphic >> 8);
        p[12] = (byte)graphic;
        p[13] = (byte)(offset >> 8);
        p[14] = (byte)offset;
        for (int i = 0; i < 8; i++)
            p[15 + i] = (byte)(content >> (i * 8));
        return p;
    }

    // "spell serial" in decimal after 0x12 len 0x27, null-terminated (IncomingPlayerPackets.cs:162, TextCommand 0x27).
    private static byte[] FromBook(string text)
    {
        var p = new List<byte> { 0x12, 0x00, (byte)(4 + text.Length + 1), 0x27 };
        p.AddRange(System.Text.Encoding.ASCII.GetBytes(text));
        p.Add(0);
        return p.ToArray();
    }

    [Theory]
    [InlineData(1, 3, "4 1073746484")]     // Heal
    [InlineData(1, 0, "1 1073746484")]     // Clumsy
    [InlineData(101, 0, "101 1073746484")] // Animate Dead
    [InlineData(201, 1, "202 1073746484")] // Close Wounds
    public void CastFromBookBytes(int first, int index, string text)
    {
        var sent = new List<byte[]>();
        GameSession session = InWorld(sent, ClientVersion.Default);

        SpellbookWindow.CastFromBook(session, Book, first, index);

        Assert.Equal(FromBook(text), Assert.Single(NoPings(sent)));
    }

    [Theory]
    // A full magery book (0x0EFA); a necro book (0x2253) with Animate Dead, Corpse Skin, Wither and Exorcism; a paladin
    // book (0x2252) with Cleanse by Fire, Close Wounds and Sacred Journey, plus a stray bit past its ten spells.
    [InlineData(0x0EFA, 1, ulong.MaxValue, SpellbookKind.Magery, 64, 36)]
    [InlineData(0x2253, 101, 0x1_4005UL, SpellbookKind.Necromancy, 4, 4)]
    [InlineData(0x2252, 201, 0x1_0000_0203UL, SpellbookKind.Chivalry, 3, 3)]
    public void SpellbookContentFillsTheWindowModel(int graphic, int first, ulong bits, SpellbookKind kind, int held, int maxPage)
    {
        var sent = new List<byte[]>();
        GameSession session = InWorld(sent, ClientVersion.Default);
        SpellbookState state = null;
        session.SpellbookContent += (b, f, c) =>
        {
            if (state == null)
                state = new SpellbookState(b, f, c);
            else
                state.SetContent(f, c); // SessionUiBinder: the open window takes new content in place
        };

        session.Inject(NewSpellbookContent(Book, (ushort)graphic, (ushort)first, bits), true);

        Assert.NotNull(state);
        Assert.Equal((Book, first, bits, true, kind), (state.Book, state.First, state.Bits, state.Classic, state.Info.Kind));
        Assert.Equal(held, state.Known.Count);
        Assert.All(state.Known, i => Assert.True((bits & (1UL << i)) != 0));
        Assert.Equal((maxPage, 1, true), (state.MaxPage, state.Page, state.OnIndex));

        // An index name opens the spread describing it, and that spread's detail entry is the same spell.
        SpellbookWindow.IndexEntry last = state.Index[^1];
        state.GoTo(last.ToPage);
        Assert.False(state.OnIndex);
        Assert.Contains(state.Detail, d => d.Page == state.Page && d.Spell == last.Spell);

        // The page corners stop at the book's ends.
        state.GoTo(state.Page + 100);
        Assert.Equal(maxPage, state.Page);
        state.GoTo(-3);
        Assert.Equal(1, state.Page);

        // Content sent again with only the first held spell: the book shrinks and the open spread stays inside it.
        state.GoTo(maxPage);
        ulong one = 1UL << state.Known[0];
        session.Inject(NewSpellbookContent(Book, (ushort)graphic, (ushort)first, one), true);
        Assert.Equal(new[] { state.Known[0] }, state.Known);
        Assert.Equal(state.Info.IndexPages + 1, state.MaxPage);
        Assert.Equal(state.MaxPage, state.Page);
        Assert.Empty(NoPings(sent)); // opening and paging the book sends nothing
    }

    [Theory]
    [InlineData(1, 3, 4, 3002014)]      // Heal
    [InlineData(1, 0, 1, 3002011)]      // Clumsy
    [InlineData(101, 0, 101, 1060509)]  // Animate Dead
    [InlineData(201, 1, 202, 1060586)]  // Close Wounds
    public void AFloatingSpellIconCastsWithoutTheBook(int first, int index, int spell, int tooltip)
    {
        var sent = new List<byte[]>();
        GameSession session = InWorld(sent, ClientVersion.Default);

        // OnIconDragBegin: the icon's spell is the book's first id + index; UseSpellButtonGump double-click: CastSpell.
        SpellbookWindow.CastIcon(session, first + index);

        Assert.Equal(Extended(spell), Assert.Single(NoPings(sent)));
        Assert.Equal(tooltip, SpellbookWindow.SpellIconTooltip(spell));
    }

    [Fact]
    public void IconDragStartsAtTazUOsMoveDistance()
    {
        Assert.False(SpellbookWindow.StartsDrag(4, -4));
        Assert.True(SpellbookWindow.StartsDrag(5, 0));
        Assert.True(SpellbookWindow.StartsDrag(0, -5));
        Assert.Equal(22, SpellbookWindow.FloatingIconGrab);
        Assert.Equal(0, SpellbookWindow.SpellIconTooltip(401)); // not an AoS book here
        SpellbookWindow.CastIcon(null, 4); // no session: nothing to send, no throw
    }

    [Fact]
    public void OtherSchoolsKeepTheListModel()
    {
        var state = new SpellbookState(Book, 401, 0x3FUL); // Bushido
        Assert.False(state.Classic);
        Assert.Equal((401, 0x3FUL), (state.First, state.Bits));
        state.SetContent(1, 0x8UL); // the same serial now sends a magery book
        Assert.True(state.Classic);
        Assert.Equal(new[] { 3 }, state.Known);
    }

    [Fact]
    public void NothingIsCastOutOfWorld()
    {
        var sent = new List<byte[]>();
        var session = new GameSession(new LoginSettings { AnnounceChatLinks = false });
        session.StartOffline(sent.Add);
        session.CastSpell(1);
        session.CastSpellFromBook(1, Book);
        SpellbookWindow.CastFromBook(null, Book, 1, 0);
        SpellbookWindow.CastBookSpell(null, 1, 0);
        SpellbookWindow.CastBookSpell(session, 1, 0); // offline, not in world
        Assert.Empty(sent);
    }
}
