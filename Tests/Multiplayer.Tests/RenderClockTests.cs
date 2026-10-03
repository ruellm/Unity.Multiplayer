using Multiplayer.Game;
using Multiplayer.Protocol;
using Xunit;

namespace Multiplayer.Tests
{
    public sealed class RenderClockTests
    {
        const float Delay = RenderClock.Delay;
        const float Tolerance = 0.001f;

        readonly SnapshotBuffer buffer = new SnapshotBuffer();
        readonly RenderClock clock;

        public RenderClockTests()
        {
            clock = new RenderClock(buffer);
        }

        float XOf(int entityId)
        {
            EntityState state;
            Assert.True(buffer.TryGetState(entityId, out state));
            return state.Position.X;
        }

        void PushTwo()
        {
            Assert.True(buffer.Push(Snapshots.At(10, 0f), 1.00f));
            Assert.True(buffer.Push(Snapshots.At(11, 1f, 5f), 1.05f));
        }

        [Fact]
        public void EmptyBufferResolvesNothing()
        {
            Assert.False(clock.Advance(1f));
            Assert.Equal(0f, clock.RenderTick);
            Assert.False(clock.HasReached(1));

            EntityState state;
            Assert.False(buffer.TryGetState(1, out state));
        }

        [Fact]
        public void StaleAndDuplicateTicksAreDiscarded()
        {
            PushTwo();

            Assert.False(buffer.Push(Snapshots.At(11, 99f), 1.06f));
            Assert.False(buffer.Push(Snapshots.At(9, 99f), 1.06f));
            Assert.Equal(2, buffer.Count);
            Assert.Equal(11u, buffer.NewestTick);

            clock.Advance(10f);
            Assert.Equal(1f, XOf(1));
        }

        [Fact]
        public void BeforeOldestSampleHoldsAtOldest()
        {
            PushTwo();

            Assert.True(clock.Advance(0.95f + Delay));

            Assert.Equal(0f, XOf(1));
            Assert.Equal(10f, clock.RenderTick);
            Assert.True(clock.HasReached(10));
            Assert.False(clock.HasReached(11));
        }

        [Fact]
        public void BetweenSamplesLerpsPositionAndTickTogether()
        {
            PushTwo();

            clock.Advance(1.025f + Delay);

            Assert.InRange(XOf(1), 0.5f - Tolerance, 0.5f + Tolerance);
            Assert.InRange(clock.RenderTick, 10.5f - Tolerance, 10.5f + Tolerance);
            Assert.True(clock.HasReached(10));
            Assert.False(clock.HasReached(11));
        }

        [Fact]
        public void JustSpawnedEntityIsPlacedDirectly()
        {
            PushTwo();

            clock.Advance(1.025f + Delay);

            Assert.Equal(5f, XOf(2));
        }

        [Fact]
        public void LateSnapshotDoesNotPullTickAheadOfPositions()
        {
            buffer.Push(Snapshots.At(10, 10f), 1.00f);
            buffer.Push(Snapshots.At(11, 11f), 1.08f);
            buffer.Push(Snapshots.At(12, 12f), 1.10f);

            clock.Advance(1.04f + Delay);

            // Extrapolating back from tick 12 at the nominal rate would report 10.8 here.
            Assert.InRange(XOf(1), 10.5f - Tolerance, 10.5f + Tolerance);
            Assert.InRange(clock.RenderTick, 10.5f - Tolerance, 10.5f + Tolerance);
        }

        [Fact]
        public void LostSnapshotSpansTheGapForPositionAndTick()
        {
            buffer.Push(Snapshots.At(10, 10f), 1.00f);
            buffer.Push(Snapshots.At(12, 12f), 1.10f);

            clock.Advance(1.075f + Delay);

            Assert.InRange(XOf(1), 11.5f - Tolerance, 11.5f + Tolerance);
            Assert.InRange(clock.RenderTick, 11.5f - Tolerance, 11.5f + Tolerance);
            Assert.True(clock.HasReached(11));
            Assert.False(clock.HasReached(12));
        }

        [Fact]
        public void NewestTickIsReachedOneDelayAfterItArrives()
        {
            PushTwo();

            clock.Advance(1.05f + Delay);

            Assert.Equal(1f, XOf(1));
            Assert.Equal(11f, clock.RenderTick);
            Assert.True(clock.HasReached(11));
        }

        [Fact]
        public void LongStarvationHoldsAtNewestWithoutDrift()
        {
            PushTwo();

            for (int i = 0; i < 200; i++)
            {
                Assert.True(clock.Advance(1.2f + i * 0.5f));
                Assert.Equal(1f, XOf(1));
                Assert.Equal(1.05f, clock.RenderTime);
                Assert.Equal(11f, clock.RenderTick);
                Assert.False(clock.HasReached(12));
            }
        }

        [Fact]
        public void PushAfterAdvanceDoesNotMoveTheClockUntilNextAdvance()
        {
            PushTwo();
            clock.Advance(1.025f + Delay);
            float before = clock.RenderTick;

            buffer.Push(Snapshots.At(12, 2f), 1.10f);

            Assert.Equal(before, clock.RenderTick);
            Assert.False(clock.HasReached(11));
        }

        [Fact]
        public void FullBufferEvictsOldestSample()
        {
            for (uint tick = 1; tick <= 10; tick++)
                Assert.True(buffer.Push(Snapshots.At(tick, tick), tick * 0.05f));

            Assert.Equal(8, buffer.Count);

            clock.Advance(0f);
            Assert.Equal(3f, XOf(1));
            Assert.Equal(3f, clock.RenderTick);
        }

        [Fact]
        public void ClearAndResetAcceptARestartedServersLowTicks()
        {
            PushTwo();
            clock.Advance(10f);

            buffer.Clear();
            clock.Reset();

            Assert.Equal(0, buffer.Count);
            Assert.False(clock.HasReached(10));
            Assert.Equal(0f, clock.RenderTick);
            Assert.True(buffer.Push(Snapshots.At(1, 7f), 200f));

            clock.Advance(200f + Delay);
            Assert.Equal(7f, XOf(1));
            Assert.True(clock.HasReached(1));
        }

        [Fact]
        public void HasReachedIsExactBeyondFloatPrecision()
        {
            buffer.Push(Snapshots.At(2999999999u, 0f), 10.00f);
            buffer.Push(Snapshots.At(3000000000u, 1f), 10.05f);

            clock.Advance(10.03f + Delay);

            Assert.True(clock.HasReached(2999999999u));
            Assert.False(clock.HasReached(3000000000u));
        }
    }
}
