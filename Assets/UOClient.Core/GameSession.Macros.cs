using System;
using System.Collections.Generic;
using System.Linq;

namespace UOClient.Core
{
    /// <summary>
    /// The hotkeys' macro actions that need the session's memory: TazUO's MacroType LastSpell / LastSkill
    /// (GameActions.LastSpellIndex / LastSkillIndex), AttackLast, TargetNext (World.FindNext over the mobiles),
    /// a nearest-hostile pick, the weapon's primary / secondary ability, and bow / salute. All of it is this session's
    /// alone and is forgotten when the session drops or is disposed, so nothing carries over to the next login.
    /// </summary>
    public sealed partial class GameSession
    {
        /// <summary>The last spell we asked to cast this session (the "lastspell" macro), or 0.</summary>
        public int LastSpell { get; private set; }

        /// <summary>The last skill we asked to use this session (0-based, the "lastskill" macro), or -1.</summary>
        public int LastSkill { get; private set; } = -1;

        /// <summary>Casts <see cref="LastSpell"/> again; false (nothing sent) when there is none or we are not in the world.</summary>
        public bool CastLastSpell()
        {
            if (LastSpell <= 0 || State != SessionState.InWorld)
                return false;
            CastSpell(LastSpell);
            return true;
        }

        /// <summary>Uses <see cref="LastSkill"/> again; false (nothing sent) when there is none or we are not in the world.</summary>
        public bool UseLastSkill()
        {
            if (LastSkill < 0 || State != SessionState.InWorld)
                return false;
            UseSkill(LastSkill);
            return true;
        }

        /// <summary>
        /// TazUO's AttackLast: attacks <see cref="LastTarget"/> through <see cref="Attack"/> (so the criminal query still
        /// asks first), but only a mobile we can see that is not us. False, nothing sent, otherwise.
        /// </summary>
        public bool AttackLast()
        {
            uint serial = LastTarget;
            if (State != SessionState.InWorld || !IsMobileSerial(serial) || serial == World.PlayerSerial
                || !World.Mobiles.ContainsKey(serial))
                return false;
            Attack(serial);
            return true;
        }

        /// <summary>
        /// TazUO's TargetNext (World.FindNext with ScanTypeObject.Mobiles): the mobile after <see cref="LastTarget"/>,
        /// wrapping round, becomes the last target and the last attack. Nothing is sent (an attack or a cursor answer
        /// does that). The order here is by serial, since our mobiles are not kept in arrival order. 0 when there is no
        /// other mobile in view.
        /// </summary>
        public uint TargetNext() => SelectTarget(NextMobile(World.Mobiles.Values, World.PlayerSerial, LastTarget));

        /// <summary>
        /// The nearest mobile that is not us, an ally, an innocent or invulnerable (TazUO's ScanTypeObject.Hostile, without
        /// its friends list), ties broken by serial; it becomes the last target and last attack, nothing sent. 0 if none.
        /// </summary>
        public uint TargetNearest()
        {
            Mobile p = World.Player;
            return p == null ? 0 : SelectTarget(NearestHostile(World.Mobiles.Values, p));
        }

        private uint SelectTarget(uint serial)
        {
            if (serial == 0 || State != SessionState.InWorld)
                return 0;
            LastTarget = serial;
            SetLastAttack(serial);
            return serial;
        }

        /// <summary>The mobile after <paramref name="last"/> by serial (wrapping), not the player's; the first if last is unknown.</summary>
        public static uint NextMobile(IEnumerable<Mobile> mobiles, uint player, uint last)
        {
            List<uint> serials = mobiles.Select(m => m.Serial).Where(s => s != player && IsMobileSerial(s)).OrderBy(s => s).ToList();
            if (serials.Count == 0)
                return 0;
            foreach (uint s in serials)
                if (s > last)
                    return s;
            return serials[0];
        }

        /// <summary>ClassicUO's NotorietyFlag Invulnerable (ModernUO's Notoriety.Invulnerable).</summary>
        public const byte NotorietyInvulnerable = 7;

        /// <summary>The nearest (by tiles, the larger of dx and dy) hostile-looking mobile to <paramref name="player"/>, or 0.</summary>
        public static uint NearestHostile(IEnumerable<Mobile> mobiles, Mobile player)
        {
            uint best = 0;
            int bestDistance = int.MaxValue;
            foreach (Mobile m in mobiles)
            {
                if (m.Serial == player.Serial || !IsMobileSerial(m.Serial) || m.Notoriety == NotorietyInnocent
                    || m.Notoriety == NotorietyAlly || m.Notoriety == NotorietyInvulnerable)
                    continue;
                int d = Math.Max(Math.Abs(m.X - player.X), Math.Abs(m.Y - player.Y));
                if (d < bestDistance || (d == bestDistance && m.Serial < best))
                {
                    best = m.Serial;
                    bestDistance = d;
                }
            }
            return best;
        }

        /// <summary>
        /// The worn weapon's primary or secondary ability (WeaponAbilities.Of, as the paperdoll book's icons), turned on or
        /// off with <see cref="UseWeaponAbility"/>. <paramref name="animIdOf"/> reads tiledata for weapons the map lacks
        /// (null: the graphic alone). False, nothing sent, when not in the world or the shard has no AoS features.
        /// </summary>
        public bool UseWeaponAbilitySlot(bool secondary, Func<int, int> animIdOf = null)
        {
            var (primary, second) = WeaponAbilities.Of(WornWeapon()?.Graphic ?? 0, animIdOf);
            return UseWeaponAbility(secondary, secondary ? second : primary);
        }

        /// <summary>TazUO's Bow / Salute macros (GameActions.EmoteAction): 0x12 0xC7 "bow" or "salute".</summary>
        public void Bow() => Animate("bow");

        public void Salute() => Animate("salute");

        /// <summary>Forgets the last spell and skill (the session dropped or was disposed).</summary>
        private void ClearMacroMemory()
        {
            LastSpell = 0;
            LastSkill = -1;
        }
    }

    /// <summary>The arguments of the hotkeys' <c>ability</c> and <c>delay</c> / <c>wait</c> actions.</summary>
    public static class MacroArgs
    {
        /// <summary>The longest a played-back <c>delay</c> pauses (a typo'd 600000 must not stall the macro for ten minutes).</summary>
        public const int MaxDelayMs = 30000;

        /// <summary><c>primary</c> or <c>1</c>, <c>secondary</c> or <c>2</c> (any case); false for anything else.</summary>
        public static bool TryParseAbility(string arg, out bool secondary)
        {
            switch ((arg ?? "").Trim().ToLowerInvariant())
            {
                case "primary": case "1": secondary = false; return true;
                case "secondary": case "2": secondary = true; return true;
                default: secondary = false; return false;
            }
        }

        /// <summary>Milliseconds, 0 through <see cref="MaxDelayMs"/>; false for anything else (negative, too long, not a number).</summary>
        public static bool TryParseDelay(string arg, out int ms) =>
            int.TryParse((arg ?? "").Trim(), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out ms)
            && ms <= MaxDelayMs;

        /// <summary>True for <c>delay N</c> / <c>wait N</c> lines (the playback pauses on them instead of running them).</summary>
        public static bool IsDelay(string action, out string arg)
        {
            string verb = (action ?? "").Trim();
            arg = "";
            int space = verb.IndexOf(' ');
            if (space > 0)
            {
                arg = verb.Substring(space + 1).Trim();
                verb = verb.Substring(0, space);
            }
            verb = verb.ToLowerInvariant();
            return verb == "delay" || verb == "wait";
        }
    }
}
