using System;

namespace UOClient.Net
{
    /// <summary>A classic client version, e.g. 7.0.23.1. Decides packet lengths and some packet layouts.</summary>
    public readonly struct ClientVersion : IComparable<ClientVersion>
    {
        public readonly int Major, Minor, Revision, Patch;

        public ClientVersion(int major, int minor, int revision, int patch)
        {
            Major = major;
            Minor = minor;
            Revision = revision;
            Patch = patch;
        }

        /// <summary>The version ModernUO verifies against for this project's test shard (its data folder's client).</summary>
        public static readonly ClientVersion Default = new ClientVersion(7, 0, 23, 1);

        public static ClientVersion Parse(string s)
        {
            string[] p = s.Split('.');
            if (p.Length != 4)
                throw new FormatException($"Client version '{s}' should look like 7.0.23.1.");
            return new ClientVersion(int.Parse(p[0]), int.Parse(p[1]), int.Parse(p[2]), int.Parse(p[3]));
        }

        public int CompareTo(ClientVersion o)
        {
            int c = Major.CompareTo(o.Major);
            if (c != 0) return c;
            c = Minor.CompareTo(o.Minor);
            if (c != 0) return c;
            c = Revision.CompareTo(o.Revision);
            return c != 0 ? c : Patch.CompareTo(o.Patch);
        }

        public static bool operator >=(ClientVersion a, ClientVersion b) => a.CompareTo(b) >= 0;
        public static bool operator <=(ClientVersion a, ClientVersion b) => a.CompareTo(b) <= 0;
        public static bool operator >(ClientVersion a, ClientVersion b) => a.CompareTo(b) > 0;
        public static bool operator <(ClientVersion a, ClientVersion b) => a.CompareTo(b) < 0;

        public override string ToString() => $"{Major}.{Minor}.{Revision}.{Patch}";
    }
}
