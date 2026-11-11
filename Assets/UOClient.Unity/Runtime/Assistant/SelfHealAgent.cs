using System;
using UnityEngine;
using UOClient.Core;
using UOClient.Core.Assistant;

namespace UOClient.Unity.Assistant
{
    /// <summary>
    /// Self heal (TazUO's SelfHealManager and its state machine): casts the chosen heal spell on ourselves, or the cure
    /// when poisoned, waits for the cast's cursor, targets ourselves and goes again, at timings worked out from Faster
    /// Casting and Faster Cast Recovery. It runs while a key is held, as in TazUO, and also by itself while health is
    /// under a threshold. Beside the spells it can drink a heal or cure potion from the backpack.
    /// </summary>
    public sealed class SelfHealAgent : AssistantAgent, ISelfHealEnv
    {
        [Serializable]
        public sealed class Settings
        {
            public bool enabled;
            /// <summary>0 Heal, 1 Greater Heal, 2 Close Wounds.</summary>
            public int spell;
            public bool auto = true;
            public int healBelow = 70;
            public bool cure = true;
            /// <summary>A Unity key name (F5, Mouse3, BackQuote...); empty for none.</summary>
            public string holdKey = "";
            public int fc, fcr;
            public int cureVerifyMs = (int)SelfHealStateMachine.DefaultCureVerifyMs;
            public int interruptRetryMs = (int)SelfHealStateMachine.DefaultInterruptRetryMs;
            public bool healPotion;
            public int potionBelow = 40;
            public int potionDelayMs = 10000;
            public bool curePotion;
        }

        private static readonly string[] SpellNames = { "Heal", "Greater Heal", "Close Wounds" };
        private static readonly int[] SpellIds =
        {
            SelfHealStateMachine.MageryHealSpellId, SelfHealStateMachine.MageryGreaterHealSpellId, SelfHealStateMachine.ChivalryHealSpellId,
        };

        private const string File = "selfheal";
        private const int AssumedPingMs = 150;      // the client does not measure its ping
        private const float CurePotionWait = 2f;    // between cure potions (a failed one is worth another try)
        private const float MinPotionWait = 1.5f;

        private Settings _s = new Settings();
        private readonly SelfHealStateMachine _machine = new SelfHealStateMachine();
        private long _nowMs, _castFailedAt;
        private float _potionAt, _potionCheckAt, _warnAt;
        private KeyCode _key = KeyCode.None;
        private string _keyParsed;

        public override string Name => "Self Heal";
        public override bool Active => _s.enabled;

        public override void Attach()
        {
            _s = Host.Load<Settings>(File);
            Host.Session.World.JournalAdded += OnJournal;
        }

        public override void Detach()
        {
            if (Host.Session != null)
                Host.Session.World.JournalAdded -= OnJournal;
        }

        /// <summary>The server's own lines only (or our own overhead text): another player saying the words must not count.</summary>
        private void OnJournal(JournalEntry e)
        {
            if (!_s.enabled || !_machine.AwaitingCursor)
                return;
            if ((e.Serial == 0xFFFFFFFF || e.Serial == Host.Session.World.PlayerSerial || e.Type == 6) && SelfHealRules.IsCastFailure(e.Text))
                _castFailedAt = (long)(Time.unscaledTimeAsDouble * 1000.0);
        }

        public override void Tick(float now)
        {
            _nowMs = (long)(Time.unscaledTimeAsDouble * 1000.0);
            Mobile p = Host.Session.World.Player;
            bool wanted = _s.enabled && (KeyHeld()
                || (_s.auto && SelfHealRules.WantsSpell(p.Hits, p.HitsMax, p.Poisoned, _s.healBelow, _s.cure)));
            _machine.Tick(this, wanted);
            if (_s.enabled)
                Potions(now, p);
        }

        private int HealSpell => SpellIds[Mathf.Clamp(_s.spell, 0, SpellIds.Length - 1)];

        /// <summary>The held key, unless a text box has the keyboard (typing in chat must not cast).</summary>
        private bool KeyHeld()
        {
            if (_keyParsed != _s.holdKey)
            {
                _keyParsed = _s.holdKey;
                if (string.IsNullOrWhiteSpace(_keyParsed) || !Enum.TryParse(_keyParsed.Trim(), true, out _key))
                    _key = KeyCode.None;
            }
            return _key != KeyCode.None && GUIUtility.keyboardControl == 0 && Input.GetKey(_key);
        }

