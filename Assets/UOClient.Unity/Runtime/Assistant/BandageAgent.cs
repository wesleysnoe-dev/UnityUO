using System;
using UnityEngine;
using UOClient.Core;
using UOClient.Core.Assistant;

namespace UOClient.Unity.Assistant
{
    /// <summary>
    /// Bandage (TazUO's BandageManager, for the player only): when health falls under a percentage, or on being
    /// poisoned if asked, a bandage from the backpack is used on ourselves, and the next no sooner than the delay (or
    /// the dexterity formula, or the healing buff's end). It stays its hand while a target cursor is up, an item rides
    /// the cursor, other actions are queued, or we are dead.
    /// </summary>
    public sealed class BandageAgent : AssistantAgent
    {
        [Serializable]
        public sealed class Settings
        {
            public bool enabled;
            public int hpPercent = 80;
            public bool whenPoisoned;
            public int delayMs = 3000;
            public bool useDexFormula;
            public bool waitForBuff;
            public bool notWhenHidden;
            public bool notWhenInvulnerable;
            /// <summary>One request with no target cursor (0xBF 0x2C) instead of a double-click and a cursor.</summary>
            public bool direct;
            public int graphic = BandageRules.BandageGraphic;
            public bool useJournal;
            public string journalPhrases = "";
        }

        private const string File = "bandage";
        private const double Margin = 0.25;     // the trip to the server and back, allowed for in the timings
        private const float CursorWait = 3f;    // how long the cursor our double-click brings may take

        private Settings _s = new Settings();
        private readonly BandagePacer _pacer = new BandagePacer();
        private bool _awaitingCursor;
        private float _cursorUntil, _checkAt, _warnAt;
        private string _graphicText;

        public override string Name => "Bandage";
        public override bool Active => _s.enabled;

        public override void Attach()
        {
            _s = Host.Load<Settings>(File);
            Host.Session.BuffAdded += OnBuffAdded;
            Host.Session.BuffRemoved += OnBuffRemoved;
            Host.Session.World.JournalAdded += OnJournal;
        }

        public override void Detach()
        {
            if (Host.Session == null)
                return;
            Host.Session.BuffAdded -= OnBuffAdded;
            Host.Session.BuffRemoved -= OnBuffRemoved;
            Host.Session.World.JournalAdded -= OnJournal;
        }

        private void OnBuffAdded(Buff b)
        {
            if (b.Icon == BandageRules.HealingBuff)
                _pacer.BuffStarted(Time.unscaledTime);
        }

        private void OnBuffRemoved(Buff b)
        {
            if (b.Icon == BandageRules.HealingBuff)
                _pacer.BuffEnded(Time.unscaledTime, _s.waitForBuff, Margin);
        }

        private void OnJournal(JournalEntry e)
        {
            if (_s.enabled && _s.useJournal && BandageRules.JournalTriggers(_s.journalPhrases, e.Text))
                _pacer.Reset();
        }

        public override void Tick(float now)
        {
            GameSession s = Host.Session;
            Mobile p = s.World.Player;
            if (!_s.enabled)
            {
                _awaitingCursor = false;
                return;
            }

            // The cursor our own double-click asked for: a bandage's is a beneficial one, so a cursor of another kind
            // (the player's attack spell, cast in the same moment) is never answered for them.
            if (_awaitingCursor)
            {
                if (now > _cursorUntil)
                    _awaitingCursor = false;
                else if (s.Target is TargetRequest t && t.Local == null && !t.IsMulti && t.Flags == GameSession.TargetBeneficial)
                {
                    s.AnswerTargetUnrecorded(p.Serial);
                    _awaitingCursor = false;
                }
                return;
            }

            if (now < _checkAt)
                return;
            _checkAt = now + 0.1f;
            if (s.PlayerDead || p.IsDead || s.Target != null || s.Holding != 0 || s.QueuedActions > 0)
                return;
            if (!BandageRules.Needed(p.Hits, p.HitsMax, p.Poisoned, _s.hpPercent, _s.whenPoisoned))
                return;
            if ((_s.notWhenHidden && p.IsHidden) || (_s.notWhenInvulnerable && p.YellowHits))
                return;
            if (!_pacer.Ready(now, _s.waitForBuff))
                return;

            uint bandage = FindInBackpack(Host, _s.graphic);
            if (bandage == 0)
            {
                if (now >= _warnAt)
                {
                    _warnAt = now + 30f;
                    Host.Log("Bandage: you have no bandages.");
                }
                _pacer.Sent(now, 2); // look again shortly, not every frame
                return;
            }

            if (_s.direct)
                s.BandageTarget(bandage, p.Serial);
            else
            {
                s.UseUnrecorded(bandage);
                _awaitingCursor = true;
                _cursorUntil = now + CursorWait;
            }
            _pacer.Sent(now, BandageRules.WaitAfter(_s.useDexFormula, _s.waitForBuff, p.Dex, _s.delayMs, Margin));
        }

