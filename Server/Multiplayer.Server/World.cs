using System.Collections.Generic;
using Multiplayer.Protocol;

namespace Multiplayer.Server
{
    public sealed class Entity
    {
        public int EntityId;
        public int OwnerId;
        public Vec2 Position;
        public Vec2 Target;
        public bool HasTarget;
    }

    public sealed class World
    {
        readonly Dictionary<int, Entity> entities = new Dictionary<int, Entity>();
        readonly List<int> removeScratch = new List<int>();
        int nextEntityId = 1;

        public int Count
        {
            get { return entities.Count; }
        }

        public IEnumerable<Entity> Entities
        {
            get { return entities.Values; }
        }

        public bool TryGet(int entityId, out Entity entity)
        {
            return entities.TryGetValue(entityId, out entity);
        }

        public Entity Spawn(int ownerId, Vec2 position)
        {
            Entity entity = new Entity
            {
                EntityId = nextEntityId++,
                OwnerId = ownerId,
                Position = position
            };
            entities[entity.EntityId] = entity;
            return entity;
        }

        public void SetTarget(int entityId, Vec2 target)
        {
            Entity entity;
            if (!entities.TryGetValue(entityId, out entity))
                return;

            entity.Target = target;
            entity.HasTarget = true;
        }

        public void Integrate(float dt)
        {
            foreach (Entity entity in entities.Values)
            {
                if (!entity.HasTarget)
                    continue;

                entity.Position = MovementMath.Step(entity.Position, entity.Target, NetConfig.UnitSpeed, dt);
                if (entity.Position.X == entity.Target.X && entity.Position.Z == entity.Target.Z)
                    entity.HasTarget = false;
            }
        }

        public int CountByOwner(int ownerId)
        {
            int count = 0;
            foreach (Entity entity in entities.Values)
            {
                if (entity.OwnerId == ownerId)
                    count++;
            }
            return count;
        }

        public int RemoveByOwner(int ownerId)
        {
            removeScratch.Clear();
            foreach (Entity entity in entities.Values)
            {
                if (entity.OwnerId == ownerId)
                    removeScratch.Add(entity.EntityId);
            }

            for (int i = 0; i < removeScratch.Count; i++)
                entities.Remove(removeScratch[i]);

            return removeScratch.Count;
        }
    }
}
