using UOClient.Core;
using UOClient.IO;
using UOClient.IO.Formats;
using UOClient.Net;
using UOClient.Unity.UI;

namespace UOClient.Tests;

/// <summary>
/// The buff bar (0xDF into TazUO's classic BuffGump) and active spell icons (0xBF 0x25 into hue 38), from packets as
/// ModernUO's BuffIconPackets and WeaponAbilityPackets.SendToggleSpecialAbility write them, through an offline session.
/// </summary>
public sealed class BuffBarTests
{
    private const uint Me = 0x00000042;

    private static GameSession InWorld(List<byte[]> sent)
    {
        var session = new GameSession(new LoginSettings { AnnounceChatLinks = false });
        session.StartOffline(sent.Add);
        session.Inject(LoginConfirm(Me), false);
        session.Inject(new byte[] { 0x55 }, false);
        Assert.Equal(SessionState.InWorld, session.State);
        return session;
    }

    private static byte[] LoginConfirm(uint serial) =>
        new PacketWriter(0x1B, 37).UInt32(serial).UInt32(0).UInt16(0x190).UInt16(1495).UInt16(1629).UInt16(0)
            .Byte(0).Zero(9).UInt16(7168).UInt16(4096).Zero(6).ToArray();

    /// <summary>SendToggleSpecialAbility: 0xBF, length 8, sub 0x25, the id (int16), active (bool).</summary>
    private static byte[] Toggle(int id, bool active) =>
        new byte[] { 0xBF, 0x00, 0x08, 0x00, 0x25, (byte)(id >> 8), (byte)id, (byte)(active ? 1 : 0) };

    /// <summary>SendAddBuffPacket, as SessionPacketTests builds it.</summary>
    private static byte[] AddBuff(uint mob, int icon, int title, int secondary, string? args, int seconds)
    {
        var w = new PacketWriter(0xDF).UInt32(mob).UInt16(icon).UInt16(1).UInt32(0)
            .UInt16(icon).UInt16(1).UInt32(0).UInt16(seconds).Zero(3).Int32(title).Int32(secondary);
        if (args == null)
            return w.Zero(10).ToArray();
        w.UInt32(0).UInt16(1).UInt16(0);
        foreach (char c in "\t" + args)
            w.UInt16(((c & 0xFF) << 8) | (c >> 8));
        return w.UInt16(0).UInt16(1).UInt16(0).ToArray();
    }

    private static byte[] RemoveBuff(uint mob, int icon) =>
        new PacketWriter(0xDF).UInt32(mob).UInt16(icon).UInt16(0).UInt32(0).ToArray();

    [Fact]
    public void ToggleSpecialAbilityTurnsASpellIconOnAndOff()
    {
        var sent = new List<byte[]>();
        GameSession session = InWorld(sent);
        var changes = new List<(int, bool)>();
        session.ActiveSpellChanged += (id, on) => changes.Add((id, on));

        byte[] on = Toggle(4, true);
        Assert.Equal(8, on.Length); // SpanWriter(stackalloc byte[8])
        session.Inject(on, true);
        Assert.True(session.IsSpellActive(4));
        Assert.Equal(GameSession.ActiveSpellHue, SpellbookWindow.IconHue(session, 4));
        Assert.Equal(0, SpellbookWindow.IconHue(session, 5));

        session.Inject(Toggle(4, true), true); // already on: a set, as ActiveSpellIconsManager.Add
        session.Inject(Toggle(201, true), true);
        Assert.Equal(new[] { 4, 201 }, session.ActiveSpells.OrderBy(i => i));

        session.Inject(Toggle(4, false), true);
        Assert.False(session.IsSpellActive(4));
        Assert.Equal(0, SpellbookWindow.IconHue(session, 4));
        session.Inject(Toggle(4, false), true); // already off
        Assert.Equal(new[] { (4, true), (201, true), (4, false) }, changes);
        Assert.Equal(0, SpellbookWindow.IconHue(null, 201));
        Assert.DoesNotContain(sent, p => p[0] != 0x73); // nothing answered
    }

