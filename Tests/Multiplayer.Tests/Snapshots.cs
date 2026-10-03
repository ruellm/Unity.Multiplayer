using Multiplayer.Protocol;

namespace Multiplayer.Tests
{
    static class Snapshots
    {
        // Entity ids are 1-based indexes into xs. A NaN entry leaves that entity out of the snapshot.
        public static WorldSnapshotMessage At(uint tick, params float[] xs)
        {
            WorldSnapshotMessage snapshot = new WorldSnapshotMessage();
            snapshot.Tick = tick;
            for (int i = 0; i < xs.Length; i++)
            {
                if (float.IsNaN(xs[i]))
                    continue;

                EntityState state = default;
                state.EntityId = i + 1;
                state.Position = new Vec2(xs[i], 0f);
                snapshot.Entities.Add(state);
            }
            return snapshot;
        }
    }
}
