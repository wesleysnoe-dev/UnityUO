using System;
using System.Collections.Generic;

namespace UOClient.Unity
{
    // No UnityEngine here: the test project compiles this file on its own to check the rules.

    /// <summary>
    /// TazUO's nameplates (NameOverheadGump, NameOverHeadManager) as rules: which characters get one, the plate's size and
    /// parts, its colours and opacities, where it sits over the head, culling, overlap avoidance and which plate is under
    /// the mouse. HudPresenter feeds it the world's mobiles (only what the server sent: name, notoriety, hits, and mana /
    /// stamina for ourselves and party members); DemoHud measures the text, lays plates out and draws them; WorldInput
    /// sends a click on a plate to its character. Characters only: TazUO's item and corpse plates are not offered.
    /// </summary>
    public static class NamePlates
    {
        /// <summary>NameOverheadGump's MIN_NAMEPLATE_WIDTH, NAMEPLATE_HORIZONTAL/VERTICAL_PADDING, COLLISION_SPACING; Constants.OBJECT_HANDLES_GUMP_HEIGHT.</summary>
        public const int MinWidth = 60, HorizontalPadding = 4, VerticalPadding = 4, CollisionSpacing = 8, HandlesHeight = 18;

        /// <summary>Profile.NamePlateFixedWidth / NamePlateHealthBarFixedWidth (60-300) and NamePlateHeight (0-80, 0 = automatic).</summary>
        public const int MinFixedWidth = 60, MaxFixedWidth = 300, MaxHeight = 80;

        /// <summary>AdjustPositionToAvoidOverlap's maxIterations.</summary>
        public const int MaxOverlapIterations = 10;

        // NameOverHeadManager's NameOverheadOptions bits for characters (the item and corpse bits are not used here).
        public const int Humanoid = 1 << 8, Monster = 1 << 9, OwnFollowers = 1 << 10, Self = 1 << 11, ExcludeSelf = 1 << 12;
        public const int Innocent = 1 << 13, Ally = 1 << 14, Gray = 1 << 15, Criminal = 1 << 16, Enemy = 1 << 17, Murderer = 1 << 18,
                         Invulnerable = 1 << 19;

        /// <summary>TazUO's "Mobiles only" entry (NameOverheadOptions.AllMobiles): the filter by default.</summary>
        public const int AllMobiles = Humanoid | Monster | OwnFollowers | Self;

        /// <summary>Every character bit; anything else in a saved filter is dropped (Clamp).</summary>
        public const int MobileFlags = Humanoid | Monster | OwnFollowers | Self | ExcludeSelf
                                       | Innocent | Ally | Gray | Criminal | Enemy | Murderer | Invulnerable;

        /// <summary>Profile's NamePlateBackgroundMode: FixedColor, NotorietyColor, EntityNotorietyColor.</summary>
        public const int BackgroundFixed = 0, BackgroundNotoriety = 1, BackgroundEntityNotoriety = 2;

        public static readonly string[] BackgroundModeNames = { "Fixed colour", "Notoriety", "Their notoriety" };

        /// <summary>Profile's NamePlateHealthBarMode: StatusColor (0), then ten fixed colours.</summary>
        public const int FillStatus = 0, FillLast = 10;

        public static readonly string[] FillNames = { "Status", "Green", "Blue", "Red", "Cyan", "Yellow", "Orange", "Purple", "White", "Gray", "Black" };

        // XNA's Color.Green ... Color.Black, as GetHealthFillColor returns them.
        private static readonly Rgb[] FillColors =
        {
            default, new Rgb(0, 128, 0), new Rgb(0, 0, 255), new Rgb(255, 0, 0), new Rgb(0, 255, 255), new Rgb(255, 255, 0),
            new Rgb(255, 165, 0), new Rgb(128, 0, 128), new Rgb(255, 255, 255), new Rgb(128, 128, 128), new Rgb(0, 0, 0),
        };

