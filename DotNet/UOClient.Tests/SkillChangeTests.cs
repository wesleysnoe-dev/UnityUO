using UOClient.Core;
using UOClient.IO.Formats;
using UOClient.Net;

namespace UOClient.Tests;

/// <summary>The skill change line TazUO's UpdateSkills prints for a single 0x3A update.</summary>
public sealed class SkillChangeTests
{
    private static GameSession InWorld()
    {
        var session = new GameSession(new LoginSettings());
        session.Inject(new PacketWriter(0x1B, 37).UInt32(0x42).UInt32(0).UInt16(0x190).UInt16(1000).UInt16(1000).UInt16(0)
            .Byte(0).Zero(9).UInt16(7168).UInt16(4096).Zero(6).ToArray(), false);
        return session;
    }

    // ModernUO's SendSkillsUpdate (type 0x02): 1-based id, value, base, lock, cap, ..., a 0 id to end.
    private static byte[] FullList(params (int id, int value)[] skills)
    {
        var w = new PacketWriter(0x3A).Byte(0x02);
        foreach (var (id, value) in skills)
            w.UInt16(id + 1).UInt16(value).UInt16(value).Byte(0).UInt16(1000);
        return w.UInt16(0).ToArray();
    }

    // ModernUO's SendSkillChange (OutgoingPlayerPackets): 0xDF, id, value, base, lock, cap.
    private static byte[] Change(int id, int value, byte lockState = 0)
    {
        byte[] p = new PacketWriter(0x3A).Byte(0xDF).UInt16(id).UInt16(value).UInt16(value).Byte(lockState).UInt16(1000).ToArray();
        Assert.Equal(13, p.Length);
        return p;
    }

    [Fact]
    public void AGainPrintsTazUOsSystemLine()
    {
        GameSession session = InWorld();
        session.Inject(FullList((25, 500), (17, 803)), true);
        var lines = new List<JournalEntry>();
        session.World.JournalAdded += lines.Add;

        session.Inject(Change(25, 501), true);

        JournalEntry e = Assert.Single(lines);
        Assert.Equal("Your skill in Magery has increased by 0.1.  It is now 50.1.", e.Text);
        Assert.Equal(GameSession.SkillChangeHue, e.Hue);
        Assert.Equal(0x58, e.Hue);
        Assert.Equal(1, e.Type);
        Assert.Equal(0xFFFFFFFFu, e.Serial);
        Assert.Null(e.Speaker);
        Assert.Equal(50.1, session.Skills[25].Value, 3);
    }

    [Fact]
    public void ALossSaysDecreased()
    {
        GameSession session = InWorld();
        session.Inject(FullList((17, 803)), true);
        var lines = new List<string>();
        session.World.JournalAdded += e => lines.Add(e.Text);

        session.Inject(Change(17, 790), true);

        Assert.Equal(new[] { "Your skill in Healing has decreased by 1.3.  It is now 79.0." }, lines);
    }

    [Fact]
    public void NoLineWithoutAChangeOrBeforeTheList()
    {
        GameSession session = InWorld();
        var lines = new List<string>();
        session.World.JournalAdded += e => lines.Add(e.Text);

        session.Inject(Change(25, 501), true);     // no list yet: nothing to compare with
        session.Inject(FullList((25, 500)), true);  // the full list itself prints nothing
        session.Inject(Change(25, 500, 2), true);  // a lock change echoes the same value

        Assert.Empty(lines);
        Assert.Equal(2, session.Skills[25].Lock);
    }
}

/// <summary>The skills window's default groups (TazUO SkillsGroupManager.MakeDefault / LoadMULFile).</summary>
public sealed class SkillGroupTests
{
    private static byte[] Mul(string[] names, int[] groupOfSkill)
    {
        var bytes = new List<byte>(BitConverter.GetBytes(names.Length + 1));
        foreach (string n in names)
        {
            var field = new byte[17];
            System.Text.Encoding.ASCII.GetBytes(n).CopyTo(field, 0);
            bytes.AddRange(field);
        }
        foreach (int g in groupOfSkill)
            bytes.AddRange(BitConverter.GetBytes(g));
        return bytes.ToArray();
    }

    [Fact]
    public void ParsesNamesAndOneGroupPerSkill()
    {
        List<SkillGroup> groups = SkillGroups.Parse(Mul(new[] { "Combat", "Magic" }, new[] { 2, 1, 0, 1, 9, 2 }), 5)!;
        Assert.Equal(new[] { "Miscellaneous", "Combat", "Magic" }, groups.Select(g => g.Name));
        // Skill 4 names group 9 (no such group): skipped without using up an id, as LoadMULFile does; the sixth
        // entry then gives skill 4 to Magic.
        Assert.Equal(new[] { 2 }, groups[0].Skills);
        Assert.Equal(new[] { 1, 3 }, groups[1].Skills);
        Assert.Equal(new[] { 0, 4 }, groups[2].Skills);
    }

    [Fact]
    public void MalformedFilesFallBack()
    {
        Assert.Null(SkillGroups.Parse(new byte[] { 1, 2 }, 58));
        Assert.Null(SkillGroups.Parse(Mul(new[] { "Combat" }, new[] { -1 }), 58));
        Assert.Null(SkillGroups.Parse(BitConverter.GetBytes(40), 58)); // names run past the end
    }

    [Fact]
    public void BuiltInGroupsAreTazUOsSeven()
    {
        List<SkillGroup> groups = SkillGroups.BuiltIn(58);
        Assert.Equal(new[] { "Miscellaneous", "Combat", "Trade Skills", "Magic", "Wilderness", "Thieving", "Bard" }, groups.Select(g => g.Name));
        Assert.Equal(58, groups.Sum(g => g.Skills.Count));
        Assert.Equal(Enumerable.Range(0, 58), groups.SelectMany(g => g.Skills).OrderBy(i => i));
        Assert.DoesNotContain(57, SkillGroups.BuiltIn(57).SelectMany(g => g.Skills)); // Throwing only when the client has it
    }
}

[Collection("UO install")]
public sealed class SkillGroupFileTests
{
    private readonly UOClient.IO.UOData _data;

    public SkillGroupFileTests(UOInstallFixture fixture) => _data = fixture.Data;

    [Fact]
    public void TheClientsSkillgrpMulGroupsEverySkill()
    {
        List<SkillGroup> groups = _data.GetSkillGroups(GameSession.SkillNames.Length);
        Assert.Equal("Miscellaneous", groups[0].Name);
        Assert.Equal(Enumerable.Range(0, GameSession.SkillNames.Length), groups.SelectMany(g => g.Skills).OrderBy(i => i));
        if (File.Exists(Path.Combine(_data.Directory, "skillgrp.mul")))
        {
            Assert.Equal(new[] { "Miscellaneous", "Combat", "Trade Skills", "Magic", "Wilderness", "Thieving", "Bard" }, groups.Select(g => g.Name));
            Assert.Contains(25, groups.Single(g => g.Name == "Magic").Skills);         // Magery
            Assert.Contains(0, groups.Single(g => g.Name == "Trade Skills").Skills);   // Alchemy
        }
    }
}
