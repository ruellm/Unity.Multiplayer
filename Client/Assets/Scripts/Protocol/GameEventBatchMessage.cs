using System.Collections.Generic;
using LiteNetLib.Utils;

namespace Multiplayer.Protocol
{
    // Payload of MessageId.GameEvent: every event from one server tick, in the order they occurred.
    public sealed class GameEventBatchMessage : INetSerializable
    {
        public readonly List<GameEventMessage> Events = new List<GameEventMessage>();

        // Set when an unknown event type cut the read short. Events holds what came before it.
        public bool Truncated;

        public void Serialize(NetDataWriter writer)
        {
            writer.Put((ushort)Events.Count);
            for (int i = 0; i < Events.Count; i++)
                Events[i].Serialize(writer);
        }

        public void Deserialize(NetDataReader reader)
        {
            Events.Clear();
            Truncated = false;

            int count = reader.GetUShort();
            for (int i = 0; i < count; i++)
            {
                GameEventMessage message = default;
                if (!message.TryDeserialize(reader))
                {
                    Truncated = true;
                    return;
                }

                Events.Add(message);
            }
        }
    }
}
