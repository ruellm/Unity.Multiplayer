using LiteNetLib.Utils;
using Multiplayer.Protocol;
using Xunit;

namespace Multiplayer.Tests
{
    public sealed class GameEventMessageTests
    {
        static GameEventBatchMessage RoundTrip(GameEventBatchMessage batch)
        {
            NetDataWriter writer = new NetDataWriter();
            batch.Serialize(writer);

            NetDataReader reader = new NetDataReader(writer.CopyData());
            GameEventBatchMessage result = new GameEventBatchMessage();
            result.Deserialize(reader);
            Assert.Equal(0, reader.AvailableBytes);
            return result;
        }

        [Fact]
        public void BatchOfBothEventTypesSurvivesTheWire()
        {
            GameEventBatchMessage batch = new GameEventBatchMessage();
            batch.Events.Add(GameEventMessage.Died(1234, 42, DeathCause.Explosion));
            batch.Events.Add(GameEventMessage.Exploded(1234, new Vec2(7.5f, -3.25f), ExplosionType.StructureDestroyed));
            batch.Events.Add(GameEventMessage.Died(1234, 43, DeathCause.Shot));

            GameEventBatchMessage result = RoundTrip(batch);

            Assert.False(result.Truncated);
            Assert.Equal(3, result.Events.Count);

            Assert.Equal((byte)GameEventType.EntityDied, result.Events[0].EventType);
            Assert.Equal(1234u, result.Events[0].Tick);
            Assert.Equal(42, result.Events[0].EntityDied.EntityId);
            Assert.Equal((byte)DeathCause.Explosion, result.Events[0].EntityDied.Cause);

            Assert.Equal((byte)GameEventType.Explosion, result.Events[1].EventType);
            Assert.Equal(1234u, result.Events[1].Tick);
            Assert.Equal(7.5f, result.Events[1].Explosion.Position.X);
            Assert.Equal(-3.25f, result.Events[1].Explosion.Position.Z);
            Assert.Equal((byte)ExplosionType.StructureDestroyed, result.Events[1].Explosion.ExplosionType);

            Assert.Equal(43, result.Events[2].EntityDied.EntityId);
            Assert.Equal((byte)DeathCause.Shot, result.Events[2].EntityDied.Cause);
        }

        [Fact]
        public void EmptyBatchSurvivesTheWire()
        {
            Assert.Empty(RoundTrip(new GameEventBatchMessage()).Events);
        }

        [Fact]
        public void UnknownEventTypeKeepsWhatCameBeforeItAndFlagsTheBatch()
        {
            NetDataWriter writer = new NetDataWriter();
            writer.Put((ushort)3);
            GameEventMessage.Died(10, 1, DeathCause.Shot).Serialize(writer);
            writer.Put(11u);
            writer.Put((byte)200);
            writer.Put(12345);
            GameEventMessage.Died(12, 2, DeathCause.Shot).Serialize(writer);

            GameEventBatchMessage result = new GameEventBatchMessage();
            result.Deserialize(new NetDataReader(writer.CopyData()));

            Assert.True(result.Truncated);
            GameEventMessage kept = Assert.Single(result.Events);
            Assert.Equal(1, kept.EntityDied.EntityId);
        }
    }
}
