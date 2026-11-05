using UOClient.Core;
using UOClient.Net;

namespace UOClient.Tests;

/// <summary>Smaller world packets, injected as ModernUO sends them.</summary>
public sealed class SessionPacketTests
{
    private static GameSession InWorldAs(uint player, int x, int y)
    {
        var session = new GameSession(new LoginSettings());
        // 0x1B login confirm: serial, 0, body, x, y, z, direction, 9 unknown, map width, height (37 bytes).
        session.Inject(new PacketWriter(0x1B, 37).UInt32(player).UInt32(0).UInt16(0x190).UInt16(x).UInt16(y).UInt16(0)
            .Byte(0).Zero(9).UInt16(7168).UInt16(4096).Zero(6).ToArray(), false);
        return session;
    }

    [Fact]
    public void ServerChangeMovesThePlayerAndResizesTheMap()
    {
        GameSession session = InWorldAs(0x00000042, 1495, 1629);
        (int, int, sbyte)? changed = null;
        session.ServerChanged += (x, y, z) => changed = (x, y, z);
        Mobile? updated = null;
        session.World.MobileUpdated += m => updated = m;

        // ModernUO's SendServerChange: x, y, z, 0, 0, 0, map width, map height.
        byte[] packet = new PacketWriter(0x76, 16).UInt16(1200).UInt16(800).UInt16(unchecked((ushort)-5)).Byte(0)
            .UInt16(0).UInt16(0).UInt16(2304).UInt16(1600).ToArray();
        Assert.Equal(PacketLengths.For(ClientVersion.Default)[0x76], packet.Length);
        session.Inject(packet, false);

        Assert.Equal((1200, 800, (sbyte)-5), changed);
        Assert.Same(session.World.Player, updated);
        Assert.Equal(1200, session.World.Player.X);
        Assert.Equal(800, session.World.Player.Y);
        Assert.Equal(-5, session.World.Player.Z);
        Assert.Equal(2304, session.World.MapWidth);
        Assert.Equal(1600, session.World.MapHeight);
        Assert.Contains("1200,800,-5", session.Status);
    }

    [Fact]
    public void MultiTargetCursorCarriesThePlacement()
    {
        var session = new GameSession(new LoginSettings());
        TargetRequest? asked = null;
        session.TargetRequested += t => asked = t;

        // ModernUO's SendMultiTargetReq for a 7.0.9+ client: allow ground, id, flags, 11 zero, multi, offsets, 0.
        byte[] packet = new PacketWriter(0x99, 30).Byte(1).UInt32(0x0000ABCD).Byte(0).Zero(11)
            .UInt16(0x0064).UInt16(0).UInt16(4).UInt16(0).UInt32(0).ToArray();
        Assert.Equal(PacketLengths.For(ClientVersion.Default)[0x99], packet.Length);
        session.Inject(packet, false);

        Assert.NotNull(asked);
        Assert.Same(asked, session.Target);
        Assert.True(asked!.IsMulti);
        Assert.True(asked.AllowGround);
        Assert.Equal(0xABCDu, asked.CursorId);
        Assert.Equal(0x64, asked.MultiGraphic);
        Assert.Equal((0, 4, 0), (asked.MultiOffsetX, asked.MultiOffsetY, asked.MultiOffsetZ));
        TargetRequest house = asked;

        // A boat deed's multi 0 and negative offsets.
        session.Inject(new PacketWriter(0x99, 30).Byte(1).UInt32(7).Byte(0).Zero(11)
            .UInt16(0).UInt16(unchecked((ushort)-1)).UInt16(7).UInt16(unchecked((ushort)-3)).UInt32(0).ToArray(), false);
        Assert.True(session.Target.IsMulti);
        Assert.Equal(0, session.Target.MultiGraphic);
        Assert.Equal((-1, 7, -3), (session.Target.MultiOffsetX, session.Target.MultiOffsetY, session.Target.MultiOffsetZ));

        // Answered with a 0x6C ground target, as ModernUO's TargetResponse reads it.
        byte[] reply = GameSession.GroundTargetReply(house, 1500, 1600, 5);
        Assert.Equal(new byte[] { 0x6C, 1, 0, 0, 0xAB, 0xCD, 0, 0, 0, 0, 0, 0x05, 0xDC, 0x06, 0x40, 0, 5, 0, 0 }, reply);
    }

