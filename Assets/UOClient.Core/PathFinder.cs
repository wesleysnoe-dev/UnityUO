using System;
using System.Collections.Generic;

namespace UOClient.Core
{
    /// <summary>
    /// The way on foot from one tile to another: A* over the eight directions, asking the caller's step rule where each
    /// step would stand (the same rule the player's own steps use), so a path never asks for a step the client would
    /// refuse. A diagonal step needs both tiles beside it passable too, as the server asks of players. Every step costs
    /// the same time in UO; diagonals cost a hair more here only so that straight runs are preferred among equals.
    /// </summary>
    public static class PathFinder
    {
        /// <summary>The search gives up after this many tiles looked at, or for a target further than this.</summary>
        public const int MaxNodes = 8000, MaxDistance = 60;

        public struct Node
        {
            public int X, Y, Z;
        }

        private static readonly (int Dx, int Dy)[] Deltas =
        {
            (0, -1), (1, -1), (1, 0), (1, 1), (0, 1), (-1, 1), (-1, 0), (-1, -1)
        };

        /// <summary>The tile offset of a UO direction (0 north, clockwise).</summary>
        public static (int Dx, int Dy) Delta(int direction) => Deltas[direction & 7];

        /// <summary>The UO direction of a one-tile offset, or -1 when it is not one.</summary>
        public static int DirectionOf(int dx, int dy)
        {
            for (int d = 0; d < 8; d++)
                if (Deltas[d].Dx == dx && Deltas[d].Dy == dy)
                    return d;
            return -1;
        }

        /// <summary>Tiles between two points as steps are counted (a diagonal is one step).</summary>
        public static int Distance(int x0, int y0, int x1, int y1) => Math.Max(Math.Abs(x1 - x0), Math.Abs(y1 - y0));

        private sealed class Entry
        {
            public int X, Y, Z, G, F;
            public Entry Parent;
        }

        /// <summary>
        /// The tiles to walk, the start first and the target last; null when there is no way. With
        /// <paramref name="nearest"/>, a target that cannot be reached gives the way to the reachable tile closest to it
        /// (null if nothing is closer than the start). <paramref name="step"/>: the Z a step onto (x, y) from Z ends at,
        /// or null if that step is refused.
        /// </summary>
        public static List<Node> Find(int sx, int sy, int sz, int tx, int ty, Func<int, int, int, int?> step, bool nearest = true)
        {
            if (step == null || Distance(sx, sy, tx, ty) > MaxDistance)
                return null;
            var start = new Entry { X = sx, Y = sy, Z = sz, G = 0, F = Heuristic(sx, sy, tx, ty) };
            if (sx == tx && sy == ty)
                return Build(start);

            var open = new List<Entry> { start };
            var best = new Dictionary<long, int> { [Key(sx, sy)] = 0 };
            var closed = new HashSet<long>();
            Entry closest = start;
            int looked = 0;
            while (open.Count > 0 && looked++ < MaxNodes)
            {
                Entry at = Pop(open);
                long key = Key(at.X, at.Y);
                if (!closed.Add(key))
                    continue;
                if (at.X == tx && at.Y == ty)
                    return Build(at);
                if (Heuristic(at.X, at.Y, tx, ty) < Heuristic(closest.X, closest.Y, tx, ty))
                    closest = at;

                for (int d = 0; d < 8; d++)
                {
                    int nx = at.X + Deltas[d].Dx, ny = at.Y + Deltas[d].Dy;
                    long nk = Key(nx, ny);
                    if (closed.Contains(nk) || Distance(sx, sy, nx, ny) > MaxDistance)
                        continue;
                    bool diagonal = (d & 1) == 1;
                    // A diagonal squeezes between its two neighbours: both must be passable.
                    if (diagonal && (!step(at.X + Deltas[d].Dx, at.Y, at.Z).HasValue || !step(at.X, at.Y + Deltas[d].Dy, at.Z).HasValue))
                        continue;
                    int? z = step(nx, ny, at.Z);
                    if (!z.HasValue)
                        continue;
                    int g = at.G + (diagonal ? 101 : 100);
                    if (best.TryGetValue(nk, out int had) && had <= g)
                        continue;
                    best[nk] = g;
                    Push(open, new Entry { X = nx, Y = ny, Z = z.Value, G = g, F = g + Heuristic(nx, ny, tx, ty), Parent = at });
                }
            }
            return nearest && closest != start ? Build(closest) : null;
        }

        private static int Heuristic(int x, int y, int tx, int ty) => 100 * Distance(x, y, tx, ty);

        private static long Key(int x, int y) => ((long)x << 32) | (uint)y;

        private static List<Node> Build(Entry end)
        {
            var path = new List<Node>();
            for (Entry e = end; e != null; e = e.Parent)
                path.Add(new Node { X = e.X, Y = e.Y, Z = e.Z });
            path.Reverse();
            return path;
        }

        // A binary heap on F (ties: the one further along, so the search runs straight at the target).
        private static bool Before(Entry a, Entry b) => a.F < b.F || a.F == b.F && a.G > b.G;

        private static void Push(List<Entry> heap, Entry e)
        {
            heap.Add(e);
            int i = heap.Count - 1;
            while (i > 0)
            {
                int parent = (i - 1) / 2;
                if (!Before(heap[i], heap[parent]))
                    break;
                (heap[i], heap[parent]) = (heap[parent], heap[i]);
                i = parent;
            }
        }

        private static Entry Pop(List<Entry> heap)
        {
            Entry top = heap[0];
            int last = heap.Count - 1;
            heap[0] = heap[last];
            heap.RemoveAt(last);
            int i = 0;
            while (true)
            {
                int l = 2 * i + 1, r = l + 1, m = i;
                if (l < heap.Count && Before(heap[l], heap[m])) m = l;
                if (r < heap.Count && Before(heap[r], heap[m])) m = r;
                if (m == i)
                    break;
                (heap[i], heap[m]) = (heap[m], heap[i]);
                i = m;
            }
            return top;
        }
    }
}
