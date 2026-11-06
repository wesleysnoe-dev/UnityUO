using System;
using System.Collections.Generic;

namespace UOClient.IO.Formats
{
    /// <summary>
    /// What a body draws for an animation the server asks for (0x6E, 0xE2), as TazUO turns the packet's numbers into an
    /// action: <c>Mobile.GetReplacedObjectAnimation</c> (Anim1.def / Anim2.def), <c>Mobile.GetObjectNewAnimation</c>
    /// (MobileAnimation.cs:1509-2070) and the <c>AnimationFromServer</c> branch of <c>GetGroupForAnimation</c> with its
    /// <c>LABEL_190</c> / <c>LABEL_222</c> remaps (MobileAnimation.cs:199-520, 549-1026). No UnityEngine and no files:
    /// <see cref="AnimationLibrary"/> supplies a body's type, flags and which actions it has.
    /// </summary>
    public static class ServerAnimationGroups
    {
        /// <summary>The "no server animation" group (TazUO's <c>SetAnimation(0xFF)</c>): keep walking, running or standing.</summary>
        public const int None = 0xFF;

        // TazUO AnimationFlags (AnimationsLoader.cs:1825-1849) the remaps read.
        public const uint Unknown1 = 0x1, Use2IfHitWhileRunning = 0x2, IdleAt8Frame = 0x4, LowGroupExtended = 0x20, ByLowGroup = 0x40,
            Unknown80 = 0x80, Unknown100 = 0x100, Unknown200 = 0x200, ByPeopleGroup = 0x400, Unknown1000 = 0x1000,
            UseUop = 0x10000, Found = 0x80000000;

        // LowAnimationGroup / PeopleAnimationGroup / HighAnimationGroup .AnimationCount (AnimationsLoader.cs:1739-1822).
        public const int LowCount = 13, PeopleCount = 35, HighCount = 22;

        /// <summary>A body as the rules see it.</summary>
        public struct Body
        {
            /// <summary>The type of the body as sent (TazUO's <c>originalType</c>); null for TazUO's Unknown.</summary>
            public MobType? OriginalType;
            /// <summary>The type after Body.def (<c>ConvertBodyIfNeeded</c>).</summary>
            public MobType Type;
            /// <summary>mobtypes.txt flags, with <see cref="Found"/> when the body is listed there.</summary>
            public uint Flags;
            /// <summary>Whether the body has frames for an action (TazUO's <c>AnimationExists</c>).</summary>
            public Func<int, bool> Exists;
            public bool Mounted, WarMode, Gargoyle, Flying;
        }

        /// <summary>
        /// TazUO's <c>GetReplacedObjectAnimation</c> (MobileAnimation.cs:1458-1506): animals look the action up in Anim1.def,
        /// people in Anim2.def (a -1 replacement meaning the walk), each wrapped to its group's action count; monsters wrap to 22.
        /// </summary>
        public static int Replaced(MobType type, int index, IReadOnlyList<(int Group, int Replace)> low,
            IReadOnlyList<(int Group, int Replace)> people)
        {
            if (type == MobType.Animal)
                return Lookup(low, index, 0) % LowCount;
            if (type == MobType.Human || type == MobType.Equipment)
                return Lookup(people, index, 0) % PeopleCount;
            return index % HighCount;
        }

        private static int Lookup(IReadOnlyList<(int Group, int Replace)> list, int index, int walk)
        {
            if (list != null)
                foreach (var (group, replace) in list)
                    if (group == index)
                        return (replace & 0xFF) == 0xFF ? walk : replace & 0xFF;
            return index;
        }

        private static bool IsReplaced(IReadOnlyList<(int Group, int Replace)> list, int v13)
        {
            if (list != null)
                foreach (var (group, replace) in list)
                    if (group == v13)
                        return (replace & 0xFF) != 0xFF;
            return false;
        }

