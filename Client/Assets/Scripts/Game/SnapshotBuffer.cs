using System;
using System.Collections.Generic;
using Multiplayer.Protocol;

namespace Multiplayer.Game
{
    // Samples sit on the server tick timeline. Arrival times only feed the smoothed offset that
    // maps local time onto that timeline, so arrival jitter never changes the spacing of samples.
    public sealed class SnapshotBuffer
    {
        sealed class Sample
        {
            public uint Tick;
            public readonly Dictionary<int, EntityState> States = new Dictionary<int, EntityState>();
        }

        public const double TickLength = 1.0 / NetConfig.TickRate;
        const int Capacity = 8;
        const double OffsetAlpha = 0.05;
        const double OffsetResetThreshold = 0.25;

        readonly List<Sample> samples = new List<Sample>(Capacity);
        readonly Stack<Sample> pool = new Stack<Sample>(Capacity);

        Sample from;
        Sample to;
        float blend;
        float ticksBehindNewest;
        uint resolvedTick;
        bool starved;
        double offset;
        bool hasOffset;
        int offsetResets;

        public int Count
        {
            get { return samples.Count; }
        }

        public uint NewestTick
        {
            get { return samples.Count > 0 ? samples[samples.Count - 1].Tick : 0; }
        }

        public uint OldestTick
        {
            get { return samples.Count > 0 ? samples[0].Tick : 0; }
        }

        // Local time at which server tick 0 would have arrived. Local time minus this is tick time.
        public double Offset
        {
            get { return offset; }
        }

        public bool HasOffset
        {
            get { return hasOffset; }
        }

        public int OffsetResets
        {
            get { return offsetResets; }
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

        // The last resolve asked for a tick past the newest sample.
        public bool Starved
        {
            get { return starved; }
        }

        // The last resolve held a single sample instead of blending two.
        public bool Holding
        {
            get { return from != null && to == null; }
        }

        public bool Push(WorldSnapshotMessage snapshot, float receiveTime)
        {
            if (snapshot.Tick <= NewestTick)
                return false;

            ObserveArrival(snapshot.Tick, receiveTime);

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
            sample.States.Clear();
            List<EntityState> states = snapshot.Entities;
            for (int i = 0; i < states.Count; i++)
                sample.States[states[i].EntityId] = states[i];

            samples.Add(sample);
            return true;
        }

        // Exponential average of arrival minus tick time. A sample far from the average means a
        // reconnect or a long hitch, so the offset snaps to it instead of easing over.
        void ObserveArrival(uint tick, float receiveTime)
        {
            double sample = receiveTime - tick * TickLength;
            if (!hasOffset || Math.Abs(sample - offset) > OffsetResetThreshold)
            {
                offset = sample;
                hasOffset = true;
                offsetResets++;
                return;
            }

            offset += (sample - offset) * OffsetAlpha;
        }

        public void Clear()
        {
            for (int i = 0; i < samples.Count; i++)
                pool.Push(samples[i]);
            samples.Clear();
            from = null;
            to = null;
            blend = 0f;
            ticksBehindNewest = 0f;
            resolvedTick = 0;
            starved = false;
            offset = 0.0;
            hasOffset = false;
        }

        // Brackets renderTick between two samples by tick. Past the newest or before the oldest it
        // holds that sample, never extrapolates.
        public bool Resolve(double renderTick)
        {
            from = null;
            to = null;
            blend = 0f;
            ticksBehindNewest = 0f;
            resolvedTick = 0;
            starved = false;
            if (samples.Count == 0)
                return false;

            Sample newest = samples[samples.Count - 1];
            Sample oldest = samples[0];

            if (renderTick >= newest.Tick)
            {
                from = newest;
                resolvedTick = newest.Tick;
                starved = renderTick > newest.Tick;
                return true;
            }

            if (renderTick <= oldest.Tick)
            {
                from = oldest;
                resolvedTick = oldest.Tick;
                ticksBehindNewest = newest.Tick - oldest.Tick;
                return true;
            }

            int index = samples.Count - 2;
            while (index > 0 && samples[index].Tick > renderTick)
                index--;

            from = samples[index];
            to = samples[index + 1];
            resolvedTick = from.Tick;
            blend = (float)((renderTick - from.Tick) / (to.Tick - from.Tick));
            ticksBehindNewest = (float)(newest.Tick - renderTick);
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

        // The later sample of the bracket, or the held sample. Discrete fields only, position is
        // whatever that sample carries. Used for what a unit is about to do rather than what it did.
        public bool TryGetNextState(int entityId, out EntityState state)
        {
            state = default;
            if (from == null)
                return false;

            if (to != null && to.States.TryGetValue(entityId, out state))
                return true;

            return from.States.TryGetValue(entityId, out state);
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