    [Fact]
    public void OnlyTheToggleSetsActive()
    {
        var sent = new List<byte[]>();
        GameSession session = InWorld(sent);
        session.Update(10);

        // A cast, a Bless buff (0x3F0) and its timer running out: none of them highlight a spell.
        session.CastSpell(17);
        session.Inject(AddBuff(Me, 0x3F0, 1075847, 1075848, "10\t10\t10", 5), true);
        session.Update(100);
        Assert.Empty(session.ActiveSpells);
        Assert.Single(session.Buffs); // and the run-out buff stays until the server removes it
        Assert.Equal(0, session.BuffSecondsLeft(session.Buffs[0]));
        Assert.Equal("", BuffBarWindow.TimerText(session.BuffSecondsLeft(session.Buffs[0])));
    }

    [Fact]
    public void ActiveSpellsClearOnLoginAndDisconnect()
    {
        var sent = new List<byte[]>();
        GameSession session = InWorld(sent);
        session.Inject(Toggle(202, true), true);
        session.Inject(LoginConfirm(Me), false); // World.Clear on a new world
        Assert.Empty(session.ActiveSpells);

        session.Inject(Toggle(202, true), true);
        session.Inject(new byte[] { 0x82, 0x00 }, false); // login denied: the session ends
        Assert.Equal(SessionState.Disconnected, session.State);
        Assert.Empty(session.ActiveSpells);
    }

    [Fact]
    public void BuffIconsTakeTazUOsBuffTableArt()
    {
        Assert.Equal(189, BuffBarWindow.Table.Length);
        Assert.Equal(0x754C, BuffBarWindow.Graphic(0x3E9)); // DismountPrevention, row 0
        Assert.Equal(0x755E, BuffBarWindow.Graphic(0x3ED)); // NightSight
        Assert.Equal(0x7556, BuffBarWindow.Graphic(0x3F0)); // Bless... row 7 is BuffIconType 0x3F0
        Assert.Equal(0, BuffBarWindow.Graphic(0x3EB));      // a 0 row: added with no art
        Assert.Equal(0x761B, BuffBarWindow.Graphic(0x466)); // CityTradeDeal: 0x466 is row 125
        Assert.Equal(0x0951, BuffBarWindow.Graphic(0x466 + 188 - 125)); // the last row
        Assert.Equal(-1, BuffBarWindow.Graphic(0x466 + 189 - 125));     // past the table: Receive drops it
        Assert.Equal(-1, BuffBarWindow.Graphic(0x3E8));
    }

    [Fact]
    public void TheBarLaysOutAsBuffGump()
    {
        // The button cycles 0x7580 -> 0x7581 -> 0x7582 -> 0x757F -> 0x7580.
        int g = BuffBarWindow.DefaultBackground;
        var seen = new List<int> { g };
        for (int i = 0; i < 4; i++)
            seen.Add(g = BuffBarWindow.NextBackground(g));
        Assert.Equal(new[] { 0x7580, 0x7581, 0x7582, 0x757F, 0x7580 }, seen);

        Assert.Equal((-2, 36), BuffBarWindow.ButtonPos(0x7580));
        Assert.Equal((34, 78), BuffBarWindow.ButtonPos(0x7581));
        Assert.Equal((76, 36), BuffBarWindow.ButtonPos(0x7582));
        Assert.Equal((0, 0), BuffBarWindow.ButtonPos(0x757F));

        // 0x7580 / 0x7582 are 100x60, 0x757F / 0x7581 60x100 (gumpartLegacyMUL).
        Assert.Equal((26, 5), BuffBarWindow.IconPos(0x7580, 0, 100, 60));
        Assert.Equal((57, 5), BuffBarWindow.IconPos(0x7580, 1, 100, 60));
        Assert.Equal((5, 52), BuffBarWindow.IconPos(0x7581, 0, 60, 100));
        Assert.Equal((52, 5), BuffBarWindow.IconPos(0x7582, 0, 100, 60));
        Assert.Equal((21, 5), BuffBarWindow.IconPos(0x7582, 1, 100, 60));
        Assert.Equal((25, 57), BuffBarWindow.IconPos(0x757F, 1, 60, 100));
        Assert.Equal((100, 100), (BuffBarWindow.OpenX, BuffBarWindow.OpenY));
        Assert.Equal((0x7538, 40, 50), (StatusWindow.BuffButton, StatusWindow.BuffButtonX, StatusWindow.BuffButtonY));
    }

