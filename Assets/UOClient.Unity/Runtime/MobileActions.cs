using System;

namespace UOClient.Unity
{
    /// <summary>
    /// Which action a character on screen plays, as TazUO's <c>MobileAnimation.GetGroupForAnimation</c> picks it, and
    /// how a death plays out. No UnityEngine here, so the rules are tested alongside the formats.
    /// </summary>
    public static class MobileActions
    {
        // Human actions while riding.
        public const int MountedWalk = 23, MountedRun = 24, MountedStand = 25;

        /// <summary>
        /// TazUO's <c>Mobile.ProcessAnimation</c> steps a death one frame per <c>CHARACTER_ANIMATION_DELAY</c>
        /// (80 ms, Constants.cs:19; Mobile.cs:627) and removes the dying mobile once past the last frame (Mobile.cs:706-714).
        /// </summary>
        public const float DeathFrameSeconds = 0.08f;

        /// <summary>
        /// Walk/run/stand for a look's state. Riders keep the mounted actions whatever the mode (MobileAnimation.cs:1144,
        /// 1273); the dead never take war actions (<c>!InWarMode || IsDead</c>, MobileAnimation.cs:1166, 1288).
        /// </summary>
        public static (int Walk, int Run, int Stand) Select(bool mounted, (int Walk, int Run, int Stand) peace,
            (int Walk, int Run, int Stand) war, bool warMode, bool dead)
        {
            if (mounted)
                return (MountedWalk, MountedRun, MountedStand);
            return warMode && !dead ? war : peace;
        }

        /// <summary>The action for standing, walking or running out of a walk/run/stand set.</summary>
        public static int Pick((int Walk, int Run, int Stand) actions, bool moving, bool running) =>
            moving ? (running ? actions.Run : actions.Walk) : actions.Stand;

        /// <summary>
        /// Frames to draw for <paramref name="action"/>, falling back to its peace counterpart and then the peace stand when
        /// a body has no frames for it (or they are not built yet), so a missing war or death action never blanks the sprite.
        /// <paramref name="get"/> returns null while frames are being built and an empty array when the body has none.
        /// </summary>
        public static T[] WithFallback<T>(int action, int peaceAction, int peaceStand, Func<int, T[]> get, out int chosen)
        {
            chosen = action;
            T[] frames = get(action);
            if (Missing(frames) && peaceAction != action)
                frames = Try(peaceAction, frames, get, ref chosen);
            if (Missing(frames) && peaceStand != action && peaceStand != peaceAction)
                frames = Try(peaceStand, frames, get, ref chosen);
            return frames;
        }

        private static bool Missing<T>(T[] frames) => frames == null || frames.Length == 0;

        private static T[] Try<T>(int action, T[] current, Func<int, T[]> get, ref int chosen)
        {
            T[] next = get(action);
            if (next == null)
                return current; // still building: keep what we had (or nothing)
            chosen = action;
            return next;
        }

        /// <summary>A death plays once and holds its last frame (TazUO clamps a corpse's index the same way, Item.cs:622-630).</summary>
        public static int DeathFrame(float time, int frameCount) =>
            frameCount <= 0 ? 0 : Math.Min(Math.Max(0, (int)(time / DeathFrameSeconds)), frameCount - 1);

        /// <summary>Whether a death of <paramref name="frameCount"/> frames has played through (none at all counts as done).</summary>
        public static bool DeathDone(float time, int frameCount) => frameCount <= 0 || time >= frameCount * DeathFrameSeconds;
    }
}