        private void Potions(float now, Mobile p)
        {
            if (now < _potionCheckAt)
                return;
            _potionCheckAt = now + 0.1f;
            GameSession s = Host.Session;
            if (now < _potionAt || s.PlayerDead || p.IsDead || s.Target != null || s.Holding != 0 || s.QueuedActions > 0)
                return;
            bool spellWillCure = _s.cure && p.Mana >= SelfHealRules.ManaCost(CureSpellId);
            SelfHealRules.Potion want = SelfHealRules.WantsPotion(p.Hits, p.HitsMax, p.Poisoned, _s.curePotion, spellWillCure,
                _s.healPotion, _s.potionBelow);
            if (want == SelfHealRules.Potion.None)
                return;
            bool heal = want == SelfHealRules.Potion.Heal;
            uint potion = BandageAgent.FindInBackpack(Host, heal ? SelfHealRules.HealPotionGraphic : SelfHealRules.CurePotionGraphic);
            if (potion == 0)
            {
                if (now >= _warnAt)
                {
                    _warnAt = now + 30f;
                    Host.Log(heal ? "Self Heal: you have no heal potions." : "Self Heal: you have no cure potions.");
                }
                _potionAt = now + 2f;
                return;
            }
            s.UseUnrecorded(potion);
            _potionAt = now + (heal ? Mathf.Max(MinPotionWait, _s.potionDelayMs / 1000f) : CurePotionWait);
        }

        // ---- what the casting loop sees ----

        long ISelfHealEnv.Now => _nowMs;

        bool ISelfHealEnv.CanAct => _s.enabled && !Host.Session.PlayerDead && !Host.Session.World.Player.IsDead;

        bool ISelfHealEnv.CanCast
        {
            get
            {
                GameSession s = Host.Session;
                Mobile p = s.World.Player;
                int spell = p.Poisoned && _s.cure ? CureSpellId : HealSpell;
                return s.Target == null && s.Holding == 0 && p.Mana >= SelfHealRules.ManaCost(spell);
            }
        }

        bool ISelfHealEnv.IsPoisoned => _s.cure && Host.Session.World.Player.Poisoned;

        // Heal and cure spells bring a beneficial cursor; one of another kind is the player's own business.
        bool ISelfHealEnv.IsTargetingAfterCast =>
            Host.Session.Target is TargetRequest t && t.Local == null && !t.IsMulti && t.Flags == GameSession.TargetBeneficial;

        // The client keeps no "casting" state; the grace time alone decides a cast came to nothing.
        bool ISelfHealEnv.IsCasting => false;

        int ISelfHealEnv.HealSpellId => HealSpell;
        public int CureSpellId => SelfHealTimings.CureFor(HealSpell);

        // At full cast recovery the pause would be nothing; a short one keeps casts from going out back to back.
        long ISelfHealEnv.RecastDelayMs => Math.Max(100, SelfHealTimings.Compute(HealSpell, _s.fc, _s.fcr).RecastDelayMs);
        long ISelfHealEnv.CastStartGraceMs => SelfHealTimings.Compute(HealSpell, _s.fc, _s.fcr).CastStartGraceMs;
        long ISelfHealEnv.CureVerifyMs => SelfHealTimings.CureVerifyWindow(_s.cureVerifyMs, AssumedPingMs);
        long ISelfHealEnv.InterruptRetryMs => SelfHealTimings.RetryDelay(_s.interruptRetryMs, _machine.Failures);
        long ISelfHealEnv.LastCastFailedAt => _castFailedAt;

        void ISelfHealEnv.Cast(int spellId) => Host.Session.CastUnrecorded(spellId);

        void ISelfHealEnv.TargetSelf() => Host.Session.AnswerTargetUnrecorded(Host.Session.World.PlayerSerial);

        // ---- the tab ----

