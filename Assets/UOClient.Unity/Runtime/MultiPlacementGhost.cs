using System;
using System.Collections.Generic;
using UnityEngine;
using UOClient.Core;
using UOClient.IO;
using UOClient.IO.Formats;

namespace UOClient.Unity
{
    /// <summary>
    /// House and boat placement (0x99): while the server's placement cursor is up, the multi is drawn where it
    /// would stand, centered on the tile under the mouse minus the server's offset (as ModernUO places it).
    /// The click itself is answered by the world input as a ground target.
    /// </summary>
    internal sealed class MultiPlacementGhost : IDisposable
    {
        private readonly List<ItemView> _parts = new List<ItemView>();
        private TargetRequest _for;
        private Vector3Int _center;

        /// <param name="cursorTile">The tile (with its surface z) under the mouse, or null over the UI.</param>
        public void Update(GameSession session, UOData data, IWorldSpace world, Func<WorldItem, ItemView> makeView, Func<Vector3Int?> cursorTile)
        {
            TargetRequest t = session?.Target;
            Vector3Int? tile = t != null && t.IsMulti ? cursorTile() : null;
            if (tile == null)
            {
                Clear();
                return;
            }

            var center = new Vector3Int(tile.Value.x - t.MultiOffsetX, tile.Value.y - t.MultiOffsetY, tile.Value.z - t.MultiOffsetZ);
            if (!ReferenceEquals(t, _for) || center != _center)
            {
                Clear();
                _for = t;
                _center = center;
                foreach (MultiComponent part in data.Multis.Get(t.MultiGraphic))
                {
                    if (!part.Fixed || part.Graphic <= 1)
                        continue;
                    _parts.Add(makeView(new WorldItem
                    {
                        Graphic = part.Graphic, X = center.x + part.X, Y = center.y + part.Y,
                        Z = (sbyte)Mathf.Clamp(center.z + part.Z, -128, 127)
                    }));
                }
            }
            foreach (ItemView v in _parts)
                v.Render(world);
        }

        public void Clear()
        {
            foreach (ItemView v in _parts)
                v.Dispose();
            _parts.Clear();
            _for = null;
        }

        public void Dispose() => Clear();
    }
}
