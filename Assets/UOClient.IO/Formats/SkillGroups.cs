using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;

namespace UOClient.IO.Formats
{
    /// <summary>A named group of skills in the skills window.</summary>
    public sealed class SkillGroup
    {
        public string Name;
        public readonly List<int> Skills = new List<int>();
    }

    /// <summary>
    /// The skills window's default groups, as TazUO's SkillsGroupManager.MakeDefault builds them: from the client's
    /// skillgrp.mul (LoadMULFile), else its built-in seven groups. Read only: TazUO then saves the groups to the profile's
    /// skillsgroups.xml, and edits them from the gump; neither is done here.
    /// </summary>
    public static class SkillGroups
    {
        /// <summary>The first group, which skillgrp.mul does not name (TazUO's language.ini "miscellaneous").</summary>
        public const string FirstGroupName = "Miscellaneous";

        /// <summary>
        /// skillgrp.mul: int32 group count (-1 first means unicode names and the count follows), count - 1 names of 17
        /// bytes (34 in unicode) from offset 4 (8), then one int32 group per skill in skill order until the end. Skills past
        /// <paramref name="skillCount"/> or naming a group past the count are skipped, as LoadMULFile does. Null if the file
        /// is malformed (TazUO then falls back to <see cref="BuiltIn"/>).
        /// </summary>
        public static List<SkillGroup> Parse(byte[] data, int skillCount)
        {
            if (data == null || data.Length < 4)
                return null;
            int start = 4, strlen = 17;
            int count = BinaryPrimitives.ReadInt32LittleEndian(data);
            bool unicode = count == -1;
            if (unicode)
            {
                if (data.Length < 8)
                    return null;
                count = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(4));
                start *= 2;
                strlen *= 2;
            }
            if (count <= 0 || start + (long)(count - 1) * strlen > data.Length)
                return null;

            var groups = new SkillGroup[count];
            groups[0] = new SkillGroup { Name = FirstGroupName };
            var sb = new StringBuilder(17);
            for (int i = 0; i < count - 1; i++)
            {
                int p = start + i * strlen;
                if (unicode)
                    for (; p + 1 < data.Length; p += 2)
                    {
                        char c = (char)BinaryPrimitives.ReadInt16LittleEndian(data.AsSpan(p));
                        if (c == 0)
                            break;
                        sb.Append(c);
                    }
                else
                    for (; p < data.Length && data[p] != 0; p++)
                        sb.Append((char)data[p]);
                groups[i + 1] = new SkillGroup { Name = sb.ToString() };
                sb.Clear();
            }

            int skill = 0;
            for (int p = start + (count - 1) * strlen; p + 4 <= data.Length; p += 4)
            {
                int grp = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(p));
                if (grp < 0)
                    return null; // TazUO's groups[grp] throws and it falls back
                if (grp < groups.Length && skill < skillCount)
                    groups[grp].Skills.Add(skill++);
            }
            return new List<SkillGroup>(groups);
        }

        /// <summary>
        /// TazUO's built-in groups when the client has no skillgrp.mul (MakeDefaultMiscellaneous .. MakeDefaultBard), the
        /// later skills only when the client knows them.
        /// </summary>
        public static List<SkillGroup> BuiltIn(int skillCount)
        {
            SkillGroup G(string name, params int[] ids)
            {
                var g = new SkillGroup { Name = name };
                foreach (int id in ids)
                    if (id < skillCount && !g.Skills.Contains(id))
                        g.Skills.Add(id);
                return g;
            }
            return new List<SkillGroup>
            {
                G(FirstGroupName, 4, 6, 10, 12, 19, 3, 36),
                G("Combat", 1, 31, 42, 17, 41, 5, 40, 27, 57, 43, 50, 51, 52, 53),
                G("Trade Skills", 0, 7, 8, 11, 13, 23, 44, 45, 34, 37),
                G("Magic", 16, 56, 25, 46, 55, 26, 54, 32, 49),
                G("Wilderness", 2, 35, 18, 20, 38, 39),
                G("Thieving", 14, 21, 24, 30, 48, 28, 33, 47),
                G("Bard", 15, 29, 9, 22)
            };
        }
    }
}
