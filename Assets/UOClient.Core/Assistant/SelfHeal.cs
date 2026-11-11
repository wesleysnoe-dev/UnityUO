using System;

namespace UOClient.Core.Assistant
{
    /// <summary>What the self-heal loop acts against (TazUO's ISelfHealEnv), so it can be tested without a game.</summary>
    public interface ISelfHealEnv
    {
        /// <summary>A steady clock in milliseconds.</summary>
        long Now { get; }
        /// <summary>The tool is on and the player is alive.</summary>
        bool CanAct { get; }
        /// <summary>A new cast may start now: no cursor up, nothing on the cursor, mana for it.</summary>
        bool CanCast { get; }
        /// <summary>Poisoned, and curing is wanted.</summary>
        bool IsPoisoned { get; }
        /// <summary>The cursor our cast brings is up.</summary>
        bool IsTargetingAfterCast { get; }
        /// <summary>A cast is known to be under way (false when the client cannot tell).</summary>
        bool IsCasting { get; }
        int HealSpellId { get; }
        int CureSpellId { get; }
        /// <summary>The pause after a heal before the next cast (the cast recovery).</summary>
        long RecastDelayMs { get; }
        /// <summary>How long a cast may take to bring its cursor before it is taken as failed.</summary>
        long CastStartGraceMs { get; }
        /// <summary>How long to wait for the poison to clear before curing again.</summary>
        long CureVerifyMs { get; }
        /// <summary>The pause before casting again after a cast that came to nothing.</summary>
        long InterruptRetryMs { get; }
        /// <summary>When (on <see cref="Now"/>'s clock) the server last said a cast failed; 0 for never.</summary>
        long LastCastFailedAt { get; }
        void Cast(int spellId);
        void TargetSelf();
    }

    /// <summary>
    /// TazUO's SelfHealStateMachine: while wanted, cast Heal (Cure when poisoned), wait for the cursor the cast brings,
    /// target ourselves, pause, and go again. After a Cure it waits to see the poison gone before curing again. A cast
    /// whose cursor never comes inside the grace time, or that the server reports failed, is retried after a short
    /// pause. No longer being wanted only stops the next cast; one under way is finished.
    /// </summary>
    public sealed class SelfHealStateMachine
    {
        public const int MageryHealSpellId = 4;
        public const int MageryCureSpellId = 11;
        public const int MageryGreaterHealSpellId = 29;
        public const int ChivalryCureSpellId = 201;   // Cleanse by Fire
        public const int ChivalryHealSpellId = 202;   // Close Wounds

        public const long DefaultCureVerifyMs = 600;
        public const long DefaultInterruptRetryMs = 100;

        private enum State { Idle, WaitingForCursor, Settle, VerifyingCure, InterruptRetry }

        private State _state = State.Idle;
        private long _stallUntil, _settleUntil, _verifyUntil, _interruptUntil, _castIssuedAt;
        private bool _lastCastWasCure;

        /// <summary>Casts in a row that came to nothing (no cursor, or reported failed); back to 0 when one reaches its cursor.</summary>
        public int Failures { get; private set; }

        /// <summary>Waiting for the cursor of a cast we made.</summary>
        public bool AwaitingCursor => _state == State.WaitingForCursor;

        public bool Idle => _state == State.Idle;

