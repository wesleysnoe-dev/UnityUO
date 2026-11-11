using System;
using UnityEngine;
using UOClient.Core;
using UOClient.Core.Assistant;

namespace UOClient.Unity.Assistant
{
    /// <summary>
    /// Stat lock (TazUO's AutoStatLockManager): keeps strength, dexterity and intelligence on the gain locks the player
    /// chose. A stat can be held to a lock outright, or given a value to reach (set to rise under it, to fall over it,
    /// locked at it). The locks are put right a few seconds after login, once the server has told us our stats, and
    /// again whenever they come to differ.
    /// </summary>
    public sealed class StatLockAgent : AssistantAgent
    {
        [Serializable]
        public sealed class Settings
        {
            public bool enabled;
            /// <summary>StatLockRules' choices: 0 leave, 1 up, 2 down, 3 locked, 4 reach the value.</summary>
            public int str, dex, intel;
            public int strGoal, dexGoal, intGoal;
        }

        private static readonly string[] Choices = { "Leave", "Up", "Down", "Locked", "Reach" };
        private static readonly string[] StatNames = { "Strength", "Dexterity", "Intelligence" };

        private const string File = "statlock";
        private const float SettleAfterStatus = 3f; // the server's own lock states follow our status; let them arrive first
        private const float Between = 1f;           // one request a second at most
        private const float SameStatAgain = 5f;     // and the same stat no sooner than this

        private Settings _s = new Settings();
        private float _readyAt = -1f, _nextAt;
        private readonly float[] _statAt = new float[3];

        public override string Name => "Stat Lock";
        public override bool Active => _s.enabled;

        public override void Attach() => _s = Host.Load<Settings>(File);

        public override void Tick(float now)
        {
            Mobile p = Host.Session.World.Player;
            if (!_s.enabled || !p.HasFullStatus)
                return;
            if (_readyAt < 0f)
                _readyAt = now + SettleAfterStatus;
            if (now < _readyAt || now < _nextAt)
                return;
            _nextAt = now + Between;
            for (int stat = 0; stat < 3; stat++)
            {
                int wanted = StatLockRules.Wanted(Choice(stat), Value(p, stat), Goal(stat));
                if (wanted < 0 || wanted == Lock(p, stat) || now < _statAt[stat])
                    continue;
                _statAt[stat] = now + SameStatAgain;
                Host.Session.SetStatLock(stat, (byte)wanted);
                Host.Log($"Stat Lock: {StatNames[stat].ToLowerInvariant()} set to {StatLockRules.LockName(wanted)}.");
                return; // the next stat on the next turn
            }
        }

        private int Choice(int stat) => stat == 0 ? _s.str : stat == 1 ? _s.dex : _s.intel;
        private int Goal(int stat) => stat == 0 ? _s.strGoal : stat == 1 ? _s.dexGoal : _s.intGoal;
        private static int Value(Mobile p, int stat) => stat == 0 ? p.Str : stat == 1 ? p.Dex : p.Int;
        private static int Lock(Mobile p, int stat) => stat == 0 ? p.StrLock : stat == 1 ? p.DexLock : p.IntLock;

        public override void DrawTab(float width)
        {
            AssistantGui.Header("Stat Lock");
            AssistantGui.Note("Sets your stat locks for you after you log in, and keeps them that way. Reach: the stat is set to rise "
                              + "while under the value, to fall while over it, and is locked once there.");
            bool changed = AssistantGui.Toggle(ref _s.enabled, "Set my stat locks automatically");

            Mobile p = Host.Session.World.Player;
            GUILayout.Space(6);
            changed |= Row(0, p, ref _s.str, ref _s.strGoal);
            changed |= Row(1, p, ref _s.dex, ref _s.dexGoal);
            changed |= Row(2, p, ref _s.intel, ref _s.intGoal);
            if (p != null && !p.HasFullStatus)
                AssistantGui.Note("Waiting for the server to send your stats.");

            if (changed)
            {
                Array.Clear(_statAt, 0, _statAt.Length); // a new choice goes out without waiting
                Host.Save(File, _s);
            }
        }

        private bool Row(int stat, Mobile p, ref int choice, ref int goal)
        {
            bool changed = false;
            GUILayout.BeginHorizontal();
            AssistantGui.Cell(StatNames[stat], 90);
            int picked = GUILayout.Toolbar(Mathf.Clamp(choice, 0, Choices.Length - 1), Choices, GUILayout.Width(300));
            if (picked != choice)
            {
                choice = picked;
                changed = true;
            }
            if (choice == StatLockRules.Reach)
                changed |= AssistantGui.Int(ref goal, 50, 0, 255);
            else
                GUILayout.Space(54);
            if (p != null && p.HasFullStatus)
                AssistantGui.Cell($"now {Value(p, stat)}, {StatLockRules.LockName(Lock(p, stat))}", 120);
            GUILayout.EndHorizontal();
            return changed;
        }

        /// <summary>Self-tests: switched on with these settings, without the window.</summary>
        public void SetForTest(Settings settings) => _s = settings ?? new Settings();
    }
}
