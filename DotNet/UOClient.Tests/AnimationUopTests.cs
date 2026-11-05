using System.Buffers.Binary;
using UOClient.IO;
using UOClient.IO.Files;
using UOClient.IO.Formats;

namespace UOClient.Tests;

/// <summary>AnimationFrame*.uop bodies, as TazUO's AnimationsLoader reads them (format checks need no install).</summary>
public sealed class AnimationUopFormatTests
{
    // One frame's pixel block: a palette (colour 1 = pure red, 0 = transparent), then the sprite.
    internal static byte[] Sprite(short centerX, short centerY, short width, short height, params (int X, int Y, byte[] Pixels)[] runs)
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
    internal static byte[] Entry(IList<(int FrameId, byte[] Pixels)> frames)
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

    internal static byte[] Sequence(int body, int count, params (int Old, uint Frames, int New)[] replaces)
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

/// <summary>AnimationLibrary over synthetic AnimationFrame*.uop packages and mobtypes.txt in a temp folder.</summary>
public sealed class AnimationUopLibraryTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "uoclient-animuop-" + Guid.NewGuid().ToString("N"));

    public AnimationUopLibraryTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, true);

    /// <summary>A minimal MYP package: one block, uncompressed entries stored under their name hashes.</summary>
    private void Package(string name, params (string Entry, byte[] Data)[] files)
    {
        const int headerSize = 28, blockHeader = 12, record = 34;
        int dataStart = headerSize + blockHeader + files.Length * record;
        var buf = new byte[dataStart + files.Sum(f => f.Data.Length)];
        BinaryPrimitives.WriteUInt32LittleEndian(buf, 0x0050594D);
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(4), 5);
        BinaryPrimitives.WriteInt64LittleEndian(buf.AsSpan(12), headerSize);
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(20), (uint)files.Length);
        BinaryPrimitives.WriteInt32LittleEndian(buf.AsSpan(24), files.Length);
        BinaryPrimitives.WriteInt32LittleEndian(buf.AsSpan(headerSize), files.Length); // next block 0
        long pos = dataStart;
        for (int i = 0; i < files.Length; i++)
        {
            Span<byte> r = buf.AsSpan(headerSize + blockHeader + i * record, record);
            BinaryPrimitives.WriteInt64LittleEndian(r, pos);
            BinaryPrimitives.WriteInt32LittleEndian(r.Slice(12), files[i].Data.Length);
            BinaryPrimitives.WriteInt32LittleEndian(r.Slice(16), files[i].Data.Length);
            BinaryPrimitives.WriteUInt64LittleEndian(r.Slice(20), UopHash.Create(files[i].Entry));
            files[i].Data.CopyTo(buf, (int)pos);
            pos += files[i].Data.Length;
        }
        File.WriteAllBytes(Path.Combine(_dir, name), buf);
    }

    /// <summary>An action entry whose five directions each hold one frame of the given width, to tell packages apart.</summary>
    private static (string, byte[]) Action(int body, int action, short width) =>
        (AnimationUop.EntryName(body, action), AnimationUopFormatTests.Entry(Enumerable.Range(1, 5)
            .Select(id => (id, AnimationUopFormatTests.Sprite(0, -1, width, 1, (0, 0, Enumerable.Repeat((byte)1, width).ToArray()))))
            .ToList()));

    private AnimationLibrary Library(params string[] mobTypes)
    {
        File.WriteAllLines(Path.Combine(_dir, "mobtypes.txt"), mobTypes);
        return new AnimationLibrary(_dir);
    }

    private static int Width(AnimationLibrary lib, int body, int action) =>
        lib.Read(body, action, 0) is { Length: > 0 } f ? f[0].Image.Width : 0;

    [Fact]
    public void ActionsComeFromThePackageOfTheLastActionFound()
    {
        // 100: actions 0 and 25 in package 1, 22 and 25 in package 2. The last action, 25, is found first in package 1.
        // 101: action 0 in both, 25 only in package 2, so package 2 is recorded and 0 is read from it.
        Package("AnimationFrame1.uop", Action(100, 0, 1), Action(100, 25, 1), Action(101, 0, 1));
        Package("AnimationFrame2.uop", Action(100, 22, 2), Action(100, 25, 3), Action(101, 0, 2), Action(101, 25, 3));
        using var lib = Library("100\tMONSTER\t10000", "101\tMONSTER\t10000");

        Assert.Equal(0, lib.UopPackageOf(100));
        Assert.Equal(1, Width(lib, 100, 25));
        Assert.Equal(1, Width(lib, 100, 0));
        Assert.Empty(lib.Read(100, 22, 0)); // only in package 2: not read (TazUO would read package 2's offset from package 1)
        Assert.False(lib.HasUopAction(100, 22));

        Assert.Equal(1, lib.UopPackageOf(101));
        Assert.Equal(2, Width(lib, 101, 0)); // package 1 also has it, and comes first, but is not the recorded package
        Assert.Equal(3, Width(lib, 101, 25));
    }

    [Fact]
    public void PackageSearchFollowsTheSequenceSwap()
    {
        // 110: action 40 in package 1, 5 in package 2; AnimationSequence.uop files action 70 under 5.
        // Unswapped, 40 would be last (package 1); swapped, 70 -> 5 is last (package 2).
        Package("AnimationFrame1.uop", Action(110, 40, 1));
        Package("AnimationFrame2.uop", Action(110, 5, 2));
        Package("AnimationSequence.uop", ("build/animationsequence/00000110.bin", AnimationUopFormatTests.Sequence(110, 1, (70, 0, 5))));
        using var lib = Library("110\tMONSTER\t10000");

        Assert.Equal(1, lib.UopPackageOf(110));
        Assert.Equal(2, Width(lib, 110, 5));
        Assert.Empty(lib.Read(110, 40, 0));
    }

    [Fact]
    public void FlaggedBodiesWithNoEntriesAreNotUop()
    {
        Package("AnimationFrame1.uop", Action(100, 0, 1));
        using var lib = Library("100\tMONSTER\t10000", "120\tMONSTER\t10000", "121\tMONSTER\t0");
        Assert.True(lib.UsesUop(100));
        Assert.False(lib.UsesUop(120));
        Assert.Equal(-1, lib.UopPackageOf(120));
        Assert.Equal(-1, lib.UopPackageOf(121)); // not flagged
    }

    [Fact]
    public void WarModeActionsFollowMobileAnimation()
    {
        // 130 monster; 131 animal with war walk/stand 0/1; 132 animal without them; 133 monster idling at 8; 134 low group.
        Package("AnimationFrame1.uop",
            Action(130, 22, 1), Action(130, 24, 1), Action(130, 25, 1),
            Action(131, 0, 1), Action(131, 1, 1), Action(131, 22, 1), Action(131, 24, 1), Action(131, 25, 1),
            Action(132, 22, 1), Action(132, 24, 1), Action(132, 25, 1),
            Action(133, 8, 1), Action(133, 22, 1),
            Action(134, 0, 1));
        using var lib = Library("130 MONSTER 10000", "131 ANIMAL 10000", "132 ANIMAL 10000", "133 MONSTER 10004", "134 MONSTER 10040");

        // CalculateHight (MobileAnimation.cs:150-195): war walks 0 and stands 1 with no existence check; run stays 24.
        Assert.Equal((22, 24, 25), lib.Actions(130));
        Assert.Equal((0, 24, 1), lib.Actions(130, warMode: true));
        // Animal branch (:1041-1107): 0 and 1 only if the body has them.
        Assert.Equal((22, 24, 25), lib.Actions(131));
        Assert.Equal((0, 24, 1), lib.Actions(131, warMode: true));
        Assert.Equal((22, 24, 25), lib.Actions(132, warMode: true));
        // IdleAt8Frame wins in both modes; the low group ignores war mode.
        Assert.Equal((0, 24, 8), lib.Actions(133, warMode: true));
        Assert.Equal((0, 1, 2), lib.Actions(134, warMode: true));
    }

    [Fact]
    public void DeathActionsFollowGetDeathAction()
    {
        using var lib = Library(
            "140 MONSTER 10000", "141 ANIMAL 10000", "142 ANIMAL 10002", "143 ANIMAL 0",
            "144 SEA_MONSTER 0", "145 HUMAN 10000", "146 ANIMAL 40", "147 ANIMAL 20", "148 ANIMAL 8");
        Assert.Equal((2, 3), (lib.DeathAction(140, false), lib.DeathAction(140, true)));
        Assert.Equal((2, 3), (lib.DeathAction(141, false), lib.DeathAction(141, true)));
        Assert.Equal((2, 2), (lib.DeathAction(142, false), lib.DeathAction(142, true)));   // Use2IfHittedWhileRunning
        Assert.Equal((2, 2), (lib.DeathAction(148, false), lib.DeathAction(148, true)));   // CanFlying
        Assert.Equal((8, 12), (lib.DeathAction(143, false), lib.DeathAction(143, true)));  // MUL animal
        Assert.Equal(8, lib.DeathAction(144, true));
        Assert.Equal(3, lib.DeathAction(144, true, running: true));
        Assert.Equal((21, 22), (lib.DeathAction(145, false), lib.DeathAction(145, true)));
        Assert.Equal(12, lib.DeathAction(146, true));                                     // low group: animal
        Assert.Equal(3, lib.DeathAction(147, true));                                      // extended: monster
        Assert.Equal(22, lib.DeathAction(450, true));                                     // unlisted: by layout (human range)
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
        // War mode: walk 0, stand 1 (MobileAnimation.cs:148-160, 186-193), and both have frames here.
        Assert.Equal((0, 24, 1), Anims.Actions(753, warMode: true));
        Assert.True(Anims.HasUopAction(753, 0) && Anims.HasUopAction(753, 1));
        Assert.Equal((2, 3), (Anims.DeathAction(753, false), Anims.DeathAction(753, true)));
        Assert.True(Anims.HasUopAction(753, 2) && Anims.HasUopAction(753, 3));
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

    [Fact]
    public void NoInstalledBodyIsSplitAcrossPackages()
    {
        // With the recorded package, an action stored only in another package would be lost (and garbage in TazUO).
        var packages = Enumerable.Range(1, 4)
            .Select(i => Path.Combine(_data.Directory, $"AnimationFrame{i}.uop"))
            .Select(p => File.Exists(p) ? new UopFile(p) : null)
            .ToArray();
        try
        {
            var split = new List<string>();
            int checkedBodies = 0;
            for (int body = 0; body < 0x1000; body++)
            {
                int recorded = Anims.UopPackageOf(body);
                if (recorded < 0)
                    continue;
                checkedBodies++;
                for (int a = 0; a < AnimationUop.MaxActions; a++)
                {
                    ulong hash = UopHash.Create(AnimationUop.EntryName(body, a));
                    if (packages[recorded]!.TryGetByHash(hash, out _))
                        continue;
                    int other = Array.FindIndex(packages, p => p != null && p.TryGetByHash(hash, out _));
                    if (other >= 0)
                        split.Add($"{body}/{a}: in {other + 1}, recorded {recorded + 1}");
                }
            }
            Assert.True(checkedBodies > 100, $"{checkedBodies} bodies");
            Assert.True(split.Count == 0, string.Join("; ", split.Take(20)) + $" ({split.Count} actions)");
        }
        finally
        {
            foreach (UopFile? p in packages)
                p?.Dispose();
        }
    }
}
