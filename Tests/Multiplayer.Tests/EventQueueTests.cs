using System.Collections.Generic;
using Multiplayer.Game;
using Multiplayer.Protocol;
using Xunit;

namespace Multiplayer.Tests
{
    public sealed class EventQueueTests
    {
        const float Delay = RenderClock.Delay;

        readonly SnapshotBuffer buffer = new SnapshotBuffer();
        readonly RenderClock clock;
        readonly EventQueue queue = new EventQueue();
        readonly List<GameEventMessage> released = new List<GameEventMessage>();

        public EventQueueTests()
        {
            clock = new RenderClock(buffer);
        }

        // Mirrors WorldView: a snapshot is buffered and, if accepted, shown to the queue.
        void Receive(uint tick, float time)
        {
            if (buffer.Push(Snapshots.At(tick, 0f), time))
                queue.ObserveSnapshot(tick);
        }

        static GameEventMessage Died(uint tick, int entityId)
        {
            return GameEventMessage.Died(tick, entityId, DeathCause.Shot);
        }

        int[] ReleaseAt(float localTime)
        {
            clock.Advance(localTime);
            queue.Release(clock, released);

            int[] ids = new int[released.Count];
            for (int i = 0; i < released.Count; i++)
                ids[i] = released[i].EntityDied.EntityId;
            return ids;
        }

        [Fact]
        public void EventIsHeldUntilTheRenderClockReachesItsTick()
        {
            Receive(10, 1.00f);
            Receive(11, 1.05f);
            Receive(12, 1.10f);
            queue.Enqueue(Died(12, 7));

            Assert.Empty(ReleaseAt(1.10f));
            Assert.Empty(ReleaseAt(1.07f + Delay));
            Assert.Empty(ReleaseAt(1.095f + Delay));
            Assert.Equal(1, queue.Count);

            Assert.Equal(new[] { 7 }, ReleaseAt(1.11f + Delay));
            Assert.Equal(0, queue.Count);
            Assert.Empty(ReleaseAt(1.2f + Delay));
        }

        [Fact]
        public void EventIsReleasedOnTheFrameItsSnapshotStateIsDrawn()
        {
            Receive(10, 1.00f);
            Receive(11, 1.05f);
            Receive(12, 1.10f);
            queue.Enqueue(Died(11, 7));

            float[] times = { 1.01f, 1.03f, 1.049f, 1.06f, 1.08f };
            for (int i = 0; i < times.Length; i++)
            {
                ReleaseAt(times[i] + Delay);

                Assert.Equal(clock.StateTick >= 11, queue.ReleasedCount == 1);
            }
        }

        [Fact]
        public void EventsAreReleasedInTickOrderWhateverOrderTheyArrived()
        {
            Receive(10, 1.00f);
            Receive(13, 1.15f);
            queue.Enqueue(Died(13, 3));
            queue.Enqueue(Died(11, 1));
            queue.Enqueue(Died(12, 2));
            queue.Enqueue(Died(11, 4));

            Assert.Equal(new[] { 1, 4, 2, 3 }, ReleaseAt(5f));
            Assert.Equal(3, queue.LastReleased.EntityDied.EntityId);
            Assert.Equal(4, queue.ReleasedCount);
        }

        [Fact]
        public void OnlyTheEventsReachedAreReleased()
        {
            Receive(10, 1.00f);
            Receive(11, 1.05f);
            Receive(12, 1.10f);
            queue.Enqueue(Died(10, 1));
            queue.Enqueue(Died(11, 2));
            queue.Enqueue(Died(12, 3));

            Assert.Equal(new[] { 1, 2 }, ReleaseAt(1.07f + Delay));
            Assert.Equal(1, queue.Count);
            Assert.Equal(new[] { 3 }, ReleaseAt(1.2f + Delay));
        }

        [Fact]
        public void EventForATickNotYetReceivedWaits()
        {
            Receive(10, 1.00f);
            queue.Enqueue(Died(40, 9));

            Assert.Empty(ReleaseAt(30f));

            Receive(40, 31f);
            Assert.Equal(new[] { 9 }, ReleaseAt(31f + Delay + 0.01f));
        }

        [Fact]
        public void NothingIsReleasedBeforeAnySnapshot()
        {
            queue.Enqueue(Died(5, 1));

            Assert.Empty(ReleaseAt(10f));
            Assert.Equal(1, queue.Count);
        }

        [Fact]
        public void EventsOlderThanTheFirstSnapshotAreDropped()
        {
            Receive(500, 1.00f);
            queue.Enqueue(Died(120, 1));
            queue.Enqueue(Died(499, 2));
            queue.Enqueue(Died(500, 3));
            queue.Enqueue(Died(501, 4));
            Receive(501, 1.05f);

            Assert.Equal(2, queue.DroppedCount);
            Assert.Equal(new[] { 3, 4 }, ReleaseAt(5f));
        }

        [Fact]
        public void EventsThatArrivedBeforeTheFirstSnapshotAreDroppedIfOlderThanIt()
        {
            queue.Enqueue(Died(120, 1));
            queue.Enqueue(Died(500, 2));
            queue.Enqueue(Died(499, 3));

            Receive(500, 1.00f);

            Assert.Equal(2, queue.DroppedCount);
            Assert.Equal(new[] { 2 }, ReleaseAt(5f));
        }

        [Fact]
        public void TheFloorIsTheFirstSnapshotEverNotTheOldestBuffered()
        {
            Receive(500, 1.00f);
            for (uint tick = 501; tick <= 530; tick++)
                Receive(tick, 1.00f + (tick - 500) * 0.05f);

            queue.Enqueue(Died(505, 1));
            queue.Enqueue(Died(499, 2));

            Assert.Equal(1, queue.DroppedCount);
            Assert.Equal(new[] { 1 }, ReleaseAt(10f));
        }

        [Fact]
        public void ClearForgetsTheFloorAndEverythingQueued()
        {
            Receive(500, 1.00f);
            queue.Enqueue(Died(500, 1));
            queue.Enqueue(Died(900, 2));
            ReleaseAt(5f);

            queue.Clear();
            buffer.Clear();
            clock.Reset();

            Assert.Equal(0, queue.Count);
            Assert.Equal(0, queue.ReleasedCount);
            Assert.Equal(0, queue.DroppedCount);

            Receive(3, 20f);
            queue.Enqueue(Died(3, 7));
            Assert.Equal(new[] { 7 }, ReleaseAt(25f));
        }
    }
}