        /// <summary>NameOverheadGump.MissingHealthBackgroundColor.</summary>
        public static readonly Rgb MissingHealth = new Rgb(14, 14, 14);

        /// <summary>The server's notoriety bytes (TazUO's NotorietyFlag).</summary>
        public const int NotorietyInnocent = 1, NotorietyAlly = 2, NotorietyGray = 3, NotorietyCriminal = 4, NotorietyEnemy = 5,
                         NotorietyMurderer = 6, NotorietyInvulnerable = 7;

        /// <summary>A colour as bytes (Unity-free); the HUD turns it into its own.</summary>
        public readonly struct Rgb : IEquatable<Rgb>
        {
            public readonly byte R, G, B;
            public Rgb(byte r, byte g, byte b) { R = r; G = g; B = b; }
            public Rgb(int r, int g, int b) : this((byte)Clamp255(r), (byte)Clamp255(g), (byte)Clamp255(b)) { }
            public bool Equals(Rgb o) => R == o.R && G == o.G && B == o.B;
            public override bool Equals(object obj) => obj is Rgb o && Equals(o);
            public override int GetHashCode() => R << 16 | G << 8 | B;
            public override string ToString() => $"({R}, {G}, {B})";
        }

        /// <summary>A rectangle (x right, y down) in whatever units the caller uses.</summary>
        public readonly struct Box
        {
            public readonly float X, Y, W, H;
            public Box(float x, float y, float w, float h) { X = x; Y = y; W = w; H = h; }
            public float Right => X + W;
            public float Bottom => Y + H;
            public bool Contains(float px, float py) => px >= X && px < X + W && py >= Y && py < Y + H;
            public bool Intersects(Box o) => X < o.Right && o.X < Right && Y < o.Bottom && o.Y < Bottom;
            public Box Moved(float x, float y) => new Box(x, y, W, H);
            public Box Scaled(float s) => new Box(X * s, Y * s, W * s, H * s);
            public override string ToString() => $"({X}, {Y}, {W}, {H})";
        }

        /// <summary>What a plate is drawn from: one mobile's fields as the server sent them, and how it relates to us.</summary>
        public struct Subject
        {
            public uint Serial;
            public string Name;
            public int Notoriety;
            public bool IsPlayer, InParty, IsHuman, Renamable, Poisoned, YellowHits;
            public int Hits, HitsMax, Mana, ManaMax, Stamina, StaminaMax;
        }

        /// <summary>
        /// Whether the filter lets a character have a plate: NameOverHeadManager.HandleMobileOverhead. Exclude-self wins for
        /// ourselves; then any of type (humanoid body, monster body, one of our pets that is not invulnerable or an enemy),
        /// notoriety, or Self.
        /// </summary>
        public static bool Allowed(int flags, in Subject m)
        {
            if (m.IsPlayer && Has(flags, ExcludeSelf))
                return false;
            if (Has(flags, Humanoid) && m.IsHuman || Has(flags, Monster) && !m.IsHuman)
                return true;
            if (Has(flags, OwnFollowers) && m.Renamable && m.Notoriety != NotorietyInvulnerable && m.Notoriety != NotorietyEnemy)
                return true;
            switch (m.Notoriety)
            {
                case NotorietyInnocent when Has(flags, Innocent):
                case NotorietyAlly when Has(flags, Ally):
                case NotorietyGray when Has(flags, Gray):
                case NotorietyCriminal when Has(flags, Criminal):
                case NotorietyEnemy when Has(flags, Enemy):
                case NotorietyMurderer when Has(flags, Murderer):
                case NotorietyInvulnerable when Has(flags, Invulnerable):
                    return true;
            }
            return Has(flags, Self) && m.IsPlayer;
        }

        private static bool Has(int flags, int bit) => (flags & bit) != 0;

