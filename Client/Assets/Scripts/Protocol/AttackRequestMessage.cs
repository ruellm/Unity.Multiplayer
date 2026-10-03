using LiteNetLib.Utils;

namespace Multiplayer.Protocol
{
    public struct AttackRequestMessage : INetSerializable
    {
        public int AttackerId;
        public int TargetEntityId;

        public void Serialize(NetDataWriter writer)
        {
            writer.Put(AttackerId);
            writer.Put(TargetEntityId);
        }

        public void Deserialize(NetDataReader reader)
        {
            AttackerId = reader.GetInt();
            TargetEntityId = reader.GetInt();
        }
    }
}
