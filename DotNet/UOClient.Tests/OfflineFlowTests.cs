using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using UOClient.Core;
using UOClient.IO.Formats;
using UOClient.Net;

namespace UOClient.Tests;

/// <summary>
/// Whole round trips with no server: the session runs offline, what ModernUO would send is injected, and what the
/// session sends back is read the way ModernUO's incoming handlers read it.
/// </summary>
public sealed class OfflineFlowTests
{
    private const uint Me = 0x00000042, Bob = 0x00000077, Ann = 0x00000088;

    private static GameSession InWorld(List<byte[]> sent, ushort body = 0x190)
    {
        var session = new GameSession(new LoginSettings { AnnounceChatLinks = false });
        session.StartOffline(sent.Add);
        session.Inject(new PacketWriter(0x1B, 37).UInt32(Me).UInt32(0).UInt16(body).UInt16(1495).UInt16(1629).UInt16(0)
            .Byte(0).Zero(9).UInt16(7168).UInt16(4096).Zero(6).ToArray(), false);
        session.Inject(new byte[] { 0x55 }, false);
        Assert.Equal(SessionState.InWorld, session.State);
        session.World.Mobiles[Bob] = new Mobile { Serial = Bob, Name = "Bob" };
        session.World.Mobiles[Ann] = new Mobile { Serial = Ann, Name = "Ann" };
        sent.Clear();
        return session;
    }

    // ---- party ----

    private static PacketWriter Party(int command) => new PacketWriter(0xBF).UInt16(0x06).Byte(command);

    // PartyPackets.CreatePartyMemberList / CreatePartyRemoveMember / SendPartyInvitation / CreatePartyTextMessage.
    private static byte[] MemberList(params uint[] members)
    {
        PacketWriter w = Party(0x01).Byte(members.Length);
        foreach (uint m in members)
            w.UInt32(m);
        return w.ToArray();
    }

    private static byte[] Removed(uint removed, params uint[] left)
    {
        PacketWriter w = Party(0x02).Byte(left.Length).UInt32(removed);
        foreach (uint m in left)
            w.UInt32(m);
        return w.ToArray();
    }

    private static byte[] Invitation(uint leader) => Party(0x07).UInt32(leader).ToArray();

    private static byte[] TextMessage(uint from, string text, bool toAll) => Party(toAll ? 0x04 : 0x03).UInt32(from).UnicodeZ(text).ToArray();

    /// <summary>What ModernUO's PartyMessage handler makes of a request: the command and its fields.</summary>
    private sealed record PartyRequest(int Command, uint Serial, string Text, bool CanLoot);

    private static PartyRequest ReadParty(byte[] p)
    {
        Assert.Equal(0xBF, p[0]);
        Assert.Equal(p.Length, BinaryPrimitives.ReadUInt16BigEndian(p.AsSpan(1)));
        Assert.Equal(0x06, BinaryPrimitives.ReadUInt16BigEndian(p.AsSpan(3)));
        int command = p[5], pos = 6;
        uint U32() { uint v = BinaryPrimitives.ReadUInt32BigEndian(p.AsSpan(pos)); pos += 4; return v; }
        string BigUniSafe()
        {
            int end = pos;
            while (end + 1 < p.Length && (p[end] | p[end + 1]) != 0)
                end += 2;
            string s = Encoding.BigEndianUnicode.GetString(p, pos, end - pos);
            pos = Math.Min(end + 2, p.Length);
            return s;
        }
        return command switch
        {
            0x01 => new PartyRequest(command, 0, null, false),                      // OnAdd(from): nothing read
            0x02 or 0x08 or 0x09 => new PartyRequest(command, U32(), null, false),  // OnRemove / OnAccept / OnDecline
            0x03 => new PartyRequest(command, U32(), BigUniSafe(), false),           // OnPrivateMessage(to, text)
            0x04 => new PartyRequest(command, 0, BigUniSafe(), false),               // OnPublicMessage(text)
            0x06 => new PartyRequest(command, 0, null, p[pos] != 0),                 // OnSetCanLoot(bool)
            _ => throw new Xunit.Sdk.XunitException($"ModernUO traces unknown party command 0x{command:X2}")
        };
    }

