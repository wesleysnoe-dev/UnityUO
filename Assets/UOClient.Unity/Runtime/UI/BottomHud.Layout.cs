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

        /// <summary>How many action slots fit the panel (its inner width less the margins), within the limits.</summary>
        public static int SlotCount(float width)
        {
            float inner = Panel(width).Width - 2f * (Overlap + 8f);
            int n = (int)Math.Floor((inner + SlotGap) / (Slot + SlotGap));
            return Math.Max(MinSlots, Math.Min(MaxSlots, n));
        }

        /// <summary>The index-th slot's left edge inside the HUD, the row centred in the panel.</summary>
        public static float SlotX(float width, int index)
        {
            int n = SlotCount(width);
            float row = n * Slot + (n - 1) * SlotGap;
            (float px, float pw) = Panel(width);
            return px + (pw - row) / 2f + index * (Slot + SlotGap);
        }

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
