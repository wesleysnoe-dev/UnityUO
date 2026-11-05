using UnityEngine;
using UOClient.Core;
using UOClient.IO;

namespace UOClient.Unity
{
    /// <summary>
    /// The shared handles the world demo's parts work with. Several are replaced while playing (the world on
    /// entering it, the walkability on a map change, the player avatar, the session after a failed login), so
    /// read them from here on every use rather than keeping a copy.
    /// </summary>
    public sealed class GameContext
    {
        public UOData Data;
        public Shader Shader;
        public int ArtScale;
        public Transform Root;
        public Camera Camera;
        public MobileArt MobileArt;
        public WorldStreamer World;
        public Walkability Walk;
        public DemoHud Hud;

        /// <summary>Null offline and before logging in.</summary>
        public GameSession Session;

        /// <summary>The player's avatar; null before entering the world.</summary>
        public Avatar Player;

        /// <summary>See <see cref="ClientPaths.ProjectRoot"/>.</summary>
        public static string ProjectRoot => ClientPaths.ProjectRoot;
    }
}
