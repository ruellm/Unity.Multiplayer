using System.Collections.Generic;
using Multiplayer.Protocol;

namespace Multiplayer.Game
{
    public sealed class SnapshotBuffer
    {
        sealed class Sample
        {
            public uint Tick;
            public float Time;
            public readonly Dictionary<int, EntityState> States = new Dictionary<int, EntityState>();
        }

        const int Capacity = 8;

        readonly List<Sample> samples = new List<Sample>(Capacity);
        readonly Stack<Sample> pool = new Stack<Sample>(Capacity);

        Sample from;
        Sample to;
        float blend;
        float ticksBehindNewest;
        uint resolvedTick;

        public int Count
        {
            get { return samples.Count; }
        }

        public uint NewestTick
        {
            get { return samples.Count > 0 ? samples[samples.Count - 1].Tick : 0; }
        }

        public float NewestTime
        {
            get { return samples.Count > 0 ? samples[samples.Count - 1].Time : 0f; }
        }

        // How far the last resolved render point sits behind the newest sample, in ticks.
        // Built from the same bracket and blend as the position lerp, so the two cannot disagree.
        public float TicksBehindNewest
        {
            get { return ticksBehindNewest; }
        }

        // Tick of the sample the discrete fields are read from at the last resolved render point.
        public uint ResolvedTick
        {
            get { return resolvedTick; }
        }

        public bool Push(WorldSnapshotMessage snapshot, float receiveTime)
        {
            if (snapshot.Tick <= NewestTick)
                return false;

            Sample sample;
            if (samples.Count == Capacity)
            {
                sample = samples[0];
                samples.RemoveAt(0);
            }
            else
            {
                sample = pool.Count > 0 ? pool.Pop() : new Sample();
            }

            sample.Tick = snapshot.Tick;
            sample.Time = receiveTime;
            sample.States.Clear();
            List<EntityState> states = snapshot.Entities;
            for (int i = 0; i < states.Count; i++)
                sample.States[states[i].EntityId] = states[i];

            samples.Add(sample);
            return true;
        }

        public void Clear()
        {
            for (int i = 0; i < samples.Count; i++)
                pool.Push(samples[i]);
            samples.Clear();
            from = null;
            to = null;
            ticksBehindNewest = 0f;
            resolvedTick = 0;
        }

        public bool Resolve(float renderTime)
        {
            from = null;
            to = null;
            ticksBehindNewest = 0f;
            resolvedTick = 0;
            if (samples.Count == 0)
                return false;

            int index = samples.Count - 1;
            while (index > 0 && samples[index].Time > renderTime)
                index--;

            uint newestTick = NewestTick;
            from = samples[index];
            resolvedTick = from.Tick;
            if (index == samples.Count - 1 || renderTime <= from.Time)
            {
                // Past the newest sample or before the oldest: hold, never extrapolate.
                to = null;
                blend = 0f;
                ticksBehindNewest = newestTick - from.Tick;
                return true;
            }

            to = samples[index + 1];
            float span = to.Time - from.Time;
            blend = span > 0f ? (renderTime - from.Time) / span : 1f;
            if (blend < 0f) blend = 0f;
            if (blend > 1f) blend = 1f;
            ticksBehindNewest = (newestTick - to.Tick) + (1f - blend) * (to.Tick - from.Tick);
            return true;
        }

        // Position is interpolated across the bracket. Everything else is discrete and comes from
        // the earlier sample, so a change shows only once the render point reaches its snapshot.
        public bool TryGetState(int entityId, out EntityState state)
        {
            state = default;
            if (from == null)
                return false;

            if (to == null)
                return from.States.TryGetValue(entityId, out state);

            EntityState end;
            if (!to.States.TryGetValue(entityId, out end))
                return false;

            if (!from.States.TryGetValue(entityId, out state))
            {
                state = end;
                return true;
            }

            Vec2 start = state.Position;
            state.Position = new Vec2(start.X + (end.Position.X - start.X) * blend, start.Z + (end.Position.Z - start.Z) * blend);
            return true;
        }

        public void GetResolved(List<EntityState> results)
        {
            results.Clear();
            if (from == null)
                return;

            Sample membership = to != null ? to : from;
            foreach (int entityId in membership.States.Keys)
            {
                EntityState state;
                if (TryGetState(entityId, out state))
                    results.Add(state);
            }
        }
    }
}
