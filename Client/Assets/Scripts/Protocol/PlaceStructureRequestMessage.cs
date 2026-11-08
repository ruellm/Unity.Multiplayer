using LiteNetLib.Utils;

namespace Multiplayer.Protocol
{
    public struct PlaceStructureRequestMessage : INetSerializable
    {
        public Vec2 Position;
        public byte UnitType;
        public byte Yaw;

        public void Serialize(NetDataWriter writer)
        {
            Position.Serialize(writer);
            writer.Put(UnitType);
            writer.Put(Yaw);
        }

        public void Deserialize(NetDataReader reader)
        {
            Position = Vec2.Deserialize(reader);
            UnitType = reader.GetByte();
            Yaw = reader.GetByte();
        }
    }
}
