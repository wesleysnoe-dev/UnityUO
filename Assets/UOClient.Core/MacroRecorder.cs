using System.Collections.Generic;

namespace UOClient.Core
{
    /// <summary>
    /// Records what the player does as macro action lines (the hotkeys' syntax: <c>say text</c>, <c>cast 29</c>,
    /// <c>skill 21</c>, <c>useobject 0x40001234</c>, <c>lastobject</c>, <c>target 0x...</c>, <c>targetself</c>,
    /// <c>lasttarget</c>) while <see cref="Recording"/>; the session notes each request as it sends it.
    /// </summary>
    public sealed class MacroRecorder
    {
        /// <summary>The most a recording keeps; later actions are dropped.</summary>
        public const int MaxActions = 100;

        private readonly List<string> _actions = new List<string>();

        public bool Recording { get; private set; }

        public IReadOnlyList<string> Actions => _actions;

        /// <summary>Starts a new recording, discarding anything noted before.</summary>
        public void Start()
        {
            _actions.Clear();
            Recording = true;
        }

        /// <summary>Stops recording and returns what was recorded.</summary>
        public List<string> Stop()
        {
            Recording = false;
            return new List<string>(_actions);
        }

        /// <summary>Adds an action line, if recording.</summary>
        public void Note(string action)
        {
            if (Recording && _actions.Count < MaxActions && !string.IsNullOrWhiteSpace(action))
                _actions.Add(action);
        }

        public static string Hex(uint serial) => $"0x{serial:X8}";

        /// <summary>
        /// True for the actions that answer a target cursor (and so, played back, wait for one).
        /// </summary>
        public static bool IsTargetAction(string action)
        {
            string verb = action.Trim();
            int space = verb.IndexOf(' ');
            if (space > 0)
                verb = verb.Substring(0, space);
            switch (verb.ToLowerInvariant())
            {
                case "target":
                case "targetself":
                case "lasttarget":
                    return true;
                default:
                    return false;
            }
        }
    }
}