    [Fact]
    public void PartyInviteAcceptMessageLeaveRoundTrip()
    {
        var sent = new List<byte[]>();
        GameSession session = InWorld(sent);

        // We invite: "/add" asks for the server's target cursor.
        session.Say("/add");
        Assert.Equal(new PartyRequest(0x01, 0, null, false), ReadParty(Assert.Single(sent)));
        sent.Clear();

        // Bob invites us; "/accept" answers with his serial (OnAccept finds the leader by it).
        session.Inject(Invitation(Bob), true);
        Assert.Equal(Bob, session.PartyInviter);
        session.Say("/accept");
        Assert.Equal(new PartyRequest(0x08, Bob, null, false), ReadParty(Assert.Single(sent)));
        sent.Clear();
        Assert.Equal(0u, session.PartyInviter);

        // Party.OnAccept: everyone gets the list, leader first.
        session.Inject(MemberList(Bob, Me, Ann), true);
        Assert.Equal(new[] { Bob, Me, Ann }, session.PartyMembers);

        // To everyone, then privately to member 3 (Ann), then the loot flag.
        session.Say("/heal me");
        session.Say("/3 just you");
        session.SetPartyCanLoot(true);
        Assert.Equal(new[]
        {
            new PartyRequest(0x04, 0, "heal me", false),
            new PartyRequest(0x03, Ann, "just you", false),
            new PartyRequest(0x06, 0, null, true)
        }, sent.Select(ReadParty));
        sent.Clear();

        // Party.SendPublicMessage echoes ours back to every member, us included.
        session.Inject(TextMessage(Me, "heal me", true), true);
        Assert.Equal("heal me", session.World.Journal[^1].Text);
        Assert.Equal(GameSession.PartyMessageType, session.World.Journal[^1].Type);

        // "/quit" removes ourselves; Party.Remove sends us an empty removal.
        session.Say("/quit");
        Assert.Equal(new PartyRequest(0x02, Me, null, false), ReadParty(Assert.Single(sent)));
        session.Inject(Removed(Me), true);
        Assert.False(session.InParty);
        Assert.False(session.PartyCanLoot);
    }

    [Fact]
    public void PartyDeclineRoundTrip()
    {
        var sent = new List<byte[]>();
        GameSession session = InWorld(sent);

        session.Inject(Invitation(Ann), true);
        session.Say("/decline");
        Assert.Equal(new PartyRequest(0x09, Ann, null, false), ReadParty(Assert.Single(sent)));

        // Party.OnDecline sends the candidate an empty removal of themselves.
        session.Inject(Removed(Me), true);
        Assert.False(session.InParty);
        Assert.Equal(0u, session.PartyInviter);

        // Nothing left to answer: a second "/decline" sends nothing.
        sent.Clear();
        session.Say("/decline");
        session.AcceptPartyInvite();
        Assert.Empty(sent);
    }

    [Fact]
    public void PartyLeaderRemovesAMember()
    {
        var sent = new List<byte[]>();
        GameSession session = InWorld(sent);
        session.Inject(MemberList(Me, Bob, Ann), true);
        Assert.Equal(Me, session.PartyLeader);

        session.PartyRemove(Bob);
        Assert.Equal(new PartyRequest(0x02, Bob, null, false), ReadParty(Assert.Single(sent)));

        session.Inject(Removed(Bob, Me, Ann), true);
        Assert.Equal(new[] { Me, Ann }, session.PartyMembers);
    }

    // ---- stack split ----

    [Fact]
    public void ShiftSplitLiftsTheTypedAmountAsModernUoReadsIt()
    {
        var sent = new List<byte[]>();
        GameSession session = InWorld(sent);
        const uint gold = 0x40000020;

        // A stack of 100 gold (tiledata Generic): Shift opens the picker, 25 is typed, OK lifts.
        Assert.True(GameSession.CanSplit(100, TileFlags.Generic));
        int amount = GameSession.SplitAmount(GameSession.ParseSplitAmount("25", 100), 100);
        session.Lift(gold, amount);

        // IncomingItemPackets.LiftReq: serial, uint16 amount; fixed length 7.
        byte[] lift = Assert.Single(sent);
        Assert.Equal(7, lift.Length);
        Assert.Equal(0x07, lift[0]);
        Assert.Equal(gold, BinaryPrimitives.ReadUInt32BigEndian(lift.AsSpan(1)));
        Assert.Equal(25, BinaryPrimitives.ReadUInt16BigEndian(lift.AsSpan(5)));
        Assert.Equal(gold, session.Holding);

        // Mobile.Lift leaves 75 behind as a new item and the lifted 25 keeps the serial; we drop it in the pack.
        sent.Clear();
        session.DropInto(gold, 0x40000001, 44, 65);
        Assert.Equal(0x08, Assert.Single(sent)[0]);

        // Without Shift the whole stack goes, after the action delay a lift starts.
        sent.Clear();
        session.Lift(gold, 100);
        Assert.Empty(sent);
        session.Update(2);
        Assert.Equal(100, BinaryPrimitives.ReadUInt16BigEndian(Assert.Single(sent).AsSpan(5)));
    }

    // ---- death and resurrection ----

    private static byte[] PlayerUpdate(ushort body) =>
        new PacketWriter(0x20, 19).UInt32(Me).UInt16(body).Byte(0).UInt16(0).Byte(0)
            .UInt16(1495).UInt16(1629).UInt16(0).Byte(0).Byte(0).ToArray();

    // ResurrectGump.BuildDefaultLayout / BuildPricedLayout, as GumpLayoutBuilder writes the commands that matter.
    private const string ResurrectLayout =
        "{ page 0 }{ resizepic 0 0 2600 400 350 }{ xmfhtmlgump 0 20 400 35 1011022 0 0 }" +
        "{ xmfhtmlgump 50 55 300 140 1011025 1 1 }" +
        "{ button 200 227 4005 4007 1 0 0 }{ xmfhtmlgump 235 230 110 35 1011012 0 0 }" +
        "{ button 65 227 4005 4007 1 0 1 }{ xmfhtmlgump 100 230 110 35 1011011 0 0 }";

