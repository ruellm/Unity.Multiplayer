using System;
using System.Collections.Generic;
using Multiplayer.Protocol;
using RTS;
using UnityEngine;

namespace Multiplayer.Game
{
    public sealed class WorldView : MonoBehaviour
    {
        const float HealthBarGap = 0.25f;
        const float ExplosionHeight = 0.5f;

        readonly Dictionary<int, Unit> units = new Dictionary<int, Unit>();
        readonly HashSet<int> seen = new HashSet<int>();
        readonly List<int> removeScratch = new List<int>();
        readonly List<EntityState> resolved = new List<EntityState>();
        readonly SnapshotBuffer buffer = new SnapshotBuffer();
        readonly EventQueue events = new EventQueue();
        readonly List<GameEventMessage> released = new List<GameEventMessage>();
        readonly List<CorpseTimer> corpses = new List<CorpseTimer>();
        RenderClock clock;

        UnitVisualSet visuals;
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

        public void Initialize(UnitVisualSet visualSet, Material healthBar, Material effect, PlayerRoster playerRoster, Camera camera)
        {
            visuals = visualSet;
            healthBarMaterial = healthBar;
            effectMaterial = effect;
            roster = playerRoster;
            worldCamera = camera;

            if (visuals == null)
            {
                Debug.LogError("WorldView: no UnitVisualSet. No entities will be shown.");
                enabled = false;
            }
        }

