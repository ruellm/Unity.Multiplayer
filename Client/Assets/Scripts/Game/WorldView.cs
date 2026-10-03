using System;
using System.Collections.Generic;
using Multiplayer.Protocol;
using UnityEngine;

namespace Multiplayer.Game
{
    public sealed class WorldView : MonoBehaviour
    {
        const float HealthBarGap = 0.6f;

        static readonly Vector3 StructureSize = new Vector3(3f, 1f, 3f);
        static readonly Vector3 SoldierSize = new Vector3(0.8f, 0.8f, 0.8f);
        static readonly Vector3 TankSize = new Vector3(1.8f, 0.6f, 1.3f);
        static readonly Vector3 BarrelSize = new Vector3(0.16f, 0.16f, 0.6f);
        const float BarrelHeight = 0.2f;
        const float ExplosionHeight = 0.5f;

        readonly Dictionary<int, Unit> units = new Dictionary<int, Unit>();
        readonly HashSet<int> seen = new HashSet<int>();
        readonly List<int> removeScratch = new List<int>();
        readonly List<EntityState> resolved = new List<EntityState>();
        readonly SnapshotBuffer buffer = new SnapshotBuffer();
        readonly EventQueue events = new EventQueue();
        readonly List<GameEventMessage> released = new List<GameEventMessage>();
        RenderClock clock;

        Material unitMaterial;
        Material barrelMaterial;
        Material healthBarMaterial;
        Material effectMaterial;
        PlayerRoster roster;
        Camera worldCamera;

        public event Action<Unit> EntityRemoved;
        public event Action EntitiesChanged;

        public int EntityCount
        {
            get { return units.Count; }
        }

        public RenderClock Clock
        {
            get { return clock; }
        }

        public EventQueue Events
        {
            get { return events; }
        }

        public Dictionary<int, Unit>.ValueCollection Units
        {
            get { return units.Values; }
        }

        public bool TryGetUnit(int entityId, out Unit unit)
        {
            return units.TryGetValue(entityId, out unit);
        }

        void Awake()
        {
            clock = new RenderClock(buffer);
        }

        public void Initialize(Material unit, Material barrel, Material healthBar, Material effect, PlayerRoster playerRoster, Camera camera)
        {
            unitMaterial = unit;
            barrelMaterial = barrel;
            healthBarMaterial = healthBar;
            effectMaterial = effect;
            roster = playerRoster;
            worldCamera = camera;
        }

        public int CountOwnedBy(int playerId, out bool hasStructure)
        {
            int count = 0;
            hasStructure = false;
            foreach (Unit unit in units.Values)
            {
                if (unit.OwnerId != playerId)
                    continue;

                count++;
                if (unit.UnitType == UnitType.Structure)
                    hasStructure = true;
            }
            return count;
        }

        // Snapshots are only buffered here. Units are created, updated and removed in Update, from
        // the state resolved at the render clock, so everything drawn comes from one point in time.
        public void Apply(WorldSnapshotMessage snapshot)
        {
            if (buffer.Push(snapshot, Time.time))
                events.ObserveSnapshot(snapshot.Tick);
        }

        public void Enqueue(GameEventMessage gameEvent)
        {
            events.Enqueue(gameEvent);
        }

        public void Clear()
        {
            removeScratch.Clear();
            removeScratch.AddRange(units.Keys);
            for (int i = 0; i < removeScratch.Count; i++)
                Remove(removeScratch[i]);

            buffer.Clear();
            clock.Reset();
            events.Clear();
        }

        void Update()
        {
            if (!clock.Advance(Time.time))
                return;

            buffer.GetResolved(resolved);
            seen.Clear();
            bool changed = false;

            for (int i = 0; i < resolved.Count; i++)
            {
                EntityState state = resolved[i];
                seen.Add(state.EntityId);

                Unit unit;
                if (!units.TryGetValue(state.EntityId, out unit))
                {
                    unit = Create(state);
                    units[state.EntityId] = unit;
                    changed = true;
                }

                unit.Position = state.Position;
                unit.SetHealth(state.Health);
                unit.ActionState = (ActionState)state.ActionState;
                unit.TargetEntityId = state.TargetEntityId;
            }

            if (units.Count != resolved.Count)
            {
                removeScratch.Clear();
                foreach (int id in units.Keys)
                {
                    if (!seen.Contains(id))
                        removeScratch.Add(id);
                }

                for (int i = 0; i < removeScratch.Count; i++)
                    Remove(removeScratch[i]);

                changed = true;
            }

            // After every unit holds this frame's state, so a visual can read its target's.
            foreach (Unit unit in units.Values)
                unit.Visual.Refresh(clock, this);

            events.Release(clock, released);
            for (int i = 0; i < released.Count; i++)
                Play(released[i]);

            if (changed)
            {
                Action handler = EntitiesChanged;
                if (handler != null)
                    handler();
            }
        }

