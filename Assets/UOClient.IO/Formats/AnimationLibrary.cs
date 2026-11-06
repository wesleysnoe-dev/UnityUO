using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UOClient.IO.Files;

namespace UOClient.IO.Formats
{
    /// <summary>How a body's actions are laid out: monsters have 22 actions, animals 13, humans (and equipment) 35.</summary>
    public enum AnimGroup
    {
        Monster,
        Animal,
        Human
    }

    /// <summary>mobtypes.txt's type column (TazUO's <c>AnimationGroupsType</c> order).</summary>
    public enum MobType
    {
        Monster,
        SeaMonster,
        Animal,
        Human,
        Equipment
    }

    /// <summary>
    /// All classic mobile animations: anim.mul plus the expansion files anim2..anim6. Bodyconv.def moves a
    /// body into one of the expansion files (often under another number), and each file lays bodies out
    /// differently. Body.def names a stand-in body (and hue) for bodies with no art at all.
    /// Rules follow ClassicUO's MUL animation loader (BSD-2-Clause).
    /// Bodies whose mobtypes.txt flags have <see cref="UseUopAnimation"/> come from AnimationFrame1..4.uop instead,
    /// with no Bodyconv.def or Body.def, as in TazUO's <c>AnimationsLoader.GetIndices</c> (AnimationsLoader.cs:314-349).
    /// When those packages hold nothing for such a body, it falls back to the MUL rules above (TazUO would draw nothing).
    /// </summary>
    public sealed class AnimationLibrary : IDisposable
    {
        // mobtypes.txt flags (TazUO AnimationFlags, AnimationsLoader.cs:1825-1849).
        public const uint Use2IfHitWhileRunning = 0x2, IdleAt8Frame = 0x4, CanFlying = 0x8, LowGroupExtended = 0x20, ByLowGroup = 0x40, ByPeopleGroup = 0x400;
        public const uint UseUopAnimation = 0x10000;

        private readonly string _dir;
        private readonly MulFile[] _files = new MulFile[6];   // anim, anim2 .. anim6
        private readonly bool[] _tried = new bool[6];
        private readonly UopFile[] _uop = new UopFile[4];    // AnimationFrame1..4.uop
        private bool _uopTried;
        private Dictionary<int, int[]> _sequences;          // AnimationSequence.uop: body -> drawn action per action
        private readonly Dictionary<int, int> _uopPackage = new Dictionary<int, int>(); // body -> recorded AnimationFrame index, or -1
        private readonly Dictionary<int, (int File, int Body)> _conv = new Dictionary<int, (int, int)>();
        private readonly Dictionary<int, (int Body, int Hue)> _replace = new Dictionary<int, (int, int)>();
        private readonly Dictionary<int, (MobType Type, uint Flags)> _mobTypes = new Dictionary<int, (MobType, uint)>();
        private readonly object _lock = new object();

        public AnimationLibrary(string directory)
        {
            _dir = directory;
            ReadBodyConv(Path.Combine(directory, "Bodyconv.def"));
            ReadBodyDef(Path.Combine(directory, "Body.def"));
            ReadMobTypes(Path.Combine(directory, "mobtypes.txt"));
        }

        /// <summary>mobtypes.txt's type and flags for a body, if it lists it.</summary>
        public bool TryGetMobType(int body, out MobType type, out uint flags)
        {
            bool found = _mobTypes.TryGetValue(body, out var m);
            type = m.Type;
            flags = m.Flags;
            return found;
        }

        /// <summary>True when the body is drawn from AnimationFrame*.uop: flagged in mobtypes.txt and present there.</summary>
        public bool UsesUop(int body)
        {
            if (!_mobTypes.TryGetValue(body, out var m) || (m.Flags & UseUopAnimation) == 0)
                return false;
            lock (_lock)
                return UopPackage(body) >= 0;
        }

        /// <summary>
        /// The AnimationFrame*.uop (0..3) a UOP body's actions are read from, or -1. TazUO's <c>GetIndices</c>
        /// (AnimationsLoader.cs:321-345) searches the packages per action, but keeps only the package of the last
        /// action it found, and <c>Animation.cs:224</c> reads every action from that one package.
        /// </summary>
        public int UopPackageOf(int body)
        {
            if (!_mobTypes.TryGetValue(body, out var m) || (m.Flags & UseUopAnimation) == 0)
                return -1;
            lock (_lock)
                return UopPackage(body);
        }

