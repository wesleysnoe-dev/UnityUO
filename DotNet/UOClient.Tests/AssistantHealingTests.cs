using System.Collections.Generic;
using UOClient.Core;
using UOClient.Core.Assistant;

namespace UOClient.Tests;

/// <summary>The assistant's bandage, self heal and stat lock tools: when they act, with what, and how long they wait.</summary>
public class AssistantHealingTests
{
    // ---- bandage ----

    [Theory]
    [InlineData(79, 100, false, 80, false, true)]   // under the threshold
    [InlineData(80, 100, false, 80, false, false)]  // at it
    [InlineData(100, 100, true, 80, false, false)]  // poisoned, but poison does not count
    [InlineData(100, 100, true, 80, true, true)]    // poisoned and it does
    [InlineData(0, 0, true, 80, true, false)]       // our health not known yet
    [InlineData(119, 150, false, 80, false, true)]  // 79.3 % counts as 79
    public void Bandage_NeededBelowThresholdOrPoisoned(int hits, int max, bool poisoned, int below, bool whenPoisoned, bool needed) =>
        Assert.Equal(needed, BandageRules.Needed(hits, max, poisoned, below, whenPoisoned));

    [Theory]
    [InlineData(0, 11)]
    [InlineData(80, 7)]
    [InlineData(100, 6)]
    [InlineData(125, 5)]
    [InlineData(400, 1)] // never less than a second
    public void Bandage_DexFormula(int dex, double seconds) => Assert.Equal(seconds, BandageRules.DexSeconds(dex));

    [Fact]
    public void Bandage_WaitAfter_PicksTheFormulaTheBuffOrTheDelay_AndNeverSpams()
    {
        Assert.Equal(3.0, BandageRules.WaitAfter(false, false, 100, 3000, 0.25));
        Assert.Equal(6.25, BandageRules.WaitAfter(true, false, 100, 3000, 0.25));
        Assert.Equal(BandageRules.BuffModeWait, BandageRules.WaitAfter(false, true, 100, 3000, 0.25));
        Assert.Equal(BandageRules.MinimumWait, BandageRules.WaitAfter(false, false, 100, 0, 0.25));
    }

    [Fact]
    public void Bandage_Pacer_HoldsForTheDelay()
    {
        var pacer = new BandagePacer();
        Assert.True(pacer.Ready(10, false));
        pacer.Sent(10, 3);
        Assert.False(pacer.Ready(12.9, false));
        Assert.True(pacer.Ready(13, false));
    }

    [Fact]
    public void Bandage_Pacer_HoldsWhileTheBuffShows_ButNotForEver()
    {
        var pacer = new BandagePacer();
        pacer.Sent(10, 1.5);
        pacer.BuffStarted(10.2);
        Assert.False(pacer.Ready(12, true));      // the wait is over, the buff is not
        Assert.True(pacer.Ready(12, false));      // not going by the buff: the delay alone counts
        pacer.BuffEnded(16, true, 0.25);
        Assert.False(pacer.Ready(16.1, true));    // a moment for our health to arrive
        Assert.True(pacer.Ready(16.3, true));

        pacer.BuffStarted(20);
        Assert.False(pacer.Ready(34, true));
        Assert.True(pacer.Ready(20 + BandagePacer.BuffMaxAge, true)); // the "removed" never came
    }

    [Fact]
    public void Bandage_Pacer_AJournalLineFreesIt()
    {
        var pacer = new BandagePacer();
        pacer.Sent(10, 8);
        pacer.BuffStarted(10);
        pacer.Reset();
        Assert.True(pacer.Ready(10.1, true));
    }

    [Theory]
    [InlineData("finish applying;barely help", "You finish applying the bandages.", true)]
    [InlineData("finish applying; BARELY HELP ", "You apply the bandages, but they barely help.", true)]
    [InlineData("finish applying", "You begin applying the bandages.", false)]
    [InlineData("", "You finish applying the bandages.", false)]
    [InlineData(";;", "anything", false)]
    [InlineData("finish", null, false)]
    public void Bandage_JournalPhrases(string phrases, string line, bool triggers) =>
        Assert.Equal(triggers, BandageRules.JournalTriggers(phrases, line));

    [Fact]
    public void Bandage_DirectRequest_IsTheExtendedBandageTarget()
    {
        byte[] p = GameSession.BandageTargetRequest(0x40001234, 0x00000ABC);
        Assert.Equal(new byte[] { 0xBF, 0x00, 0x0D, 0x00, 0x2C, 0x40, 0x00, 0x12, 0x34, 0x00, 0x00, 0x0A, 0xBC }, p);
    }

