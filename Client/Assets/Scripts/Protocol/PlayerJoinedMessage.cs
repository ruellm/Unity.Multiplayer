using LiteNetLib.Utils;

namespace Multiplayer.Protocol
{
    public struct PlayerJoinedMessage : INetSerializable
    {
        public int PlayerId;
        public byte R;
        public byte G;
        public byte B;

        public void Serialize(NetDataWriter writer)
        {
            writer.Put(PlayerId);
            writer.Put(R);
            writer.Put(G);
            writer.Put(B);
        }

        public void Deserialize(NetDataReader reader)
        {
            PlayerId = reader.GetInt();
            R = reader.GetByte();
            G = reader.GetByte();
            B = reader.GetByte();
        }
    }
}
