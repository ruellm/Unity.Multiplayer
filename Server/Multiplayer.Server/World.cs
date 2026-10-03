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
        public ActionState ActionState;
        public int TargetEntityId;
        public uint DeathTick;
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
        readonly List<GameEventMessage> events = new List<GameEventMessage>();
        int nextEntityId = 1;

        public int Count
        {
            get { return entities.Count; }
        }

        public IEnumerable<Entity> Entities
        {
            get { return entities.Values; }
        }

        // Events raised by Integrate. The caller sends them and clears the list.
        public List<GameEventMessage> Events
        {
            get { return events; }
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
            entity.TargetEntityId = 0;
        }

        public void SetAttackTarget(int entityId, int targetEntityId)
        {
            Entity entity;
            if (!entities.TryGetValue(entityId, out entity))
                return;

            entity.TargetEntityId = targetEntityId;
            entity.HasTarget = false;
        }

        public void Integrate(uint tick, float dt)
        {
            removeScratch.Clear();

            foreach (Entity entity in entities.Values)
            {
                if (entity.ActionState == ActionState.Dying)
                {
                    if (tick - entity.DeathTick >= (uint)UnitDefs.DeathTicks(entity.UnitType))
                        removeScratch.Add(entity.EntityId);
                    continue;
                }

                UnitDef def = UnitDefs.Get(entity.UnitType);

                // A dying target is as good as gone: it cannot be attacked or followed.
                Entity target = null;
                if (entity.TargetEntityId != 0
                    && (!entities.TryGetValue(entity.TargetEntityId, out target) || target.ActionState == ActionState.Dying))
                {
                    entity.TargetEntityId = 0;
                    target = null;
                }

                if (target != null)
                {
                    float dx = target.Position.X - entity.Position.X;
                    float dz = target.Position.Z - entity.Position.Z;
                    if (dx * dx + dz * dz <= def.Range * def.Range)
                    {
                        entity.ActionState = ActionState.Attacking;
                        if (AttackSchedule.Fires(tick, entity.EntityId, AttackSchedule.CooldownTicks(entity.UnitType)))
                            Damage(target, def.Damage, DeathCause.Shot, tick);
                        continue;
                    }

                    entity.ActionState = ActionState.MovingToAttack;
                    entity.Position = MovementMath.Step(entity.Position, target.Position, def.Speed, dt);
                    continue;
                }

                if (!entity.HasTarget)
                {
                    entity.ActionState = ActionState.Idle;
                    continue;
                }

                entity.ActionState = ActionState.Moving;
                entity.Position = MovementMath.Step(entity.Position, entity.Target, def.Speed, dt);
                if (entity.Position.X == entity.Target.X && entity.Position.Z == entity.Target.Z)
                    entity.HasTarget = false;
            }

            for (int i = 0; i < removeScratch.Count; i++)
            {
                Entity removed = entities[removeScratch[i]];
                entities.Remove(removed.EntityId);

                // Stamped with the removal tick, so clients show it as the collapse ends, not as it starts.
                if (removed.UnitType == UnitType.Structure)
                    events.Add(GameEventMessage.Exploded(tick, removed.Position, ExplosionType.StructureDestroyed));
            }
        }

        void Damage(Entity target, int amount, DeathCause cause, uint tick)
        {
            target.Health = Math.Max(0, target.Health - amount);
            if (target.Health > 0)
                return;

            target.ActionState = ActionState.Dying;
            target.DeathTick = tick;
            target.TargetEntityId = 0;
            target.HasTarget = false;
            events.Add(GameEventMessage.Died(tick, target.EntityId, cause));
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