        /// <summary>
        /// The first item of a graphic in the backpack or in a bag inside it (0 for none). The server tells us a bag's
        /// contents only once it has been opened, so a bandage in a bag never opened this session is not found.
        /// </summary>
        public static uint FindInBackpack(AssistantHost host, int graphic)
        {
            uint pack = host.Backpack;
            if (pack == 0)
                return 0;
            var contained = host.Session.World.Contained;
            foreach (ContainedItem c in contained.Values)
            {
                if (c.Graphic != graphic)
                    continue;
                uint parent = c.Container;
                for (int depth = 0; depth < 8 && parent != 0; depth++)
                {
                    if (parent == pack)
                        return c.Serial;
                    parent = contained.TryGetValue(parent, out ContainedItem bag) ? bag.Container : 0;
                }
            }
            return 0;
        }

        public override void DrawTab(float width)
        {
            AssistantGui.Header("Bandage");
            AssistantGui.Note("Uses a bandage from your backpack on yourself when your health is low. It waits while you have a "
                              + "target cursor up or are holding an item.");
            bool changed = AssistantGui.Toggle(ref _s.enabled, "Bandage myself automatically");

            GUILayout.BeginHorizontal();
            AssistantGui.Cell("When health is below", 150);
            changed |= AssistantGui.Int(ref _s.hpPercent, 50, 1, 100);
            AssistantGui.Cell("%", 30);
            GUILayout.EndHorizontal();
            changed |= AssistantGui.Toggle(ref _s.whenPoisoned, "Also when poisoned");
            changed |= AssistantGui.Toggle(ref _s.notWhenHidden, "Not while hidden (bandaging reveals you)");
            changed |= AssistantGui.Toggle(ref _s.notWhenInvulnerable, "Not while invulnerable (yellow health bar)");

            AssistantGui.Header("Timing");
            GUILayout.BeginHorizontal();
            AssistantGui.Cell("Wait between bandages", 150);
            changed |= AssistantGui.Int(ref _s.delayMs, 60, 0, 60000);
            AssistantGui.Cell("ms (never under 500)", 160);
            GUILayout.EndHorizontal();
            changed |= AssistantGui.Toggle(ref _s.useDexFormula, "Use the dexterity formula instead (11 s, less 1 s per 20 dexterity)");
            changed |= AssistantGui.Toggle(ref _s.waitForBuff, "Wait for the healing buff to end instead (shards that show one)");
            changed |= AssistantGui.Toggle(ref _s.useJournal, "A journal line means the bandage is done:");
            if (_s.useJournal)
            {
                changed |= AssistantGui.Text(ref _s.journalPhrases, Mathf.Max(200f, width - 20f), 200);
                AssistantGui.Note("Part of the line; several separated by ';'. For example: finish applying;barely help");
            }

            AssistantGui.Header("How");
            changed |= AssistantGui.Toggle(ref _s.direct, "Send one direct request, with no target cursor");
            AssistantGui.Note("Off: the bandage is double-clicked and its cursor answered with yourself, as the bandage hotkey does. "
                              + "On: quicker and never shows a cursor, on servers that accept it.");
            GUILayout.BeginHorizontal();
            AssistantGui.Cell("Bandage graphic", 150);
            _graphicText ??= "0x" + _s.graphic.ToString("X4");
            if (AssistantGui.Text(ref _graphicText, 80, 8) && TryGraphic(_graphicText, out int g) && g != _s.graphic)
            {
                _s.graphic = g;
                changed = true;
            }
            int have = Host.Backpack == 0 ? 0 : Host.CountIn(Host.Backpack, _s.graphic, -1);
            AssistantGui.Cell($"{have} in your backpack", 160);
            GUILayout.EndHorizontal();

            if (changed)
                Host.Save(File, _s);
        }

        private static bool TryGraphic(string text, out int graphic)
        {
            text = (text ?? "").Trim();
            bool ok = text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                ? int.TryParse(text.Substring(2), System.Globalization.NumberStyles.HexNumber, null, out graphic)
                : int.TryParse(text, out graphic);
            return ok && graphic > 0 && graphic <= 0xFFFF;
        }

        /// <summary>Self-tests: switched on with these settings, without the window.</summary>
        public void SetForTest(Settings settings) => _s = settings ?? new Settings();
    }
}