        /// <summary>
        /// The group the server's <paramref name="group"/> draws as on <paramref name="b"/>: the <c>AnimationFromServer</c>
        /// branch of TazUO's <c>GetGroupForAnimation</c>. <paramref name="coin"/> (0 or 1) stands for TazUO's random pick
        /// between two attacks of extended low-group bodies.
        /// </summary>
        public static int Group(int group, Body b, IReadOnlyList<(int Group, int Replace)> low,
            IReadOnlyList<(int Group, int Replace)> people, int coin = 0)
        {
            int v13 = group;
            MobType type = b.Type;
            MobType? original = b.OriginalType;
            uint flags = b.Flags;
            bool human = type == MobType.Human || type == MobType.Equipment;

            if (v13 == 12 && !(human || (flags & Unknown1000) != 0))
                v13 = type != MobType.Monster ? 5 : 4;

            if (type != MobType.Monster)
            {
                if (type != MobType.SeaMonster)
                {
                    if (type == MobType.Animal)
                    {
                        if (IsReplaced(low, v13))
                            original = null;
                        if (v13 > 12)
                        {
                            switch (v13)
                            {
                                case 23: v13 = 0; break;
                                case 24: v13 = 1; break;
                                case 26:
                                    if (!Has(b, 26) || (b.WarMode && Has(b, 9)))
                                        v13 = 9;
                                    break;
                                case 28: v13 = Has(b, 10) ? 10 : 5; break;
                                default: v13 = 2; break;
                            }
                        }
                    }
                    else if (IsReplaced(people, v13))
                        return Label190(flags, v13, coin);
                }
                else if (v13 > 8)
                    v13 = 2;
            }
            else
            {
                if (!Has(b, v13))
                    v13 = 1;
                if ((flags & UseUop) == 0 && v13 > 21)
                    v13 = 1;
            }

            if (original == null)
                return Label190(flags, v13, coin);

            if (original != MobType.Monster)
            {
                if (original == MobType.Animal && type == MobType.Monster)
                {
                    switch (v13)
                    {
                        case 0: v13 = 0; break;
                        case 1: v13 = 19; break;
                        case 3: v13 = 11; break;
                        case 5: v13 = 4; break;
                        case 6: v13 = 5; break;
                        case 7: case 11: v13 = 10; break;
                        case 8: v13 = 2; break;
                        case 9: v13 = 17; break;
                        case 10: v13 = 18; break;
                        case 12: v13 = 3; break;
                        default: v13 = 1; break;
                    }
                }
                return Label190(flags, v13, coin);
            }

            // A monster body drawn as another type by Body.def.
            switch (type)
            {
                case MobType.Human:
                case MobType.Equipment:
                    // Equipment is grouped with people for the replaces, but the type switch only knows Human.
                    if (type == MobType.Human)
                        v13 = PeopleFromHigh(v13);
                    break;
                case MobType.Animal:
                    v13 = LowFromHigh(v13);
                    break;
                case MobType.SeaMonster:
                    v13 = SeaFromHigh(v13);
                    break;
            }
            return Label190(flags, v13, coin);
        }

        private static bool Has(Body b, int action) => b.Exists != null && b.Exists(action);

        private static int PeopleFromHigh(int v)
        {
            switch (v)
            {
                case 0: return 0;
                case 2: return 21;
                case 3: return 22;
                case 4: case 9: return 9;
                case 5: return 11;
                case 6: return 13;
                case 7: return 18;
                case 8: return 19;
                case 10: case 21: return 20;
                case 12: case 14: return 16;
                case 13: return 17;
                case 15: case 16: return 30;
                case 17: return 5;
                case 18: return 6;
                case 19: return 1;
                default: return 4;
            }
        }

        private static int LowFromHigh(int v)
        {
            switch (v)
            {
                case 0: return 0;
                case 2: return 8;
                case 3: return 12;
                case 4: case 6: case 7: case 8: case 9: case 12: case 13: case 14: return 5;
                case 5: return 6;
                case 10: case 21: return 7;
                case 11: return 3;
                case 17: return 9;
                case 18: return 10;
                case 19: return 1;
                default: return 2;
            }
        }

        private static int SeaFromHigh(int v)
        {
            switch (v)
            {
                case 0: return 0;
                case 2: case 3: return 8;
                case 4: case 6: case 7: case 8: case 9: case 12: case 13: case 14: return 5;
                case 5: return 6;
                case 10: case 21: return 7;
                case 17: return 3;
                case 18: return 4;
                case 19: return 19;
                default: return 2;
            }
        }

        /// <summary>TazUO's <c>LABEL_190</c> (MobileAnimation.cs:341-520).</summary>
        public static int Label190(uint flags, int v13, int coin = 0)
        {
            if ((flags & Unknown80) != 0 && v13 == 4)
                v13 = 5;

            if ((flags & Unknown200) != 0)
            {
                if (v13 - 7 > 9)
                {
                    if (v13 == 19)
                        v13 = 0;
                    else if (v13 > 19)
                        v13 = 1;
                    return Label222(flags, v13, coin);
                }
            }
            else
            {
                if ((flags & Unknown100) != 0)
                {
                    switch (v13)
                    {
                        case 10: case 15: case 16: v13 = 1; break;
                        case 11: v13 = 17; break;
                    }
                    return Label222(flags, v13, coin);
                }
                if ((flags & Unknown1) != 0)
                {
                    if (v13 == 21)
                        v13 = 10;
                    return Label222(flags, v13, coin);
                }
                if ((flags & ByPeopleGroup) == 0)
                    return Label222(flags, v13, coin);
                return Label222(flags, PeopleFromHighWith11(v13), coin);
            }
            // Unknown200 with 7..16: the people remap's fall-through, stand.
            return Label222(flags, 4, coin);
        }

        // LABEL_190's people switch: as PeopleFromHigh, plus 11 -> 3.
        private static int PeopleFromHighWith11(int v) => v == 11 ? 3 : PeopleFromHigh(v);