        /// <summary>
        /// NameOverheadGump.Draw's full-health hide: with NamePlateHideAtFullHealth, a character at full health has no plate,
        /// or (with NamePlateHideAtFullHealthInWarmode) only while we are in war mode. Unknown health (no max yet) is never
        /// "full" (TazUO's GetResourcePercent gives 0).
        /// </summary>
        public static bool HiddenAtFullHealth(bool hideAtFullHealth, bool onlyInWarMode, bool warMode, int hits, int hitsMax)
        {
            if (!hideAtFullHealth || Percent(hits, hitsMax) < 1)
                return false;
            return !onlyInWarMode || warMode;
        }

        /// <summary>Whether a character gets a plate this frame (before it is placed on screen).</summary>
        public static bool Shows(GameOptions o, in Subject m, bool warMode, ICollection<uint> closed) =>
            o.nameOverheadToggled && !string.IsNullOrEmpty(m.Name) && Allowed(o.nameOverheadFlags, m)
            && (closed == null || !closed.Contains(m.Serial))
            && !HiddenAtFullHealth(o.namePlateHideAtFullHealth, o.namePlateHideAtFullHealthInWarmode, warMode, m.Hits, m.HitsMax);

        /// <summary>
        /// TazUO's CloseWithRightClick marks a plate closed until its character stops being shown (out of view, or filtered
        /// out) or the plates go off; then it may come back. Drops the closed marks that no longer apply.
        /// </summary>
        public static void PruneClosed(HashSet<uint> closed, bool showing, ICollection<uint> stillEligible)
        {
            if (!showing)
                closed.Clear();
            else
                closed.RemoveWhere(s => !stillEligible.Contains(s));
        }

        // ---- bars ----

        /// <summary>Which resource a bar shows.</summary>
        public enum Resource { Hits, Mana, Stamina }

        /// <summary>
        /// The plate's bars, top down: with NamePlateHealthBar, health when the server has sent a max (no max, no bar: nothing
        /// is made up), and for ourselves and party members (TazUO's showAllResources) mana and stamina too, each only once
        /// its max is known.
        /// </summary>
        public static List<Resource> Bars(GameOptions o, in Subject m)
        {
            var bars = new List<Resource>(3);
            if (!o.namePlateHealthBar)
                return bars;
            if (m.HitsMax > 0)
                bars.Add(Resource.Hits);
            if (m.IsPlayer || m.InParty)
            {
                if (m.ManaMax > 0)
                    bars.Add(Resource.Mana);
                if (m.StaminaMax > 0)
                    bars.Add(Resource.Stamina);
            }
            return bars;
        }

        /// <summary>TazUO's GetResourcePercent: current / max clamped 0-1, 0 with no max.</summary>
        public static double Percent(int current, int max) => max <= 0 ? 0 : Math.Max(0, Math.Min(1, (double)current / max));

        /// <summary>How much of a bar's inside is filled: DrawResourceBar's <c>min(width, round(width x percent))</c>.</summary>
        public static int FillWidth(int innerWidth, double percent)
        {
            if (double.IsNaN(percent) || double.IsInfinity(percent))
                percent = 0;
            percent = Math.Max(0, Math.Min(1, percent));
            return Math.Max(0, Math.Min(innerWidth, (int)Math.Round(innerWidth * percent, MidpointRounding.AwayFromZero)));
        }

        // ---- layout ----

        /// <summary>A plate's measurements (ApplyNameLayout), in UI pixels.</summary>
        public struct Layout
        {
            public int Width, Height;
            public int NameWidth, BarWidth;

            /// <summary>The name band's height: the whole plate unless <see cref="Split"/>.</summary>
            public int NameBandHeight;

            /// <summary>Each bar's height in the split layout (0 in the combined one, where bars share the plate's height).</summary>
            public int BarHeight;
            public int BarCount;
            public bool Split;
        }

