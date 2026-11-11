using System;
using System.Collections.Generic;

namespace UOClient.Core.Assistant
{
    /// <summary>One piece of an outfit: the very item (by serial) and the layer it is worn on. Public fields: saved with JsonUtility.</summary>
    [Serializable]
    public sealed class DressItem
    {
        public uint serial;
        public int layer;
        public string name = "";
        public int graphic = -1;
        public int hue;
    }

    /// <summary>A named outfit. <see cref="undressBag"/> is where its pieces go when taken off (0 = the backpack).</summary>
    [Serializable]
    public sealed class DressOutfit
    {
        public string name = "Outfit";
        public uint undressBag;
        public List<DressItem> items = new List<DressItem>();

        public bool Contains(uint serial)
        {
            foreach (DressItem i in items)
                if (i != null && i.serial == serial)
                    return true;
            return false;
        }
    }

    public enum DressStepKind
    {
        /// <summary>Nothing left to do.</summary>
        None,
        /// <summary>Move a worn item to the undress bag.</summary>
        TakeOff,
        /// <summary>Wear an item on its layer.</summary>
        PutOn,
    }

    public readonly struct DressStep
    {
        public readonly DressStepKind Kind;
        public readonly uint Serial;
        public readonly byte Layer;

        public DressStep(DressStepKind kind, uint serial, byte layer)
        {
            Kind = kind;
            Serial = serial;
            Layer = layer;
        }
    }

    /// <summary>
    /// The dress agent's decisions: which layers are clothing, what an outfit captured from the character holds, and
    /// the next single move of a dress or undress run. A run asks for one step at a time and records each item it
    /// has acted on in <c>done</c>; the server's answer (the item appearing on or leaving the character) arrives a
    /// moment after the request, and an item the server refuses must not be asked for again and again.
    /// </summary>
    public static class DressPlan
    {
        public const byte OneHanded = 1, TwoHanded = 2;

        private static readonly string[] Names =
        {
            "", "Right hand", "Left hand", "Shoes", "Pants", "Shirt", "Helm", "Gloves", "Ring", "Talisman", "Neck", "Hair",
            "Waist", "Inner torso", "Bracelet", "Face", "Beard", "Middle torso", "Earrings", "Arms", "Cloak", "Backpack",
            "Outer torso", "Outer legs", "Inner legs",
        };

        /// <summary>
        /// Whether a layer holds something that can be put on and taken off: the hands and clothing (1-24), without
        /// hair (11), face (15), beard (16) and the backpack (21). Mount (25), the shop layers (26-28) and the bank
        /// (29) are above the range.
        /// </summary>
        public static bool IsClothingLayer(int layer) =>
            layer >= 1 && layer <= 24 && layer != 11 && layer != 15 && layer != 16 && layer != 21;

        public static string LayerName(int layer) => layer >= 1 && layer < Names.Length ? Names[layer] : "Layer " + layer;

        /// <summary>The clothing a character wears now, as outfit pieces in layer order (names are filled in by the caller).</summary>
        public static List<DressItem> Capture(IReadOnlyDictionary<byte, EquippedItem> worn)
        {
            var items = new List<DressItem>();
            if (worn == null)
                return items;
            foreach (KeyValuePair<byte, EquippedItem> e in worn)
                if (IsClothingLayer(e.Key) && e.Value.Serial != 0)
                    items.Add(new DressItem { serial = e.Value.Serial, layer = e.Key, graphic = e.Value.Graphic, hue = e.Value.Hue });
            items.Sort((a, b) => a.layer.CompareTo(b.layer));
            return items;
        }

        private static bool IsWorn(IReadOnlyDictionary<byte, EquippedItem> worn, uint serial)
        {
            foreach (EquippedItem e in worn.Values)
                if (e.Serial == serial)
                    return true;
            return false;
        }

        /// <summary>
        /// The next step of wearing an outfit. First everything in the outfit's way comes off: whatever else sits on
        /// one of its layers, and for a piece held in a hand whatever else is in the other hand too (a two-handed
        /// weapon cannot go on beside a shield, and the client cannot tell which weapons are two-handed). Then each
        /// piece not yet worn is put on, in the outfit's order.
        /// </summary>
        public static DressStep NextDress(DressOutfit outfit, IReadOnlyDictionary<byte, EquippedItem> worn, ICollection<uint> done)
        {
            if (outfit == null || worn == null)
                return default;
            foreach (DressItem piece in outfit.items)
            {
                if (piece == null || !IsClothingLayer(piece.layer) || IsWorn(worn, piece.serial))
                    continue;
                byte layer = (byte)piece.layer;
                if (InTheWay(outfit, worn, done, layer, out DressStep off))
                    return off;
                if (layer == OneHanded && InTheWay(outfit, worn, done, TwoHanded, out off))
                    return off;
                if (layer == TwoHanded && InTheWay(outfit, worn, done, OneHanded, out off))
                    return off;
            }
            foreach (DressItem piece in outfit.items)
            {
                if (piece == null || piece.serial == 0 || !IsClothingLayer(piece.layer) || IsWorn(worn, piece.serial))
                    continue;
                if (done != null && done.Contains(piece.serial))
                    continue;
                return new DressStep(DressStepKind.PutOn, piece.serial, (byte)piece.layer);
            }
            return default;
        }

        private static bool InTheWay(DressOutfit outfit, IReadOnlyDictionary<byte, EquippedItem> worn, ICollection<uint> done, byte layer, out DressStep off)
        {
            off = default;
            if (!worn.TryGetValue(layer, out EquippedItem there) || there.Serial == 0 || outfit.Contains(there.Serial))
                return false;
            if (done != null && done.Contains(there.Serial))
                return false;
            off = new DressStep(DressStepKind.TakeOff, there.Serial, layer);
            return true;
        }

        /// <summary>The next step of taking an outfit off: its first piece that is still worn. Other clothing stays on.</summary>
        public static DressStep NextUndress(DressOutfit outfit, IReadOnlyDictionary<byte, EquippedItem> worn, ICollection<uint> done)
        {
            if (outfit == null || worn == null)
                return default;
            foreach (DressItem piece in outfit.items)
            {
                if (piece == null || piece.serial == 0 || (done != null && done.Contains(piece.serial)))
                    continue;
                foreach (KeyValuePair<byte, EquippedItem> e in worn)
                    if (e.Value.Serial == piece.serial && IsClothingLayer(e.Key))
                        return new DressStep(DressStepKind.TakeOff, piece.serial, e.Key);
            }
            return default;
        }
    }
}
