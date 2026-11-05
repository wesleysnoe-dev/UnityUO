using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UOClient.Core;

namespace UOClient.Unity
{
    /// <summary>
    /// Keyboard macros: each binding is a key (with optional Ctrl/Alt/Shift) and an action, kept in
    /// UserSettings/unity-uo-hotkeys.json (written with defaults on first run). Actions:
    /// <c>cast Heal</c> or <c>cast 4</c>, <c>skill Hiding</c> or <c>skill 21</c>, <c>say text</c>, <c>war</c>, <c>peace</c>,
    /// <c>lasttarget</c>, <c>targetself</c>, <c>bandageself</c>, <c>use 0x0E21</c> (first item of that graphic in the
    /// backpack), <c>backpack</c>, <c>paperdoll</c>, <c>skills</c>, <c>status</c>, <c>journal</c>, <c>cancel</c>.
    /// </summary>
    public sealed class Hotkeys
    {
        [Serializable]
        public sealed class Binding
        {
            public string key = "";
            public string action = "";
        }

        [Serializable]
        private sealed class FileData
        {
            public List<Binding> bindings = new List<Binding>();
        }

        private static readonly Binding[] Defaults =
        {
            new Binding { key = "F2", action = "bandageself" },
            new Binding { key = "F4", action = "lasttarget" },
            new Binding { key = "F5", action = "targetself" },
            new Binding { key = "F6", action = "cast Heal" },
            new Binding { key = "F7", action = "cast Magic Arrow" },
            new Binding { key = "F8", action = "skill Hiding" },
            new Binding { key = "F9", action = "skill Meditation" },
            new Binding { key = "Ctrl+1", action = "say Hail!" },
        };

        private readonly List<(KeyCode Key, bool Ctrl, bool Alt, bool Shift, Binding Binding)> _parsed = new List<(KeyCode, bool, bool, bool, Binding)>();
        private uint _autoTarget;
        private TargetRequest _skipCursor;
        private float _autoTargetUntil;

        public string FilePath { get; }
        public IReadOnlyList<Binding> Bindings => _parsed.Select(p => p.Binding).ToList();

        /// <summary>Called with a line to show in the journal (unknown actions, missing items).</summary>
        public Action<string> Log;

        /// <summary>Client-side actions the macros can open (paperdoll buttons and the like).</summary>
        public Action<string> Command;

        public Hotkeys(string projectRoot)
        {
            FilePath = Path.Combine(projectRoot, "UserSettings", "unity-uo-hotkeys.json");
            Load();
        }

        public void Load()
        {
            var data = new FileData();
            try
            {
                if (File.Exists(FilePath))
                    data = JsonUtility.FromJson<FileData>(File.ReadAllText(FilePath)) ?? data;
                else
                {
                    data.bindings.AddRange(Defaults);
                    Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                    File.WriteAllText(FilePath, JsonUtility.ToJson(data, true));
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"UOClient: hotkeys file unreadable ({e.Message}); using defaults.");
                data.bindings = Defaults.ToList();
            }

            _parsed.Clear();
            foreach (Binding b in data.bindings)
                if (TryParseKey(b.key, out KeyCode k, out bool ctrl, out bool alt, out bool shift))
                    _parsed.Add((k, ctrl, alt, shift, b));
        }

        /// <summary>Runs bindings whose key went down this frame. Call when no text field has focus.</summary>
        public void Update(GameSession session)
        {
            if (session == null || session.State != SessionState.InWorld)
                return;
            bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            bool alt = Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
            bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            foreach (var p in _parsed)
                if (Input.GetKeyDown(p.Key) && p.Ctrl == ctrl && p.Alt == alt && p.Shift == shift)
                    Run(session, p.Binding.action);

            // A queued self/last target (bandages, etc.) answers the next cursor the server raises: not one
            // that was already up when the macro ran (that one belongs to something else, e.g. a spell).
            if (_autoTarget != 0 && session.Target != null && !ReferenceEquals(session.Target, _skipCursor))
            {
                session.TargetObject(_autoTarget);
                _autoTarget = 0;
            }
            if (_autoTarget != 0 && Time.realtimeSinceStartup > _autoTargetUntil)
                _autoTarget = 0;
        }

        /// <summary>True when a binding uses this key without modifiers (so the game's own single-key shortcuts step aside).</summary>
        public bool Claims(KeyCode key) => _parsed.Any(p => p.Key == key && !p.Ctrl && !p.Alt && !p.Shift);