        public override void DrawTab(float width)
        {
            AssistantGui.Header("Self Heal");
            AssistantGui.Note("Casts a heal on yourself, or a cure when you are poisoned, and targets you by itself. It waits while "
                              + "you have a target cursor up or are holding an item, and skips a spell you have no mana for.");
            bool changed = AssistantGui.Toggle(ref _s.enabled, "Enable self heal");

            AssistantGui.Header("Spells");
            GUILayout.BeginHorizontal();
            AssistantGui.Cell("Heal with", 110);
            int spell = GUILayout.Toolbar(Mathf.Clamp(_s.spell, 0, SpellNames.Length - 1), SpellNames, GUILayout.Width(330));
            if (spell != _s.spell)
            {
                _s.spell = spell;
                changed = true;
            }
            GUILayout.EndHorizontal();
            changed |= AssistantGui.Toggle(ref _s.cure, SelfHealTimings.IsChivalry(HealSpell)
                ? "Cure poison first (Cleanse by Fire)" : "Cure poison first (Cure)");
            GUILayout.BeginHorizontal();
            changed |= AssistantGui.Toggle(ref _s.auto, "Cast by itself when health is below", 270);
            changed |= AssistantGui.Int(ref _s.healBelow, 50, 1, 100);
            AssistantGui.Cell("%", 30);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            AssistantGui.Cell("Or while this key is held", 180);
            changed |= AssistantGui.Text(ref _s.holdKey, 100, 20);
            KeyHeld(); // reads the name just typed
            AssistantGui.Cell(string.IsNullOrWhiteSpace(_s.holdKey) ? "(none)" : _key == KeyCode.None ? "not a key name" : "ok", 110);
            GUILayout.EndHorizontal();
            AssistantGui.Note("A key name such as F5, BackQuote, Mouse3 or Keypad0. Held, it heals whatever your health is.");

            AssistantGui.Header("Timing");
            bool chivalry = SelfHealTimings.IsChivalry(HealSpell);
            GUILayout.BeginHorizontal();
            AssistantGui.Cell("Faster Casting", 150);
            changed |= AssistantGui.Int(ref _s.fc, 40, 0, SelfHealTimings.FcCap(chivalry), 0, null);
            AssistantGui.Cell("Faster Cast Recovery", 160);
            changed |= AssistantGui.Int(ref _s.fcr, 40, 0, SelfHealTimings.FcrCap(chivalry), 0, null);
            GUILayout.EndHorizontal();
            (int recast, int grace) = SelfHealTimings.Compute(HealSpell, _s.fc, _s.fcr);
            AssistantGui.Note($"From these: {Math.Max(100, recast)} ms pause after a heal, and a cast is given {grace} ms to bring its cursor "
                              + "before it is tried again.");
            GUILayout.BeginHorizontal();
            AssistantGui.Cell("Cure again after", 150);
            changed |= AssistantGui.Int(ref _s.cureVerifyMs, 60, 0, 5000, 0, null);
            AssistantGui.Cell("ms still poisoned", 160);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            AssistantGui.Cell("Retry a failed cast after", 150);
            changed |= AssistantGui.Int(ref _s.interruptRetryMs, 60, 0, 5000, 0, null);
            AssistantGui.Cell("ms (never under 250; longer after each failure in a row)", 360);
            GUILayout.EndHorizontal();

            AssistantGui.Header("Potions");
            GUILayout.BeginHorizontal();
            changed |= AssistantGui.Toggle(ref _s.healPotion, "Drink a heal potion when health is below", 290);
            changed |= AssistantGui.Int(ref _s.potionBelow, 50, 1, 100);
            AssistantGui.Cell("%", 30);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            AssistantGui.Cell("Wait between heal potions", 180);
            changed |= AssistantGui.Int(ref _s.potionDelayMs, 60, 0, 60000, 0, null);
            AssistantGui.Cell("ms", 30);
            GUILayout.EndHorizontal();
            changed |= AssistantGui.Toggle(ref _s.curePotion, "Drink a cure potion when poisoned and no cure spell can be cast");
            AssistantGui.Note("Potions are taken from your backpack: yellow heal potions and orange cure potions.");

            if (changed)
                Host.Save(File, _s);
        }

        /// <summary>Self-tests: switched on with these settings, without the window.</summary>
        public void SetForTest(Settings settings) => _s = settings ?? new Settings();
    }
}
