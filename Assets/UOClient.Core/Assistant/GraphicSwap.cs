using System;
using System.Collections.Generic;

namespace UOClient.Core.Assistant
{
    /// <summary>
    /// One graphic replacement (TazUO's GraphicChangeFilter): things of a kind with this graphic are drawn with another,
    /// and in another hue if one is given. Public fields: saved with JsonUtility.
    /// </summary>
    [Serializable]
    public sealed class GraphicSwap
    {
        /// <summary><see cref="Mobile"/> (the graphic is a body) or <see cref="Item"/> (the graphic is item art).</summary>
        public int kind;

        public int graphic = -1;

        /// <summary>The graphic drawn instead.</summary>
        public int to = -1;

        /// <summary>The hue drawn instead; -1 keeps the thing's own.</summary>
        public int hue = -1;

        public bool enabled = true;

        public const int Mobile = 0, Item = 1;

        /// <summary>A line that is switched on and says something: a graphic to look for and one to draw.</summary>
        public bool Usable => enabled && graphic >= 0 && to >= 0 && (kind == Mobile || kind == Item);
    }

    /// <summary>
    /// The replacements as they are looked up while drawing: by kind and graphic, built once when the list changes. As in
    /// TazUO a graphic has one replacement (the first usable line for it wins) and a replacement is not itself replaced,
    /// so two lines swapping A for B and B for A swap them rather than looping.
    /// </summary>
    public sealed class GraphicSwapTable
    {
        private readonly Dictionary<int, GraphicSwap> _byKey = new Dictionary<int, GraphicSwap>();

        public int Count => _byKey.Count;

        public GraphicSwapTable(IEnumerable<GraphicSwap> swaps)
        {
            if (swaps == null)
                return;
            foreach (GraphicSwap s in swaps)
                if (s != null && s.Usable && !_byKey.ContainsKey(Key(s.kind, s.graphic)))
                    _byKey.Add(Key(s.kind, s.graphic), s);
        }

        private static int Key(int kind, int graphic) => (kind << 24) | (graphic & 0xFFFFFF);

        /// <summary>
        /// What a thing is drawn as: its own graphic and hue, or its replacement's. The hue changes only when the line
        /// names one. True when there was a replacement.
        /// </summary>
        public bool Apply(int kind, ref int graphic, ref int hue)
        {
            if (_byKey.Count == 0 || graphic < 0 || !_byKey.TryGetValue(Key(kind, graphic), out GraphicSwap s))
                return false;
            graphic = s.to;
            if (s.hue >= 0)
                hue = s.hue;
            return true;
        }
    }
}
