using Multiplayer.Protocol;

namespace Multiplayer.Game
{
    public sealed class RenderClock
    {
        public const int DelayTicks = NetConfig.InterpolationDelayTicks;
        public const float Delay = (float)DelayTicks / NetConfig.TickRate;
        const double TickSnap = 0.0001;

        readonly SnapshotBuffer buffer;
        bool resolved;
        uint newestTick;
        float ticksBehindNewest;
        uint stateTick;
        int starvedFrames;

        public RenderClock(SnapshotBuffer snapshotBuffer)
        {
            buffer = snapshotBuffer;
        }

        public float RenderTick
        {
            get { return resolved ? newestTick - ticksBehindNewest : 0f; }
        }

        // Tick of the snapshot that discrete state is currently drawn from. Whole ticks only, so
        // anything counted against it steps exactly when that state does.
        public uint StateTick
        {
            get { return stateTick; }
        }

        public double Offset
        {
            get { return buffer.Offset; }
        }

        // Frames drawn past the newest snapshot, holding it instead of extrapolating.
        public int StarvedFrames
        {
            get { return starvedFrames; }
        }

        public bool Starved
        {
            get { return resolved && buffer.Starved; }
        }

        public bool Holding
        {
            get { return resolved && buffer.Holding; }
        }

        // Local time mapped onto the server tick timeline, minus the interpolation delay. A result
        // within microseconds of a whole tick is that tick, so float noise cannot leave a snapshot
        // one hair short of reached.
        public double RenderTickFor(float time)
        {
            double tick = (time - buffer.Offset) / SnapshotBuffer.TickLength - DelayTicks;
            double whole = System.Math.Round(tick);
            return System.Math.Abs(tick - whole) < TickSnap ? whole : tick;
        }

        // Resolves the buffer at the new render tick, so positions read from it afterwards and the
        // tick reported here come from one bracketing. Both stay fixed until the next Advance.
        public bool Advance(float time)
        {
            resolved = buffer.HasOffset && buffer.Resolve(RenderTickFor(time));
            newestTick = buffer.NewestTick;
            ticksBehindNewest = buffer.TicksBehindNewest;
            stateTick = buffer.ResolvedTick;
            if (resolved && buffer.Starved)
                starvedFrames++;
            return resolved;
        }

        public bool HasReached(uint tick)
        {
            if (!resolved || tick > newestTick)
                return false;

            // Compared as a distance from the newest tick so large tick values keep float precision.
            return ticksBehindNewest <= newestTick - tick;
        }

        public void Reset()
        {
            resolved = false;
            newestTick = 0;
            ticksBehindNewest = 0f;
            stateTick = 0;
            starvedFrames = 0;
        }
    }
}