        public void Tick(ISelfHealEnv env, bool wanted)
        {
            if (!env.CanAct)
            {
                _state = State.Idle;
                return;
            }

            switch (_state)
            {
                case State.Idle:
                    if (wanted && env.CanCast)
                    {
                        _lastCastWasCure = env.IsPoisoned;
                        _castIssuedAt = env.Now;
                        env.Cast(_lastCastWasCure ? env.CureSpellId : env.HealSpellId);
                        _stallUntil = env.Now + env.CastStartGraceMs;
                        _state = State.WaitingForCursor;
                    }
                    break;

                case State.WaitingForCursor:
                    if (env.IsTargetingAfterCast)
                    {
                        env.TargetSelf();
                        Failures = 0;
                        if (_lastCastWasCure)
                        {
                            _verifyUntil = env.Now + env.CureVerifyMs;
                            _state = State.VerifyingCure;
                        }
                        else
                        {
                            _settleUntil = env.Now + env.RecastDelayMs;
                            _state = State.Settle;
                        }
                    }
                    else if (env.LastCastFailedAt > _castIssuedAt)
                    {
                        // The server said this cast failed (disturbed, no mana, no reagents...): no point waiting out the grace.
                        Failed(env);
                    }
                    else if (env.IsCasting)
                    {
                        // Known to be under way still: a slow cast is not a failed one.
                        _stallUntil = env.Now + env.CastStartGraceMs;
                    }
                    else if (env.Now > _stallUntil)
                    {
                        Failed(env);
                    }
                    break;

                case State.InterruptRetry:
                    if (env.Now > _interruptUntil)
                        _state = State.Idle;
                    break;

                case State.Settle:
                    if (env.Now > _settleUntil)
                        _state = State.Idle;
                    break;

                case State.VerifyingCure:
                    // Cure again only once the poison is seen to remain after a fair wait.
                    if (!env.IsPoisoned || env.Now > _verifyUntil)
                        _state = State.Idle;
                    break;
            }
        }

        private void Failed(ISelfHealEnv env)
        {
            Failures++;
            _interruptUntil = env.Now + env.InterruptRetryMs;
            _state = State.InterruptRetry;
        }
    }

    /// <summary>
    /// TazUO's SelfHealTimings: the loop's pauses from the player's Faster Casting and Faster Cast Recovery.
    /// Magery: recovery base 6, FC cap 2. Chivalry: recovery base 7, FC cap 4. Recovery = max(0, base - FCR) x 0.25 s;
    /// cast = max(0.25 s, spell's base - FC x 0.25 s). Base cast times: Heal 1.0 s, Cure 1.25 s, Greater Heal 1.75 s,
    /// Close Wounds 1.5 s, Cleanse by Fire 1.0 s.
    /// </summary>
    public static class SelfHealTimings
    {
        private const int GraceMarginMs = 400; // the trip to the server and back, over the longest cast
        private const int MaxPingMs = 1000;

        /// <summary>A retry is never sooner than this, and a run of failures never waits longer than <see cref="MaxRetryMs"/>.</summary>
        public const int MinRetryMs = 250, MaxRetryMs = 10000;

        public static bool IsChivalry(int healSpellId) => healSpellId == SelfHealStateMachine.ChivalryHealSpellId;

        public static int FcCap(bool chivalry) => chivalry ? 4 : 2;
        public static int FcrCap(bool chivalry) => chivalry ? 7 : 6;

        private static double BaseCastSeconds(int spellId)
        {
            switch (spellId)
            {
                case SelfHealStateMachine.MageryCureSpellId: return 1.25;
                case SelfHealStateMachine.MageryGreaterHealSpellId: return 1.75;
                case SelfHealStateMachine.ChivalryHealSpellId: return 1.5;
                default: return 1.0; // Heal, Cleanse by Fire
            }
        }

        /// <summary>The cure that goes with a heal spell: Cleanse by Fire with Close Wounds, Cure with the Magery heals.</summary>
        public static int CureFor(int healSpellId) =>
            IsChivalry(healSpellId) ? SelfHealStateMachine.ChivalryCureSpellId : SelfHealStateMachine.MageryCureSpellId;

        /// <summary>
        /// The pause after a heal (the cast recovery) and the grace a cast gets to bring its cursor (the longer of the
        /// heal's and the cure's cast time, plus a margin). FC and FCR are held to the school's caps.
        /// </summary>
        public static (int RecastDelayMs, int CastStartGraceMs) Compute(int healSpellId, int fc, int fcr)
        {
            bool chivalry = IsChivalry(healSpellId);
            fc = Math.Max(0, Math.Min(fc, FcCap(chivalry)));
            fcr = Math.Max(0, Math.Min(fcr, FcrCap(chivalry)));
            int recoveryMs = Math.Max(0, (chivalry ? 7 : 6) - fcr) * 250;
            double heal = Math.Max(0.25, BaseCastSeconds(healSpellId) - fc * 0.25);
            double cure = Math.Max(0.25, BaseCastSeconds(CureFor(healSpellId)) - fc * 0.25);
            return (recoveryMs, (int)Math.Round(Math.Max(heal, cure) * 1000.0) + GraceMarginMs);
        }

