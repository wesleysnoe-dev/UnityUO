using System;
using System.Collections.Generic;
using System.Globalization;

namespace UOClient.Unity
{
    // No UnityEngine here: the test project compiles this file on its own.

    /// <summary>One cooldown (TazUO's CoolDownBar conditions): a line of journal text that starts a named timer.</summary>
    public struct CooldownDef
    {
        public string Name, Trigger;
        public int Seconds, Hue;
    }

    /// <summary>
    /// The cooldown bars' rules: the saved list ("Name|Trigger text|Seconds|Hue" a line), which journal lines start a
    /// timer, and the timers running. A line that starts a timer already running starts it again from full.
    /// </summary>
    public sealed class Cooldowns
    {
        public const int MaxDefs = 30, MaxNameLength = 20, MaxTriggerLength = 60, MinSeconds = 1, MaxSeconds = 3600;

        /// <summary>The bar's size and the gap between two.</summary>
        public const int BarWidth = 180, BarHeight = 18, BarGap = 2;

        public static List<CooldownDef> Parse(string list)
        {
            var defs = new List<CooldownDef>();
            foreach (string line in (list ?? "").Split('\n', '\r'))
            {
                string[] p = line.Split('|');
                if (p.Length != 4 || defs.Count >= MaxDefs
                    || !int.TryParse(p[2].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int seconds)
                    || !int.TryParse(p[3].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int hue))
                    continue;
                defs.Add(new CooldownDef
                {
                    Name = Cut(p[0], MaxNameLength), Trigger = Cut(p[1], MaxTriggerLength),
                    Seconds = Math.Max(MinSeconds, Math.Min(MaxSeconds, seconds)), Hue = GameOptions.ClampHue(hue)
                });
            }
            return defs;
        }

        private static string Cut(string s, int max)
        {
            s = (s ?? "").Trim();
            return (s.Length > max ? s.Substring(0, max) : s).Trim();
        }

        public static string Format(IEnumerable<CooldownDef> defs)
        {
            var lines = new List<string>();
            foreach (CooldownDef d in defs)
                lines.Add(Clean(d.Name) + "|" + Clean(d.Trigger) + "|" + d.Seconds.ToString(CultureInfo.InvariantCulture) + "|"
                          + d.Hue.ToString(CultureInfo.InvariantCulture));
            return string.Join("\n", lines);
        }

        private static string Clean(string s) => (s ?? "").Replace("|", "").Replace("\n", "").Replace("\r", "").Trim();

        /// <summary>What a trigger listens to: any journal line, only your own lines ("me:..."), or a buff's name ("buff:...").</summary>
        public enum TriggerKind { Text, OwnText, Buff }

        public const string OwnPrefix = "me:", BuffPrefix = "buff:";

        /// <summary>A trigger's kind and the text it looks for (the prefix taken off).</summary>
        public static (TriggerKind Kind, string Text) Trigger(string trigger)
        {
            string t = (trigger ?? "").Trim();
            if (t.StartsWith(BuffPrefix, StringComparison.OrdinalIgnoreCase))
                return (TriggerKind.Buff, t.Substring(BuffPrefix.Length).Trim());
            if (t.StartsWith(OwnPrefix, StringComparison.OrdinalIgnoreCase))
                return (TriggerKind.OwnText, t.Substring(OwnPrefix.Length).Trim());
            return (TriggerKind.Text, t);
        }

        /// <summary>The line starts this cooldown: its trigger text is in it (case ignored). A blank trigger starts nothing.</summary>
        public static bool Starts(CooldownDef d, string text) => Starts(d, text, true);

        /// <summary>As <see cref="Starts(CooldownDef, string)"/>; a "me:" trigger only for a line of the player's own (<paramref name="own"/>).</summary>
        public static bool Starts(CooldownDef d, string text, bool own)
        {
            (TriggerKind kind, string look) = Trigger(d.Trigger);
            return kind != TriggerKind.Buff && (kind == TriggerKind.Text || own) && look.Length > 0 && text != null
                   && text.IndexOf(look, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>A buff with this name starts the cooldown: a "buff:" trigger whose text is in the name (case ignored).</summary>
        public static bool StartsOnBuff(CooldownDef d, string buffName)
        {
            (TriggerKind kind, string look) = Trigger(d.Trigger);
            return kind == TriggerKind.Buff && look.Length > 0 && buffName != null
                   && buffName.IndexOf(look, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>The time left as the bar shows it: seconds with a tenth under ten, whole seconds above, m:ss from a minute.</summary>
        public static string TimeText(double secondsLeft)
        {
            if (secondsLeft <= 0)
                return "0.0";
            if (secondsLeft < 10)
                return (Math.Floor(secondsLeft * 10) / 10).ToString("0.0", CultureInfo.InvariantCulture);
            int whole = (int)Math.Ceiling(secondsLeft);
            return whole < 60 ? whole.ToString(CultureInfo.InvariantCulture) : (whole / 60) + ":" + (whole % 60).ToString("00", CultureInfo.InvariantCulture);
        }

        /// <summary>The stack's size for <paramref name="bars"/> bars.</summary>
        public static (int Width, int Height) StackSize(int bars) =>
            (BarWidth, bars <= 0 ? 0 : bars * BarHeight + (bars - 1) * BarGap);

        // ---- the timers running ----

        public struct Running
        {
            public CooldownDef Def;
            public double EndsAt;
        }

        private readonly List<Running> _running = new List<Running>();

        public List<CooldownDef> Defs { get; set; } = new List<CooldownDef>();

        /// <summary>A journal line arrived at <paramref name="now"/> (seconds): every cooldown it triggers starts, or starts again.</summary>
        public int OnText(string text, double now) => OnText(text, now, true);

        /// <summary>As <see cref="OnText(string, double)"/>; <paramref name="own"/>: the player said or cast it.</summary>
        public int OnText(string text, double now, bool own) => Start(d => Starts(d, text, own), now);

        /// <summary>A buff appeared on the player at <paramref name="now"/>: the "buff:" cooldowns naming it start.</summary>
        public int OnBuff(string buffName, double now) => Start(d => StartsOnBuff(d, buffName), now);

        private int Start(Func<CooldownDef, bool> starts, double now)
        {
            int started = 0;
            foreach (CooldownDef d in Defs)
            {
                if (!starts(d))
                    continue;
                _running.RemoveAll(r => r.Def.Name == d.Name && r.Def.Trigger == d.Trigger);
                _running.Add(new Running { Def = d, EndsAt = now + d.Seconds });
                started++;
            }
            return started;
        }

        /// <summary>The timers still running at <paramref name="now"/>, the one ending first on top; finished ones are dropped.</summary>
        public List<(CooldownDef Def, double Left, float Fraction)> Active(double now)
        {
            _running.RemoveAll(r => r.EndsAt <= now);
            _running.Sort((a, b) => a.EndsAt.CompareTo(b.EndsAt));
            var list = new List<(CooldownDef, double, float)>();
            foreach (Running r in _running)
            {
                double left = r.EndsAt - now;
                list.Add((r.Def, left, (float)Math.Max(0, Math.Min(1, left / r.Def.Seconds))));
            }
            return list;
        }

        public void Clear() => _running.Clear();
    }
}
