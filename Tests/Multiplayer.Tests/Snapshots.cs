using Multiplayer.Protocol;

namespace Multiplayer.Tests
{
    static class Snapshots
    {
        // Entity ids are 1-based indexes into xs. A NaN entry leaves that entity out of the snapshot.
        // Every discrete field is derived from the tick, so a resolved state names the sample it came from.
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
                state.UnitType = UnitTypeAt(tick);
                state.Health = (int)tick;
                state.ActionState = ActionStateAt(tick);
                state.TargetEntityId = TargetAt(tick);
                state.Position = new Vec2(xs[i], 0f);
                snapshot.Entities.Add(state);
            }
            return snapshot;
        }

        public static byte UnitTypeAt(uint tick)
        {
            return (byte)(1 + tick % 3);
        }

        public static byte ActionStateAt(uint tick)
        {
            return (byte)(tick % 4);
        }

        public static int TargetAt(uint tick)
        {
            return (int)tick + 1000;
        }
    }
}