        public int CountOwnedBy(int playerId, out bool hasHQ)
        {
            int count = 0;
            hasHQ = false;
            foreach (Unit unit in units.Values)
            {
                if (unit.OwnerId != playerId)
                    continue;

                count++;
                if (unit.UnitType == UnitType.HQ)
                    hasHQ = true;
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

            DestroyCorpses();
            buffer.Clear();
            clock.Reset();
            events.Clear();
        }

        void OnDestroy()
        {
            DestroyCorpses();
        }

        void DestroyCorpses()
        {
            for (int i = 0; i < corpses.Count; i++)
            {
                if (corpses[i] != null)
                    Destroy(corpses[i].gameObject);
            }
            corpses.Clear();
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
                unit.Yaw = state.Yaw;
                unit.SetHealth(state.Health);
                unit.ActionState = (ActionState)state.ActionState;
                unit.TargetEntityId = state.TargetEntityId;

                EntityState next;
                unit.NextActionState = buffer.TryGetNextState(state.EntityId, out next)
                    ? (ActionState)next.ActionState
                    : unit.ActionState;
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
            string entryName = EntryName(unitType);
            UnitVisualEntry entry = visuals.Find(entryName);
            if (entry == null || entry.Prefab == null)
                throw new InvalidOperationException("UnitVisualSet has no prefab for entry '" + entryName + "'. Run Tools/RTS/Create Unit Visual Set.");

            // Unscaled root. Body is what UnitVisual moves, the model and the pickable collider sit under it.
            GameObject root = new GameObject("Entity " + state.EntityId + " " + unitType);

            GameObject body = new GameObject("Body");
            body.transform.SetParent(root.transform, false);

            GameObject model = Instantiate(entry.Prefab, body.transform);
            model.name = "Model";
            Transform modelTransform = model.transform;
            modelTransform.localPosition = Vector3.zero;
            modelTransform.localRotation = Quaternion.identity;
            modelTransform.localScale = Vector3.one * entry.Scale;

            BoxCollider collider = body.AddComponent<BoxCollider>();
            collider.center = entry.ColliderCenter * entry.Scale;
            collider.size = entry.ColliderSize * entry.Scale;

            Transform muzzle = CreateMuzzle(root.transform, modelTransform, entry);

            Transform turret = null;
            if (!string.IsNullOrEmpty(entry.TurretPivotName))
            {
                turret = FindChild(modelTransform, entry.TurretPivotName);
                if (turret == null)
                    Debug.LogWarning("WorldView: turret pivot '" + entry.TurretPivotName + "' not found on prefab '" + entry.Prefab.name + "'.", model);
            }

            TeamColor teamColor = model.GetComponent<TeamColor>();
            if (teamColor == null)
                Debug.LogWarning("WorldView: prefab '" + entry.Prefab.name + "' has no TeamColor component, owner colour will not show.", model);

            float barLift = collider.center.y + collider.size.y * 0.5f + HealthBarGap;
            HealthBar bar = HealthBar.Create(root.transform, healthBarMaterial, worldCamera, collider.size.x, barLift);

            Unit unit = root.AddComponent<Unit>();
            unit.EntityId = state.EntityId;
            unit.OwnerId = state.OwnerId;
            unit.UnitType = unitType;
            unit.Position = state.Position;
            unit.Yaw = state.Yaw;
            unit.Initialize(collider, teamColor, bar);
            unit.OwnerColor = roster.GetColor(state.OwnerId);

            unit.Visual = root.AddComponent<UnitVisual>();
            unit.Visual.Initialize(unit, body.transform, modelTransform, turret, muzzle, entry, effectMaterial, clock.StateTick);
            return unit;
        }

        static Transform CreateMuzzle(Transform root, Transform model, UnitVisualEntry entry)
        {
            if (!string.IsNullOrEmpty(entry.MuzzleAttach))
            {
                Transform attach = FindChild(model, entry.MuzzleAttach);
                if (attach == null)
                {
                    Debug.LogWarning("WorldView: muzzle attach '" + entry.MuzzleAttach + "' not found on prefab '" + entry.Prefab.name + "'.", model);
                    return null;
                }

                GameObject muzzle = new GameObject("Muzzle");
                muzzle.transform.SetParent(attach, false);
                muzzle.transform.localPosition = entry.MuzzleOffset;
                return muzzle.transform;
            }

            Transform rifle = FindChild(model, "Rifle");
            return rifle != null ? CreateRifleMuzzle(root, rifle) : null;
        }

        // Muzzle at the far end of the rifle mesh along whichever mesh axis points closest to the
        // root's forward. Done before the Animator moves the arm, while the prefab pose still holds.
        static Transform CreateRifleMuzzle(Transform root, Transform rifle)
        {
            MeshFilter filter = rifle.GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null)
                return null;

            Bounds bounds = filter.sharedMesh.bounds;
            Vector3 forward = root.forward;
            Vector3[] axes = { Vector3.right, Vector3.up, Vector3.forward };
            Vector3 bestAxis = Vector3.forward;
            float bestDot = 0f;
            for (int i = 0; i < axes.Length; i++)
            {
                float dot = Vector3.Dot(rifle.TransformDirection(axes[i]).normalized, forward);
                if (Mathf.Abs(dot) > Mathf.Abs(bestDot))
                {
                    bestDot = dot;
                    bestAxis = axes[i];
                }
            }

            if (bestDot < 0f)
                bestAxis = -bestAxis;

            Vector3 tip = bounds.center + Vector3.Scale(bestAxis, bounds.extents);
            GameObject muzzle = new GameObject("Muzzle");
            muzzle.transform.position = rifle.TransformPoint(tip);
            muzzle.transform.SetParent(rifle, true);
            return muzzle.transform;
        }

        static Transform FindChild(Transform parent, string childName)
        {
            if (parent.name == childName)
                return parent;

            for (int i = 0; i < parent.childCount; i++)
            {
                Transform found = FindChild(parent.GetChild(i), childName);
                if (found != null)
                    return found;
            }
            return null;
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

            CorpseTimer corpse;
            if (unit.Visual != null && unit.Visual.TryDetachCorpse(out corpse))
            {
                corpses.RemoveAll(IsGone);
                corpses.Add(corpse);
            }

            Destroy(unit.gameObject);
        }

        static bool IsGone(CorpseTimer corpse)
        {
            return corpse == null;
        }

        public static string EntryName(UnitType unitType)
        {
            switch (unitType)
            {
                case UnitType.HQ:
                    return "HQ";
                case UnitType.Tower:
                    return "Tower";
                case UnitType.Wall:
                    return "Wall";
                case UnitType.Soldier:
                    return "Soldier";
                case UnitType.Tank:
                    return "Tank";
                default:
                    throw new ArgumentOutOfRangeException("unitType", unitType, "No visual entry for unit type.");
            }
        }
    }
}