    [Theory]
    [InlineData(-1, "")]      // untimed
    [InlineData(0, "")]       // run out: no text
    [InlineData(0.4, "00s")]
    [InlineData(9.9, "09s")]
    [InlineData(59, "59s")]
    [InlineData(60, "1:00")]
    [InlineData(89.5, "1:29")]
    [InlineData(3599, "59:59")]
    [InlineData(3600, "+1hr")]
    [InlineData(32767, "+9hr")] // ModernUO's (short)seconds at most
    public void TimerTextIsBuffControlEntrys(double left, string text) =>
        Assert.Equal(text, BuffBarWindow.TimerText(left));

    [Fact]
    public void TooltipIsTitleDescriptionAndTimeLeft()
    {
        Assert.Equal("<left>Bless\n+10% Strength.</left>\nTime left: 00:01:30",
            BuffBarWindow.TooltipText("bless", "+10% Strength.", 90.7));
        Assert.Equal("<left>Night Sight</left>", BuffBarWindow.TooltipText("Night Sight", "", -1));
        Assert.Equal("<left>Bless</left>", BuffBarWindow.TooltipText("Bless", null!, 0));
    }

    [Fact]
    public void LastTenSecondsPulse()
    {
        Assert.Equal(((byte)255, true), BuffBarWindow.Fade(120, false, 30)); // plenty left: full
        Assert.Equal(((byte)255, true), BuffBarWindow.Fade(120, false, -1)); // untimed
        Assert.Equal(((byte)120, false), BuffBarWindow.Fade(120, false, 0)); // run out: holds
        // 4 s left: (10000 - 4000) / 600 = 10 per frame, down to 60 then back up.
        Assert.Equal(((byte)245, true), BuffBarWindow.Fade(255, true, 4));
        Assert.Equal(((byte)60, false), BuffBarWindow.Fade(65, true, 4));
        Assert.Equal(((byte)70, false), BuffBarWindow.Fade(60, false, 4));
        Assert.Equal(((byte)255, true), BuffBarWindow.Fade(250, false, 4));
    }

    [Fact]
    public void BuffsKeepServerOrderAndReplaceInPlace()
    {
        var sent = new List<byte[]>();
        GameSession session = InWorld(sent);
        session.Update(0);
        session.Inject(AddBuff(Me, 0x3F0, 1075847, 1075848, "10\t10\t10", 90), true);
        session.Inject(AddBuff(Me, 0x3ED, 1075643, 1075644, null, 0), true);
        session.Inject(AddBuff(0x99, 0x3F0, 1075847, 1075848, "1\t1\t1", 9), true); // someone else's: not on our bar
        session.Inject(AddBuff(Me, 0x3F0, 1075847, 1075848, "12\t12\t12", 120), true); // recast

        int[] bar = session.Buffs.Where(b => b.Serial == Me && BuffBarWindow.Graphic(b.Icon) >= 0)
            .Select(b => BuffBarWindow.Graphic(b.Icon)).ToArray();
        Assert.Equal(new[] { 0x7556, 0x755E }, bar); // Bless keeps its slot
        Assert.Equal("2:00", BuffBarWindow.TimerText(session.BuffSecondsLeft(session.Buffs[0])));

        session.Inject(RemoveBuff(Me, 0x3F0), true);
        Assert.Equal(new[] { 0x3ED, 0x3F0 }, session.Buffs.Select(b => b.Icon)); // ours gone, the other mobile's left
    }

    [Fact]
    public void BookIconTooltipsAreTheBooksOwnClilocs()
    {
        Assert.Equal(1061290, SpellbookWindow.BookIconTooltip(SpellbookKind.Magery, 0));
        Assert.Equal(1061353, SpellbookWindow.BookIconTooltip(SpellbookKind.Magery, 63));
        Assert.Equal(0, SpellbookWindow.BookIconTooltip(SpellbookKind.Magery, 64));
        Assert.Equal(1061390, SpellbookWindow.BookIconTooltip(SpellbookKind.Necromancy, 0));
        Assert.Equal(1061406, SpellbookWindow.BookIconTooltip(SpellbookKind.Necromancy, 16));
        Assert.Equal(0, SpellbookWindow.BookIconTooltip(SpellbookKind.Necromancy, 17));
        Assert.Equal(1061490, SpellbookWindow.BookIconTooltip(SpellbookKind.Chivalry, 0));
        Assert.Equal(1061499, SpellbookWindow.BookIconTooltip(SpellbookKind.Chivalry, 9));
        Assert.Equal(0, SpellbookWindow.BookIconTooltip(SpellbookKind.Chivalry, 10));
        Assert.Equal(0, SpellbookWindow.BookIconTooltip(SpellbookKind.Chivalry, -1));
    }
}