        /// <summary>The action a UOP body really draws for <paramref name="action"/> (AnimationSequence.uop), or -1.</summary>
        public int UopAction(int body, int action)
        {
            if ((uint)action >= AnimationUop.MaxActions)
                return -1;
            lock (_lock)
            {
                LoadUop();
                // TazUO swaps twice: GetIndices files action a under the entry for seq[a] (AnimationsLoader.cs:324),
                // then GetAnimationFrames looks up slot seq[a] (Animation.cs:286-305), so a is drawn as seq[seq[a]].
                if (_sequences.TryGetValue(body, out int[] seq))
                {
                    int slot = seq[action];
                    if ((uint)slot >= AnimationUop.MaxActions)
                        return -1;
                    action = seq[slot];
                }
                return (uint)action < AnimationUop.MaxActions ? action : -1;
            }
        }

        /// <summary>Whether a UOP body has frames stored for an action (after AnimationSequence.uop).</summary>
        public bool HasUopAction(int body, int action)
        {
            int real = UopAction(body, action);
            lock (_lock)
                return real >= 0 && TryUopEntry(body, real, out _, out _);
        }

        /// <summary>Where a body's animation actually lives: (file 0..5, body in that file, replacement hue or 0).</summary>
        public (int File, int Body, int Hue) Resolve(int body) => Resolve(body, 0);

        /// <summary>Walk, run and stand outside war mode, as TazUO's <c>MobileAnimation.GetGroupForAnimation</c> picks them.</summary>
        public (int Walk, int Run, int Stand) Actions(int body) => Actions(body, false);

        /// <summary>
        /// Walk, run and stand in or out of war mode, as TazUO's <c>MobileAnimation.GetGroupForAnimation</c> picks them.
        /// War mode only changes UOP monsters and animals; other bodies (UOP humans included) keep their peace actions here.
        /// </summary>
        public (int Walk, int Run, int Stand) Actions(int body, bool warMode)
        {
            if (UsesUop(body) && _mobTypes.TryGetValue(body, out var m))
            {
                // MobileAnimation.cs:1041-1107: animals without the extended flag.
                if (m.Type == MobType.Animal && (m.Flags & LowGroupExtended) == 0)
                {
                    if (!warMode)
                        return (22, 24, 25);
                    return (HasUopAction(body, 0) ? 0 : 22, 24, HasUopAction(body, 1) ? 1 : 25);
                }
                // CalculateHight (:99-197) for monsters and extended animals; the flying group 19 needs a flying mobile.
                if (m.Type == MobType.Monster || m.Type == MobType.Animal)
                {
                    if ((m.Flags & ByPeopleGroup) != 0)
                        return (0, 0, 0); // :106-111 only sets idle to 0; moving keeps the current group
                    if ((m.Flags & ByLowGroup) != 0)
                        return (0, 1, 2);
                    int stand = (m.Flags & IdleAt8Frame) != 0 && HasUopAction(body, 8) ? 8 : warMode ? 1 : 25;
                    int run = (m.Flags & CanFlying) != 0 && HasUopAction(body, 19) ? 19 : 24;
                    return (warMode ? 0 : 22, run, stand);
                }
            }
            return DefaultActions(GroupOf(body));
        }

