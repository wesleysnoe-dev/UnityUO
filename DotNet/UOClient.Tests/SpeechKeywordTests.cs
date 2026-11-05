using System.Buffers.Binary;
using System.Text;
using UOClient.Core;
using UOClient.IO;
using UOClient.IO.Formats;

namespace UOClient.Tests;

public sealed class SpeechKeywordTests
{
    // Banker ids from ModernUO's Banker.OnSpeech; stable/claim are AnimalTrainer's.
    private static readonly SpeechKeywords Table = new(new[]
    {
        (0x0000, "*withdraw*"), (0x0001, "*balance*"), (0x0002, "*bank*"), (0x0003, "*check*"),
        (0x0008, "*stable*"), (0x0009, "*claim*"), (0x0154, "*vendor buy*")
    });

    [Fact]
    public void BankIsSentEncodedWithItsKeyword()
    {
        byte[] packet = GameSession.UnicodeSpeechRequest("bank", Table.Match("bank"));

        Assert.Equal(new byte[]
        {
            0xAD, 0x00, 0x14,       // id, length 20
            0xC0,                   // regular speech | encoded
            0x03, 0xB2,             // hue
            0x00, 0x03,             // font
            (byte)'E', (byte)'N', (byte)'U', 0x00,
            0x00, 0x10, 0x02,       // 12-bit count 1, 12-bit keyword 0x002
            (byte)'b', (byte)'a', (byte)'n', (byte)'k', 0x00 // UTF-8, null-terminated
        }, packet);
    }

    [Fact]
    public void UntaggedSpeechStaysBigEndianUnicode()
    {
        byte[] packet = GameSession.UnicodeSpeechRequest("hi", Table.Match("hi"));
        Assert.Equal(new byte[]
        {
            0xAD, 0x00, 0x12, 0x00, 0x03, 0xB2, 0x00, 0x03, (byte)'E', (byte)'N', (byte)'U', 0x00,
            0x00, (byte)'h', 0x00, (byte)'i', 0x00, 0x00
        }, packet);
    }

    [Theory]
    [InlineData("bank", new[] { 0x0002 })]
    [InlineData("Bank Balance", new[] { 0x0001, 0x0002 })]
    [InlineData("stable", new[] { 0x0008 })]
    [InlineData("vendor buy", new[] { 0x0154 })]
    [InlineData("hello there", new int[0])]
    public void KeywordsMatchCaseInsensitively(string text, int[] expected)
    {
        Assert.Equal(expected, Table.Match(text));
    }

    [Theory]
    [InlineData("bank")]
    [InlineData("balance bank check stable")]
    [InlineData("vendor buy bank")]
    public void ModernUODecodesTheKeywordBlock(string text)
    {
        List<int> ids = Table.Match(text);
        byte[] p = GameSession.UnicodeSpeechRequest(text, ids);

        // Mirrors IncomingMessagePackets.UnicodeSpeech.
        Assert.Equal(p.Length, BinaryPrimitives.ReadUInt16BigEndian(p.AsSpan(1)));
        Assert.Equal(0xC0, p[3] & 0xC0);
        int pos = 12;
        int value = BinaryPrimitives.ReadInt16BigEndian(p.AsSpan(pos)); pos += 2;
        int count = (value & 0xFFF0) >> 4, hold = value & 0xF;
        var decoded = new List<int>();
        for (int i = 0; i < count; i++)
        {
            if ((i & 1) == 0)
            {
                decoded.Add((hold << 8) | p[pos++]);
                hold = 0;
            }
            else
            {
                value = BinaryPrimitives.ReadInt16BigEndian(p.AsSpan(pos)); pos += 2;
                decoded.Add((value & 0xFFF0) >> 4);
                hold = value & 0xF;
            }
        }
        Assert.Equal(ids, decoded);
        int end = Array.IndexOf(p, (byte)0, pos);
        Assert.Equal(p.Length - 1, end);
        Assert.Equal(text, Encoding.UTF8.GetString(p, pos, end - pos));
    }

    [Theory]
    [InlineData("hello there", 0, "hello there", 0x3B2)]
    [InlineData(": waves", 2, "*waves*", 0x21)]
    [InlineData("; psst, over here", 8, "psst, over here", 0x33)]
    [InlineData("! Guards!", 9, "Guards!", 0x21)]
    [InlineData(":)", 0, ":)", 0x3B2)]             // a smiley is not an emote
    [InlineData("!bank", 0, "!bank", 0x3B2)]       // no space: said as typed
    [InlineData("[g hi all", 0, "[g hi all", 0x3B2)] // the chat window's channel commands are plain speech
    [InlineData(": ", 2, "", 0x21)]                 // nothing to emote: Say sends nothing
    public void TypedPrefixesPickTheSpeechMode(string line, int type, string text, int hue)
    {
        var (t, said, h) = GameSession.SpeechMode(line);
        Assert.Equal((byte)type, t);
        Assert.Equal(text, said);
        Assert.Equal((ushort)hue, h);
    }

    [Fact]
    public void AnEmoteReachesModernUOAsAnEmote()
    {
        var (type, text, hue) = GameSession.SpeechMode(": bows to the bank");
        byte[] p = GameSession.UnicodeSpeechRequest(text, Table.Match(text), hue, type);

        // IncomingMessagePackets.UnicodeSpeech: type with the encoded bits cleared, hue, and the keywords still tagged.
        Assert.Equal(0xC0, p[3] & 0xC0);
        Assert.Equal(2, p[3] & ~0xC0);
        Assert.Equal(0x21, BinaryPrimitives.ReadInt16BigEndian(p.AsSpan(4)));
        Assert.Equal(new byte[] { 0x00, 0x10, 0x02 }, p[12..15]);
        Assert.Equal("*bows to the bank*", Encoding.UTF8.GetString(p, 15, p.Length - 16));

        (type, text, hue) = GameSession.SpeechMode("! help");
        p = GameSession.UnicodeSpeechRequest(text, Table.Match(text), hue, type);
        Assert.Equal(9, p[3]); // yell, not encoded
    }

    [Fact]
    public void ParsesSpeechMulEntries()
    {
        var data = new List<byte>();
        foreach (var (id, s) in new[] { (2, "*bank*"), (7, ""), (8, "*stable*") })
        {
            byte[] b = Encoding.UTF8.GetBytes(s);
            data.AddRange(new[] { (byte)(id >> 8), (byte)id, (byte)(b.Length >> 8), (byte)b.Length });
            data.AddRange(b);
        }
        var table = new SpeechKeywords(data.ToArray());
        Assert.Equal(2, table.Count);
        Assert.Equal(new[] { 2 }, table.Match("BANK"));
        Assert.Equal(new[] { 8 }, table.Match("stable"));
    }
}

[Collection("UO install")]
public sealed class SpeechMulInstallTests
{
    private readonly UOData _data;

    public SpeechMulInstallTests(UOInstallFixture fixture) => _data = fixture.Data;

    [Fact]
    public void InstallTableTagsBankerAndStableWords()
    {
        var table = new SpeechKeywords(Path.Combine(_data.Directory, "speech.mul"));
        Assert.Contains(0x0002, table.Match("bank"));
        Assert.Contains(0x0001, table.Match("balance"));
        Assert.Contains(0x0008, table.Match("stable"));
    }
}