    [Fact]
    public void TextEntryDialogIsReadAndAnswered()
    {
        var session = new GameSession(new LoginSettings());
        TextEntryRequest? entry = null;
        session.TextEntryRequested += e => entry = e;

        // serial, parent, button, caption (length incl. terminator), cancel, style 2 (number), max, description.
        session.Inject(new PacketWriter(0xAB).UInt32(0x01020304).Byte(5).Byte(9)
            .UInt16(12).AsciiZ("How many?!!").Bool(true).Byte(2).UInt32(500)
            .UInt16(9).AsciiZ("1 to 500").ToArray(), true);
        Assert.Equal(-1, PacketLengths.For(ClientVersion.Default)[0xAB]);

        Assert.NotNull(entry);
        Assert.Equal(0x01020304u, entry!.Serial);
        Assert.Equal((5, 9), (entry.ParentId, entry.ButtonId));
        Assert.Equal("How many?!!", entry.Text);
        Assert.True(entry.CanCancel);
        Assert.True(entry.Numeric);
        Assert.Equal(500, entry.Max);
        Assert.Equal("1 to 500", entry.Description);

        // 0xAC: serial, parent, button, 1 = OK, length incl. terminator, text.
        Assert.Equal(new byte[] { 0xAC, 0, 16, 1, 2, 3, 4, 5, 9, 1, 0, 4, (byte)'4', (byte)'2', (byte)'0', 0 },
            GameSession.TextEntryReply(entry, "420"));
        // Cancel: code 0, empty text.
        Assert.Equal(new byte[] { 0xAC, 0, 13, 1, 2, 3, 4, 5, 9, 0, 0, 1, 0 }, GameSession.TextEntryReply(entry, null));
    }

    [Fact]
    public void ProfileIsReadAndRequested()
    {
        GameSession session = InWorldAs(0x00000042, 1495, 1629);
        CharacterProfile? shown = null;
        session.ProfileReceived += p => shown = p;

        // ModernUO's SendDisplayProfile: serial, header (Latin-1, zero-terminated), footer, body (BE UTF-16, zero-terminated).
        string header = "Lord Wes", footer = "This account is 3 days old.", body = "Smith of Britain.\nCafé ♥";
        byte[] packet = new PacketWriter(0xB8).UInt32(0x00000042).AsciiZ(header).UnicodeZ(footer).UnicodeZ(body).ToArray();
        Assert.Equal(-1, PacketLengths.For(ClientVersion.Default)[0xB8]);
        Assert.Equal(12 + header.Length + footer.Length * 2 + body.Length * 2, packet.Length); // ModernUO's length formula
        session.Inject(packet, true);

        Assert.NotNull(shown);
        Assert.Equal(0x42u, shown!.Serial);
        Assert.Equal(header, shown.Header);
        Assert.Equal(footer, shown.Footer);
        Assert.Equal(body, shown.Body);

        // Empty strings (a locked own profile hides the serial as 0).
        session.Inject(new PacketWriter(0xB8).UInt32(0).AsciiZ("").UnicodeZ("").UnicodeZ("").ToArray(), true);
        Assert.Equal((0u, "", "", ""), (shown!.Serial, shown.Header, shown.Footer, shown.Body));

        // The request, as ModernUO's ProfileReq reads it: length, mode 0 (display), serial.
        Assert.Equal(new byte[] { 0xB8, 0, 8, 0, 0, 0, 0, 0x42 }, GameSession.ProfileRequest(0x00000042));
    }