        /// <summary>
        /// The death action (first or second fall) as TazUO's <c>AnimationsLoader.GetDeathAction</c> picks it
        /// (AnimationsLoader.cs:1236-1304). <paramref name="running"/> matters only for sea monsters.
        /// The type comes from mobtypes.txt, or from the body's action layout when it is not listed.
        /// </summary>
        public int DeathAction(int body, bool second, bool running = false)
        {
            MobType type;
            uint flags = 0;
            if (!_mobTypes.TryGetValue(body, out var m))
            {
                AnimGroup g = GroupOf(body);
                type = g == AnimGroup.Animal ? MobType.Animal : g == AnimGroup.Human ? MobType.Human : MobType.Monster;
            }
            else
            {
                type = m.Type;
                flags = m.Flags;
            }

            if ((flags & ByLowGroup) != 0)
                type = MobType.Animal;
            if ((flags & LowGroupExtended) != 0)
                type = MobType.Monster;
            bool uop = (flags & UseUopAnimation) != 0;

            switch (type)
            {
                case MobType.Animal:
                    if ((flags & (Use2IfHitWhileRunning | CanFlying)) != 0)
                        return 2;
                    return uop ? (second ? 3 : 2) : second ? 12 : 8;   // LowAnimationGroup.Die2 / Die1
                case MobType.SeaMonster:
                    if (!running)
                        return 8;
                    goto case MobType.Monster;
                case MobType.Monster:
                    return second ? 3 : 2;                              // UOP and HighAnimationGroup.Die2 / Die1 agree
                default:
                    return second ? 22 : 21;                            // PeopleAnimationGroup.Die2 / Die1
            }
        }

        // MobileAnimation.cs:14-36: two-handed weapon animations that stand with action 8 in war mode.
        private static readonly int[] HandsBaseAnimIds =
        {
            0x0263, 0x0264, 0x0265, 0x0266, 0x0267, 0x0268, 0x0269, 0x026D, 0x0270, 0x0272,
            0x0274, 0x027A, 0x027C, 0x027F, 0x0281, 0x0286, 0x0288, 0x0289, 0x028B
        };

        /// <summary>
        /// Walk, run and stand of an unmounted, living MUL human (no UseUopAnimation flag, not a gargoyle) by what it holds,
        /// as TazUO's People branch of <c>MobileAnimation.GetGroupForAnimation</c> picks them (MobileAnimation.cs:1131-1433):
        /// anything in the two-handed layer walks and runs armed (1/3); war stands 7, or 8 with only a two-handed weapon
        /// of <c>HANDS_BASE_ANIMID</c>, and walks 15. <paramref name="twoHandedAnim"/> is that item's tiledata AnimId.
        /// The light-in-hand stands (:1139-1142) need an AnimId equal to the body, which no human item has.
        /// </summary>
        public static (int Walk, int Run, int Stand) PeopleActions(bool warMode, bool oneHanded, bool twoHanded, int twoHandedAnim)
        {
            int run = twoHanded ? 3 : 2;                          // :1352-1354, :1362-1389
            if (!warMode)
                return (twoHanded ? 1 : 0, run, 4);               // :1356-1358, :1185
            bool twoHandStand = !oneHanded && twoHanded && Array.IndexOf(HandsBaseAnimIds, twoHandedAnim) >= 0;
            return (15, run, twoHandStand ? 8 : 7);               // :1432; :1212-1268
        }

        /// <summary>Whether <see cref="PeopleActions"/> applies: a human layout without UOP actions, and not a gargoyle (Mobile.cs:1142-1144).</summary>
        public bool UsesPeopleActions(int body) =>
            GroupOf(body) == AnimGroup.Human && body != 666 && body != 667 && body != 0x02B6 && body != 0x02B7
            && !(_mobTypes.TryGetValue(body, out var m) && (m.Flags & UseUopAnimation) != 0);

        /// <summary>Walk, run and stand of a classic action layout.</summary>
        public static (int Walk, int Run, int Stand) DefaultActions(AnimGroup group)
        {
            switch (group)
            {
                case AnimGroup.Human: return (0, 2, 4);
                case AnimGroup.Animal: return (0, 1, 2);
                default: return (0, 0, 1);
            }
        }

        private (int File, int Body, int Hue) Resolve(int body, int depth)
        {
            if (UsesUop(body))
                return (-1, body, 0);
            (int file, int real) = _conv.TryGetValue(body, out var c) && File(c.File) != null ? c : (0, body);
            if (HasAny(file, real) || depth >= 3 || !_replace.TryGetValue(body, out var rep) || rep.Body == body)
                return (file, real, 0);
            // Body.def: draw another body instead (with the def's hue, if it gives one).
            var (f2, r2, h2) = Resolve(rep.Body, depth + 1);
            return (f2, r2, rep.Hue != 0 ? rep.Hue : h2);
        }

