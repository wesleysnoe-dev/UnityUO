using System;
using System.Collections.Generic;
using System.Linq;

namespace UOClient.Unity
{
    // No UnityEngine here: the test project compiles this file on its own to check the rules.
    /// <summary>
    /// The options window's Macros page: an editable copy of the hotkey bindings, as <see cref="OptionsDraft"/> is for the
    /// options. Edits change <see cref="Rows"/> only; <see cref="Apply"/> binds them in the game now (nothing written),
    /// <see cref="Okay"/> applies and writes UserSettings/unity-uo-hotkeys.json, but only when something differs from the
    /// file; <see cref="Cancel"/> drops the edits. Only actions <see cref="Hotkeys"/> runs are known (<see cref="Verbs"/>).
    /// </summary>
    public sealed class MacroDraft
    {
        public sealed class Row
        {
            public string Key = "";
            public string Action = "";

            public Row Clone() => new Row { Key = Key, Action = Action };
        }

        /// <summary>The actions Hotkeys.Run carries out, and nothing else (a test reads Hotkeys.cs to keep them the same).</summary>
        public static readonly string[] Verbs =
        {
            "cast", "skill", "say", "war", "peace", "togglewar", "lasttarget", "targetself", "bandageself", "use", "target",
            "useobject", "lastobject", "record", "playmacro", "cancel", "backpack", "paperdoll", "skills", "status", "journal",
            "options", "help",
        };

        /// <summary>The actions that need something after them (<c>cast Heal</c>, <c>use 0x0E21</c>).</summary>
        public static readonly string[] VerbsWithArgument = { "cast", "skill", "say", "use", "target", "useobject" };

        private readonly Func<IReadOnlyList<Row>> _read;
        private readonly Action<IReadOnlyList<Row>> _apply;
        private readonly Func<IReadOnlyList<Row>, bool> _save;
        private bool _appliedUnsaved; // an Apply put bindings in the game that the file doesn't have yet

        /// <param name="read">The bindings the game uses now.</param>
        /// <param name="apply">Binds rows in the game; writes nothing.</param>
        /// <param name="save">Binds rows and writes the hotkeys file; false if it could not.</param>
        public MacroDraft(Func<IReadOnlyList<Row>> read, Action<IReadOnlyList<Row>> apply, Func<IReadOnlyList<Row>, bool> save)
        {
            _read = read;
            _apply = apply;
            _save = save;
            Rows = Copy(_read());
        }

        public List<Row> Rows { get; private set; }

        /// <summary>The rows differ from what the game binds now (blank rows don't count).</summary>
        public bool Changed => !Same(Cleaned(Rows), Cleaned(_read()));

        /// <summary>The last Okay could not write the file.</summary>
        public bool SaveFailed { get; private set; }

        public void Add() => Rows.Add(new Row());

        public void Remove(int index)
        {
            if (index >= 0 && index < Rows.Count)
                Rows.RemoveAt(index);
        }

        public void Apply()
        {
            if (Changed)
            {
                _apply(Cleaned(Rows));
                _appliedUnsaved = true;
            }
            Rows = Copy(_read());
        }

        /// <summary>Applies and writes the file, unless nothing was edited or applied since it was read.</summary>
        public void Okay()
        {
            if (Changed || _appliedUnsaved)
            {
                SaveFailed = !_save(Cleaned(Rows));
                _appliedUnsaved = SaveFailed;
            }
            Rows = Copy(_read());
        }

        public void Cancel() => Rows = Copy(_read());

        /// <summary>After the file was read again (Reload): the game's bindings are the file's.</summary>
        public void Reloaded()
        {
            _appliedUnsaved = false;
            SaveFailed = false;
            Rows = Copy(_read());
        }

        /// <summary>Trimmed, with rows that have neither key nor action left out.</summary>
        public static List<Row> Cleaned(IEnumerable<Row> rows) =>
            rows.Select(r => new Row { Key = (r.Key ?? "").Trim(), Action = (r.Action ?? "").Trim() })
                .Where(r => r.Key.Length > 0 || r.Action.Length > 0).ToList();

        private static List<Row> Copy(IEnumerable<Row> rows) => rows.Select(r => r.Clone()).ToList();

        private static bool Same(List<Row> a, List<Row> b) =>
            a.Count == b.Count && a.Zip(b, (x, y) => x.Key == y.Key && x.Action == y.Action).All(s => s);

        /// <summary>
        /// What is wrong with a row, or null: the key's modifiers (Ctrl, Alt, Shift, then the key, joined by +) and the
        /// action's verb. Whether the key's name is a real key is Hotkeys.TryParseKey's to say (it needs Unity's KeyCode).
        /// </summary>
        public static string Problem(Row row)
        {
            string key = (row.Key ?? "").Trim(), action = (row.Action ?? "").Trim();
            if (key.Length == 0 && action.Length == 0)
                return null;
            if (key.Length == 0)
                return "No key.";
            string[] parts = key.Split('+').Select(p => p.Trim()).ToArray();
            for (int i = 0; i < parts.Length - 1; i++)
                if (!IsModifier(parts[i]))
                    return $"\"{parts[i]}\" is not Ctrl, Alt or Shift.";
            if (parts[parts.Length - 1].Length == 0)
                return "No key after the +.";
            if (action.Length == 0)
                return "No action.";
            int space = action.IndexOf(' ');
            string verb = (space > 0 ? action.Substring(0, space) : action).ToLowerInvariant();
            string arg = space > 0 ? action.Substring(space + 1).Trim() : "";
            if (Array.IndexOf(Verbs, verb) < 0)
                return $"Unknown action \"{verb}\".";
            if (arg.Length == 0 && Array.IndexOf(VerbsWithArgument, verb) >= 0)
                return $"\"{verb}\" needs something after it.";
            return null;
        }

        private static bool IsModifier(string s)
        {
            switch (s.ToLowerInvariant())
            {
                case "ctrl": case "control": case "alt": case "shift": return true;
                default: return false;
            }
        }
    }
}
