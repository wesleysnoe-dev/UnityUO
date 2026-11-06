using System;
using System.Collections.Generic;

namespace UOClient.Core
{
    /// <summary>
    /// TazUO's AbilityData (Game/Data/Ability.cs): the weapon abilities by index (1-based, as ModernUO's
    /// WeaponAbility.Abilities and the 0xD7 0x19 request number them), their book icons 0x5200 + index - 1, and which two
    /// a weapon graphic has. ModernUO's table stops at Mystic Arc (31); TazUO's 32nd, Disrobe, has no ability on the
    /// server and no icon (0x521F) in the 7.0.23.1 data, so it is left out.
    /// </summary>
    public static class WeaponAbilities
    {
        public const int Count = 31;

        /// <summary>The first ability icon (Armor Ignore); ability i is at IconBase + i - 1.</summary>
        public const int IconBase = 0x5200;

        /// <summary>TazUO's AbilityData.Abilities names, index 1 first (index 0 is "none").</summary>
        public static readonly string[] Names =
        {
            null, "Armor Ignore", "Bleed Attack", "Concussion Blow", "Crushing Blow", "Disarm", "Dismount", "Double Strike",
            "Infecting", "Mortal Strike", "Moving Shot", "Paralyzing Blow", "Shadow Strike", "Whirlwind Attack",
            "Riding Swipe", "Frenzied Whirlwind", "Block", "Defense Mastery", "Nerve Strike", "Talon Strike", "Feint",
            "Dual Wield", "Double Shot", "Armor Pierce", "Bladeweave", "Force Arrow", "Lightning Arrow", "Psychic Attack",
            "Serpent Arrow", "Force of Nature", "Infused Throw", "Mystic Arc"
        };

        public const int ArmorIgnore = 1, BleedAttack = 2, ConcussionBlow = 3, CrushingBlow = 4, Disarm = 5, Dismount = 6,
            DoubleStrike = 7, InfectiousStrike = 8, MortalStrike = 9, MovingShot = 10, ParalyzingBlow = 11, ShadowStrike = 12,
            WhirlwindAttack = 13, RidingSwipe = 14, FrenziedWhirlwind = 15, Block = 16, DefenseMastery = 17, NerveStrike = 18,
            TalonStrike = 19, Feint = 20, DualWield = 21, DoubleShot = 22, ArmorPierce = 23, Bladeweave = 24, ForceArrow = 25,
            LightningArrow = 26, PsychicAttack = 27, SerpentArrow = 28, ForceOfNature = 29, InfusedThrow = 30, MysticArc = 31;

        /// <summary>Bare hands, or a weapon TazUO's map lacks: Paralyzing Blow and Disarm (DefaultItemAbilities).</summary>
        public static readonly (int Primary, int Secondary) Default = (ParalyzingBlow, Disarm);

        public static int Icon(int ability) => IconBase + ability - 1;

        /// <summary>
        /// The two abilities of a worn weapon, as PlayerMobile.UpdateAbilities: its graphic in the map, else the graphic
        /// beside it (one less, then one more) whose tiledata AnimId is the weapon's, else <see cref="Default"/>.
        /// <paramref name="animIdOf"/> reads tiledata (null: the graphic alone). No weapon (graphic 0): the default.
        /// </summary>
        public static (int Primary, int Secondary) Of(int graphic, Func<int, int> animIdOf)
        {
            if (graphic <= 0)
                return Default;
            if (Map.TryGetValue(graphic, out var a))
                return a;
            if (animIdOf != null)
            {
                int anim = animIdOf(graphic);
                int beside = animIdOf(graphic - 1) == anim ? graphic - 1 : animIdOf(graphic + 1) == anim ? graphic + 1 : 0;
                if (beside != 0 && Map.TryGetValue(beside, out a))
                    return a;
            }
            return Default;
        }