        /// <summary>The action layout of a body (after conversion).</summary>
        public AnimGroup GroupOf(int body)
        {
            var (file, real, _) = Resolve(body);
            if (file < 0)
                return GroupOf(_mobTypes[real].Type);
            return Layout(file, real).Group;
        }

        private static AnimGroup GroupOf(MobType type) =>
            type == MobType.Animal ? AnimGroup.Animal
            : type == MobType.Human || type == MobType.Equipment ? AnimGroup.Human
            : AnimGroup.Monster;

        /// <summary>Frames for (body, action, stored direction 0..4); empty when there is no art.</summary>
        public AnimationFrame[] Read(int body, int action, int fileDirection)
        {
            var (file, real, _) = Resolve(body);
            if (file < 0)
                return ReadUop(real, action, fileDirection);
            byte[] data = ReadRaw(file, real, action, fileDirection);
            if (data == null || data.Length < 512 + 4)
                return Array.Empty<AnimationFrame>();
            return AnimationFile.Decode(data);
        }

        /// <summary>
        /// The creature body a mount item (worn on layer 25) draws as, taken from ModernUO's mounts
        /// (body, item id pairs). 0 when the item is not a known mount.
        /// </summary>
        public static int MountBody(int itemGraphic) => Mounts.TryGetValue(itemGraphic, out int body) ? body : 0;

        private static readonly Dictionary<int, int> Mounts = new Dictionary<int, int>
        {
            [0x3E9F] = 0xC8, [0x3EA0] = 0xE2, [0x3EA1] = 0xE4, [0x3EA2] = 0xCC,   // horses
            [0x3EA3] = 0xD2, [0x3EA4] = 0xDA, [0x3EA5] = 0xDB,                   // ostards
            [0x3EA6] = 0xDC,                                                     // llama
            [0x3EA7] = 0x74, [0x3EA8] = 0x75, [0x3E9E] = 0xBE,                   // nightmare, silver steed, fire steed
            [0x3EAF] = 0x78, [0x3EB0] = 0x79, [0x3EB1] = 0x77, [0x3EB2] = 0x76,   // faction war horses
            [0x3EAD] = 0x84, [0x3EB4] = 0x7A,                                    // ki-rin, unicorn
            [0x3EB3] = 0x90,                                                     // sea horse
            [0x3EB8] = 0xBC, [0x3EBA] = 0xBB,                                    // ridgebacks
            [0x3EBB] = 0x319,                                                    // skeletal mount, hell steed
            [0x3EBC] = 0x317, [0x3E95] = 0xA9,                                   // beetle, fire beetle
            [0x3EBD] = 0x31A, [0x3EBE] = 0x31F,                                  // swamp dragons
            [0x3E94] = 0xF3, [0x3E91] = 0x115, [0x3E90] = 0x114,                 // hiryu, cu sidhe, reptalon
            // Ethereals ride the same bodies.
            [0x3EAA] = 0xE2, [0x3EAB] = 0xDC, [0x3EAC] = 0xDB, [0x3E9A] = 0xBB, [0x3E9B] = 0x7A,
            [0x3E97] = 0x317, [0x3E9C] = 0x84, [0x3E98] = 0x31A, [0x3EC5] = 0xD5
        };

        /// <summary>A stand-in hue from Body.def for bodies drawn as another body, or 0.</summary>
        public int ReplacementHue(int body) => Resolve(body).Hue;

        private bool HasAny(int file, int real)
        {
            MulFile f = File(file);
            if (f == null)
                return false;
            var (start, _) = Layout(file, real);
            // Stand (or walk) facing down is present for every real body.
            for (int i = 0; i < 15; i++)
                if (f.TryGetEntry(start + i, out _))
                    return true;
            return false;
        }

        private byte[] ReadRaw(int file, int real, int action, int fileDirection)
        {
            MulFile f = File(file);
            if (f == null)
                return null;
            var (start, group) = Layout(file, real);
            int actions = group == AnimGroup.Monster ? 22 : group == AnimGroup.Animal ? 13 : 35;
            if ((uint)action >= (uint)actions)
                return null;
            int index = start + action * 5 + fileDirection;
            lock (_lock)
                return f.TryGetEntry(index, out FileEntry e) ? f.Read(e) : null;
        }

