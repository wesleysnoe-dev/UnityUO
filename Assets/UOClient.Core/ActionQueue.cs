using System;
using System.Collections.Generic;

namespace UOClient.Core
{
    /// <summary>
    /// Requests that share the server's action delay, sent in order at its pace. ModernUO's UseReq and
    /// Mobile.Lift both refuse to act before Mobile.NextActionTime and both push it on by ActionDelay, so a
    /// double-click right after picking something up (or the other way round) would get "You must wait to
    /// perform another action" or a lift reject. Uses (0x06) and lifts (0x07) are paced; a drop or equip
    /// (0x08, 0x13) has no delay of its own but must follow its lift, so it waits behind anything queued.
    /// </summary>
    public sealed class ActionQueue
    {
        private readonly Queue<(byte[] Packet, bool Paced, uint Use)> _queue = new Queue<(byte[], bool, uint)>();
        private double _nextAt;

        /// <summary>Seconds between paced requests.</summary>
        public double Delay { get; set; } = 1.05;

        public int Count => _queue.Count;

        /// <summary>A double-click; the same thing already waiting isn't queued twice.</summary>
        public void Use(uint serial, byte[] packet)
        {
            foreach (var e in _queue)
                if (e.Use == serial)
                    return;
            _queue.Enqueue((packet, true, serial));
        }

        /// <summary>A request that uses up the action delay (a lift).</summary>
        public void Paced(byte[] packet) => _queue.Enqueue((packet, true, 0));

        /// <summary>A request with no delay of its own that must not overtake what is queued (a drop, an equip).</summary>
        public void Follow(byte[] packet) => _queue.Enqueue((packet, false, 0));

        /// <summary>Sends everything that may go out at <paramref name="now"/>.</summary>
        public void Pump(double now, Action<byte[]> send)
        {
            while (_queue.Count > 0)
            {
                var next = _queue.Peek();
                if (next.Paced && now < _nextAt)
                    return;
                _queue.Dequeue();
                send(next.Packet);
                if (next.Paced)
                    _nextAt = now + Delay;
            }
        }

        public void Clear() => _queue.Clear();
    }
}
