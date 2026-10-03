using LiteNetLib.Utils;

namespace Multiplayer.Protocol
{
    public struct EntityState : INetSerializable
    {
        public int EntityId;
        public int OwnerId;
        public byte UnitType;
        public int Health;
        public byte ActionState;
        public int TargetEntityId;
        public Vec2 Position;

        public void Serialize(NetDataWriter writer)
        {
            writer.Put(EntityId);
            writer.Put(OwnerId);
            writer.Put(UnitType);
            writer.Put(Health);
            writer.Put(ActionState);
            writer.Put(TargetEntityId);
            Position.Serialize(writer);
        }

        public void Deserialize(NetDataReader reader)
        {
            EntityId = reader.GetInt();
            OwnerId = reader.GetInt();
            UnitType = reader.GetByte();
            Health = reader.GetInt();
            ActionState = reader.GetByte();
            TargetEntityId = reader.GetInt();
            Position = Vec2.Deserialize(reader);
        }
    }
}