    // ---- self heal: timings ----

    [Theory]
    [InlineData(SelfHealStateMachine.MageryHealSpellId, 0, 0, 1500, 1650)]        // recovery 6 x 250; Cure's 1.25 s is the longer cast
    [InlineData(SelfHealStateMachine.MageryHealSpellId, 2, 6, 0, 1150)]           // capped out
    [InlineData(SelfHealStateMachine.MageryHealSpellId, 9, 9, 0, 1150)]           // over the caps: held to them
    [InlineData(SelfHealStateMachine.MageryGreaterHealSpellId, 0, 0, 1500, 2150)] // Greater Heal's 1.75 s
    [InlineData(SelfHealStateMachine.ChivalryHealSpellId, 0, 0, 1750, 1900)]      // recovery 7 x 250; Close Wounds 1.5 s
    [InlineData(SelfHealStateMachine.ChivalryHealSpellId, 4, 7, 0, 900)]
    public void SelfHeal_Timings_FromFcAndFcr(int spell, int fc, int fcr, int recast, int grace) =>
        Assert.Equal((recast, grace), SelfHealTimings.Compute(spell, fc, fcr));

    [Fact]
    public void SelfHeal_Timings_CureWindowAndRetry()
    {
        Assert.Equal(900, SelfHealTimings.CureVerifyWindow(600, 150));
        Assert.Equal(2600, SelfHealTimings.CureVerifyWindow(600, 5000)); // an absurd ping is held to a second
        Assert.Equal(0, SelfHealTimings.CureVerifyWindow(-5, -5));

        Assert.Equal(250, SelfHealTimings.RetryDelay(100, 0));  // never under the floor
        Assert.Equal(250, SelfHealTimings.RetryDelay(100, 1));
        Assert.Equal(500, SelfHealTimings.RetryDelay(100, 2));
        Assert.Equal(1000, SelfHealTimings.RetryDelay(100, 3));
        Assert.Equal(10000, SelfHealTimings.RetryDelay(100, 30)); // and never over the ceiling
        Assert.Equal(800, SelfHealTimings.RetryDelay(800, 1));
    }

    [Fact]
    public void SelfHeal_TheCureGoesWithTheSchool()
    {
        Assert.Equal(SelfHealStateMachine.MageryCureSpellId, SelfHealTimings.CureFor(SelfHealStateMachine.MageryHealSpellId));
        Assert.Equal(SelfHealStateMachine.MageryCureSpellId, SelfHealTimings.CureFor(SelfHealStateMachine.MageryGreaterHealSpellId));
        Assert.Equal(SelfHealStateMachine.ChivalryCureSpellId, SelfHealTimings.CureFor(SelfHealStateMachine.ChivalryHealSpellId));
    }

    // ---- self heal: rules ----

    [Theory]
    [InlineData(60, 100, false, 70, true, true)]
    [InlineData(70, 100, false, 70, true, false)]
    [InlineData(100, 100, true, 70, true, true)]    // poisoned, curing on
    [InlineData(100, 100, true, 70, false, false)]  // poisoned, curing off
    [InlineData(0, 0, false, 70, true, false)]
    public void SelfHeal_WantsSpell(int hits, int max, bool poisoned, int below, bool cure, bool wants) =>
        Assert.Equal(wants, SelfHealRules.WantsSpell(hits, max, poisoned, below, cure));

    [Theory]
    [InlineData(30, 100, false, true, false, true, 40, SelfHealRules.Potion.Heal)]
    [InlineData(40, 100, false, true, false, true, 40, SelfHealRules.Potion.None)]  // at the threshold
    [InlineData(30, 100, false, true, false, false, 40, SelfHealRules.Potion.None)] // heal potions off
    [InlineData(30, 100, true, true, false, true, 40, SelfHealRules.Potion.Cure)]   // poisoned: the cure first
    [InlineData(30, 100, true, true, true, true, 40, SelfHealRules.Potion.None)]    // a cure spell is coming; a heal potion would do nothing
    [InlineData(30, 100, true, false, false, true, 40, SelfHealRules.Potion.None)]  // cure potions off
    [InlineData(0, 0, true, true, false, true, 40, SelfHealRules.Potion.None)]
    public void SelfHeal_WantsPotion(int hits, int max, bool poisoned, bool curePotion, bool spellWillCure, bool healPotion,
        int below, SelfHealRules.Potion want) =>
        Assert.Equal(want, SelfHealRules.WantsPotion(hits, max, poisoned, curePotion, spellWillCure, healPotion, below));