        private AnimationFrame[] ReadUop(int body, int action, int fileDirection)
        {
            int real = UopAction(body, action);
            byte[] data;
            lock (_lock)
            {
                if (real < 0 || !TryUopEntry(body, real, out UopFile file, out FileEntry entry))
                    return Array.Empty<AnimationFrame>();
                data = file.Read(entry);
            }
            bool equipment = _mobTypes.TryGetValue(body, out var m) && m.Type == MobType.Equipment;
            return AnimationUop.Decode(data, fileDirection, equipment);
        }

        /// <summary>
        /// (body, action) in the body's recorded package only (<see cref="UopPackageOf"/>). When the action is stored
        /// only in another package, TazUO reads that package's offset from the recorded file; we read nothing. Call under the lock.
        /// </summary>
        private bool TryUopEntry(int body, int action, out UopFile file, out FileEntry entry)
        {
            int package = UopPackage(body);
            file = package >= 0 ? _uop[package] : null;
            if (file != null && file.TryGetByHash(UopHash.Create(AnimationUop.EntryName(body, action)), out entry))
                return true;
            file = null;
            entry = FileEntry.Invalid;
            return false;
        }

        /// <summary>
        /// TazUO's <c>GetIndices</c> loop: for each action (after one AnimationSequence.uop swap), the first package
        /// holding it; the package found for the last such action is kept. Call under the lock.
        /// </summary>
        private int UopPackage(int body)
        {
            if (_uopPackage.TryGetValue(body, out int package))
                return package;
            LoadUop();
            package = -1;
            _sequences.TryGetValue(body, out int[] seq);
            for (int a = 0; a < AnimationUop.MaxActions; a++)
            {
                ulong hash = UopHash.Create(AnimationUop.EntryName(body, seq != null ? seq[a] : a));
                for (int i = 0; i < _uop.Length; i++)
                {
                    if (_uop[i] != null && _uop[i].TryGetByHash(hash, out _))
                    {
                        package = i;
                        break;
                    }
                }
            }
            _uopPackage[body] = package;
            return package;
        }

        /// <summary>Opens AnimationFrame1..4.uop and reads AnimationSequence.uop, once. Call under the lock.</summary>
        private void LoadUop()
        {
            if (_uopTried)
                return;
            _uopTried = true;
            _sequences = new Dictionary<int, int[]>();
            for (int i = 0; i < _uop.Length; i++)
            {
                string path = Path.Combine(_dir, $"AnimationFrame{i + 1}.uop");
                if (System.IO.File.Exists(path))
                    _uop[i] = new UopFile(path);
            }

            string seqPath = Path.Combine(_dir, "AnimationSequence.uop");
            if (!System.IO.File.Exists(seqPath))
                return;
            using (var seq = new UopFile(seqPath))
            {
                foreach (FileEntry e in seq.AllEntries)
                {
                    var parsed = AnimationUop.ParseSequence(seq.Read(e));
                    if (parsed.HasValue)
                        _sequences[parsed.Value.Body] = parsed.Value.Actions;
                }
            }
        }

        /// <summary>
        /// mobtypes.txt: "id type flags" with hex flags, as TazUO reads it (AnimationsLoader.cs:124-204):
        /// lines must start with a digit, and a '#' in the flags column ends it.
        /// </summary>
        private void ReadMobTypes(string path)
        {
            if (!System.IO.File.Exists(path))
                return;
            foreach (string raw in System.IO.File.ReadAllLines(path))
            {
                string line = raw.Trim();
                if (line.Length == 0 || !char.IsDigit(line[0]))
                    continue;
                string[] parts = line.Split(new[] { '\t', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 3 || !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int id))
                    continue;
                string flags = parts[2];
                int hash = flags.IndexOf('#');
                if (hash == 0)
                    continue;
                if (hash > 0)
                    flags = flags.Substring(0, hash).Trim();
                if (!uint.TryParse(flags, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint value))
                    continue;
                MobType? type = ParseMobType(parts[1]);
                if (type.HasValue)
                    _mobTypes[id] = (type.Value, value);
            }
        }