    [Fact]
    public void MapDetailsAndPinsAreKeptPerMap()
    {
        GameSession session = InWorldAs(0x00000042, 1495, 1629);
        MapItemDetails? shown = null;
        int changes = 0;
        session.MapDisplayed += m => shown = m;
        session.MapChanged += _ => changes++;

        // ModernUO's DisplayTo: SendMapDetails (0x90 before 7.0.13), then 0x56 clear (5), an add (1) per pin, editable (7).
        byte[] details = new PacketWriter(0x90, 19).UInt32(0x40000010).UInt16(0x139D)
            .UInt16(1344).UInt16(1600).UInt16(1856).UInt16(2112).UInt16(400).UInt16(400).ToArray();
        Assert.Equal(PacketLengths.For(ClientVersion.Default)[0x90], details.Length);
        session.Inject(details, false);
        byte[] clear = new PacketWriter(0x56, 11).UInt32(0x40000010).Byte(5).Byte(0).UInt16(0).UInt16(0).ToArray();
        Assert.Equal(PacketLengths.For(ClientVersion.Default)[0x56], clear.Length);
        session.Inject(clear, false);
        session.Inject(new PacketWriter(0x56, 11).UInt32(0x40000010).Byte(1).Byte(0).UInt16(120).UInt16(80).ToArray(), false);
        session.Inject(new PacketWriter(0x56, 11).UInt32(0x40000010).Byte(1).Byte(0).UInt16(300).UInt16(399).ToArray(), false);
        session.Inject(new PacketWriter(0x56, 11).UInt32(0x40000010).Byte(7).Byte(1).UInt16(0).UInt16(0).ToArray(), false);

        Assert.NotNull(shown);
        Assert.Same(shown, session.Maps[0x40000010]);
        Assert.Equal((0x139D, 1344, 1600, 1856, 2112, 400, 400, -1), (shown!.Gump, shown.X1, shown.Y1, shown.X2, shown.Y2, shown.Width, shown.Height, shown.Facet));
        Assert.Equal(new[] { new MapPin(120, 80), new MapPin(300, 399) }, shown.Pins);
        Assert.True(shown.Editable);
        Assert.Equal(4, changes);

        // Insert, move and remove by index, as ModernUO's MapItem applies them.
        session.Inject(new PacketWriter(0x56, 11).UInt32(0x40000010).Byte(2).Byte(1).UInt16(10).UInt16(20).ToArray(), false);
        session.Inject(new PacketWriter(0x56, 11).UInt32(0x40000010).Byte(3).Byte(0).UInt16(5).UInt16(6).ToArray(), false);
        session.Inject(new PacketWriter(0x56, 11).UInt32(0x40000010).Byte(4).Byte(2).UInt16(0).UInt16(0).ToArray(), false);
        Assert.Equal(new[] { new MapPin(5, 6), new MapPin(10, 20) }, shown.Pins);
        session.Inject(new PacketWriter(0x56, 11).UInt32(0x40000010).Byte(6).Byte(0).UInt16(0).UInt16(0).ToArray(), false);
        Assert.False(shown.Editable);

        // A second map keeps its own pins; 0xF5 (7.0.13+, what ModernUO sends our default client) adds the facet.
        byte[] newer = new PacketWriter(0xF5, 21).UInt32(0x40000020).UInt16(0x139D)
            .UInt16(0).UInt16(0).UInt16(5119).UInt16(4095).UInt16(200).UInt16(200).UInt16(3).ToArray();
        Assert.Equal(PacketLengths.For(ClientVersion.Default)[0xF5], newer.Length);
        session.Inject(newer, false);
        session.Inject(new PacketWriter(0x56, 11).UInt32(0x40000020).Byte(1).Byte(0).UInt16(1).UInt16(2).ToArray(), false);
        Assert.Equal(3, session.Maps[0x40000020].Facet);
        Assert.Equal(new[] { new MapPin(1, 2) }, session.Maps[0x40000020].Pins);
        Assert.Equal(2, session.Maps[0x40000010].Pins.Count);

        // Reopening a map: ModernUO clears (5) before resending its pins.
        session.Inject(clear, false);
        Assert.Empty(session.Maps[0x40000010].Pins);

        // The request, as ModernUO's OnMapCommand reads it: serial, command, pin index, x, y.
        Assert.Equal(new byte[] { 0x56, 0x40, 0, 0, 0x10, 2, 3, 0, 120, 1, 44 }, GameSession.MapCommandRequest(0x40000010, 2, 3, 120, 300));
    }

