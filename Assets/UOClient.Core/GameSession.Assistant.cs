using System;

namespace UOClient.Core
{
    /// <summary>What the assistant's agents (auto loot, organizer, dress...) need from the session beyond what a player's own hands use.</summary>
    public sealed partial class GameSession
    {
        /// <summary>
        /// Moves an item (or <paramref name="amount"/> of a stack) into a container without it ever riding the cursor: the
        /// lift waits its turn in the action queue (the server's action delay) and the drop follows it, "onto" the
        /// container (x = y = -1: wherever it fits, a like stack merges).
        /// </summary>
        public void MoveItem(uint serial, int amount, uint container)
        {
            if (State != SessionState.InWorld || serial == 0 || container == 0)
                return;
            _actions.Paced(LiftRequest(serial, amount));
            _actions.Follow(DropRequest(serial, 0xFFFF, 0xFFFF, 0, container));
            SendQueuedActions();
        }

        /// <summary>Picks an item up and wears it, without the cursor (the dress agent): the server picks the layer's slot.</summary>
        public void MoveToLayer(uint serial, byte layer)
        {
            if (State != SessionState.InWorld || serial == 0)
                return;
            _actions.Paced(LiftRequest(serial, 1));
            _actions.Follow(new Net.PacketWriter(0x13, 10).UInt32(serial).Byte(layer).UInt32(World.PlayerSerial).ToArray());
            SendQueuedActions();
        }

        /// <summary>
        /// A target cursor of the client's own (nothing is sent to the server): the next object the player targets goes to
        /// <paramref name="picked"/>; Esc or a right-click cancels it. Used to add things to the assistant's lists by
        /// pointing at them. A cursor the server had up is cancelled first.
        /// </summary>
        public void BeginLocalTarget(Action<uint> picked)
        {
            if (picked == null || State != SessionState.InWorld)
                return;
            if (Target != null)
                CancelTarget();
            Target = new TargetRequest { Type = 0, Local = picked };
            TargetRequested?.Invoke(Target);
        }
    }
}
