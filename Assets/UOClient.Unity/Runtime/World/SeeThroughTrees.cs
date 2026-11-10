using UnityEngine;

namespace UOClient.Unity
{
    /// <summary>
    /// Trees (and other plants) in front of the player thin out round them, so they are never lost behind foliage, as in
    /// modern isometric games: WorldSprite's plant pixels nearer the camera than the player, inside a soft circle round
    /// the player's body, are dithered away (most at the middle, none at the rim). The circle and the player's depth are
    /// a shader global, set once a frame after the player is drawn.
    /// </summary>
    public static class SeeThroughTrees
    {
        /// <summary>On (the default) or off (the options).</summary>
        public static bool On { get; set; } = true;

        private static readonly int Id = Shader.PropertyToID("_UOSeeThrough");

        /// <summary>Publishes the circle round <paramref name="player"/> as drawn this frame (radius 0: off).</summary>
        public static void Apply(Avatar player)
        {
            Rect r = player?.LastRect ?? default;
            if (!On || r.width <= 0f || r.height <= 0f)
            {
                Shader.SetGlobalVector(Id, Vector4.zero);
                return;
            }
            // Centred on the body (the frame's middle), wide enough for the whole figure and a little round it.
            float radius = Mathf.Max(r.height * 0.85f, r.width * 1.3f);
            Shader.SetGlobalVector(Id, new Vector4(r.center.x, r.center.y, radius, player.LastDepth));
        }
    }
}
