using LiteNetLib.Utils;

namespace Multiplayer.Protocol
{
    public struct Vec2
    {
        public float X;
        public float Z;

        public Vec2(float x, float z)
        {
            X = x;
            Z = z;
        }

        public void Serialize(NetDataWriter writer)
        {
            writer.Put(X);
            writer.Put(Z);
        }

        public static Vec2 Deserialize(NetDataReader reader)
        {
            Vec2 value;
            value.X = reader.GetFloat();
            value.Z = reader.GetFloat();
            return value;
        }
    }
}
