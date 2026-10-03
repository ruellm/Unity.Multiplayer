using System.Collections.Generic;
using Multiplayer.Protocol;

namespace Multiplayer.Tests
{
    // Frozen copy of SnapshotBuffer from before RenderClock existed. It is the baseline the
    // equivalence tests compare positions against, so it must never be edited.
    public sealed class ReferenceSnapshotBuffer
    {
        sealed class Sample
        {
            public uint Tick;
            public float Time;
            public readonly Dictionary<int, Vec2> Positions = new Dictionary<int, Vec2>();
        }

        const int Capacity = 8;

        readonly List<Sample> samples = new List<Sample>(Capacity);
        readonly Stack<Sample> pool = new Stack<Sample>(Capacity);

        Sample from;
        Sample to;
        float blend;

        public int Count
        {
            get { return samples.Count; }
        }

        public uint NewestTick
        {
            get { return samples.Count > 0 ? samples[samples.Count - 1].Tick : 0; }
        }

        public void Push(WorldSnapshotMessage snapshot, float receiveTime)
        {
            if (samples.Count > 0 && snapshot.Tick <= NewestTick)
                return;

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
            sample.Positions.Clear();
            List<EntityState> states = snapshot.Entities;
            for (int i = 0; i < states.Count; i++)
                sample.Positions[states[i].EntityId] = states[i].Position;

            samples.Add(sample);
        }

        public void Clear()
        {
            for (int i = 0; i < samples.Count; i++)
                pool.Push(samples[i]);
            samples.Clear();
            from = null;
            to = null;
        }

        public bool Resolve(float renderTime)
        {
            from = null;
            to = null;
            if (samples.Count == 0)
                return false;

            int index = samples.Count - 1;
            while (index > 0 && samples[index].Time > renderTime)
                index--;

            from = samples[index];
            if (index == samples.Count - 1 || renderTime <= from.Time)
            {
                // Past the newest sample or before the oldest: hold, never extrapolate.
                to = null;
                blend = 0f;
                return true;
            }

            to = samples[index + 1];
            float span = to.Time - from.Time;
            blend = span > 0f ? (renderTime - from.Time) / span : 1f;
            if (blend < 0f) blend = 0f;
            if (blend > 1f) blend = 1f;
            return true;
        }

        public bool TryGetPosition(int entityId, out Vec2 position)
        {
            position = default;
            if (from == null)
                return false;

            if (to == null)
                return from.Positions.TryGetValue(entityId, out position);

            Vec2 end;
            if (!to.Positions.TryGetValue(entityId, out end))
                return false;

            Vec2 start;
            if (!from.Positions.TryGetValue(entityId, out start))
            {
                position = end;
                return true;
            }

            position = new Vec2(start.X + (end.X - start.X) * blend, start.Z + (end.Z - start.Z) * blend);
            return true;
        }
    }
}
