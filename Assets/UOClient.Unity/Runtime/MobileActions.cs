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
        public const float DeathFrameSeconds = CharacterFrameSeconds;

        /// <summary>TazUO's CHARACTER_ANIMATION_DELAY: every character animation steps one frame per 80 ms (Constants.cs:19).</summary>
        public const float CharacterFrameSeconds = 0.08f;

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

        /// <summary>
        /// Seconds per frame of a server animation: <c>CHARACTER_ANIMATION_DELAY</c> plus that times (delay + 1)
        /// (Mobile.ProcessAnimation, Mobile.cs:649).
        /// </summary>
        public static float ServerFrameSeconds(int delay) => DeathFrameSeconds * (delay + 2);
    }

    /// <summary>
    /// A server animation (0x6E / 0xE2) being played, stepped as TazUO's <c>Mobile.ProcessAnimation</c> steps one with
    /// <c>AnimationFromServer</c> (Mobile.cs:610-728): one frame per <see cref="MobileActions.ServerFrameSeconds"/>, forwards
    /// over all the body's frames, or backwards from the frame count asked for (SetAnimation keeps the count only for a
    /// backwards animation, Mobile.cs:392-412); each pass through counts down the repeats, and when
    /// they run out it ends (<c>SetAnimation(0xFF)</c>), unless the repeat flag was set, which keeps it going. A repeat count
    /// of 0 plays for ever. A step (Mobile.ProcessSteps, Mobile.cs:747-750), a death or the next server animation ends it early.
    /// </summary>
    public sealed class ServerPlayback
    {
        public readonly int Action;
        public readonly float FrameSeconds;

        /// <summary>The frame shown (TazUO's AnimIndex), before wrapping to the body's frame count.</summary>
        public int Index { get; private set; }

        /// <summary>The repeats ran out: back to walking, running or standing.</summary>
        public bool Done { get; private set; }

        private int _frameCount;   // AnimationFrameCount: 0 until the first step takes the body's own
        private int _repeatsLeft;  // _animationRepeateMode: 0 = for ever
        private bool _repeat;
        private readonly bool _forward;
        private float _untilStep;  // the first step comes on the first tick, as LastAnimationChangeTime is "now"

        public ServerPlayback(int action, int delay, int frameCount, int repeatCount, bool repeat, bool forward)
        {
            Action = action;
            FrameSeconds = MobileActions.ServerFrameSeconds(delay);
            frameCount &= 0xFF;
            Index = forward ? 0 : frameCount;
            _frameCount = forward ? 0 : frameCount;
            _repeatsLeft = repeatCount & 0xFFFF;
            _repeat = repeat;
            _forward = forward;
        }

        /// <summary>The frame to draw out of a body's <paramref name="frames"/>.</summary>
        public int Frame(int frames) => frames <= 0 ? 0 : ((Index % frames) + frames) % frames;

        /// <summary>Lets <paramref name="dt"/> seconds pass over a body with <paramref name="frames"/> frames for the action.</summary>
        public void Advance(float dt, int frames)
        {
            if (Done || frames <= 0)
                return;
            _untilStep -= dt;
            for (int guard = 0; _untilStep <= 0 && !Done && guard < 256; guard++)
            {
                Step(frames);
                _untilStep += FrameSeconds;
            }
            if (_untilStep < 0)
                _untilStep = 0; // a long stall does not leave a backlog of steps
        }

        /// <summary>One step of ProcessAnimation's server branch.</summary>
        public void Step(int frames)
        {
            if (Done || frames <= 0)
                return;
            int fc = frames;
            int index = Index + (_forward ? 1 : -1);
            if (_frameCount == 0)
                _frameCount = fc;
            else
                fc = _frameCount;

            bool wrapped = false;
            if (_forward && index >= fc)
            {
                index = 0;
                wrapped = true;
            }
            else if (!_forward && index < 0)
            {
                index = fc == 0 ? 0 : frames - 1;
                wrapped = true;
            }

            if (wrapped && _repeatsLeft != 0 && --_repeatsLeft == 0)
            {
                if (_repeat)
                    _repeat = false; // the count is spent and now reads 0: it plays on for ever
                else
                    Done = true;
            }
            Index = index % frames;
        }
    }
}
