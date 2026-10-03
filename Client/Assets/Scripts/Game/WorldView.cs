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

        readonly Dictionary<int, Unit> units = new Dictionary<int, Unit>();
        readonly HashSet<int> seen = new HashSet<int>();
        readonly List<int> removeScratch = new List<int>();
        readonly SnapshotBuffer buffer = new SnapshotBuffer();
        RenderClock clock;

        Material unitMaterial;
        Material healthBarMaterial;
        PlayerRoster roster;
        Camera worldCamera;

        public event Action<Unit> EntityRemoved;

        public int EntityCount
        {
            get { return units.Count; }
        }

        public RenderClock Clock
        {
            get { return clock; }
        }

        void Awake()
        {
            clock = new RenderClock(buffer);
        }

        public void Initialize(Material unit, Material healthBar, PlayerRoster playerRoster, Camera camera)
        {
            unitMaterial = unit;
            healthBarMaterial = healthBar;
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

        public void Apply(WorldSnapshotMessage snapshot)
        {
            if (!buffer.Push(snapshot, Time.time))
                return;

            seen.Clear();

            List<EntityState> states = snapshot.Entities;
            for (int i = 0; i < states.Count; i++)
            {
                EntityState state = states[i];
                seen.Add(state.EntityId);

                Unit unit;
                if (units.TryGetValue(state.EntityId, out unit))
                    unit.SetHealth(state.Health);
                else
                    units[state.EntityId] = Create(state);
            }

            removeScratch.Clear();
            foreach (int id in units.Keys)
            {
                if (!seen.Contains(id))
                    removeScratch.Add(id);
            }

            for (int i = 0; i < removeScratch.Count; i++)
                Remove(removeScratch[i]);
        }

        public void Clear()
        {
            removeScratch.Clear();
            removeScratch.AddRange(units.Keys);
            for (int i = 0; i < removeScratch.Count; i++)
                Remove(removeScratch[i]);

            buffer.Clear();
            clock.Reset();
        }

        void Update()
        {
            if (!clock.Advance(Time.time))
                return;

            foreach (Unit unit in units.Values)
            {
                Vec2 position;
                if (buffer.TryGetPosition(unit.EntityId, out position))
                    unit.transform.position = ToWorld(position);
            }
        }

        Unit Create(EntityState state)
        {
            UnitType unitType = (UnitType)state.UnitType;
            Vector3 size = BodySize(unitType);

            // Unscaled root so the health bar and selection ring are not skewed by the body's scale.
            GameObject root = new GameObject("Entity " + state.EntityId + " " + unitType);
            root.transform.position = ToWorld(state.Position);

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
            unit.Initialize(bodyRenderer, bar);
            unit.OwnerColor = roster.GetColor(state.OwnerId);
            unit.SetHealth(state.Health);
            return unit;
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

        static Vector3 ToWorld(Vec2 position)
        {
            return new Vector3(position.X, 0f, position.Z);
        }
    }
}
