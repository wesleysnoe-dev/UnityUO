using System;
using System.Collections.Generic;

namespace UOClient.Unity.UI
{
    // No UnityEngine here: the test project compiles this file on its own.

    /// <summary>What an action slot of the bottom HUD does.</summary>
    public enum HudSlotKind
    {
        Empty = 0,
        /// <summary>Casts spell <see cref="HudSlot.id"/> (the server's spell number, 1 = Clumsy).</summary>
        Spell = 1,
        /// <summary>Uses skill <see cref="HudSlot.id"/>.</summary>
        Skill = 2,
        /// <summary>Uses the first item of <see cref="HudSlot.graphic"/> (and hue, -1 = any) in the backpack.</summary>
        Item = 3
    }

    /// <summary>One action slot. Public fields: saved with JsonUtility.</summary>
    [Serializable]
    public sealed class HudSlot
    {
        public int kind;
        public int id;
        public int graphic = -1;
        public int hue = -1;
        public string label = "";

        public HudSlotKind Kind => kind >= 0 && kind <= 3 ? (HudSlotKind)kind : HudSlotKind.Empty;
    }

    /// <summary>
    /// The bottom HUD's measures, in UI units (the UI scale already follows the screen's height, so a unit is about a
    /// thousandth of it): a health orb at the left end, a mana orb at the right, and between them a panel with the
    /// stamina bar on top, a row of action slots and a row of menu buttons. Its width follows the screen's: half of it,
    /// between <see cref="MinWidth"/> and <see cref="MaxWidth"/>, and never wider than the screen.
    /// </summary>
    public static class BottomHudLayout
    {
        public const float Orb = 124f;          // an orb's diameter
        public const float PanelHeight = 86f;   // the middle panel
        public const float StaminaHeight = 12f; // the bar on top of the panel
        public const float Slot = 46f, SlotGap = 5f, SlotTop = 10f;
        public const float MenuHeight = 20f;
        public const float Overlap = 22f;       // how far the panel runs under each orb
        public const float MinWidth = 700f, MaxWidth = 1040f;
        public const int MinSlots = 6, MaxSlots = 12;

        /// <summary>The whole HUD's height: an orb stands taller than the panel.</summary>
        public const float Height = Orb + 6f;

        public static float Width(float screenWidth) =>
            Math.Max(Math.Min(MinWidth, screenWidth), Math.Min(Math.Min(MaxWidth, screenWidth), screenWidth * 0.5f));

        /// <summary>The panel between the orbs: (x, width) inside the HUD.</summary>
        public static (float X, float Width) Panel(float width) => (Orb - Overlap, width - 2f * (Orb - Overlap));

        /// <summary>The pager after the last slot (up, the bar's number, down): its width, and the bars there are.</summary>
        public const float Pager = 24f;
        public const int Pages = 5;

        /// <summary>How many action slots fit the panel (its inner width less the margins and the pager), within the limits.</summary>
        public static int SlotCount(float width)
        {
            float inner = Panel(width).Width - 2f * (Overlap + 8f) - Pager - SlotGap;
            int n = (int)Math.Floor((inner + SlotGap) / (Slot + SlotGap));
            return Math.Max(MinSlots, Math.Min(MaxSlots, n));
        }

        /// <summary>The slots and the pager after them, as one row.</summary>
        private static float RowWidth(int slots) => slots * Slot + (slots - 1) * SlotGap + SlotGap + Pager;

        /// <summary>The index-th slot's left edge inside the HUD, the row (slots, then the pager) centred in the panel.</summary>
        public static float SlotX(float width, int index)
        {
            (float px, float pw) = Panel(width);
            return px + (pw - RowWidth(SlotCount(width))) / 2f + index * (Slot + SlotGap);
        }

        /// <summary>The pager's left edge: just after the last slot, on the mana orb's side.</summary>
        public static float PagerX(float width) => SlotX(width, SlotCount(width) - 1) + Slot + SlotGap;

        /// <summary>The bar an arrow leads to: one up or down, coming round at the ends.</summary>
        public static int NextPage(int page, int step) => ((page + step) % Pages + Pages) % Pages;

        /// <summary>
        /// A shown value following the real one: it closes a share of the gap each second (quick at first, settling), and
        /// lands exactly once within half a percent. Rising and falling alike.
        /// </summary>
        public static float Ease(float shown, float target, float seconds, float rate = 9f)
        {
            if (Math.Abs(target - shown) < 0.005f || seconds <= 0f)
                return seconds <= 0f ? shown : target;
            float k = 1f - (float)Math.Exp(-rate * seconds);
            return shown + (target - shown) * k;
        }

        /// <summary>A ratio 0..1, 0 when the maximum is unknown.</summary>
        public static float Ratio(int value, int max) => max <= 0 ? 0f : Math.Max(0f, Math.Min(1f, value / (float)max));

        /// <summary>
        /// How long a slot stays dark after use, in seconds. The server does not say, so these are the shard's usual waits
        /// with no casting bonuses: a Magery spell's cast time ((3 + circle) / 4 s) plus the 1.5 s recovery; a Necromancy
        /// or Chivalry spell 1 s plus the recovery; a skill 10 s (most skills' wait); bandages 11 s less 1 s per 20
        /// dexterity (at least 3 s); a heal, cure or refresh potion 10 s; any other item 1 s (the action delay).
        /// </summary>
        public static float Cooldown(HudSlot s, int dex)
        {
            switch (s?.Kind ?? HudSlotKind.Empty)
            {
                case HudSlotKind.Spell:
                    if (s.id >= 1 && s.id <= 64)
                        return (3 + (s.id - 1) / 8 + 1) * 0.25f + 1.5f;
                    return 2.5f;
                case HudSlotKind.Skill:
                    return 10f;
                case HudSlotKind.Item:
                    if (s.graphic == 0x0E21 || s.graphic == 0x0EE9)
                        return Math.Max(3f, 11f - dex / 20);
                    if (s.graphic == 0x0F0C || s.graphic == 0x0F07 || s.graphic == 0x0F0B)
                        return 10f;
                    return 1f;
                default:
                    return 0f;
            }
        }

        /// <summary>The slots a saved list gives the bar: as many as fit, the missing ones empty.</summary>
        public static List<HudSlot> Fit(List<HudSlot> saved, int count)
        {
            var slots = new List<HudSlot>(count);
            for (int i = 0; i < count; i++)
                slots.Add(saved != null && i < saved.Count && saved[i] != null ? saved[i] : new HudSlot());
            return slots;
        }
    }
}