        /// <summary>
        /// ApplyNameLayout: the name band is the text's width (at least 60) plus padding, or the fixed width; the bars are
        /// as wide, or their own fixed width; the plate takes the wider. Height: the taller of the 18-pixel handle and the
        /// text, plus padding, or NamePlateHeight when set (never below that). Split (NamePlateSplitHealthBar): the bars
        /// go in their own rows under the name band, 4-8 pixels each; combined: they share the plate's height behind the name.
        /// </summary>
        public static Layout Measure(GameOptions o, float textWidth, float textHeight, int barCount)
        {
            var l = new Layout();
            l.NameWidth = o.namePlateUseFixedWidth
                ? ClampWidth(o.namePlateFixedWidth)
                : Math.Max(MinWidth, (int)Math.Ceiling(textWidth)) + HorizontalPadding;
            l.BarWidth = barCount > 0 && o.namePlateUseFixedHealthBarWidth ? ClampWidth(o.namePlateHealthBarFixedWidth) : l.NameWidth;
            l.Width = Math.Max(l.NameWidth, l.BarWidth);
            int minBand = Math.Max(HandlesHeight, (int)Math.Ceiling(textHeight)) + VerticalPadding;
            l.NameBandHeight = minBand;
            l.BarCount = Math.Max(0, barCount);
            l.Split = o.namePlateSplitHealthBar && l.BarCount > 0;
            if (l.Split)
                l.BarHeight = Math.Max(4, Math.Min(8, minBand / 4));
            int automatic = minBand + (l.Split ? l.BarHeight * l.BarCount + 2 : 0);
            int height = o.namePlateHeight > 0 ? Math.Max(o.namePlateHeight, minBand) : automatic;
            if (l.Split && o.namePlateHeight > 0)
            {
                int minBar = Math.Max(3, l.BarCount);
                height = Math.Max(height, minBand + minBar + 2);
                int area = Math.Max(minBar, height - minBand - 2);
                l.BarHeight = Math.Max(1, area / l.BarCount);
                l.NameBandHeight = height - l.BarHeight * l.BarCount - 2;
            }
            l.Height = height;
            return l;
        }

        private static int ClampWidth(int w) => Math.Max(MinFixedWidth, Math.Min(MaxFixedWidth, w));

        /// <summary>The widest text that fits the fixed width: <see cref="Measure"/>'s name band less the padding.</summary>
        public static int TextRoom(in Layout l) => Math.Max(1, l.NameWidth - HorizontalPadding);

        /// <summary>
        /// SetFittedText: the name as is if it fits, else as many leading characters as fit before "...", else "..." alone.
        /// </summary>
        public static string Fit(string text, float maxWidth, Func<string, float> measure)
        {
            text ??= "";
            if (measure(text) <= maxWidth)
                return text;
            const string ellipsis = "...";
            if (measure(ellipsis) > maxWidth || text.Length == 0)
                return ellipsis;
            int low = 0, high = text.Length;
            while (low < high)
            {
                int mid = (low + high + 1) >> 1;
                if (measure(text.Substring(0, mid) + ellipsis) <= maxWidth)
                    low = mid;
                else
                    high = mid - 1;
            }
            return text.Substring(0, low) + ellipsis;
        }

        /// <summary>
        /// Where a plate goes: centred on the head point, its bottom on it (TazUO: <c>x - Width / 2</c>, <c>y - Height</c>).
        /// </summary>
        public static Box Place(float headX, float headY, in Layout l) => new Box(headX - (l.Width >> 1), headY - l.Height, l.Width, l.Height);

        /// <summary>The HUD's plain name over a head: its bottom 4 UI pixels above the head point, 22 tall.</summary>
        public const int PlainNameBottom = 4, PlainNameHeight = 22;

        /// <summary>
        /// The y the plate's bottom sits on: just over the head point, or, with the plain names over heads on as well (TazUO's
        /// ShowMobileNameOverhead, drawn too), above that name, as TazUO lifts the plate by NameOverheadTextExtraHeight.
        /// </summary>
        public static float PlateBottom(float headY, bool plainNameShown) =>
            headY - PlainNameBottom - (plainNameShown ? PlainNameHeight : 0);