    [Fact]
    public void SeasonIsStoredAndRaised()
    {
        GameSession session = InWorldAs(0x00000042, 1495, 1629);
        var changes = new List<(Season, bool)>();
        session.SeasonChanged += (s, sound) => changes.Add((s, sound));
        Assert.Equal(Season.Summer, session.Season);

        // ModernUO's SendSeasonChange: 0xBC, season, play-sound flag (3 bytes).
        byte[] winter = { 0xBC, 3, 1 };
        Assert.Equal(PacketLengths.For(ClientVersion.Default)[0xBC], winter.Length);
        session.Inject(winter, false);
        Assert.Equal(Season.Winter, session.Season);

        session.Inject(new byte[] { 0xBC, 4, 0 }, false);
        Assert.Equal(Season.Desolation, session.Season);

        // Out of range reads as spring, like the classic client.
        session.Inject(new byte[] { 0xBC, 9, 0 }, false);
        Assert.Equal(Season.Spring, session.Season);

        Assert.Equal(new[] { (Season.Winter, true), (Season.Desolation, false), (Season.Spring, false) }, changes);
    }

    [Fact]
    public void WeatherIsStoredAndRaised()
    {
        GameSession session = InWorldAs(0x00000042, 1495, 1629);
        var changes = new List<Weather>();
        session.WeatherChanged += changes.Add;
        Assert.Equal(WeatherType.None, session.Weather.Type);
        Assert.False(session.Weather.IsFalling);

        // ModernUO's SendWeather: 0x65, type, density, temperature (4 bytes).
        byte[] rain = { 0x65, 0, 35, 15 };
        Assert.Equal(PacketLengths.For(ClientVersion.Default)[0x65], rain.Length);
        session.Inject(rain, false);
        Assert.Equal(WeatherType.Rain, session.Weather.Type);
        Assert.Equal(35, session.Weather.Count);
        Assert.Equal(15, session.Weather.Temperature);
        Assert.True(session.Weather.IsFalling);

        // Extreme cold: ModernUO negates the temperature and sends snow; the byte is signed.
        session.Inject(new byte[] { 0x65, 2, 70, unchecked((byte)-15) }, false);
        Assert.Equal(WeatherType.Snow, session.Weather.Type);
        Assert.Equal(70, session.Weather.Count);
        Assert.Equal(-15, session.Weather.Temperature);

        // Stage 0 of a weather cycle: density 0, type 0xFE. 0xFF and unknown types clear too.
        session.Inject(new byte[] { 0x65, 0xFE, 0, 15 }, false);
        Assert.Equal(WeatherType.None, session.Weather.Type);
        Assert.False(session.Weather.IsFalling);
        session.Inject(new byte[] { 0x65, 0xFF, 10, 0 }, false);
        Assert.Equal(WeatherType.None, session.Weather.Type);
        Assert.Equal(0, session.Weather.Count);

        Assert.Equal(new[] { WeatherType.Rain, WeatherType.Snow, WeatherType.None, WeatherType.None }, changes.Select(w => w.Type));
    }

    [Fact]
    public void QuestArrowShowsAndClearsOnHighSeasClients()
    {
        GameSession session = InWorldAs(0x00000042, 1495, 1629);
        var changes = new List<QuestArrow?>();
        session.QuestArrowChanged += a => changes.Add(a);

        // ModernUO's SendSetArrow for a High Seas client: 1, x, y, tracked serial (10 bytes).
        byte[] set = new PacketWriter(0xBA, 10).Byte(1).UInt16(1520).UInt16(1650).UInt32(0x00001234).ToArray();
        Assert.Equal(PacketLengths.For(ClientVersion.Default)[0xBA], set.Length);
        session.Inject(set, false);

        Assert.NotNull(session.QuestArrow);
        Assert.Equal((1520, 1650, 0x1234u), (session.QuestArrow!.X, session.QuestArrow.Y, session.QuestArrow.Serial));
        Assert.Single(changes);
        Assert.Same(session.QuestArrow, changes[0]);

        // SendCancelArrow on High Seas still carries the last location and serial.
        session.Inject(new PacketWriter(0xBA, 10).Byte(0).UInt16(1520).UInt16(1650).UInt32(0x00001234).ToArray(), false);
        Assert.Null(session.QuestArrow);
        Assert.Equal(2, changes.Count);
        Assert.Null(changes[1]);
    }