    [Theory]
    [InlineData("Your concentration is disturbed, thus ruining thy spell.", true)]
    [InlineData("Insufficient mana. You must have at least 11 Mana to use this spell.", true)]
    [InlineData("More reagents are needed for this spell.", true)]
    [InlineData("You have not yet recovered from casting a spell.", true)]
    [InlineData("In Mani", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void SelfHeal_KnowsAFailedCast(string line, bool failed) => Assert.Equal(failed, SelfHealRules.IsCastFailure(line));

    [Fact]
    public void SelfHeal_ManaCosts()
    {
        Assert.Equal(4, SelfHealRules.ManaCost(SelfHealStateMachine.MageryHealSpellId));
        Assert.Equal(6, SelfHealRules.ManaCost(SelfHealStateMachine.MageryCureSpellId));
        Assert.Equal(11, SelfHealRules.ManaCost(SelfHealStateMachine.MageryGreaterHealSpellId));
        Assert.Equal(10, SelfHealRules.ManaCost(SelfHealStateMachine.ChivalryHealSpellId));
        Assert.Equal(0, SelfHealRules.ManaCost(999));
    }

    // ---- self heal: the casting loop ----

    private sealed class Env : ISelfHealEnv
    {
        public long Now { get; set; }
        public bool CanAct { get; set; } = true;
        public bool CanCast { get; set; } = true;
        public bool IsPoisoned { get; set; }
        public bool IsTargetingAfterCast { get; set; }
        public bool IsCasting { get; set; }
        public int HealSpellId => 4;
        public int CureSpellId => 11;
        public long RecastDelayMs => 500;
        public long CastStartGraceMs => 1000;
        public long CureVerifyMs => 600;
        public long InterruptRetryMs => 250;
        public long LastCastFailedAt { get; set; }
        public readonly List<int> Casts = new List<int>();
        public int Targeted;
        public void Cast(int spellId) => Casts.Add(spellId);

        public void TargetSelf()
        {
            Targeted++;
            IsTargetingAfterCast = false; // answering takes the cursor down
        }
    }

    [Fact]
    public void Loop_CastsWaitsForTheCursorTargetsSelfAndPauses()
    {
        var env = new Env();
        var m = new SelfHealStateMachine();
        m.Tick(env, false);
        Assert.Empty(env.Casts);              // not wanted: nothing

        m.Tick(env, true);
        Assert.Equal(new[] { 4 }, env.Casts); // Heal
        Assert.True(m.AwaitingCursor);
        env.Now = 300;
        m.Tick(env, true);
        Assert.Equal(0, env.Targeted);        // no cursor yet, and no second cast
        Assert.Single(env.Casts);

        env.Now = 900;
        env.IsTargetingAfterCast = true;
        m.Tick(env, true);
        Assert.Equal(1, env.Targeted);

        env.Now = 1400;                       // the recovery pause: strictly after it
        m.Tick(env, true);
        m.Tick(env, true);
        Assert.Single(env.Casts);
        env.Now = 1401;
        m.Tick(env, true);                    // back to idle
        m.Tick(env, true);
        Assert.Equal(new[] { 4, 4 }, env.Casts);
    }

    [Fact]
    public void Loop_NoLongerWanted_StillFinishesTheCastUnderWay()
    {
        var env = new Env();
        var m = new SelfHealStateMachine();
        m.Tick(env, true);
        env.Now = 800;
        env.IsTargetingAfterCast = true;
        m.Tick(env, false);
        Assert.Equal(1, env.Targeted);
        env.Now = 5000;
        m.Tick(env, false);
        m.Tick(env, false);
        Assert.Single(env.Casts);
    }

    [Fact]
    public void Loop_DoesNotCastOverACursorOrAHeldItem()
    {
        var env = new Env { CanCast = false };
        var m = new SelfHealStateMachine();
        m.Tick(env, true);
        Assert.Empty(env.Casts);
        Assert.True(m.Idle);
        env.CanCast = true;
        m.Tick(env, true);
        Assert.Single(env.Casts);
    }

    [Fact]
    public void Loop_Poisoned_CuresAndWaitsToSeeItGone()
    {
        var env = new Env { IsPoisoned = true };
        var m = new SelfHealStateMachine();
        m.Tick(env, true);
        Assert.Equal(new[] { 11 }, env.Casts);
        env.Now = 900;
        env.IsTargetingAfterCast = true;
        m.Tick(env, true);

        env.Now = 1400;                 // still poisoned, inside the wait: no second cure
        m.Tick(env, true);
        m.Tick(env, true);
        Assert.Single(env.Casts);

        env.IsPoisoned = false;         // gone: the heal follows at once
        m.Tick(env, true);
        m.Tick(env, true);
        Assert.Equal(new[] { 11, 4 }, env.Casts);
    }

    [Fact]
    public void Loop_PoisonThatStays_IsCuredAgainAfterTheWait()
    {
        var env = new Env { IsPoisoned = true };
        var m = new SelfHealStateMachine();
        m.Tick(env, true);
        env.Now = 900;
        env.IsTargetingAfterCast = true;
        m.Tick(env, true);
        env.Now = 1501;
        m.Tick(env, true);
        m.Tick(env, true);
        Assert.Equal(new[] { 11, 11 }, env.Casts);
    }

    [Fact]
    public void Loop_ACursorThatNeverComes_IsRetriedAfterTheGraceAndThePause()
    {
        var env = new Env();
        var m = new SelfHealStateMachine();
        m.Tick(env, true);
        env.Now = 1000;
        m.Tick(env, true);
        Assert.Equal(0, m.Failures);    // the grace is not over at its last millisecond
        env.Now = 1001;
        m.Tick(env, true);
        Assert.Equal(1, m.Failures);
        env.Now = 1251;
        m.Tick(env, true);
        Assert.Single(env.Casts);       // the pause
        env.Now = 1252;
        m.Tick(env, true);
        m.Tick(env, true);
        Assert.Equal(2, env.Casts.Count);

        env.Now = 1500;                 // this one works: the count starts over
        env.IsTargetingAfterCast = true;
        m.Tick(env, true);
        Assert.Equal(0, m.Failures);
    }

    [Fact]
    public void Loop_AReportedFailure_SkipsTheGrace()
    {
        var env = new Env { LastCastFailedAt = 50 }; // an old failure, before this cast
        var m = new SelfHealStateMachine();
        env.Now = 100;
        m.Tick(env, true);
        env.Now = 200;
        m.Tick(env, true);
        Assert.Equal(0, m.Failures);
        env.LastCastFailedAt = 250;
        env.Now = 260;
        m.Tick(env, true);
        Assert.Equal(1, m.Failures);
        env.Now = 511;
        m.Tick(env, true);
        m.Tick(env, true);
        Assert.Equal(2, env.Casts.Count);
    }

    [Fact]
    public void Loop_ACastKnownToBeUnderWay_IsNotTakenAsFailed()
    {
        var env = new Env { IsCasting = true };
        var m = new SelfHealStateMachine();
        m.Tick(env, true);
        env.Now = 3000;
        m.Tick(env, true);
        Assert.Equal(0, m.Failures);
        env.IsCasting = false;
        env.Now = 4000;                 // the grace was pushed on to 4000
        m.Tick(env, true);
        Assert.Equal(0, m.Failures);
        env.Now = 4001;
        m.Tick(env, true);
        Assert.Equal(1, m.Failures);
    }

    [Fact]
    public void Loop_DeadOrSwitchedOff_GoesIdle()
    {
        var env = new Env();
        var m = new SelfHealStateMachine();
        m.Tick(env, true);
        env.CanAct = false;
        env.IsTargetingAfterCast = true;
        m.Tick(env, true);
        Assert.Equal(0, env.Targeted);
        Assert.True(m.Idle);
    }

    // ---- stat lock ----

    [Theory]
    [InlineData(StatLockRules.Leave, 90, 100, -1)]
    [InlineData(StatLockRules.Up, 90, 0, 0)]
    [InlineData(StatLockRules.Down, 90, 0, 1)]
    [InlineData(StatLockRules.Locked, 90, 0, 2)]
    [InlineData(StatLockRules.Reach, 90, 100, 0)]   // under the value: up
    [InlineData(StatLockRules.Reach, 110, 100, 1)]  // over it: down
    [InlineData(StatLockRules.Reach, 100, 100, 2)]  // there: locked
    [InlineData(StatLockRules.Reach, 90, 0, -1)]    // no value given
    [InlineData(StatLockRules.Reach, 0, 100, -1)]   // our stats not here yet
    [InlineData(99, 90, 100, -1)]                   // a choice from a damaged settings file
    public void StatLock_Wanted(int choice, int current, int goal, int wanted) =>
        Assert.Equal(wanted, StatLockRules.Wanted(choice, current, goal));
}
