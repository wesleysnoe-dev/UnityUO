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
    /// <c>cast Heal</c> or <c>cast 4</c>, <c>skill Hiding</c> or <c>skill 21</c>, <c>say text</c> (and <c>emote</c>,
    /// <c>whisper</c>, <c>yell</c>), <c>spellbook</c> (or <c>spellbook necromancy</c>), <c>war</c>, <c>peace</c>,
    /// <c>lasttarget</c>, <c>targetself</c>, <c>target 0x...</c>, <c>bandageself</c>, <c>use 0x0E21</c> (first item of
    /// that graphic in the backpack), <c>useobject 0x...</c> (that serial), <c>lastobject</c>, <c>backpack</c>,
    /// <c>paperdoll</c>, <c>skills</c>, <c>status</c>, <c>journal</c>, <c>party</c>, <c>worldmap</c>, <c>minimap</c>,
    /// <c>worldchat</c>, <c>logout</c> (asks first), <c>cancel</c>, <c>attacklast</c>, <c>targetnext</c>,
    /// <c>targetnearest</c> (nearest hostile), <c>lastspell</c>, <c>lastskill</c>, <c>ability primary</c> / <c>secondary</c>
    /// (or 1 / 2), <c>bow</c>, <c>salute</c>, <c>opendoor</c>, <c>delay 500</c> / <c>wait 500</c> (milliseconds; pauses a
    /// played-back macro only, alone on a key it does nothing), and <c>record</c> (start/stop
    /// recording what you do, kept in the file as <c>recorded</c>) and <c>playmacro</c> (play it back; each target
    /// step waits for the next cursor). None of the newer actions has a default key. The options window's Macros page edits the bindings (<see cref="SetBindings"/> on
    /// Apply, <see cref="SaveBindings"/> on Okay; MacroDraft).
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
            public List<string> recorded = new List<string>();
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
            new Binding { key = "F3", action = "lastobject" },
            new Binding { key = "F10", action = "record" },
            new Binding { key = "F11", action = "playmacro" },
        };

        /// <summary>How long a played-back target step waits for its cursor before the rest of the macro is dropped.</summary>
        private const float PlaybackTargetWait = 5f;

        private readonly List<(KeyCode Key, bool Ctrl, bool Alt, bool Shift, Binding Binding)> _parsed = new List<(KeyCode, bool, bool, bool, Binding)>();
        private FileData _data = new FileData();
        private bool _readOnly;
        private uint _autoTarget;
        private TargetRequest _skipCursor;
        private float _autoTargetUntil;
        private readonly Queue<string> _playback = new Queue<string>();
        private TargetRequest _playbackSkip;
        private float _playbackWaitUntil;
        private float _playbackDelayUntil; // a played-back "delay" holds the next step until then
        private GameSession _session;      // a new session (another login) drops whatever the last one left queued

        /// <summary>The recorded macro <c>playmacro</c> plays.</summary>
        public IReadOnlyList<string> Recorded => _data.recorded;

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
            _readOnly = false;
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
                _readOnly = true; // never overwrite a file we could not read
            }
            data.recorded ??= new List<string>();
            data.bindings ??= new List<Binding>();
            _data = data;
            Rebind();
        }

        private void Rebind()
        {
            _parsed.Clear();
            foreach (Binding b in _data.bindings)
                if (TryParseKey(b.key, out KeyCode k, out bool ctrl, out bool alt, out bool shift))
                    _parsed.Add((k, ctrl, alt, shift, b));
        }

        /// <summary>Every binding as the file has it, those whose key doesn't parse too (the options window edits these).</summary>
        public IReadOnlyList<Binding> AllBindings => _data.bindings.Select(b => new Binding { key = b.key, action = b.action }).ToList();

        /// <summary>The file could not be read, so it is never written (defaults are in use).</summary>
        public bool ReadOnly => _readOnly;

        /// <summary>Uses these bindings from now on, for this run only; the file is not written (the options window's Apply).</summary>
        public void SetBindings(IEnumerable<Binding> bindings)
        {
            _data.bindings = bindings.Select(b => new Binding { key = b.key ?? "", action = b.action ?? "" }).ToList();
            Rebind();
        }

        /// <summary>
        /// Uses these bindings and writes them, with the recorded macro, to the hotkeys file (the options window's Okay,
        /// and only that). False when the file was unreadable at load (never overwritten) or could not be written.
        /// </summary>
        public bool SaveBindings(IEnumerable<Binding> bindings)
        {
            SetBindings(bindings);
            if (_readOnly)
                return false;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                string temp = FilePath + ".tmp";
                File.WriteAllText(temp, JsonUtility.ToJson(_data, true));
                if (File.Exists(FilePath))
                    File.Replace(temp, FilePath, null);
                else
                    File.Move(temp, FilePath);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"UOClient: could not write {FilePath} ({e.Message}).");
                return false;
            }
        }

        /// <summary>Runs bindings whose key went down this frame. Call when no text field has focus.</summary>
        public void Update(GameSession session)
        {
            if (session == null || session.State != SessionState.InWorld)
                return;
            if (!ReferenceEquals(session, _session))
            {
                _session = session;
                _playback.Clear();
                _playbackDelayUntil = 0;
                _autoTarget = 0;
            }
            bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            bool alt = Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
            bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            foreach (var p in _parsed)
                if (Input.GetKeyDown(p.Key) && p.Ctrl == ctrl && p.Alt == alt && p.Shift == shift)
                    Run(session, p.Binding.action);
            Pump(session);

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
                // The typed prefixes (GameSession.SpeechMode), so each goes out in its options hue.
                case "emote": session.Say(": " + arg); break;
                case "whisper": session.Say("; " + arg); break;
                case "yell": session.Say("! " + arg); break;
                case "spellbook":
                    int book = SpellbookType(arg);
                    if (book > 0) session.OpenSpellbook(book);
                    else Log?.Invoke($"Unknown spellbook \"{arg}\" (magery, necromancy, chivalry).");
                    break;
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
                case "target":
                    if (!TryParseSerial(arg, out uint targetSerial))
                        Log?.Invoke($"Bad target \"{arg}\".");
                    else if (session.Target != null)
                        session.TargetObject(targetSerial);
                    else
                        QueueTarget(session, targetSerial);
                    break;
                case "useobject":
                    if (TryParseSerial(arg, out uint useSerial)) session.Use(useSerial);
                    else Log?.Invoke($"Bad object \"{arg}\".");
                    break;
                case "lastobject":
                    if (!session.UseLastObject())
                        Log?.Invoke("You have not used anything yet.");
                    break;
                case "attacklast":
                    if (!session.AttackLast())
                        Log?.Invoke("You have no last target to attack.");
                    break;
                case "targetnext":
                    if (session.TargetNext() == 0)
                        Log?.Invoke("No one to target.");
                    break;
                case "targetnearest":
                    if (session.TargetNearest() == 0)
                        Log?.Invoke("No hostile nearby.");
                    break;
                case "lastspell":
                    if (!session.CastLastSpell())
                        Log?.Invoke("You have not cast a spell yet.");
                    break;
                case "lastskill":
                    if (!session.UseLastSkill())
                        Log?.Invoke("You have not used a skill yet.");
                    break;
                case "ability":
                    if (!MacroArgs.TryParseAbility(arg, out bool secondary))
                        Log?.Invoke($"Unknown ability \"{arg}\" (primary or secondary).");
                    else if (!session.UseWeaponAbilitySlot(secondary))
                        Log?.Invoke("Weapon abilities are not available here.");
                    break;
                case "bow": session.Bow(); break;
                case "salute": session.Salute(); break;
                case "opendoor": session.OpenDoor(); break;
                // Pauses only a played-back macro (Pump takes the line before it reaches here); bound to a key alone it does nothing.
                case "delay":
                case "wait":
                    if (!MacroArgs.TryParseDelay(arg, out _))
                        Log?.Invoke($"Bad delay \"{arg}\" (milliseconds, up to {MacroArgs.MaxDelayMs}).");
                    break;
                case "record": ToggleRecording(session); break;
                case "playmacro": Play(session); break;
                case "cancel": session.CancelTarget(); break;
                case "backpack": session.OpenBackpack(); break;
                case "paperdoll": session.OpenPaperdoll(); break;
                case "skills":
                case "status":
                case "journal":
                case "options":
                case "help":
                case "party":
                case "worldmap":
                case "minimap":
                case "worldchat":
                case "logout": // asks "Log out?" first, as the paperdoll's button
                    Command?.Invoke(verb.ToLowerInvariant());
                    break;
                default:
                    Log?.Invoke($"Unknown macro action \"{action}\".");
                    break;
            }
        }

        private void ToggleRecording(GameSession session)
        {
            if (!session.Macros.Recording)
            {
                _playback.Clear();
                session.Macros.Start();
                Log?.Invoke("Recording a macro: press the record key again to stop.");
                return;
            }
            List<string> actions = session.Macros.Stop();
            if (actions.Count == 0)
            {
                Log?.Invoke("Macro recording stopped: nothing recorded, the old macro is kept.");
                return;
            }
            _data.recorded = actions;
            Log?.Invoke($"Macro recorded: {actions.Count} action{(actions.Count == 1 ? "" : "s")}.");
            if (_readOnly)
                return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                File.WriteAllText(FilePath, JsonUtility.ToJson(_data, true));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"UOClient: could not save the recorded macro ({e.Message}).");
            }
        }

        private void Play(GameSession session)
        {
            if (session.Macros.Recording)
            {
                Log?.Invoke("Stop recording before playing the macro.");
                return;
            }
            if (_data.recorded.Count == 0)
            {
                Log?.Invoke("No macro recorded yet.");
                return;
            }
            _playback.Clear();
            foreach (string a in _data.recorded)
                if (!IsMacroControl(a))
                    _playback.Enqueue(a);
            _playbackSkip = session.Target;
            _playbackDelayUntil = 0;
            _playbackWaitUntil = Time.realtimeSinceStartup + PlaybackTargetWait;
        }

        private static bool IsMacroControl(string action)
        {
            string a = action.Trim().ToLowerInvariant();
            return a == "record" || a == "playmacro";
        }

        /// <summary>
        /// Runs the played-back macro: steps go at once, except a target step, which waits for a cursor raised after
        /// the step before it (not one already up), and gives up on the rest after <see cref="PlaybackTargetWait"/>.
        /// </summary>
        private void Pump(GameSession session)
        {
            while (_playback.Count > 0)
            {
                if (Time.realtimeSinceStartup < _playbackDelayUntil)
                    return;
                string next = _playback.Peek();
                if (MacroArgs.IsDelay(next, out string delayArg))
                {
                    _playback.Dequeue();
                    if (MacroArgs.TryParseDelay(delayArg, out int ms))
                        _playbackDelayUntil = Time.realtimeSinceStartup + ms / 1000f;
                    else
                        Log?.Invoke($"Bad delay \"{delayArg}\" skipped.");
                    _playbackWaitUntil = Math.Max(_playbackDelayUntil, Time.realtimeSinceStartup) + PlaybackTargetWait;
                    continue;
                }
                if (MacroRecorder.IsTargetAction(next) && (session.Target == null || ReferenceEquals(session.Target, _playbackSkip)))
                {
                    if (Time.realtimeSinceStartup > _playbackWaitUntil)
                    {
                        _playback.Clear();
                        Log?.Invoke("Macro stopped: no target cursor came.");
                    }
                    return;
                }
                _playback.Dequeue();
                Run(session, next);
                _playbackSkip = session.Target;
                _playbackWaitUntil = Time.realtimeSinceStartup + PlaybackTargetWait;
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

        /// <summary>The 0x12 0x43 book type: none or "magery" 1, "necromancy" 2, "chivalry" 3 (AoS's books), or the number; 0 if unknown.</summary>
        public static int SpellbookType(string arg)
        {
            switch ((arg ?? "").Trim().ToLowerInvariant())
            {
                case "": case "magery": return 1;
                case "necromancy": return 2;
                case "chivalry": return 3;
                default: return int.TryParse(arg, out int n) && n >= 1 && n <= 3 ? n : 0;
            }
        }

        private static bool TryParseInt(string s, out int value)
        {
            if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                return int.TryParse(s.Substring(2), System.Globalization.NumberStyles.HexNumber, null, out value);
            return int.TryParse(s, out value);
        }

        private static bool TryParseSerial(string s, out uint value)
        {
            if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                return uint.TryParse(s.Substring(2), System.Globalization.NumberStyles.HexNumber, null, out value) && value != 0;
            return uint.TryParse(s, out value) && value != 0;
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
