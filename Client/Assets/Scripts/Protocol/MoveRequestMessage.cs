using LiteNetLib.Utils;

namespace Multiplayer.Protocol
{
    public struct MoveRequestMessage : INetSerializable
    {
        public int EntityId;
        public Vec2 Target;

        public void Serialize(NetDataWriter writer)
        {
            writer.Put(EntityId);
            Target.Serialize(writer);
        }

        public void Deserialize(NetDataReader reader)
        {
            EntityId = reader.GetInt();
            Target = Vec2.Deserialize(reader);
        }
    }
}