    [Fact]
    public void QuestArrowShowsAndClearsOnOlderClients()
    {
        GameSession session = InWorldAs(0x00000042, 1495, 1629);
        var old = new ClientVersion(7, 0, 8, 0);

        // Before 7.0.9 the packet is 6 bytes: 1, x, y to set; 0, -1, -1 to cancel.
        byte[] set = new PacketWriter(0xBA, 6).Byte(1).UInt16(300).UInt16(400).ToArray();
        Assert.Equal(PacketLengths.For(old)[0xBA], set.Length);
        session.Inject(set, false);
        Assert.Equal((300, 400, 0u), (session.QuestArrow!.X, session.QuestArrow.Y, session.QuestArrow.Serial));

        session.Inject(new PacketWriter(0xBA, 6).Byte(0).UInt16(unchecked((ushort)-1)).UInt16(unchecked((ushort)-1)).ToArray(), false);
        Assert.Null(session.QuestArrow);
    }

    /// <summary>0xDF add as ModernUO's SendAddBuffPacket writes it (args, when given, go out as "\t" + args, little-endian).</summary>
    private static byte[] AddBuffPacket(uint mob, int icon, int title, int secondary, string? args, int seconds)
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

    [Fact]
    public void BuffsAreAddedReplacedTimedAndRemoved()
    {
        GameSession session = InWorldAs(0x00000042, 1495, 1629);
        var added = new List<Buff>();
        var removed = new List<Buff>();
        session.BuffAdded += added.Add;
        session.BuffRemoved += removed.Add;
        Assert.Equal(-1, PacketLengths.For(ClientVersion.Default)[0xDF]);
        session.Update(100);

        // Bless (0x3F0) with three arguments for 90 seconds: ModernUO's length is args.Length * 2 + 52.
        string args = "10\t10\t10";
        byte[] bless = AddBuffPacket(0x42, 0x3F0, 1075847, 1075848, args, 90);
        Assert.Equal(args.Length * 2 + 52, bless.Length);
        session.Inject(bless, true);

        Buff b = Assert.Single(session.Buffs);
        Assert.Same(b, Assert.Single(added));
        Assert.Equal((0x3F0, 0x42u, 1075847, 1075848, args, 90), (b.Icon, b.Serial, b.TitleCliloc, b.DescriptionCliloc, b.Args, b.Duration));
        Assert.Equal("#1075847", b.Title);       // no cliloc file loaded
        Assert.Equal("#1075848", b.Description);
        Assert.Equal(90, session.BuffSecondsLeft(b));
        session.Update(130);
        Assert.Equal(60, session.BuffSecondsLeft(b));

        // Night sight (0x3ED) without arguments or timer: 46 bytes.
        byte[] nightSight = AddBuffPacket(0x42, 0x3ED, 1075643, 1075644, null, 0);
        Assert.Equal(46, nightSight.Length);
        session.Inject(nightSight, true);
        Assert.Equal(2, session.Buffs.Count);
        Buff n = session.Buffs[1];
        Assert.Equal(("", 0, -1.0), (n.Args, n.Duration, session.BuffSecondsLeft(n)));

        // Bless again (recast) replaces the first one in place.
        session.Inject(AddBuffPacket(0x42, 0x3F0, 1075847, 1075848, "12\t12\t12", 120), true);
        Assert.Equal(2, session.Buffs.Count);
        Assert.Equal(("12\t12\t12", 120.0), (session.Buffs[0].Args, session.BuffSecondsLeft(session.Buffs[0])));
        Assert.Equal(3, added.Count);

        // SendRemoveBuffPacket: serial, icon, command 0, 4 zero (15 bytes).
        byte[] remove = new PacketWriter(0xDF).UInt32(0x42).UInt16(0x3F0).UInt16(0).UInt32(0).ToArray();
        Assert.Equal(15, remove.Length);
        session.Inject(remove, true);
        Assert.Same(n, Assert.Single(session.Buffs));
        Assert.Equal(0x3F0, Assert.Single(removed).Icon);

        // Removing what isn't there changes nothing.
        session.Inject(remove, true);
        Assert.Single(removed);
        Assert.Single(session.Buffs);
    }
}