        /// <summary>The name band (background) inside a placed plate: GetNamePlateBackgroundBounds.</summary>
        public static Box NameBounds(Box plate, in Layout l) =>
            new Box(plate.X + Math.Max(0, (l.Width - l.NameWidth) >> 1), plate.Y, Math.Max(1, l.NameWidth), Math.Max(1, l.Split ? l.NameBandHeight : l.Height));

        /// <summary>Bar <paramref name="index"/> inside a placed plate: GetResourceBarBounds (split rows, or the plate's height shared).</summary>
        public static Box BarBounds(Box plate, in Layout l, int index)
        {
            float x = plate.X + Math.Max(0, (l.Width - l.BarWidth) >> 1);
            int width = Math.Max(1, l.BarWidth);
            if (l.Split)
                return new Box(x, plate.Y + l.NameBandHeight + 1 + index * l.BarHeight, width, l.BarHeight);
            int count = Math.Max(1, l.BarCount);
            int barHeight = Math.Max(1, l.Height / count);
            int height = index == count - 1 ? Math.Max(1, l.Height - barHeight * index) : barHeight;
            return new Box(x, plate.Y + index * barHeight, width, height);
        }

        /// <summary>
        /// TazUO draws a plate only wholly inside the game view: anything sticking out of
        /// (0, <paramref name="top"/>) - (<paramref name="width"/>, <paramref name="height"/>) is not drawn.
        /// </summary>
        public static bool OnScreen(Box b, float width, float height, float top = 0) =>
            b.X >= 0 && b.Right <= width && b.Y >= top && b.Bottom <= height;

        /// <summary>
        /// AdjustPositionToAvoidOverlap (NamePlateAvoidOverlap): while the plate overlaps one placed before it, it moves
        /// to just under that one (8 pixels apart), at most ten times. X never changes.
        /// </summary>
        public static Box AvoidOverlap(Box b, IReadOnlyList<Box> placed)
        {
            for (int i = 0; i < MaxOverlapIterations; i++)
            {
                bool moved = false;
                foreach (Box other in placed)
                {
                    if (b.Intersects(other))
                    {
                        b = b.Moved(b.X, other.Bottom + CollisionSpacing);
                        moved = true;
                        break;
                    }
                }
                if (!moved)
                    break;
            }
            return b;
        }

        /// <summary>The serial of the plate under a point, the one drawn last (on top) first; 0 for none.</summary>
        public static uint HitTest(IReadOnlyList<(uint Serial, Box Box)> plates, float x, float y)
        {
            for (int i = plates.Count - 1; i >= 0; i--)
                if (plates[i].Box.Contains(x, y))
                    return plates[i].Serial;
            return 0;
        }

        // ---- colours and opacity ----

        /// <summary>
        /// The background's alpha: NamePlateOpacity, and in the combined layout with bars also times NamePlateHealthBarOpacity
        /// (GetNamePlateBackgroundOpacity), so a see-through bar doesn't leave a solid plate behind it.
        /// </summary>
        public static float BackgroundAlpha(GameOptions o, in Layout l) =>
            Alpha(o.namePlateOpacity) * (!l.Split && l.BarCount > 0 ? Alpha(o.namePlateHealthBarOpacity) : 1f);

        /// <summary>The bars' alpha: NamePlateHealthBarOpacity, times NamePlateOpacity unless split (GetNamePlateResourceOpacity).</summary>
        public static float ResourceAlpha(GameOptions o, in Layout l) =>
            Alpha(o.namePlateHealthBarOpacity) * (l.Split ? 1f : Alpha(o.namePlateOpacity));

        /// <summary>NamePlateBorderOpacity as alpha.</summary>
        public static float BorderAlpha(GameOptions o) => Alpha(o.namePlateBorderOpacity);

        /// <summary>A 0-100 option as 0-1.</summary>
        public static float Alpha(int percent) => Math.Max(0, Math.Min(100, percent)) / 100f;

