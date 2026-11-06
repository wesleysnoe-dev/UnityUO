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
    /// handed to <c>apply</c> (which only swaps land emission). A backend that fails to apply, or reports a failure
    /// later (<see cref="Fail"/>), is replaced by Original with the reason kept in <see cref="Reason"/>, never silently.
    /// Disposing puts the original land back once and ignores everything after.
    /// </summary>
    public sealed class LiveGroundSwitch : System.IDisposable
    {
        private readonly bool _optIn;
        private readonly System.Func<GroundBackendKind, string> _apply;
        private bool _attached, _disposed;

        public LiveGroundSwitch(bool optIn, GroundBackendKind requested, System.Action<GroundBackendKind> apply)
            : this(optIn, requested, Infallible(apply))
        {
        }

        /// <param name="apply">Applies a backend; returns null on success, else why it could not (Original never fails).</param>
        public LiveGroundSwitch(bool optIn, GroundBackendKind requested, System.Func<GroundBackendKind, string> apply)
        {
            _optIn = optIn;
            Requested = requested;
            _apply = apply ?? throw new System.ArgumentNullException(nameof(apply));
        }

        private static System.Func<GroundBackendKind, string> Infallible(System.Action<GroundBackendKind> apply)
        {
            if (apply == null)
                throw new System.ArgumentNullException(nameof(apply));
            return k => { apply(k); return null; };
        }

        /// <summary>What the user asked for (launch argument or last toggle).</summary>
        public GroundBackendKind Requested { get; private set; }

        /// <summary>What the land is drawn with now.</summary>
        public GroundBackendKind Active { get; private set; } = GroundBackendKind.Original;

        /// <summary>Why Active is not Requested (guard refusal or backend failure); null when granted as asked.</summary>
        public string Reason { get; private set; }

        /// <summary>The host the guard last checked (null before the first world).</summary>
        public string Host { get; private set; }

        public bool IsAttached => _attached;
        public bool IsDisposed => _disposed;

        /// <summary>A world was created for <paramref name="host"/>: re-checks the guard and applies. Returns a status line.</summary>
        public string Attach(string host)
        {
            if (_disposed)
                return null;
            Host = host;
            _attached = true;
            return Apply();
        }

        /// <summary>The world went away (logout, disconnect): nothing to apply until the next <see cref="Attach"/>.</summary>
        public void Detach()
        {
            _attached = false;
            Active = GroundBackendKind.Original;
        }

        /// <summary>Next backend (Original → Mesh → Terrain → Original). Returns a status line, or null when there is nothing to switch.</summary>
        public string Toggle()
        {
            if (_disposed || !_attached)
                return null;
            Requested = GroundBackend.Next(Active);
            return Apply();
        }

        /// <summary>
        /// The active experimental backend broke after it was applied (e.g. a chunk could not be built): back to
        /// Original with <paramref name="why"/> as the reason. Returns a status line, or null when nothing changed.
        /// </summary>
        public string Fail(string why)
        {
            if (_disposed || !_attached || Active == GroundBackendKind.Original)
                return null;
            GroundBackendKind broken = Active;
            _apply(GroundBackendKind.Original);
            Active = GroundBackendKind.Original;
            Reason = LiveGroundStatus.FailureReason(broken, why);
            return LiveGroundStatus.Line(Active, Reason);
        }

        private string Apply()
        {
            GroundBackendKind effective = LiveGroundHostGuard.Resolve(_optIn, Host, Requested, out string reason);
            string error = _apply(effective); // also when unchanged: a freshly attached world starts without an emitter
            if (error != null && effective != GroundBackendKind.Original)
            {
                reason = LiveGroundStatus.FailureReason(effective, error);
                _apply(GroundBackendKind.Original);
                effective = GroundBackendKind.Original;
            }
            Active = effective;
            Reason = reason;
            return LiveGroundStatus.Line(effective, reason);
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

    /// <summary>The strings the TEST indicator, journal and Player.log show (Unity-free so they are tested).</summary>
    public static class LiveGroundStatus
    {
        public static string FailureReason(GroundBackendKind failed, string why) =>
            $"{failed} failed: {(string.IsNullOrWhiteSpace(why) ? "unknown error" : why)}; using Original";

        /// <summary>Journal line: "Ground: Mesh", or "Ground: Original (reason)".</summary>
        public static string Line(GroundBackendKind active, string reason) =>
            reason == null ? $"Ground: {active}" : $"Ground: {active} ({reason})";

        /// <summary>True when the land is not what was asked for (refused or failed), i.e. the indicator should warn.</summary>
        public static bool IsFallback(GroundBackendKind requested, GroundBackendKind active, string reason) =>
            requested != active || reason != null;

        /// <summary>
        /// The persistent on-screen indicator / Player.log line: requested, active, fallback cause (or "ok"), host and
        /// how many land objects the active backend has built (0 for Original, which draws sprites).
        /// </summary>
        public static string Indicator(GroundBackendKind requested, GroundBackendKind active, string reason, string host, bool attached, int landObjects)
        {
            string state = !attached ? "no world" : IsFallback(requested, active, reason) ? $"FALLBACK: {reason ?? "refused"}" : "ok";
            string objects = active == GroundBackendKind.Original ? "" : $" | {active.ToString().ToLowerInvariant()} chunks {landObjects}";
            return $"LiveGround TEST | requested {requested} | active {active} | {state} | host {host ?? "-"}{objects} | Ctrl+Shift+G cycles";
        }
    }
}