    private const string PricedResurrectLayout =
        "{ noclose }{ page 0 }{ gumppic 0 0 3600 }" +
        "{ radio 30 140 9727 9730 1 1 }{ xmfhtmlgumpcolor 65 145 300 25 1060015 0 0 32767 }" +
        "{ radio 30 175 9727 9730 0 0 }{ xmfhtmlgumpcolor 65 178 300 25 1060016 0 0 32767 }" +
        "{ text 90 70 1645 0 }{ button 290 175 247 248 1 0 2 }";

    /// <summary>What GumpSystem.DisplayGumpResponse hands OnResponse: serial, type, button and the switches.</summary>
    private static (uint Serial, uint Type, int Button, int[] Switches) ReadGumpReply(byte[] p)
    {
        Assert.Equal(0xB1, p[0]);
        Assert.Equal(p.Length, BinaryPrimitives.ReadUInt16BigEndian(p.AsSpan(1)));
        int pos = 3;
        int Int() { int v = BinaryPrimitives.ReadInt32BigEndian(p.AsSpan(pos)); pos += 4; return v; }
        uint serial = (uint)Int(), type = (uint)Int();
        int button = Int();
        int[] switches = Enumerable.Range(0, Int()).Select(_ => Int()).ToArray();
        Assert.Equal(0, Int()); // no text entries
        Assert.Equal(p.Length, pos);
        return (serial, type, button, switches);
    }

    [Fact]
    public void DeathGhostAndResurrectionGumpRoundTrip()
    {
        var sent = new List<byte[]>();
        GameSession session = InWorld(sent);
        int died = 0;
        var dead = new List<bool>();
        session.PlayerDied += () => died++;
        session.PlayerDeadChanged += dead.Add;

        // Mobile.OnDeath: the death status, then the ghost body.
        session.Inject(new byte[] { 0x2C, 2 }, false);
        session.Inject(PlayerUpdate(402), false);
        Assert.Equal(1, died);
        Assert.True(session.PlayerDead);

        // ModernUO's DeathStatusResponse ignores a client 0x2C and TazUO never sends its Send_DeathScreen: no answer.
        Assert.Empty(sent);

        // A healer offers resurrection; CANCEL (button 0) keeps the ghost, CONTINUE (button 1) is what OnResponse acts on.
        ServerGump offer = null;
        session.World.GumpOpened += g => offer = g;
        session.Inject(CompressedGump(0x40001000, 0x5EED, ResurrectLayout), true);
        Assert.NotNull(offer);
        int[] buttons = offer.Commands.Where(c => c.Name == "button").Select(c => c.Arg(6)).ToArray();
        Assert.Equal(new[] { 0, 1 }, buttons);

        session.ReplyGump(offer, 1, Array.Empty<int>(), Array.Empty<(int, string)>());
        var reply = ReadGumpReply(Assert.Single(sent));
        Assert.Equal((0x40001000u, 0x5EEDu, 1), (reply.Serial, reply.Type, reply.Button));
        Assert.Empty(reply.Switches);
        Assert.Empty(session.World.Gumps);

        // Mobile.Resurrect sends no packet of its own: the living body is the resurrection.
        session.Inject(PlayerUpdate(0x190), false);
        Assert.False(session.PlayerDead);
        Assert.Equal(new[] { true, false }, dead);
    }

    [Fact]
    public void PricedResurrectionAnswersWithTheChosenRadio()
    {
        var sent = new List<byte[]>();
        GameSession session = InWorld(sent, 403);
        Assert.True(session.PlayerDead);

        ServerGump offer = null;
        session.World.GumpOpened += g => offer = g;
        session.Inject(CompressedGump(0x40001001, 0x5EEE, PricedResurrectLayout, "500"), true);

        // "Grudgingly pay the money" (switch 1) starts checked; OnResponse wants button 2 and Switches[0] == 1.
        GumpCommand pay = offer.Commands.First(c => c.Name == "radio" && c.Arg(5) == 1);
        Assert.Equal(1, pay.Arg(4));
        Assert.Equal(2, offer.Commands.Single(c => c.Name == "button").Arg(6));

        session.ReplyGump(offer, 2, new[] { 1 }, Array.Empty<(int, string)>());
        var reply = ReadGumpReply(Assert.Single(sent));
        Assert.Equal((0x40001001u, 0x5EEEu, 2), (reply.Serial, reply.Type, reply.Button));
        Assert.Equal(new[] { 1 }, reply.Switches);
    }

    /// <summary>0xDD as ModernUO's DynamicGump.SendTo writes it.</summary>
    private static byte[] CompressedGump(uint serial, uint typeId, string layout, params string[] lines)
    {
        var w = new PacketWriter(0xDD).UInt32(serial).UInt32(typeId).Int32(100).Int32(0);
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
