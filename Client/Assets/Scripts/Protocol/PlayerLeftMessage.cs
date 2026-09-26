using LiteNetLib.Utils;

namespace Multiplayer.Protocol
{
    public struct PlayerLeftMessage : INetSerializable
    {
        public int PlayerId;

        public void Serialize(NetDataWriter writer)
        {
            writer.Put(PlayerId);
        }

        public void Deserialize(NetDataReader reader)
        {
            PlayerId = reader.GetInt();
        }
    }
}
