using LiteNetLib.Utils;

namespace Multiplayer.Protocol
{
    public struct EntityDiedEvent
    {
        public int EntityId;
        public byte Cause;

        public void Serialize(NetDataWriter writer)
        {
            writer.Put(EntityId);
            writer.Put(Cause);
        }

        public void Deserialize(NetDataReader reader)
        {
            EntityId = reader.GetInt();
            Cause = reader.GetByte();
        }
    }

    public struct ExplosionEvent
    {
        public Vec2 Position;
        public byte ExplosionType;

        public void Serialize(NetDataWriter writer)
        {
            Position.Serialize(writer);
            writer.Put(ExplosionType);
        }

        public void Deserialize(NetDataReader reader)
        {
            Position = Vec2.Deserialize(reader);
            ExplosionType = reader.GetByte();
        }
    }

    // Tick-stamped envelope for one event: what happened and when, never how to show it.
    // Only the body named by EventType is meaningful. A new event adds a GameEventType value,
    // a body struct and a case in each switch here.
    public struct GameEventMessage : INetSerializable
    {
        public uint Tick;
        public byte EventType;
        public EntityDiedEvent EntityDied;
        public ExplosionEvent Explosion;

        public static GameEventMessage Died(uint tick, int entityId, DeathCause cause)
        {
            GameEventMessage message = default;
            message.Tick = tick;
            message.EventType = (byte)GameEventType.EntityDied;
            message.EntityDied.EntityId = entityId;
            message.EntityDied.Cause = (byte)cause;
            return message;
        }

        public static GameEventMessage Exploded(uint tick, Vec2 position, ExplosionType explosionType)
        {
            GameEventMessage message = default;
            message.Tick = tick;
            message.EventType = (byte)GameEventType.Explosion;
            message.Explosion.Position = position;
            message.Explosion.ExplosionType = (byte)explosionType;
            return message;
        }

        public void Serialize(NetDataWriter writer)
        {
            writer.Put(Tick);
            writer.Put(EventType);
            switch ((GameEventType)EventType)
            {
                case GameEventType.EntityDied:
                    EntityDied.Serialize(writer);
                    break;
                case GameEventType.Explosion:
                    Explosion.Serialize(writer);
                    break;
            }
        }

        public void Deserialize(NetDataReader reader)
        {
            TryDeserialize(reader);
        }

        // False for an event type this build does not know. Bodies carry no length, so nothing
        // after an unknown one can be read.
        public bool TryDeserialize(NetDataReader reader)
        {
            Tick = reader.GetUInt();
            EventType = reader.GetByte();
            switch ((GameEventType)EventType)
            {
                case GameEventType.EntityDied:
                    EntityDied.Deserialize(reader);
                    return true;
                case GameEventType.Explosion:
                    Explosion.Deserialize(reader);
                    return true;
                default:
                    return false;
            }
        }
    }
}
