using LiteNetLib.Utils;

namespace Multiplayer.Protocol
{
    public struct BuildUnitRequestMessage : INetSerializable
    {
        public int StructureId;
        public byte UnitType;

        public void Serialize(NetDataWriter writer)
        {
            writer.Put(StructureId);
            writer.Put(UnitType);
        }

        public void Deserialize(NetDataReader reader)
        {
            StructureId = reader.GetInt();
            UnitType = reader.GetByte();
        }
    }
}
