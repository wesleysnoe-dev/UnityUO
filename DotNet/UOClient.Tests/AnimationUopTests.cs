using System.Buffers.Binary;
using UOClient.IO;
using UOClient.IO.Formats;

namespace UOClient.Tests;

/// <summary>AnimationFrame*.uop bodies, as TazUO's AnimationsLoader reads them (format checks need no install).</summary>
public sealed class AnimationUopFormatTests
{
    // One frame's pixel block: a palette (colour 1 = pure red, 0 = transparent), then the sprite.
    private static byte[] Sprite(short centerX, short centerY, short width, short height, params (int X, int Y, byte[] Pixels)[] runs)
    {
        var b = new List<byte>(new byte[512]);
        b[2] = 0x00; b[3] = 0x7C; // 0x7C00: red
        void I16(short v) { b.Add((byte)v); b.Add((byte)(v >> 8)); }
        void U32(uint v) { for (int i = 0; i < 4; i++) b.Add((byte)(v >> (8 * i))); }
        I16(centerX); I16(centerY); I16(width); I16(height);
        foreach (var (x, y, pixels) in runs)
        {
            // Run positions are relative to (centerX, centerY + height), 10-bit two's complement.
            int xr = x - centerX, yr = y - centerY - height;
            U32(((uint)(xr & 0x3FF) << 22) | ((uint)(yr & 0x3FF) << 12) | (uint)pixels.Length);
            b.AddRange(pixels);
        }
        U32(0x7FFF7FFF);
        return b.ToArray();
    }

    /// <summary>An action entry: 32-byte header, count, header table at 64, one pixel block per listed frame id.</summary>
    private static byte[] Entry(IList<(int FrameId, byte[] Pixels)> frames)
    {
        const int tableStart = 64;
        int dataStart = tableStart + frames.Count * 16;
        var buf = new byte[dataStart + frames.Sum(f => f.Pixels.Length)];
        BinaryPrimitives.WriteInt32LittleEndian(buf.AsSpan(32), frames.Count);
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(36), tableStart);
        int pos = dataStart;
        for (int i = 0; i < frames.Count; i++)
        {
            int h = tableStart + i * 16;
            BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(h), 25);
            BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(h + 2), (ushort)frames[i].FrameId);
            BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(h + 12), (uint)(pos - h));
            frames[i].Pixels.CopyTo(buf, pos);
            pos += frames[i].Pixels.Length;
        }
        return buf;
    }

    [Fact]
    public void FramesSplitIntoFiveDirectionsByFrameId()
    {
        // 10 frames: 2 per direction. Direction d's frames have width d + 1, so each can be told apart.
        var frames = Enumerable.Range(1, 10)
            .Select(id => (id, Sprite(0, -4, (short)((id - 1) / 2 + 1), 4, (0, 0, new byte[] { 1 }))))
            .ToList();
        byte[] entry = Entry(frames);

        for (int dir = 0; dir < 5; dir++)
        {
            AnimationFrame[] got = AnimationUop.Decode(entry, dir);
            Assert.Equal(2, got.Length);
            Assert.All(got, f => Assert.Equal(dir + 1, f.Image.Width));
            Assert.Equal(0xFFFF0000u, got[0].Image.Pixels[0]); // red from the frame's own palette
        }
    }

    [Fact]
    public void MissingFrameIdsStayEmptySlots()
    {
        // Ids 1, 2, 4, 5 (3 missing): five slots, so one frame per direction, and direction 2 is empty.
        var frames = new[] { 1, 2, 4, 5 }.Select(id => (id, Sprite(0, -2, 3, 2, (0, 0, new byte[] { 1, 0, 1 })))).ToList();
        byte[] entry = Entry(frames);

        Assert.Single(AnimationUop.Decode(entry, 1));
        AnimationFrame gap = Assert.Single(AnimationUop.Decode(entry, 2));
        Assert.Equal(1, gap.Image.Width);
        AnimationFrame f = Assert.Single(AnimationUop.Decode(entry, 3));
        Assert.Equal(3, f.Image.Width);
        Assert.NotEqual(0u, f.Image.Pixels[0] >> 24);
        Assert.Equal(0u, f.Image.Pixels[1] >> 24); // palette colour 0 is transparent, as TazUO's alphaCheck
    }

    [Fact]
    public void EquipmentGetsAtLeastTenSlots()
    {
        var frames = Enumerable.Range(1, 5).Select(id => (id, Sprite(0, -1, 1, 1, (0, 0, new byte[] { 1 })))).ToList();
        byte[] entry = Entry(frames);
        Assert.Single(AnimationUop.Decode(entry, 0));
        // With 10 slots per direction all five frames are direction 0 (AnimationsLoader.cs:1432-1435).
        Assert.Equal(10, AnimationUop.Decode(entry, 0, equipment: true).Length);
        Assert.All(AnimationUop.Decode(entry, 0, equipment: true).Take(5), f => Assert.Equal(1, f.Image.Width));
    }

    [Fact]
    public void ShortOrBadEntriesGiveNoFrames()
    {
        Assert.Empty(AnimationUop.Decode(null!, 0));
        Assert.Empty(AnimationUop.Decode(new byte[39], 0));
        Assert.Empty(AnimationUop.Decode(new byte[64], 5));
        var bad = new byte[64];
        BinaryPrimitives.WriteInt32LittleEndian(bad.AsSpan(32), 100); // table runs off the end
        BinaryPrimitives.WriteUInt32LittleEndian(bad.AsSpan(36), 40);
        Assert.Empty(AnimationUop.Decode(bad, 0));
    }

    private static byte[] Sequence(int body, int count, params (int Old, uint Frames, int New)[] replaces)
    {
        var buf = new byte[56 + replaces.Length * 72];
        BinaryPrimitives.WriteUInt32LittleEndian(buf, (uint)body);
        BinaryPrimitives.WriteInt32LittleEndian(buf.AsSpan(52), count);
        for (int i = 0; i < replaces.Length; i++)
        {
            int p = 56 + i * 72;
            BinaryPrimitives.WriteInt32LittleEndian(buf.AsSpan(p), replaces[i].Old);
            BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(p + 4), replaces[i].Frames);
            BinaryPrimitives.WriteInt32LittleEndian(buf.AsSpan(p + 8), replaces[i].New);
        }
        return buf;
    }

    [Fact]
    public void SequenceReplacesOnlyZeroFrameGroups()
    {
        var parsed = AnimationUop.ParseSequence(Sequence(753, 3, (25, 0, 1), (22, 7, 0), (90, 0, 3)))!.Value;
        Assert.Equal(753, parsed.Body);
        Assert.Equal(1, parsed.Actions[25]);
        Assert.Equal(22, parsed.Actions[22]); // has frames of its own: kept
        Assert.Equal(AnimationUop.MaxActions, parsed.Actions.Length); // 90 is out of range: ignored
    }

    [Fact]
    public void SequenceCountsOf48And68AreNotLists()
    {
        // TazUO treats these counts as a different layout and reads no replacements (AnimationsLoader.cs:992).
        var parsed = AnimationUop.ParseSequence(Sequence(1, 48, (0, 0, 5)))!.Value;
        Assert.Equal(0, parsed.Actions[0]);
        Assert.Null(AnimationUop.ParseSequence(new byte[55]));
    }
}

