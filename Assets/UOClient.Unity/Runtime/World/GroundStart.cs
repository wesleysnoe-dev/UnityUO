namespace UOClient.Unity
{
    /// <summary>
    /// The options' natural ground and grass, as the game started with them. Like the shadows switch they are built into
    /// the world as it is made, so they are fixed for the run (WorldDemo calls <see cref="StartWith"/> once, before any
    /// world); a change in the options window takes effect at the next start. The launch switches (-liveground,
    /// -terrainstyle natural, -grass) still work and win where they say something.
    /// </summary>
    public static class GroundStart
    {
        /// <summary>The natural ground is on by the option this run.</summary>
        public static bool Natural { get; private set; }

        /// <summary>Grass on the natural ground is on by the option this run.</summary>
        public static bool Grass { get; private set; }

        /// <summary>The ground came on by the option alone (no -liveground): the developer's status strip stays hidden.</summary>
        public static bool ByOptionOnly { get; private set; }

        public static void StartWith(bool naturalGround, bool grass, bool liveGroundArgument)
        {
            Natural = naturalGround;
            Grass = naturalGround && grass;
            ByOptionOnly = naturalGround && !liveGroundArgument;
        }
    }
}
