using System;
using System.Collections.Generic;
using UOClient.Net;

namespace UOClient.Core
{
    /// <summary>One icon on the buff bar (a spell, potion or ability in effect on the player).</summary>
    public sealed class Buff
    {
        /// <summary>The server's BuffIcon id (0x3E9 = dismount prevention upwards).</summary>
        public int Icon;

        /// <summary>Whose buff it is (ModernUO only sends the player's own).</summary>
        public uint Serial;

        /// <summary>The name cliloc and the description cliloc (0 for none).</summary>
        public int TitleCliloc, DescriptionCliloc;

        /// <summary>Tab-separated cliloc arguments for both, or "" when there are none.</summary>
        public string Args = "";

        /// <summary>Seconds it lasted when sent, 0 for no timer.</summary>
        public int Duration;

        /// <summary>Session time (seconds, the clock passed to <see cref="GameSession.Update"/>) it runs out, when timed.</summary>
        public double ExpiresAt;

        /// <summary>The name and description as text (cliloc resolved when one is loaded).</summary>
        public string Title, Description;
    }

    /// <summary>The buff bar (0xDF): icons added and removed by the server, each with a name, text and timer.</summary>
    public sealed partial class GameSession
    {
        private readonly List<Buff> _buffs = new List<Buff>();

        /// <summary>The buffs and debuffs in effect, in the order they were added.</summary>
        public IReadOnlyList<Buff> Buffs => _buffs;

        /// <summary>A buff was added, or replaced by a newer one with the same icon.</summary>
        public event Action<Buff> BuffAdded;

        /// <summary>A buff ended (the removed entry).</summary>
        public event Action<Buff> BuffRemoved;

        private void RegisterBuffHandlers()
        {
            _handlers[0xDF] = BuffUpdate;
        }

        /// <summary>Seconds until a timed buff runs out (0 once past), or -1 when it has no timer.</summary>
        public double BuffSecondsLeft(Buff b) => b.Duration <= 0 ? -1 : Math.Max(0, b.ExpiresAt - _now);

        /// <summary>
        /// 0xDF (variable), as ModernUO's BuffIconPackets write it: serial, icon, command (0 remove, else add), 4 zero.
        /// A remove ends there (15 bytes). An add repeats icon, command and 4 zero, then seconds (int16), 3 zero, the
        /// title and description clilocs (int32), then a third cliloc (0), int16 1 and int16 0, the arguments as
        /// zero-terminated little-endian UTF-16 starting with a tab, and int16 1, int16 0; without arguments those
        /// last parts are 10 zero bytes instead.
        /// </summary>
        private void BuffUpdate(PacketReader r)
        {
            uint serial = r.ReadUInt32();
            int icon = r.ReadUInt16();
            int command = r.ReadUInt16();
            r.Skip(4);

            int at = _buffs.FindIndex(b => b.Icon == icon && b.Serial == serial);
            Buff old = at >= 0 ? _buffs[at] : null;
            if (command == 0)
            {
                if (old != null)
                {
                    _buffs.RemoveAt(at);
                    BuffRemoved?.Invoke(old);
                }
                return;
            }

            r.Skip(8); // icon and command again, 4 zero
            var buff = new Buff { Icon = icon, Serial = serial, Duration = r.ReadUInt16() };
            r.Skip(3);
            buff.TitleCliloc = r.ReadInt32();
            buff.DescriptionCliloc = r.ReadInt32();
            r.Skip(8); // third cliloc, 1 (or 0 without arguments), 0
            if (r.Remaining > 4)
            {
                string args = r.ReadUnicodeLEZ();
                buff.Args = args.StartsWith("\t") ? args.Substring(1) : args;
            }
            buff.ExpiresAt = buff.Duration > 0 ? _now + buff.Duration : 0;
            buff.Title = Cliloc != null ? Cliloc.Format(buff.TitleCliloc, buff.Args) : $"#{buff.TitleCliloc}";
            buff.Description = buff.DescriptionCliloc == 0 ? ""
                : Cliloc != null ? Cliloc.Format(buff.DescriptionCliloc, buff.Args) : $"#{buff.DescriptionCliloc}";

            if (old != null)
                _buffs[at] = buff;
            else
                _buffs.Add(buff);
            BuffAdded?.Invoke(buff);
        }
    }
}
