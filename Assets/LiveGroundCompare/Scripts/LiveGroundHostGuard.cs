namespace UOClient.LiveGroundCompare
{
    // No UnityEngine here: the test project compiles this file on its own.
    /// <summary>
    /// Fail-closed gate for experimental land backends: they need the explicit -liveground opt-in and a
    /// local test server (the same hosts as <c>ClientSettings.IsLocal</c>). Anything else, the live shard
    /// above all, gets the original land.
    /// </summary>
    public static class LiveGroundHostGuard
    {
        /// <summary>The live shard's host fragment; refused even if it ever resolved locally.</summary>
        public const string LiveHostFragment = "uosmallz";

        /// <summary>Same hosts as ClientSettings.IsLocal (exact match), plus a bracketed IPv6 loopback.</summary>
        public static bool IsLocalHost(string host)
        {
            if (string.IsNullOrWhiteSpace(host))
                return false;
            string h = host.Trim().ToLowerInvariant();
            if (h == "[::1]")
                h = "::1";
            if (h.Contains(LiveHostFragment))
                return false;
            return h == "127.0.0.1" || h == "localhost" || h == "::1";
        }

        /// <summary>
        /// The backend actually used. Original is always allowed; an experimental backend only with the
        /// opt-in flag, on a local host, and when it is available in this build. <paramref name="reason"/> says why
        /// a request was refused (null when granted as asked).
        /// </summary>
        public static GroundBackendKind Resolve(bool optIn, string host, GroundBackendKind requested, out string reason)
        {
            reason = null;
            if (!GroundBackend.IsExperimental(requested))
                return GroundBackendKind.Original;
            if (!optIn)
            {
                reason = "experimental ground needs -liveground";
                return GroundBackendKind.Original;
            }
            if (!IsLocalHost(host))
            {
                reason = $"experimental ground refused for non-local host '{host}' (TEST 127.0.0.1 only)";
                return GroundBackendKind.Original;
            }
            if (!GroundBackend.IsAvailable(requested))
            {
                reason = $"{requested} backend is not wired yet";
                return GroundBackendKind.Original;
            }
            return requested;
        }

        public static bool Allows(bool optIn, string host, GroundBackendKind requested) =>
            Resolve(optIn, host, requested, out _) == requested;
    }

    /// <summary>
    /// The compare toggle's state, Unity-free: every change goes through <see cref="LiveGroundHostGuard"/> and is
    /// handed to <c>apply</c> (which only swaps land emission). Disposing puts the original land back once and
    /// ignores everything after.
    /// </summary>
    public sealed class LiveGroundSwitch : System.IDisposable
    {
        private readonly bool _optIn;
        private readonly System.Action<GroundBackendKind> _apply;
        private string _host;
        private bool _attached, _disposed;

        public LiveGroundSwitch(bool optIn, GroundBackendKind requested, System.Action<GroundBackendKind> apply)
        {
            _optIn = optIn;
            Requested = requested;
            _apply = apply ?? throw new System.ArgumentNullException(nameof(apply));
        }

        /// <summary>What the user asked for (launch argument or last toggle).</summary>
        public GroundBackendKind Requested { get; private set; }

        /// <summary>What the land is drawn with now.</summary>
        public GroundBackendKind Active { get; private set; } = GroundBackendKind.Original;

        public bool IsDisposed => _disposed;

        /// <summary>A world was created for <paramref name="host"/>: re-checks the guard and applies. Returns a status line.</summary>
        public string Attach(string host)
        {
            if (_disposed)
                return null;
            _host = host;
            _attached = true;
            return Apply();
        }

        /// <summary>The world went away (logout, disconnect): nothing to apply until the next <see cref="Attach"/>.</summary>
        public void Detach()
        {
            _attached = false;
            Active = GroundBackendKind.Original;
        }

        /// <summary>Next backend (Original ⇄ Mesh). Returns a status line, or null when there is nothing to switch.</summary>
        public string Toggle()
        {
            if (_disposed || !_attached)
                return null;
            Requested = GroundBackend.Next(Active);
            return Apply();
        }

        private string Apply()
        {
            GroundBackendKind effective = LiveGroundHostGuard.Resolve(_optIn, _host, Requested, out string reason);
            _apply(effective); // also when unchanged: a freshly attached world starts without an emitter
            Active = effective;
            return reason == null ? $"Ground: {effective}" : $"Ground: {effective} ({reason})";
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            if (_attached && Active != GroundBackendKind.Original)
                _apply(GroundBackendKind.Original);
            Active = GroundBackendKind.Original;
            _attached = false;
        }
    }
}
