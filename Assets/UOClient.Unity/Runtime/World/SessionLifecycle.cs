using System;
using UOClient.Core;

namespace UOClient.Unity
{
    /// <summary>
    /// One online session's lifetime, from the Login button to the drop, logout or cancel that ends it. The game asks
    /// <see cref="HasEnded"/> each frame and, when it has, tears the world down and calls <see cref="End"/>, which disposes
    /// the session and makes every handler wrapped by <see cref="Guard"/> for it a no-op, so nothing from an old session
    /// reaches what the teardown freed (or the next session's world). There is no reconnecting here: the player logs in again.
    /// No UnityEngine, so its rules are tested in the .NET test project.
    /// </summary>
    public sealed class SessionLifecycle
    {
        /// <summary>The live session, or null between sessions.</summary>
        public GameSession Session { get; private set; }

        /// <summary>Counts sessions begun; a guarded handler only runs while its session's generation is the current one.</summary>
        public int Generation { get; private set; }

        /// <summary>Starts following <paramref name="session"/>; one still live is ended first (its handlers go quiet).</summary>
        public void Begin(GameSession session)
        {
            End();
            Session = session ?? throw new ArgumentNullException(nameof(session));
            Generation++;
        }

        /// <summary>The session dropped (server, network, logout) or the client closed it (Cancel, the character page's Log out).</summary>
        public bool HasEnded => Session != null && (Session.State == SessionState.Disconnected || Session.IsDisposed);

        /// <summary>
        /// What the login screen should say about how <paramref name="session"/> ended: its status (the server's reason, the
        /// connection's, or "Logged out."), or null when the client closed it itself and the login screen already said why.
        /// </summary>
        public static string EndMessage(GameSession session)
        {
            if (session == null || (session.IsDisposed && session.State != SessionState.Disconnected))
                return null;
            if (session.LoggedOut)
                return GameSession.LoggedOutStatus;
            return string.IsNullOrEmpty(session.Status) ? "Disconnected." : session.Status;
        }

        /// <summary>
        /// Disposes the session and silences its guarded handlers. Returns <see cref="EndMessage"/> for it, or null if there
        /// was none (calling it again does nothing).
        /// </summary>
        public string End()
        {
            GameSession s = Session;
            if (s == null)
                return null;
            string message = EndMessage(s);
            Session = null;
            Generation++;
            s.Dispose();
            return message;
        }

        /// <summary>A handler that runs only while the session it was made for is the live one.</summary>
        public Action Guard(Action handler)
        {
            int generation = Generation;
            return () =>
            {
                if (generation == Generation && Session != null)
                    handler();
            };
        }

        public Action<T> Guard<T>(Action<T> handler)
        {
            int generation = Generation;
            return a =>
            {
                if (generation == Generation && Session != null)
                    handler(a);
            };
        }

        public Action<T1, T2, T3, T4> Guard<T1, T2, T3, T4>(Action<T1, T2, T3, T4> handler)
        {
            int generation = Generation;
            return (a, b, c, d) =>
            {
                if (generation == Generation && Session != null)
                    handler(a, b, c, d);
            };
        }
    }
}