/// <summary>The same text against the install's Cliloc.enu and gump art.</summary>
[Collection("UO install")]
public sealed class BuffBarInstallTests
{
    private readonly UOData _data;

    public BuffBarInstallTests(UOInstallFixture fixture) => _data = fixture.Data;

    [Fact]
    public void BlessReadsAsTazUOShowsIt()
    {
        var session = new GameSession(new LoginSettings { AnnounceChatLinks = false })
        {
            Cliloc = new Cliloc(Path.Combine(_data.Directory, "Cliloc.enu"))
        };
        session.StartOffline(_ => { });
        session.Inject(new PacketWriter(0x1B, 37).UInt32(0x42).UInt32(0).UInt16(0x190).UInt16(1495).UInt16(1629).UInt16(0)
            .Byte(0).Zero(9).UInt16(7168).UInt16(4096).Zero(6).ToArray(), false);
        session.Update(0);
        var w = new PacketWriter(0xDF).UInt32(0x42).UInt16(0x3F0).UInt16(1).UInt32(0)
            .UInt16(0x3F0).UInt16(1).UInt32(0).UInt16(90).Zero(3).Int32(1075847).Int32(1075848).UInt32(0).UInt16(1).UInt16(0);
        foreach (char c in "\t10\t10\t10")
            w.UInt16(((c & 0xFF) << 8) | (c >> 8));
        session.Inject(w.UInt16(0).UInt16(1).UInt16(0).ToArray(), true);

        Buff b = Assert.Single(session.Buffs);
        Assert.Equal("<left>Bless\n+10% Strength.<br>+10% Dexterity.<br>+10% Intelligence.</left>\nTime left: 00:01:30",
            BuffBarWindow.TooltipText(b.Title, b.Description, session.BuffSecondsLeft(b)));
        Assert.Equal("1:30", BuffBarWindow.TimerText(session.BuffSecondsLeft(b)));
        Assert.Equal("Bless\n+10% Strength.\n+10% Dexterity.\n+10% Intelligence.\nTime left: 00:01:30",
            GumpHtml.ToRichText(BuffBarWindow.TooltipText(b.Title, b.Description, session.BuffSecondsLeft(b)), out _));
    }

    [Fact]
    public void TheArtExists()
    {
        foreach (int g in new[] { 0x757F, 0x7580, 0x7581, 0x7582, BuffBarWindow.ButtonNormal, BuffBarWindow.ButtonPressed,
                     StatusWindow.BuffButton, StatusWindow.BuffButtonPressed })
            Assert.NotNull(_data.GetGump(g));
        Assert.Equal((100, 60), (_data.GetGump(0x7580)!.Width, _data.GetGump(0x7580)!.Height));
        Assert.Equal((60, 100), (_data.GetGump(0x757F)!.Width, _data.GetGump(0x757F)!.Height));
        foreach (int icon in new[] { 0x3E9, 0x3ED, 0x3F0 })
            Assert.Equal((28, 28), (_data.GetGump(BuffBarWindow.Graphic(icon))!.Width, _data.GetGump(BuffBarWindow.Graphic(icon))!.Height));
    }

    [Theory]
    [InlineData(SpellbookKind.Magery, 0, 1, "Temporarily reduces Target's Dexterity.", "Clumsy")]
    [InlineData(SpellbookKind.Necromancy, 0, 101, null, "Animate Dead")]
    [InlineData(SpellbookKind.Chivalry, 0, 201, null, "Cleanse by Fire")]
    public void SpellIconTooltipsResolve(SpellbookKind kind, int index, int spell, string? bookText, string floatingText)
    {
        var cliloc = new Cliloc(Path.Combine(_data.Directory, "Cliloc.enu"));
        string? book = cliloc.Get(SpellbookWindow.BookIconTooltip(kind, index));
        Assert.False(string.IsNullOrEmpty(book));
        if (bookText != null)
            Assert.Equal(bookText, book);
        Assert.Equal(floatingText, cliloc.Get(SpellbookWindow.SpellIconTooltip(spell)));
    }
}
