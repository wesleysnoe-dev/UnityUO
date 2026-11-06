using System;

namespace UOClient.LiveGroundCompare
{
    // No UnityEngine here: the test project compiles this file on its own.
    /// <summary>How the world's land is emitted. Only <see cref="Original"/> is the production path.</summary>
    public enum GroundBackendKind
    {
        /// <summary>The existing compositor path: flat land sprites and rasterized stretched texmaps (the default).</summary>
        Original = 0,

        /// <summary>Experimental: a GPU mesh with the compositor's exact corners and triangulation.</summary>
        Mesh = 1,

        /// <summary>Experimental: a real UnityEngine.Terrain. Not wired yet; requesting it falls back to Original.</summary>
        Terrain = 2
    }

    public static class GroundBackend
    {
        /// <summary>Backends that can actually be shown in this build (Terrain is parsed but not wired yet).</summary>
        public static bool IsAvailable(GroundBackendKind kind) => kind == GroundBackendKind.Original || kind == GroundBackendKind.Mesh;

        public static bool IsExperimental(GroundBackendKind kind) => kind != GroundBackendKind.Original;

        /// <summary>
        /// Parses a -groundbackend value. Null/empty or anything unrecognized gives <see cref="GroundBackendKind.Original"/>
        /// and false, so a malformed argument can never switch the land away from production.
        /// </summary>
        public static bool TryParse(string text, out GroundBackendKind kind)
        {
            kind = GroundBackendKind.Original;
            switch (text?.Trim().ToLowerInvariant())
            {
                case "original":
                    return true;
                case "mesh":
                    kind = GroundBackendKind.Mesh;
                    return true;
                case "terrain":
                    kind = GroundBackendKind.Terrain;
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Whether a placement leaves the original sprite path for <paramref name="kind"/>: only unbaked land, and only
        /// for an experimental backend. Statics, mobiles, roofs and baked (HD) placements always stay original.
        /// </summary>
        public static bool Diverts(GroundBackendKind kind, in ArtPipeline.DrawItem item, bool baked) =>
            IsExperimental(kind) && IsAvailable(kind) && item.Kind == ArtPipeline.DrawKind.Land && !baked;

        /// <summary>The toggle order: Original, Mesh, back to Original (unavailable backends are skipped).</summary>
        public static GroundBackendKind Next(GroundBackendKind current)
        {
            var values = (GroundBackendKind[])Enum.GetValues(typeof(GroundBackendKind));
            int i = Array.IndexOf(values, current);
            for (int step = 1; step <= values.Length; step++)
            {
                GroundBackendKind k = values[(i + step) % values.Length];
                if (IsAvailable(k))
                    return k;
            }
            return GroundBackendKind.Original;
        }
    }
}
