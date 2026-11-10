using Multiplayer.Game;
using Multiplayer.Protocol;
using Xunit;

namespace Multiplayer.Tests
{
    // Entity 1 sits at x == tick in every snapshot, so a drawn x is the tick it was drawn at.
    public sealed class TickTimelineTests
    {
        const float Tolerance = 0.0005f;
        const double OffsetTolerance = 0.000001;

        readonly SnapshotBuffer buffer = new SnapshotBuffer();
        readonly RenderClock clock;

        public TickTimelineTests()
        {
            clock = new RenderClock(buffer);
        }

        float XOf(int entityId)
        {
            EntityState state;
            Assert.True(buffer.TryGetState(entityId, out state));
            return state.Position.X;
        }

        [Fact]
        public void JitteredArrivalsInterpolateEvenlyByTick()
        {
            float[] gaps = { 0.028f, 0.073f, 0.040f, 0.061f, 0.033f, 0.070f, 0.045f };
            float time = 1f;
            uint tick = 10;
            buffer.Push(Snapshots.At(tick, tick), time);
            for (int i = 0; i < gaps.Length; i++)
            {
                time += gaps[i];
                tick++;
                Assert.True(buffer.Push(Snapshots.At(tick, tick), time));
            }

            for (uint t = 10; t < 17; t++)
            {
                Assert.True(buffer.Resolve(t));
                Assert.Equal(t, XOf(1));
                Assert.True(buffer.Resolve(t + 0.5));
                Assert.InRange(XOf(1), t + 0.5f - Tolerance, t + 0.5f + Tolerance);
            }

            // Through the clock, with no new packets, the point moves at exactly one tick per tick length.
            float previous = float.NaN;
            for (int i = 0; i <= 10; i++)
            {
                float at = 1.2f + i * 0.01f;
                Assert.True(clock.Advance(at));
                Assert.False(clock.Holding);
                Assert.InRange(XOf(1), clock.RenderTick - Tolerance, clock.RenderTick + Tolerance);
                if (!float.IsNaN(previous))
                    Assert.InRange(clock.RenderTick - previous, 0.2f - 0.002f, 0.2f + 0.002f);
                previous = clock.RenderTick;
            }
        }

        [Fact]
        public void SnapshotsArrivingInTheSameFrameStillInterpolate()
        {
            buffer.Push(Snapshots.At(10, 10f), 1f);
            buffer.Push(Snapshots.At(11, 11f), 1f);
            buffer.Push(Snapshots.At(12, 12f), 1f);

            Assert.True(buffer.Resolve(10.5));
            Assert.InRange(XOf(1), 10.5f - Tolerance, 10.5f + Tolerance);
            Assert.True(buffer.Resolve(11.25));
            Assert.InRange(XOf(1), 11.25f - Tolerance, 11.25f + Tolerance);

            float previous = float.NaN;
            for (int i = 0; i <= 40; i++)
            {
                clock.Advance(1f + i * 0.005f);
                if (!clock.Holding && !float.IsNaN(previous))
                    Assert.InRange(clock.RenderTick - previous, 0f, 0.2f);
                previous = clock.Holding ? float.NaN : clock.RenderTick;
            }
        }

        [Fact]
        public void OffsetEasesOnSmallJitterAndResetsOnALargeJump()
        {
            buffer.Push(Snapshots.At(10, 10f), 1.00f);
            Assert.InRange(buffer.Offset, 0.5 - OffsetTolerance, 0.5 + OffsetTolerance);
            Assert.Equal(1, buffer.OffsetResets);

            buffer.Push(Snapshots.At(11, 11f), 1.07f);
            Assert.InRange(buffer.Offset, 0.501 - OffsetTolerance, 0.501 + OffsetTolerance);
            Assert.Equal(1, buffer.OffsetResets);

            buffer.Push(Snapshots.At(12, 12f), 5.00f);
            Assert.InRange(buffer.Offset, 4.4 - OffsetTolerance, 4.4 + OffsetTolerance);
            Assert.Equal(2, buffer.OffsetResets);

            buffer.Push(Snapshots.At(13, 13f), 5.05f);
            Assert.InRange(buffer.Offset, 4.4 - OffsetTolerance, 4.4 + OffsetTolerance);
            Assert.Equal(2, buffer.OffsetResets);

            // Rendering follows the new offset: tick 13 is drawn at 5.05 plus the delay.
            Assert.True(clock.Advance(5.05f + RenderClock.Delay));
            Assert.Equal(13f, clock.RenderTick);
        }

        [Fact]
        public void StarvedFramesAreCountedOnlyPastTheNewestSample()
        {
            buffer.Push(Snapshots.At(10, 10f), 1.00f);
            buffer.Push(Snapshots.At(11, 11f), 1.05f);

            clock.Advance(1.12f);
            Assert.False(clock.Starved);
            Assert.Equal(0, clock.StarvedFrames);

            clock.Advance(1.15f);
            Assert.False(clock.Starved);
            Assert.Equal(0, clock.StarvedFrames);

            clock.Advance(1.2f);
            Assert.True(clock.Starved);
            clock.Advance(2f);
            Assert.Equal(2, clock.StarvedFrames);

            clock.Reset();
            Assert.Equal(0, clock.StarvedFrames);
        }
    }
}
