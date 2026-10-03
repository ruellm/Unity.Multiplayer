using LiteNetLib.Utils;

namespace Multiplayer.Protocol
{
    public struct EntityState : INetSerializable
    {
        public int EntityId;
        public int OwnerId;
        public byte UnitType;
        public int Health;
        public Vec2 Position;

        public void Serialize(NetDataWriter writer)
        {
            writer.Put(EntityId);
            writer.Put(OwnerId);
            writer.Put(UnitType);
            writer.Put(Health);
            Position.Serialize(writer);
        }

        public void Deserialize(NetDataReader reader)
        {
            EntityId = reader.GetInt();
            OwnerId = reader.GetInt();
            UnitType = reader.GetByte();
            Health = reader.GetInt();
            Position = Vec2.Deserialize(reader);
        }
    }
}
