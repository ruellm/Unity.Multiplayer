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
        public byte Yaw;
        public Vec2 Target;
        public bool HasTarget;
    }

    public enum PlacementError
    {
        None,
        NotAStructure,
        AlreadyHasHQ,
        NeedsHQ,
        OutOfBounds,
        Overlaps
    }

    public sealed class World
    {
        const int SpawnRingCount = 4;
        const float SpawnRingGap = 1f;
        const float SpawnSlotGap = 0.5f;

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
            return Spawn(ownerId, unitType, position, 0);
        }

        public Entity Spawn(int ownerId, UnitType unitType, Vec2 position, byte yaw)
        {
            Entity entity = new Entity
            {
                EntityId = nextEntityId++,
                OwnerId = ownerId,
                UnitType = unitType,
                Health = UnitDefs.Get(unitType).MaxHealth,
                Position = position,
                Yaw = yaw
            };
            entities[entity.EntityId] = entity;
            return entity;
        }

        // Structures take no orders. Towers pick their own targets in Integrate.
        public void SetTarget(int entityId, Vec2 target)
        {
            Entity entity;
            if (!entities.TryGetValue(entityId, out entity) || entity.UnitType.IsStructure())
                return;

            entity.Target = target;
            entity.HasTarget = true;
            entity.TargetEntityId = 0;
        }

        public void SetAttackTarget(int entityId, int targetEntityId)
        {
            Entity entity;
            if (!entities.TryGetValue(entityId, out entity) || entity.UnitType.IsStructure())
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

                if (entity.UnitType == UnitType.Tower)
                    AcquireTowerTarget(entity, def.Range);

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
                    if (DistanceSqr(entity, target) <= def.Range * def.Range)
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
                if (removed.UnitType.IsStructure())
                    events.Add(GameEventMessage.Exploded(tick, removed.Position, ExplosionType.StructureDestroyed));
            }
        }

        // Keeps the current target while it is a live enemy unit in range, otherwise takes the nearest one.
        void AcquireTowerTarget(Entity tower, float range)
        {
            float rangeSqr = range * range;

            Entity current;
            if (tower.TargetEntityId != 0
                && entities.TryGetValue(tower.TargetEntityId, out current)
                && IsTowerTarget(tower, current, rangeSqr))
                return;

            tower.TargetEntityId = 0;
            float bestSqr = 0f;
            foreach (Entity candidate in entities.Values)
            {
                if (!IsTowerTarget(tower, candidate, rangeSqr))
                    continue;

                float distanceSqr = DistanceSqr(tower, candidate);
                if (tower.TargetEntityId == 0 || distanceSqr < bestSqr)
                {
                    bestSqr = distanceSqr;
                    tower.TargetEntityId = candidate.EntityId;
                }
            }
        }

        static bool IsTowerTarget(Entity tower, Entity candidate, float rangeSqr)
        {
            return candidate.OwnerId != tower.OwnerId
                && !candidate.UnitType.IsStructure()
                && candidate.ActionState != ActionState.Dying
                && DistanceSqr(tower, candidate) <= rangeSqr;
        }

        static float DistanceSqr(Entity a, Entity b)
        {
            float dx = b.Position.X - a.Position.X;
            float dz = b.Position.Z - a.Position.Z;
            return dx * dx + dz * dz;
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

        // Rings around the structure, the first one clear of its footprint, each wide enough for
        // the unit being spawned.
        public bool TryFindSpawnPoint(Entity structure, UnitType unitType, float halfExtent, out Vec2 point)
        {
            float unitRadius = UnitDefs.Get(unitType).FootprintRadius;
            float startRadius = UnitDefs.Get(structure.UnitType).FootprintRadius + unitRadius + SpawnRingGap;
            float step = 2f * unitRadius + SpawnSlotGap;

            for (int ring = 0; ring < SpawnRingCount; ring++)
            {
                float radius = startRadius + ring * step;
                int slots = (int)(2.0 * Math.PI * radius / step);
                for (int slot = 0; slot < slots; slot++)
                {
                    double angle = 2.0 * Math.PI * slot / slots;
                    Vec2 candidate = new Vec2(
                        structure.Position.X + radius * (float)Math.Cos(angle),
                        structure.Position.Z + radius * (float)Math.Sin(angle));

                    if (Math.Abs(candidate.X) > halfExtent || Math.Abs(candidate.Z) > halfExtent)
                        continue;

                    if (IsFree(candidate, unitType))
                    {
                        point = candidate;
                        return true;
                    }
                }
            }

            point = default;
            return false;
        }

        public bool HasHQ(int ownerId)
        {
            foreach (Entity entity in entities.Values)
            {
                if (entity.OwnerId == ownerId && entity.UnitType == UnitType.HQ)
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

        // Free when a footprint of the given type at point touches no existing footprint.
        public bool IsFree(Vec2 point, UnitType unitType)
        {
            float radius = UnitDefs.Get(unitType).FootprintRadius;
            foreach (Entity entity in entities.Values)
            {
                if (Footprint.Overlaps(point, radius, entity.Position, UnitDefs.Get(entity.UnitType).FootprintRadius))
                    return false;
            }
            return true;
        }

        // Every placement rule except the per-player entity cap, which the server owns.
        public PlacementError CheckPlacement(int ownerId, UnitType unitType, Vec2 position)
        {
            if (!unitType.IsStructure())
                return PlacementError.NotAStructure;

            bool hasHQ = HasHQ(ownerId);
            if (unitType == UnitType.HQ && hasHQ)
                return PlacementError.AlreadyHasHQ;
            if (unitType != UnitType.HQ && !hasHQ)
                return PlacementError.NeedsHQ;

            if (!Footprint.InBounds(position, Footprint.WorldHalfExtent))
                return PlacementError.OutOfBounds;

            if (!IsFree(position, unitType))
                return PlacementError.Overlaps;

            return PlacementError.None;
        }
    }
}
