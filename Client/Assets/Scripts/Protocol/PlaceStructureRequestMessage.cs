using LiteNetLib.Utils;

namespace Multiplayer.Protocol
{
    public struct PlaceStructureRequestMessage : INetSerializable
    {
        public Vec2 Position;

        public void Serialize(NetDataWriter writer)
        {
            Position.Serialize(writer);
        }

        public void Deserialize(NetDataReader reader)
        {
            Position = Vec2.Deserialize(reader);
        }
    }
}
