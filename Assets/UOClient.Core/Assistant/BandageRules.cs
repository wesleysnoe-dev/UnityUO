using System;

namespace UOClient.Core.Assistant
{
    /// <summary>When the bandage tool heals (TazUO's BandageManager.ShouldAttemptHeal and its timings).</summary>
    public static class BandageRules
    {
        /// <summary>The buff icon the server shows while a bandage is being applied (BuffIcon.Healing).</summary>
        public const int HealingBuff = 1069;

        /// <summary>The plain bandage's graphic.</summary>
        public const int BandageGraphic = 0x0E21;

        /// <summary>The shortest wait between two bandages, whatever the settings say.</summary>
        public const double MinimumWait = 0.5;

        /// <summary>
        /// The wait after a bandage when we go by the healing buff. TazUO waits only its ping there, trusting the buff to
        /// arrive; on a shard that sends no such buff that would use a bandage on every check, so we never go below this.
        /// </summary>
        public const double BuffModeWait = 1.5;

        /// <summary>Health as a whole percentage, rounded down as TazUO does; 100 when the maximum is unknown.</summary>
        public static int Percent(int hits, int hitsMax) => hitsMax <= 0 ? 100 : (int)((double)hits / hitsMax * 100);

        /// <summary>Below the threshold, or poisoned when poison counts. Never with an unknown maximum.</summary>
        public static bool Needed(int hits, int hitsMax, bool poisoned, int belowPercent, bool whenPoisoned) =>
            hitsMax > 0 && (Percent(hits, hitsMax) < belowPercent || (whenPoisoned && poisoned));

        /// <summary>How long a bandage on oneself takes by dexterity: 11 seconds less one for every 20 dexterity.</summary>
        public static double DexSeconds(int dexterity) => Math.Max(1, 11 - Math.Max(0, dexterity) / 20);

        /// <summary>
        /// How long to wait after sending a bandage before the next: the dexterity formula, else a short wait for the
        /// buff to show (the buff then holds us), else the delay the player set. A margin for the trip to the server
        /// is added to the first.
        /// </summary>
        public static double WaitAfter(bool useDexFormula, bool waitForBuff, int dexterity, int delayMs, double margin) =>
            Math.Max(MinimumWait, useDexFormula ? DexSeconds(dexterity) + margin : waitForBuff ? BuffModeWait : delayMs / 1000.0);

        /// <summary>Whether a journal line holds one of the player's phrases (separated by ';', any case) that mean "the bandage is done".</summary>
        public static bool JournalTriggers(string phrases, string line)
        {
            if (string.IsNullOrWhiteSpace(phrases) || string.IsNullOrEmpty(line))
                return false;
            foreach (string part in phrases.Split(';'))
            {
                string phrase = part.Trim();
                if (phrase.Length > 0 && line.IndexOf(phrase, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            return false;
        }
    }

    /// <summary>
    /// When the next bandage may go out: not before the wait set by the last one, and (when going by the buff) not
    /// while the healing buff shows. A buff older than <see cref="BuffMaxAge"/> is taken as over, so one missed
    /// "buff removed" cannot stop the healing for good. Times are seconds on any steady clock.
    /// </summary>
    public sealed class BandagePacer
    {
        public const double BuffMaxAge = 15;

        private double _nextAt, _buffAt;
        private bool _buff;

        public bool BuffActive(double now) => _buff && now - _buffAt < BuffMaxAge;

        public bool Ready(double now, bool waitForBuff) => now >= _nextAt && !(waitForBuff && BuffActive(now));

        public void Sent(double now, double wait) => _nextAt = now + wait;

        public void BuffStarted(double now)
        {
            _buff = true;
            _buffAt = now;
        }

        /// <summary>The buff ended: when going by the buff, leave a moment for our health to arrive before deciding again.</summary>
        public void BuffEnded(double now, bool waitForBuff, double margin)
        {
            _buff = false;
            if (waitForBuff && now >= _nextAt)
                _nextAt = now + margin;
        }

        /// <summary>A journal line said the bandage is done: free to go at once.</summary>
        public void Reset()
        {
            _buff = false;
            _nextAt = 0;
        }
    }
}