        /// <summary>
        /// The notoriety the background takes (TryGetBackgroundHue), or -1 for the fixed colour: "Their notoriety" is the
        /// character's own; "Notoriety" shows ourselves as innocent and party members as allies (GetNamePlateNotorietyHue).
        /// </summary>
        public static int ResolveBackgroundNotoriety(int mode, in Subject m)
        {
            switch (mode)
            {
                case BackgroundEntityNotoriety: return m.Notoriety;
                case BackgroundNotoriety: return m.IsPlayer ? NotorietyInnocent : m.InParty ? NotorietyAlly : m.Notoriety;
                default: return -1;
            }
        }

        /// <summary>The fixed background colour (NamePlateBackgroundR/G/B).</summary>
        public static Rgb FixedBackground(GameOptions o) => new Rgb(o.namePlateBackgroundR, o.namePlateBackgroundG, o.namePlateBackgroundB);

        /// <summary>A fixed health fill (modes 1-10); <see cref="FillStatus"/> has none (it is a hue: <see cref="StatusHue"/>).</summary>
        public static Rgb? FixedFill(int mode) => mode > FillStatus && mode <= FillLast ? FillColors[mode] : (Rgb?)null;

        /// <summary>
        /// The status fill's hue (GetStatusHealthHue): over 80 % 0x58, over 40 % 0x30, else 0x21; at full health 0x58 for us
        /// and party members, else -1 (the character's notoriety colour). Poisoned 63; yellow (blessed) hits 353.
        /// </summary>
        public static int StatusHue(in Subject m, double percent)
        {
            bool friendly = m.IsPlayer || m.InParty;
            int hue = percent >= 1 ? (friendly ? 0x0058 : -1) : percent > 0.8 ? 0x0058 : percent > 0.4 ? 0x0030 : 0x0021;
            if (m.Poisoned)
                hue = 63;
            else if (m.YellowHits)
                hue = 353;
            return hue;
        }

        /// <summary>Mana's and stamina's bar hues (GetResourceThresholdHue): over high 0x58, over mid 0x30, else 0x21 (mana .6 / .2, stamina .8 / .5).</summary>
        public static int ThresholdHue(double percent, double high, double mid) => percent > high ? 0x0058 : percent > mid ? 0x0030 : 0x0021;

        public static int ResourceHue(Resource r, double percent) =>
            r == Resource.Mana ? ThresholdHue(percent, 0.6, 0.2) : ThresholdHue(percent, 0.8, 0.5);

        /// <summary>
        /// The colour the name sits on (GetTextSurfaceColor): the background over black at its alpha; in the combined layout
        /// with a health bar, blended toward the fill (at least half health) or the missing-health colour (below half, with
        /// show-missing on) by the bar opacity.
        /// </summary>
        public static Rgb TextSurface(GameOptions o, in Layout l, Rgb background, bool hasHealthBar, double hitsPercent, Rgb fill)
        {
            float alpha = BackgroundAlpha(o, l);
            var surface = new Rgb((int)Math.Round(background.R * alpha), (int)Math.Round(background.G * alpha), (int)Math.Round(background.B * alpha));
            if (!l.Split && hasHealthBar && (hitsPercent >= 0.5 || o.namePlateShowMissingHealth))
                surface = Lerp(surface, hitsPercent >= 0.5 ? fill : MissingHealth, Alpha(o.namePlateHealthBarOpacity));
            return surface;
        }

        /// <summary>GetContrastingTextColor: black on a bright surface (luminance over 0.72), else white.</summary>
        public static bool BlackText(Rgb surface) => (0.2126 * surface.R + 0.7152 * surface.G + 0.0722 * surface.B) / 255.0 > 0.72;

        /// <summary>XNA's Color.Lerp, per channel.</summary>
        public static Rgb Lerp(Rgb a, Rgb b, float t)
        {
            t = Math.Max(0, Math.Min(1, t));
            return new Rgb((int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
        }

        private static int Clamp255(int v) => Math.Max(0, Math.Min(255, v));
    }
}