        /// <summary>TazUO's GraphicToAbilitiesMap (Ability.cs:98-332), graphic to (primary, secondary).</summary>
        public static readonly Dictionary<int, (int, int)> Map = new Dictionary<int, (int, int)>
        {
            { 0x0901, (MovingShot, InfusedThrow) }, { 0x0902, (InfectiousStrike, ShadowStrike) },
            { 0x0905, (DoubleStrike, MortalStrike) }, { 0x0906, (CrushingBlow, Dismount) },
            { 0x090C, (BleedAttack, MortalStrike) },
            { 0x0DF0, (WhirlwindAttack, ParalyzingBlow) }, { 0x0DF1, (WhirlwindAttack, ParalyzingBlow) },
            { 0x0DF2, (Dismount, Disarm) }, { 0x0DF3, (Dismount, Disarm) }, { 0x0DF4, (Dismount, Disarm) }, { 0x0DF5, (Dismount, Disarm) },
            { 0x0E81, (CrushingBlow, Disarm) }, { 0x0E82, (CrushingBlow, Disarm) },
            { 0x0E85, (DoubleStrike, Disarm) }, { 0x0E86, (DoubleStrike, Disarm) },
            { 0x0E87, (BleedAttack, Dismount) }, { 0x0E88, (BleedAttack, Dismount) },
            { 0x0E89, (DoubleStrike, ConcussionBlow) }, { 0x0E8A, (DoubleStrike, ConcussionBlow) },
            { 0x0EC2, (BleedAttack, InfectiousStrike) }, { 0x0EC3, (BleedAttack, InfectiousStrike) },
            { 0x0EC4, (ShadowStrike, BleedAttack) }, { 0x0EC5, (ShadowStrike, BleedAttack) },
            { 0x0F43, (ArmorIgnore, Disarm) }, { 0x0F44, (ArmorIgnore, Disarm) },
            { 0x0F45, (BleedAttack, MortalStrike) }, { 0x0F46, (BleedAttack, MortalStrike) },
            { 0x0F47, (BleedAttack, ConcussionBlow) }, { 0x0F48, (BleedAttack, ConcussionBlow) },
            { 0x0F49, (CrushingBlow, Dismount) }, { 0x0F4A, (CrushingBlow, Dismount) },
            { 0x0F4B, (DoubleStrike, WhirlwindAttack) }, { 0x0F4C, (DoubleStrike, WhirlwindAttack) },
            { 0x0F4D, (ParalyzingBlow, Dismount) }, { 0x0F4E, (ParalyzingBlow, Dismount) },
            { 0x0F4F, (ConcussionBlow, MortalStrike) }, { 0x0F50, (ConcussionBlow, MortalStrike) },
            { 0x0F51, (InfectiousStrike, ShadowStrike) }, { 0x0F52, (InfectiousStrike, ShadowStrike) },
            { 0x0F5C, (ConcussionBlow, Disarm) }, { 0x0F5D, (ConcussionBlow, Disarm) },
            { 0x0F5E, (CrushingBlow, ArmorIgnore) }, { 0x0F5F, (CrushingBlow, ArmorIgnore) },
            { 0x0F60, (ArmorIgnore, ConcussionBlow) }, { 0x0F61, (ArmorIgnore, ConcussionBlow) },
            { 0x0F62, (ArmorIgnore, ParalyzingBlow) }, { 0x0F63, (ArmorIgnore, ParalyzingBlow) },
            { 0x0FB5, (CrushingBlow, ShadowStrike) },
            { 0x13AF, (ArmorIgnore, BleedAttack) }, { 0x13B0, (ArmorIgnore, BleedAttack) },
            { 0x13B1, (ParalyzingBlow, MortalStrike) }, { 0x13B2, (ParalyzingBlow, MortalStrike) },
            { 0x13B3, (ShadowStrike, Dismount) }, { 0x13B4, (ShadowStrike, Dismount) },
            { 0x13B6, (DoubleStrike, ParalyzingBlow) }, { 0x13B7, (DoubleStrike, ParalyzingBlow) }, { 0x13B8, (DoubleStrike, ParalyzingBlow) },
            { 0x13B9, (ParalyzingBlow, CrushingBlow) }, { 0x13BA, (ParalyzingBlow, CrushingBlow) },
            { 0x13FD, (MovingShot, Dismount) }, { 0x13E3, (CrushingBlow, ShadowStrike) }, { 0x13F6, (InfectiousStrike, Disarm) },
            { 0x13F8, (ConcussionBlow, ForceOfNature) }, { 0x13FB, (WhirlwindAttack, BleedAttack) },
            { 0x13FF, (DoubleStrike, ArmorIgnore) }, { 0x1401, (ArmorIgnore, InfectiousStrike) },
            { 0x1402, (ShadowStrike, MortalStrike) }, { 0x1403, (ShadowStrike, MortalStrike) },
            { 0x1404, (BleedAttack, Disarm) }, { 0x1405, (BleedAttack, Disarm) },
            { 0x1406, (CrushingBlow, MortalStrike) }, { 0x1407, (CrushingBlow, MortalStrike) },
            { 0x1438, (WhirlwindAttack, CrushingBlow) }, { 0x1439, (WhirlwindAttack, CrushingBlow) },
            { 0x143A, (DoubleStrike, ConcussionBlow) }, { 0x143B, (DoubleStrike, ConcussionBlow) },
            { 0x143C, (ArmorIgnore, MortalStrike) }, { 0x143D, (ArmorIgnore, MortalStrike) },
            { 0x143E, (WhirlwindAttack, ConcussionBlow) }, { 0x143F, (WhirlwindAttack, ConcussionBlow) },
            { 0x1440, (BleedAttack, ShadowStrike) }, { 0x1441, (BleedAttack, ShadowStrike) },
            { 0x1442, (DoubleStrike, ShadowStrike) }, { 0x1443, (DoubleStrike, ShadowStrike) },
            { 0x26BA, (BleedAttack, ParalyzingBlow) }, { 0x26BB, (ParalyzingBlow, MortalStrike) },
            { 0x26BC, (CrushingBlow, MortalStrike) }, { 0x26BD, (ArmorIgnore, Dismount) },
            { 0x26BE, (ParalyzingBlow, InfectiousStrike) }, { 0x26BF, (DoubleStrike, InfectiousStrike) },
            { 0x26C0, (Dismount, ConcussionBlow) }, { 0x26C1, (DoubleStrike, MortalStrike) },
            { 0x26C2, (ArmorIgnore, MovingShot) }, { 0x26C3, (DoubleStrike, MovingShot) },
            { 0x26C4, (BleedAttack, ParalyzingBlow) }, { 0x26C5, (ParalyzingBlow, MortalStrike) },
            { 0x26C6, (CrushingBlow, MortalStrike) }, { 0x26C7, (ArmorIgnore, Dismount) },
            { 0x26C8, (ParalyzingBlow, InfectiousStrike) }, { 0x26C9, (DoubleStrike, InfectiousStrike) },
            { 0x26CA, (Dismount, ConcussionBlow) }, { 0x26CB, (DoubleStrike, MortalStrike) },
            { 0x26CC, (ArmorIgnore, MovingShot) }, { 0x26CD, (DoubleStrike, MovingShot) },
            { 0x26CE, (WhirlwindAttack, Disarm) }, { 0x26CF, (WhirlwindAttack, Disarm) },
            { 0x27A2, (CrushingBlow, RidingSwipe) }, { 0x27A3, (Feint, Block) }, { 0x27A4, (FrenziedWhirlwind, DoubleStrike) },
            { 0x27A5, (ArmorPierce, DoubleShot) }, { 0x27A6, (FrenziedWhirlwind, CrushingBlow) },
            { 0x27A7, (DefenseMastery, FrenziedWhirlwind) }, { 0x27A8, (Feint, NerveStrike) }, { 0x27A9, (Feint, DoubleStrike) },
            { 0x27AA, (Disarm, ParalyzingBlow) }, { 0x27AB, (DualWield, TalonStrike) }, { 0x27AD, (WhirlwindAttack, DefenseMastery) },
            { 0x27AE, (Block, DoubleStrike) }, { 0x27AF, (DualWield, ArmorPierce) },
            { 0x27ED, (CrushingBlow, RidingSwipe) }, { 0x27EE, (Feint, Block) }, { 0x27EF, (FrenziedWhirlwind, DoubleStrike) },
            { 0x27F0, (ArmorPierce, DoubleShot) }, { 0x27F1, (FrenziedWhirlwind, CrushingBlow) },
            { 0x27F2, (DefenseMastery, FrenziedWhirlwind) }, { 0x27F3, (Feint, NerveStrike) }, { 0x27F4, (Feint, DoubleStrike) },
            { 0x27F5, (Disarm, ParalyzingBlow) }, { 0x27F6, (DualWield, TalonStrike) }, { 0x27F8, (WhirlwindAttack, DefenseMastery) },
            { 0x27F9, (Block, DoubleStrike) }, { 0x27FA, (DualWield, ArmorPierce) },
            { 0x2D1E, (ForceArrow, SerpentArrow) }, { 0x2D1F, (LightningArrow, PsychicAttack) },
            { 0x2D20, (PsychicAttack, BleedAttack) }, { 0x2D21, (InfectiousStrike, ShadowStrike) },
            { 0x2D22, (Feint, ArmorIgnore) }, { 0x2D23, (Disarm, Bladeweave) }, { 0x2D24, (ConcussionBlow, CrushingBlow) },
            { 0x2D25, (Block, ForceOfNature) }, { 0x2D26, (Disarm, Bladeweave) }, { 0x2D27, (WhirlwindAttack, Bladeweave) },
            { 0x2D28, (Disarm, CrushingBlow) }, { 0x2D29, (DefenseMastery, Bladeweave) },
            { 0x2D2A, (ForceArrow, SerpentArrow) }, { 0x2D2B, (LightningArrow, PsychicAttack) },
            { 0x2D2C, (PsychicAttack, BleedAttack) }, { 0x2D2D, (InfectiousStrike, ShadowStrike) },
            { 0x2D2E, (Feint, ArmorIgnore) }, { 0x2D2F, (Disarm, Bladeweave) }, { 0x2D30, (ConcussionBlow, CrushingBlow) },
            { 0x2D31, (Block, ForceOfNature) }, { 0x2D32, (Disarm, Bladeweave) }, { 0x2D33, (WhirlwindAttack, Bladeweave) },
            { 0x2D34, (Disarm, CrushingBlow) }, { 0x2D35, (DefenseMastery, Bladeweave) },
            { 0x4067, (MysticArc, ConcussionBlow) }, { 0x08FD, (DoubleStrike, InfectiousStrike) }, { 0x4068, (DoubleStrike, InfectiousStrike) },
            { 0x406B, (ArmorIgnore, MortalStrike) }, { 0x406C, (MovingShot, InfusedThrow) },
            { 0x0904, (DoubleStrike, Disarm) }, { 0x406D, (DoubleStrike, Disarm) },
            { 0x0903, (ArmorIgnore, Disarm) }, { 0x406E, (ArmorIgnore, Disarm) },
            { 0x08FE, (BleedAttack, ParalyzingBlow) }, { 0x4072, (BleedAttack, ParalyzingBlow) },
            { 0x090B, (CrushingBlow, ConcussionBlow) }, { 0x4074, (CrushingBlow, ConcussionBlow) },
            { 0x0908, (WhirlwindAttack, Dismount) }, { 0x4075, (WhirlwindAttack, Dismount) },
            { 0x4076, (ArmorIgnore, MortalStrike) }, { 0x0907, (ArmorIgnore, MortalStrike) },
            { 0x48AE, (BleedAttack, InfectiousStrike) }, { 0x48B0, (BleedAttack, ConcussionBlow) },
            { 0x48B3, (CrushingBlow, Dismount) }, { 0x48B2, (CrushingBlow, Dismount) },
            { 0x48B5, (ParalyzingBlow, Dismount) }, { 0x48B4, (ParalyzingBlow, Dismount) },
            { 0x48B7, (InfectiousStrike, Disarm) }, { 0x48B6, (InfectiousStrike, Disarm) },
            { 0x48B9, (ConcussionBlow, ParalyzingBlow) }, { 0x48B8, (ConcussionBlow, ParalyzingBlow) },
            { 0x48BB, (DoubleStrike, ArmorIgnore) }, { 0x48BA, (DoubleStrike, ArmorIgnore) },
            { 0x48BD, (ArmorIgnore, InfectiousStrike) }, { 0x48BC, (ArmorIgnore, InfectiousStrike) },
            { 0x48BF, (BleedAttack, Disarm) }, { 0x48BE, (BleedAttack, Disarm) },
            { 0x48CB, (Dismount, ConcussionBlow) }, { 0x48CA, (Dismount, ConcussionBlow) },
            { 0x0481, (WhirlwindAttack, CrushingBlow) }, { 0x48C0, (WhirlwindAttack, CrushingBlow) },
            { 0x48C3, (DoubleStrike, ConcussionBlow) }, { 0x48C2, (DoubleStrike, ConcussionBlow) },
            { 0x48C5, (BleedAttack, ParalyzingBlow) }, { 0x48C4, (BleedAttack, ParalyzingBlow) },
            { 0x48C7, (ParalyzingBlow, MortalStrike) }, { 0x48C6, (ParalyzingBlow, MortalStrike) },
            { 0x48C9, (ParalyzingBlow, InfectiousStrike) }, { 0x48C8, (ParalyzingBlow, InfectiousStrike) },
            { 0x48CC, (Feint, Block) }, { 0x48CD, (Feint, Block) },
            { 0x48CF, (DualWield, TalonStrike) }, { 0x48CE, (DualWield, TalonStrike) },
            { 0x48D1, (Feint, DoubleStrike) }, { 0x48D0, (Feint, DoubleStrike) },
            { 0xA289, (ConcussionBlow, WhirlwindAttack) }, { 0xA291, (ConcussionBlow, WhirlwindAttack) },
            { 0xA28A, (ArmorPierce, WhirlwindAttack) }, { 0xA292, (ArmorPierce, WhirlwindAttack) },
            { 0xA28B, (BleedAttack, WhirlwindAttack) }, { 0xA293, (BleedAttack, WhirlwindAttack) },
            { 0x08FF, (MysticArc, ConcussionBlow) }, { 0x0900, (ArmorIgnore, ParalyzingBlow) }, { 0x090A, (ArmorIgnore, MortalStrike) },
            { 0xAEA5, (DoubleStrike, ArmorIgnore) }, { 0xAEB4, (DoubleStrike, ArmorIgnore) },
            { 0xAEC3, (DoubleStrike, ArmorIgnore) }, { 0xAED2, (DoubleStrike, ArmorIgnore) },
            { 0xAEA4, (DoubleStrike, WhirlwindAttack) }, { 0xAEB3, (DoubleStrike, WhirlwindAttack) },
            { 0xAEC2, (DoubleStrike, WhirlwindAttack) }, { 0xAED1, (DoubleStrike, WhirlwindAttack) },
            { 0x4071, (ArmorIgnore, ParalyzingBlow) },
            { 0xB4D4, (ArmorIgnore, MortalStrike) }, { 0xB4D5, (ArmorIgnore, MortalStrike) },
            { 0xB4DC, (ArmorIgnore, MovingShot) }, { 0xB4DD, (ArmorIgnore, MovingShot) },
            { 0xB4E0, (ArmorIgnore, MovingShot) }, { 0xB4E1, (ArmorIgnore, MovingShot) },
        };
    }
}