/// <summary>The same rules against the installed AnimationFrame*.uop, mobtypes.txt and AnimationSequence.uop.</summary>
[Collection("UO install")]
public sealed class AnimationUopInstallTests
{
    private readonly UOData _data;

    public AnimationUopInstallTests(UOInstallFixture fixture) => _data = fixture.Data;

    private AnimationLibrary Anims => _data.AnimationLibrary;

    [Fact]
    public void MobTypesLaterLinesWinAndFlagUopBodies()
    {
        // mobtypes.txt lists 666 twice: "EQUIPMENT 0", then "HUMAN 10000" (the gargoyle male). The later one counts.
        Assert.True(Anims.TryGetMobType(666, out MobType type, out uint flags));
        Assert.Equal(MobType.Human, type);
        Assert.NotEqual(0u, flags & AnimationLibrary.UseUopAnimation);
        Assert.True(Anims.TryGetMobType(400, out _, out uint human));
        Assert.Equal(0u, human & AnimationLibrary.UseUopAnimation);
    }

    [Theory]
    [InlineData(666)] // gargoyle male
    [InlineData(667)] // gargoyle female
    [InlineData(753)] // gargoyle slave
    public void UopBodiesDrawFromAnimationFrame(int body)
    {
        Assert.True(Anims.UsesUop(body));
        Assert.Equal(-1, Anims.Resolve(body).File);
        var (walk, _, stand) = Anims.Actions(body);
        for (int dir = 0; dir < 5; dir++)
        {
            AnimationFrame[] standing = Anims.Read(body, stand, dir);
            Assert.NotEmpty(standing);
            Assert.Contains(standing, f => f.Image.Pixels.Any(p => (p >> 24) != 0));
            Assert.NotEmpty(Anims.Read(body, walk, dir));
        }
    }

    [Fact]
    public void UopCreaturesUseTazUOPeaceActions()
    {
        // MONSTER 10000: no Low/People/IdleAt8/CanFlying flags, so walk 22, run 24, stand 25 (MobileAnimation.cs:150-195).
        Assert.Equal(AnimGroup.Monster, Anims.GroupOf(753));
        Assert.Equal((22, 24, 25), Anims.Actions(753));
        // A UOP human keeps the people numbering.
        Assert.Equal(AnimGroup.Human, Anims.GroupOf(666));
        Assert.Equal((0, 2, 4), Anims.Actions(666));
    }

    [Fact]
    public void MulBodiesAreUnchanged()
    {
        Assert.False(Anims.UsesUop(400));
        Assert.Equal((0, 400, 0), Anims.Resolve(400));
        Assert.Equal((0, 2, 4), Anims.Actions(400));
        Assert.Equal((0, 1, 2), Anims.Actions(200));
        Assert.Equal((0, 0, 1), Anims.Actions(1));
        Assert.NotEmpty(Anims.Read(400, 4, 1));
    }

    [Fact]
    public void ManyFlaggedBodiesResolveInTheUopPackages()
    {
        int flagged = 0, found = 0;
        for (int body = 0; body < 0x1000; body++)
        {
            if (!Anims.TryGetMobType(body, out _, out uint flags) || (flags & AnimationLibrary.UseUopAnimation) == 0)
                continue;
            flagged++;
            if (Anims.UsesUop(body))
                found++;
        }
        Assert.True(flagged > 100, $"{flagged} flagged");
        Assert.True(found > flagged / 2, $"{found} of {flagged} flagged bodies found in AnimationFrame*.uop");
    }
}
