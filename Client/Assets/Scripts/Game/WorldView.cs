using System;
using System.Collections.Generic;
using Multiplayer.Protocol;
using UnityEngine;

namespace Multiplayer.Game
{
    public sealed class WorldView : MonoBehaviour
    {
        const float CubeSize = 1f;
        const float InterpolationDelay = (float)NetConfig.InterpolationDelayTicks / NetConfig.TickRate;

        readonly Dictionary<int, Unit> units = new Dictionary<int, Unit>();
        readonly HashSet<int> seen = new HashSet<int>();
        readonly List<int> removeScratch = new List<int>();
        readonly SnapshotBuffer buffer = new SnapshotBuffer();

        Material unitMaterial;
        PlayerRoster roster;
        uint lastTick;

        public event Action<Unit> EntityRemoved;

        public int EntityCount
        {
            get { return units.Count; }
        }

        public void Initialize(Material material, PlayerRoster playerRoster)
        {
            unitMaterial = material;
            roster = playerRoster;
        }

        public void Apply(WorldSnapshotMessage snapshot)
        {
            if (snapshot.Tick <= lastTick)
                return;

            lastTick = snapshot.Tick;
            buffer.Push(snapshot, Time.time);
            seen.Clear();

            List<EntityState> states = snapshot.Entities;
            for (int i = 0; i < states.Count; i++)
            {
                EntityState state = states[i];
                seen.Add(state.EntityId);
                if (!units.ContainsKey(state.EntityId))
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
            lastTick = 0;
        }

        void Update()
        {
            if (units.Count == 0 || !buffer.Resolve(Time.time - InterpolationDelay))
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
            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = "Entity " + state.EntityId;
            cube.transform.localScale = Vector3.one * CubeSize;
            cube.transform.position = ToWorld(state.Position);
            cube.GetComponent<Renderer>().sharedMaterial = unitMaterial;

            Unit unit = cube.AddComponent<Unit>();
            unit.EntityId = state.EntityId;
            unit.OwnerId = state.OwnerId;
            unit.OwnerColor = roster.GetColor(state.OwnerId);
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

        static Vector3 ToWorld(Vec2 position)
        {
            return new Vector3(position.X, CubeSize * 0.5f, position.Z);
        }
    }
}
