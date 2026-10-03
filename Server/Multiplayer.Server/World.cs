using System;
using System.Collections.Generic;
using Multiplayer.Protocol;

namespace Multiplayer.Server
{
    public sealed class Entity
    {
        public int EntityId;
        public int OwnerId;
        public UnitType UnitType;
        public int Health;
        public Vec2 Position;
        public Vec2 Target;
        public bool HasTarget;
    }

    public sealed class World
    {
        const float SpawnRingStartRadius = 3f;
        const float SpawnSlotSpacing = 2f;
        const int SpawnRingCount = 4;
        const float UnitClearance = 1.5f;
        // Below the first ring radius, so the structure never blocks its own ring.
        const float StructureClearance = 2.8f;

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

        public Entity Spawn(int ownerId, UnitType unitType, Vec2 position)
        {
            Entity entity = new Entity
            {
                EntityId = nextEntityId++,
                OwnerId = ownerId,
                UnitType = unitType,
                Health = UnitDefs.Get(unitType).MaxHealth,
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

                float speed = UnitDefs.Get(entity.UnitType).Speed;
                entity.Position = MovementMath.Step(entity.Position, entity.Target, speed, dt);
                if (entity.Position.X == entity.Target.X && entity.Position.Z == entity.Target.Z)
                    entity.HasTarget = false;
            }
        }

        public bool TryFindSpawnPoint(Entity structure, float halfExtent, out Vec2 point)
        {
            for (int ring = 0; ring < SpawnRingCount; ring++)
            {
                float radius = SpawnRingStartRadius + ring * SpawnSlotSpacing;
                int slots = (int)(2.0 * Math.PI * radius / SpawnSlotSpacing);
                for (int slot = 0; slot < slots; slot++)
                {
                    double angle = 2.0 * Math.PI * slot / slots;
                    Vec2 candidate = new Vec2(
                        structure.Position.X + radius * (float)Math.Cos(angle),
                        structure.Position.Z + radius * (float)Math.Sin(angle));

                    if (Math.Abs(candidate.X) > halfExtent || Math.Abs(candidate.Z) > halfExtent)
                        continue;

                    if (IsFree(candidate))
                    {
                        point = candidate;
                        return true;
                    }
                }
            }

            point = default;
            return false;
        }

        public bool HasStructure(int ownerId)
        {
            foreach (Entity entity in entities.Values)
            {
                if (entity.OwnerId == ownerId && entity.UnitType == UnitType.Structure)
                    return true;
            }
            return false;
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

        bool IsFree(Vec2 point)
        {
            foreach (Entity entity in entities.Values)
            {
                float clearance = entity.UnitType == UnitType.Structure ? StructureClearance : UnitClearance;
                float dx = entity.Position.X - point.X;
                float dz = entity.Position.Z - point.Z;
                if (dx * dx + dz * dz < clearance * clearance)
                    return false;
            }
            return true;
        }
    }
}