        /// <summary>TazUO's <c>LABEL_222</c> (MobileAnimation.cs:199-339).</summary>
        public static int Label222(uint flags, int v13, int coin = 0)
        {
            if ((flags & LowGroupExtended) != 0)
            {
                switch (v13)
                {
                    case 0: v13 = 0; break;
                    case 1: v13 = 19; break;
                    case 5: case 6: v13 = (flags & IdleAt8Frame) != 0 ? 4 : 6 - (coin & 1); break;
                    case 8: v13 = 2; break;
                    case 9: v13 = 17; break;
                    case 10: v13 = (flags & IdleAt8Frame) != 0 ? 17 : 18; break;
                    case 12: v13 = 3; break;
                    default: v13 = 1; break;
                }
            }
            else if ((flags & ByLowGroup) != 0)
                v13 = LowFromHigh(v13);
            return v13 & 0x7F;
        }

        /// <summary>
        /// TazUO's <c>GetObjectNewAnimation</c> for 0xE2's type, action and mode; <see cref="None"/> where TazUO leaves the
        /// normal action. The type is a monster's unless mobtypes.txt lists the body (<see cref="Found"/>).
        /// </summary>
        public static int New(int type, int action, int mode, Body b)
        {
            MobType t = (b.Flags & Found) != 0 ? b.Type : MobType.Monster;
            uint flags = b.Flags;
            switch (type)
            {
                case 0: return NewType0(t, flags, action, mode, b);
                case 1:
                case 2:
                    if (t != MobType.Monster)
                        return t <= MobType.Animal || b.Mounted ? None : 30;
                    return mode % 2 != 0 ? 15 : 16;
                case 3:
                    if (t != MobType.Monster)
                    {
                        if (t == MobType.SeaMonster)
                            return 8;
                        if (t == MobType.Animal)
                            return mode % 2 != 0 ? 21 : 22;
                        return mode % 2 != 0 ? 8 : 12;
                    }
                    return mode % 2 != 0 ? 2 : 3;
                case 4:
                    if (t != MobType.Monster)
                    {
                        if (t > MobType.Animal)
                        {
                            if (b.Gargoyle && b.Flying && Has(b, 77))
                                return 77;
                            return b.Mounted ? None : 20;
                        }
                        return 7;
                    }
                    return 10;
                case 5:
                    if (t <= MobType.SeaMonster)
                        return mode % 2 != 0 ? 18 : 17;
                    if (t != MobType.Animal)
                        return b.Mounted ? None : mode % 2 != 0 ? 6 : 5;
                    switch (mode % 3)
                    {
                        case 1: return 10;
                        case 2: return 3;
                    }
                    return 9;
                case 6:
                case 14:
                    if (t != MobType.Monster)
                    {
                        if (t != MobType.SeaMonster)
                            return t == MobType.Animal ? 3 : b.Mounted ? None : 34;
                        return 5;
                    }
                    return 11;
                case 7:
                    if (b.Mounted)
                        return None;
                    return action == 0 ? 32 : action == 1 ? 33 : 0;
                case 8:
                    if (t != MobType.Monster)
                    {
                        if (t != MobType.SeaMonster)
                            return t == MobType.Animal ? 9 : b.Mounted ? None : 33;
                        return 3;
                    }
                    return 11;
                case 9:
                case 10:
                    if (t != MobType.Monster)
                        return b.Gargoyle && action == 0 ? (type == 9 ? 60 : 61) : None;
                    return 20;
                case 11:
                    if (t != MobType.Monster)
                    {
                        if (t >= MobType.Animal)
                        {
                            if (b.Mounted)
                                return None;
                            if (action == 1 || action == 2)
                                return b.Gargoyle && b.Flying ? 76 : 17;
                            return b.Gargoyle && b.Flying ? 75 : 16;
                        }
                        return 5;
                    }
                    return 12;
            }
            return 0;
        }

        private static int NewType0(MobType t, uint flags, int action, int mode, Body b)
        {
            if (action > 10)
                return 0;
            if (t == MobType.Monster)
            {
                switch (mode % 4)
                {
                    case 1: return 5;
                    case 2: return 6;
                    case 3: return (flags & Unknown1) != 0 ? 12 : 4;
                    default: return 4;
                }
            }
            if (t == MobType.SeaMonster)
                return mode % 2 != 0 ? 6 : 5;
            if (t != MobType.Animal)
            {
                if (b.Mounted)
                    return action > 0 ? action == 1 ? 27 : action == 2 ? 28 : 26 : 29;
                switch (action)
                {
                    case 1: return 18;
                    case 2: return 19;
                    case 3: return 11;
                    case 4: return 9;
                    case 5: return 10;
                    case 6: return 12;
                    case 7: return b.Gargoyle && b.Flying && Has(b, 72) ? 72 : 13;
                    case 8: return 14;
                    default:
                        if (b.Gargoyle && b.Flying && Has(b, 71))
                            return 71;
                        if (Has(b, 31))
                            return 31;
                        break;
                }
            }
            if ((flags & Use2IfHitWhileRunning) != 0)
                return 2;
            if (mode % 2 != 0 && Has(b, 6))
                return 6;
            return 5;
        }
    }
}