        private static MobType? ParseMobType(string s)
        {
            switch (s.ToLowerInvariant())
            {
                case "monster": return MobType.Monster;
                case "sea_monster": return MobType.SeaMonster;
                case "animal": return MobType.Animal;
                case "human": return MobType.Human;
                case "equipment": return MobType.Equipment;
                default: return null;
            }
        }

        /// <summary>The first index entry of a body in a file, and the layout that implies.</summary>
        private static (int Start, AnimGroup Group) Layout(int file, int body)
        {
            switch (file)
            {
                case 1: // anim2
                    return body < 200 ? (body * 110, AnimGroup.Monster) : (22000 + (body - 200) * 65, AnimGroup.Animal);
                case 2: // anim3
                    if (body < 300) return (body * 65, AnimGroup.Animal);
                    if (body < 400) return (33000 + (body - 300) * 110, AnimGroup.Monster);
                    return (35000 + (body - 400) * 175, AnimGroup.Human);
                case 4: // anim5 ("looks strange, but it works", says ClassicUO about body 34)
                    if (body < 200 && body != 34) return (body * 110, AnimGroup.Monster);
                    if (body < 400) return (22000 + (body - 200) * 65, AnimGroup.Animal);
                    return (35000 + (body - 400) * 175, AnimGroup.Human);
                default: // anim, anim4, anim6
                    if (body < 200) return (body * 110, AnimGroup.Monster);
                    if (body < 400) return (22000 + (body - 200) * 65, AnimGroup.Animal);
                    return (35000 + (body - 400) * 175, AnimGroup.Human);
            }
        }

        private MulFile File(int file)
        {
            lock (_lock)
            {
                if (_tried[file])
                    return _files[file];
                _tried[file] = true;
                string n = file == 0 ? "anim" : "anim" + (file + 1);
                string mul = Path.Combine(_dir, n + ".mul"), idx = Path.Combine(_dir, n + ".idx");
                if (System.IO.File.Exists(mul) && System.IO.File.Exists(idx))
                    _files[file] = new MulFile(mul, idx);
                return _files[file];
            }
        }

        /// <summary>Bodyconv.def: "body anim2 anim3 anim4 anim5 [anim6]", -1 where absent; the first present wins.</summary>
        private void ReadBodyConv(string path)
        {
            if (!System.IO.File.Exists(path))
                return;
            foreach (string raw in System.IO.File.ReadAllLines(path))
            {
                string line = StripComment(raw);
                string[] parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2 || !TryInt(parts[0], out int body))
                    continue;
                for (int col = 1; col < parts.Length && col <= 5; col++)
                {
                    if (TryInt(parts[col], out int real) && real >= 0)
                    {
                        _conv[body] = (col, real);
                        break;
                    }
                }
            }
        }

        /// <summary>Body.def: "body {new, new2} hue".</summary>
        private void ReadBodyDef(string path)
        {
            if (!System.IO.File.Exists(path))
                return;
            foreach (string raw in System.IO.File.ReadAllLines(path))
            {
                string line = StripComment(raw);
                int open = line.IndexOf('{'), close = line.IndexOf('}');
                if (open < 0 || close < open || !TryInt(line.Substring(0, open).Trim(), out int body))
                    continue;
                string[] choices = line.Substring(open + 1, close - open - 1).Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (choices.Length == 0 || !TryInt(choices[0], out int to))
                    continue;
                TryInt(line.Substring(close + 1).Trim(), out int hue);
                _replace[body] = (to, hue);
            }
        }

        private static string StripComment(string line)
        {
            int hash = line.IndexOf('#');
            return (hash >= 0 ? line.Substring(0, hash) : line).Trim();
        }

        private static bool TryInt(string s, out int value)
        {
            s = s.Trim();
            if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                return int.TryParse(s.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
            return int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
        }

        public void Dispose()
        {
            lock (_lock)
            {
                foreach (MulFile f in _files)
                    f?.Dispose();
                foreach (UopFile f in _uop)
                    f?.Dispose();
            }
        }
    }
}
