using System.Collections.Generic;
using LiteNetLib.Utils;

namespace Multiplayer.Protocol
{
    public sealed class WorldSnapshotMessage : INetSerializable
    {
        public uint Tick;
        public readonly List<EntityState> Entities = new List<EntityState>();

        public void Serialize(NetDataWriter writer)
        {
            writer.Put(Tick);
            writer.Put((ushort)Entities.Count);
            for (int i = 0; i < Entities.Count; i++)
                Entities[i].Serialize(writer);
        }

        public void Deserialize(NetDataReader reader)
        {
            Tick = reader.GetUInt();
            int count = reader.GetUShort();
            Entities.Clear();
            for (int i = 0; i < count; i++)
            {
                EntityState state = default;
                state.Deserialize(reader);
                Entities.Add(state);
            }
        }
    }
}