        void Play(GameEventMessage gameEvent)
        {
            switch ((GameEventType)gameEvent.EventType)
            {
                case GameEventType.EntityDied:
                    Unit unit;
                    if (units.TryGetValue(gameEvent.EntityDied.EntityId, out unit))
                        unit.Visual.PlayDeathFlourish((DeathCause)gameEvent.EntityDied.Cause);
                    break;
                case GameEventType.Explosion:
                    Vec2 at = gameEvent.Explosion.Position;
                    ExplosionEffect.Spawn(new Vector3(at.X, ExplosionHeight, at.Z), effectMaterial);
                    break;
            }
        }

        Unit Create(EntityState state)
        {
            UnitType unitType = (UnitType)state.UnitType;
            Vector3 size = BodySize(unitType);

            // Unscaled root so the health bar and selection ring are not skewed by the body's scale.
            GameObject root = new GameObject("Entity " + state.EntityId + " " + unitType);

            GameObject body = GameObject.CreatePrimitive(PrimitiveType.Cube);
            body.name = "Body";
            body.transform.SetParent(root.transform, false);
            body.transform.localPosition = new Vector3(0f, size.y * 0.5f, 0f);
            body.transform.localScale = size;
            Renderer bodyRenderer = body.GetComponent<Renderer>();
            bodyRenderer.sharedMaterial = unitMaterial;

            HealthBar bar = HealthBar.Create(root.transform, healthBarMaterial, worldCamera, size.x, size.y + HealthBarGap);

            Unit unit = root.AddComponent<Unit>();
            unit.EntityId = state.EntityId;
            unit.OwnerId = state.OwnerId;
            unit.UnitType = unitType;
            unit.Position = state.Position;
            unit.Initialize(bodyRenderer, bar);
            unit.OwnerColor = roster.GetColor(state.OwnerId);

            Transform muzzle = unitType != UnitType.Structure ? CreateBarrel(body.transform, size) : null;
            unit.Visual = root.AddComponent<UnitVisual>();
            unit.Visual.Initialize(unit, body.transform, muzzle, effectMaterial, clock.StateTick);
            return unit;
        }

        // The barrel is what makes facing readable on an otherwise symmetric box.
        Transform CreateBarrel(Transform body, Vector3 bodySize)
        {
            GameObject barrel = GameObject.CreatePrimitive(PrimitiveType.Cube);
            barrel.name = "Barrel";

            Collider barrelCollider = barrel.GetComponent<Collider>();
            barrelCollider.enabled = false;
            Destroy(barrelCollider);

            // Divided by the body's scale so the barrel keeps its world size under the non-uniform parent.
            float length = BarrelSize.z / bodySize.z;
            Transform barrelTransform = barrel.transform;
            barrelTransform.SetParent(body, false);
            barrelTransform.localScale = new Vector3(BarrelSize.x / bodySize.x, BarrelSize.y / bodySize.y, length);
            barrelTransform.localPosition = new Vector3(0f, BarrelHeight, 0.5f + length * 0.25f);
            barrel.GetComponent<Renderer>().sharedMaterial = barrelMaterial;

            GameObject muzzle = new GameObject("Muzzle");
            muzzle.transform.SetParent(body, false);
            muzzle.transform.localPosition = new Vector3(0f, BarrelHeight, 0.5f + length * 0.75f);
            return muzzle.transform;
        }

        void Remove(int entityId)
        {
            Unit unit;
            if (!units.TryGetValue(entityId, out unit))
                return;

            units.Remove(entityId);

            Action<Unit> handler = EntityRemoved;
            if (handler != null)
                handler(unit);

            Destroy(unit.gameObject);
        }

        static Vector3 BodySize(UnitType unitType)
        {
            switch (unitType)
            {
                case UnitType.Structure:
                    return StructureSize;
                case UnitType.Tank:
                    return TankSize;
                default:
                    return SoldierSize;
            }
        }
    }
}
