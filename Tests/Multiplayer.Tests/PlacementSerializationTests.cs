using LiteNetLib.Utils;
using Multiplayer.Protocol;
using Xunit;

namespace Multiplayer.Tests
{
    public sealed class PlacementSerializationTests
    {
        static NetDataReader RoundTrip(INetSerializable message)
        {
            NetDataWriter writer = new NetDataWriter();
            message.Serialize(writer);
            return new NetDataReader(writer.CopyData());
        }

        [Fact]
        public void PlaceIntentCarriesTypeAndYaw()
        {
            PlaceStructureRequestMessage sent;
            sent.Position = new Vec2(12.5f, -3.75f);
            sent.UnitType = (byte)UnitType.Wall;
            sent.Yaw = YawByte.FromDegrees(90f);

            NetDataReader reader = RoundTrip(sent);
            PlaceStructureRequestMessage received = default;
            received.Deserialize(reader);

            Assert.Equal(0, reader.AvailableBytes);
            Assert.Equal(12.5f, received.Position.X);
            Assert.Equal(-3.75f, received.Position.Z);
            Assert.Equal((byte)UnitType.Wall, received.UnitType);
            Assert.Equal(90f, YawByte.ToDegrees(received.Yaw));
        }

        [Fact]
        public void SnapshotCarriesYawPerEntity()
        {
            WorldSnapshotMessage sent = new WorldSnapshotMessage();
            sent.Tick = 77;

            EntityState tower = default;
            tower.EntityId = 1;
            tower.UnitType = (byte)UnitType.Tower;
            tower.Position = new Vec2(1f, 2f);
            tower.Yaw = YawByte.FromDegrees(270f);
            sent.Entities.Add(tower);

            EntityState soldier = default;
            soldier.EntityId = 2;
            soldier.UnitType = (byte)UnitType.Soldier;
            soldier.Position = new Vec2(3f, 4f);
            sent.Entities.Add(soldier);

            NetDataReader reader = RoundTrip(sent);
            WorldSnapshotMessage received = new WorldSnapshotMessage();
            received.Deserialize(reader);

            Assert.Equal(0, reader.AvailableBytes);
            Assert.Equal(77u, received.Tick);
            Assert.Equal(2, received.Entities.Count);
            Assert.Equal(270f, YawByte.ToDegrees(received.Entities[0].Yaw));
            Assert.Equal(2f, received.Entities[0].Position.Z);
            Assert.Equal((byte)0, received.Entities[1].Yaw);
            Assert.Equal(4f, received.Entities[1].Position.Z);
        }

        [Theory]
        [InlineData(0f, 0)]
        [InlineData(90f, 64)]
        [InlineData(180f, 128)]
        [InlineData(270f, 192)]
        [InlineData(360f, 0)]
        [InlineData(-90f, 192)]
        [InlineData(450f, 64)]
        public void YawByteWrapsTheCircle(float degrees, int expected)
        {
            Assert.Equal((byte)expected, YawByte.FromDegrees(degrees));
        }
    }
}