        public void Run(GameSession session, string action)
        {
            string verb = action.Trim(), arg = "";
            int space = verb.IndexOf(' ');
            if (space > 0)
            {
                arg = verb.Substring(space + 1).Trim();
                verb = verb.Substring(0, space);
            }
            switch (verb.ToLowerInvariant())
            {
                case "cast":
                    int spell = SpellId(arg);
                    if (spell > 0) session.CastSpell(spell);
                    else Log?.Invoke($"Unknown spell \"{arg}\".");
                    break;
                case "skill":
                    int skill = int.TryParse(arg, out int n) ? n
                        : Array.FindIndex(GameSession.SkillNames, s => string.Equals(s, arg, StringComparison.OrdinalIgnoreCase));
                    if (skill >= 0) session.UseSkill(skill);
                    else Log?.Invoke($"Unknown skill \"{arg}\".");
                    break;
                case "say": session.Say(arg); break;
                case "war": session.SetWarMode(true); break;
                case "peace": session.SetWarMode(false); break;
                case "togglewar": session.SetWarMode(!session.WarMode); break;
                case "lasttarget":
                    if (!session.TargetLast())
                        QueueTarget(session, session.LastTarget);
                    break;
                case "targetself":
                    if (session.Target != null) session.TargetSelf();
                    else QueueTarget(session, session.World.PlayerSerial);
                    break;
                case "bandageself":
                    if (UseFromBackpack(session, 0x0E21))
                        QueueTarget(session, session.World.PlayerSerial);
                    else
                        Log?.Invoke("You have no bandages.");
                    break;
                case "use":
                    if (!TryParseInt(arg, out int graphic) || !UseFromBackpack(session, graphic))
                        Log?.Invoke($"No {arg} in your backpack.");
                    break;
                case "cancel": session.CancelTarget(); break;
                case "backpack": session.OpenBackpack(); break;
                case "paperdoll": session.OpenPaperdoll(); break;
                case "skills":
                case "status":
                case "journal":
                case "options":
                case "help":
                    Command?.Invoke(verb.ToLowerInvariant());
                    break;
                default:
                    Log?.Invoke($"Unknown macro action \"{action}\".");
                    break;
            }
        }

        private void QueueTarget(GameSession session, uint serial)
        {
            if (serial == 0)
                return;
            _skipCursor = session.Target;
            _autoTarget = serial;
            _autoTargetUntil = Time.realtimeSinceStartup + 3f;
        }

        private static bool UseFromBackpack(GameSession session, int graphic)
        {
            uint pack = session.World.Worn(session.World.Player, 21);
            if (pack == 0)
                return false;
            ContainedItem item = session.World.ItemsIn(pack).FirstOrDefault(i => i.Graphic == graphic);
            if (item == null)
                return false;
            session.Use(item.Serial);
            return true;
        }

        /// <summary>A spell by number, or by name in any school.</summary>
        public static int SpellId(string arg)
        {
            if (int.TryParse(arg, out int id))
                return id;
            foreach (int first in new[] { 1, 101, 201, 401, 501, 601, 678 })
            {
                string[] names = UI.SpellNames.School(first, out _, out _);
                int i = Array.FindIndex(names, s => string.Equals(s, arg, StringComparison.OrdinalIgnoreCase));
                if (i >= 0)
                    return first + i;
            }
            return 0;
        }

        private static bool TryParseInt(string s, out int value)
        {
            if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                return int.TryParse(s.Substring(2), System.Globalization.NumberStyles.HexNumber, null, out value);
            return int.TryParse(s, out value);
        }

        /// <summary>"F5", "Ctrl+1", "Alt+Shift+Q": modifiers, then a KeyCode name (digits mean the number row).</summary>
        public static bool TryParseKey(string text, out KeyCode key, out bool ctrl, out bool alt, out bool shift)
        {
            key = KeyCode.None;
            ctrl = alt = shift = false;
            if (string.IsNullOrWhiteSpace(text))
                return false;
            string[] parts = text.Split('+').Select(p => p.Trim()).ToArray();
            for (int i = 0; i < parts.Length - 1; i++)
            {
                switch (parts[i].ToLowerInvariant())
                {
                    case "ctrl": case "control": ctrl = true; break;
                    case "alt": alt = true; break;
                    case "shift": shift = true; break;
                    default: return false;
                }
            }
            string last = parts[parts.Length - 1];
            if (last.Length == 1 && char.IsDigit(last[0]))
                last = "Alpha" + last;
            return Enum.TryParse(last, true, out key) && key != KeyCode.None;
        }
    }
}