        /// <summary>
        /// How long to wait for the poison to clear before curing again: the player's figure plus two trips to the
        /// server, since the news comes a round trip after we target ourselves.
        /// </summary>
        public static int CureVerifyWindow(int configuredMs, int pingMs) =>
            Math.Max(0, configuredMs) + Math.Max(0, Math.Min(pingMs, MaxPingMs)) * 2;

        /// <summary>
        /// The pause before trying again after a cast that came to nothing. It doubles with each failure in a row, so
        /// a cast that cannot work (no reagents, a no-magic area) is tried every ten seconds, not ten times a second.
        /// </summary>
        public static int RetryDelay(int configuredMs, int failuresInARow)
        {
            long wait = Math.Max(MinRetryMs, configuredMs);
            for (int i = 1; i < failuresInARow && wait < MaxRetryMs; i++)
                wait *= 2;
            return (int)Math.Min(wait, MaxRetryMs);
        }
    }

    /// <summary>What the self-heal tool decides outside the casting loop: whether to cast at all, mana, potions, failed casts.</summary>
    public static class SelfHealRules
    {
        public const int HealPotionGraphic = 0x0F0C, CurePotionGraphic = 0x0F07;

        public enum Potion { None, Heal, Cure }

        /// <summary>What a spell costs before any lower mana cost the player wears (0 for a spell we do not know).</summary>
        public static int ManaCost(int spellId)
        {
            switch (spellId)
            {
                case SelfHealStateMachine.MageryHealSpellId: return 4;
                case SelfHealStateMachine.MageryCureSpellId: return 6;
                case SelfHealStateMachine.MageryGreaterHealSpellId: return 11;
                case SelfHealStateMachine.ChivalryCureSpellId:
                case SelfHealStateMachine.ChivalryHealSpellId: return 10;
                default: return 0;
            }
        }

        /// <summary>Whether the loop should be casting by itself: health under the threshold, or poisoned with curing on.</summary>
        public static bool WantsSpell(int hits, int hitsMax, bool poisoned, int healBelowPercent, bool cure) =>
            hitsMax > 0 && (BandageRules.Percent(hits, hitsMax) < healBelowPercent || (cure && poisoned));

        /// <summary>
        /// Which potion to drink now. A cure potion when poisoned, unless a cure spell is on its way (curing by spell is
        /// on and there is mana for it). Otherwise a heal potion when health is under the potion threshold; a heal
        /// potion does nothing for a poisoned drinker, so not then.
        /// </summary>
        public static Potion WantsPotion(int hits, int hitsMax, bool poisoned, bool useCurePotion, bool spellWillCure,
            bool useHealPotion, int potionBelowPercent)
        {
            if (hitsMax <= 0)
                return Potion.None;
            if (poisoned)
                return useCurePotion && !spellWillCure ? Potion.Cure : Potion.None;
            return useHealPotion && BandageRules.Percent(hits, hitsMax) < potionBelowPercent ? Potion.Heal : Potion.None;
        }

        // The server's lines for a cast that will not happen (TazUO's SpellVisualRangeManager.stopAtClilocs, by their
        // English text since the journal keeps text, not numbers).
        private static readonly string[] FailureLines =
        {
            "concentration is disturbed",
            "Insufficient mana",
            "More reagents are needed",
            "cannot cast this in town",
            "do not have that spell",
            "cast a spell while frozen",
            "cannot cast that spell in this form",
            "not yet recovered from casting",
            "cast a spell while calmed",
        };

        public static bool IsCastFailure(string line)
        {
            if (string.IsNullOrEmpty(line))
                return false;
            foreach (string f in FailureLines)
                if (line.IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            return false;
        }
    }
}
