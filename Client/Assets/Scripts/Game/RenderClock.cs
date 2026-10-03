using Multiplayer.Protocol;

namespace Multiplayer.Game
{
    public sealed class RenderClock
    {
        public const float Delay = (float)NetConfig.InterpolationDelayTicks / NetConfig.TickRate;

        readonly SnapshotBuffer buffer;
        float localTime;
        bool resolved;
        uint newestTick;
        float ticksBehindNewest;

        public RenderClock(SnapshotBuffer snapshotBuffer)
        {
            buffer = snapshotBuffer;
        }

        public float RenderTime
        {
            get
            {
                float time = localTime - Delay;
                // Starved: hold at the newest snapshot instead of running past it.
                return buffer.Count > 0 && time > buffer.NewestTime ? buffer.NewestTime : time;
            }
        }

        public float RenderTick
        {
            get { return resolved ? newestTick - ticksBehindNewest : 0f; }
        }

        // Resolves the buffer at the new render time, so positions read from it afterwards and the
        // tick reported here come from one bracketing. Both stay fixed until the next Advance.
        public bool Advance(float time)
        {
            localTime = time;
            resolved = buffer.Resolve(RenderTime);
            newestTick = buffer.NewestTick;
            ticksBehindNewest = buffer.TicksBehindNewest;
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
        }
    }
}
