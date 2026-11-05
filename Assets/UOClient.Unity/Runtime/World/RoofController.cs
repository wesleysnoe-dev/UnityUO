using UnityEngine;

namespace UOClient.Unity
{
    /// <summary>
    /// Roofs and upper floors: hidden automatically while the player is indoors, until R takes over
    /// (after which each press flips them).
    /// </summary>
    public sealed class RoofController
    {
        private bool _roofsForced, _manualRoofs = true;

        /// <summary>The height upper floors are cut at right now (null outdoors).</summary>
        public int? CutZ { get; private set; }

        public bool AutoRoofs { get => !_roofsForced; set { _roofsForced = !value; _manualRoofs = true; } }

        /// <summary>R: the first press takes over from the automatic indoor check; each press flips.</summary>
        public string Toggle(bool currentlyVisible)
        {
            _roofsForced = true;
            _manualRoofs = !currentlyVisible;
            return _manualRoofs ? "Roofs shown." : "Roofs hidden.";
        }

        /// <summary>Shows or hides the roofs and sets the cut height for where the player stands; true if the upper parts of multis are hidden.</summary>
        public bool Apply(WorldStreamer world, Walkability walk, Avatar player)
        {
            bool under = walk.IsUnderRoof(player.X, player.Y, player.Z);
            world.SetRoofsVisible(_roofsForced ? _manualRoofs : !under);
            // Upper floors: indoors, everything from the floor over our head up is cut away (unless R shows roofs).
            int? overhead = _roofsForced && _manualRoofs ? null : walk.OverheadZ(player.X, player.Y, player.Z);
            Shader.SetGlobalFloat("_UOCutZ", overhead.HasValue ? MeshBuilder.CutHeight(overhead.Value) : 0f);
            CutZ = overhead;
            return _roofsForced ? !_manualRoofs : under;
        }
    }
}
