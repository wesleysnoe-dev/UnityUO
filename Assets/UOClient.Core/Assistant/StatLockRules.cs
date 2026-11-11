namespace UOClient.Core.Assistant
{
    /// <summary>
    /// Which gain lock a stat should have. The player either names the lock outright, or names a value to reach, as
    /// TazUO's AutoStatLockManager does: under it the stat is set to rise, over it to fall, at it locked.
    /// </summary>
    public static class StatLockRules
    {
        /// <summary>The player's choice for one stat.</summary>
        public const int Leave = 0, Up = 1, Down = 2, Locked = 3, Reach = 4;

        /// <summary>The server's lock states.</summary>
        public const int LockUp = 0, LockDown = 1, LockLocked = 2;

        /// <summary>The lock (0 up, 1 down, 2 locked) the choice asks for, or -1 to leave the stat alone.</summary>
        public static int Wanted(int choice, int current, int goal)
        {
            switch (choice)
            {
                case Up: return LockUp;
                case Down: return LockDown;
                case Locked: return LockLocked;
                case Reach:
                    if (goal <= 0 || current <= 0) // no goal set, or our stats not here yet
                        return -1;
                    return current < goal ? LockUp : current > goal ? LockDown : LockLocked;
                default: return -1;
            }
        }

        public static string LockName(int lockState) =>
            lockState == LockUp ? "up" : lockState == LockDown ? "down" : lockState == LockLocked ? "locked" : "?";
    }
}
