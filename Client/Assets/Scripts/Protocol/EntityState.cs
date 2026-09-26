using LiteNetLib.Utils;

namespace Multiplayer.Protocol
{
    public struct EntityState : INetSerializable
    {
        public int EntityId;
        public int OwnerId;
        public Vec2 Position;

        public void Serialize(NetDataWriter writer)
        {
            writer.Put(EntityId);
            writer.Put(OwnerId);
            Position.Serialize(writer);
        }

        public void Deserialize(NetDataReader reader)
        {
            EntityId = reader.GetInt();
            OwnerId = reader.GetInt();
            Position = Vec2.Deserialize(reader);
        }
    }
}
