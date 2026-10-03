using System.Collections.Generic;
using Multiplayer.Game;
using Multiplayer.Protocol;
using Xunit;

namespace Multiplayer.Tests
{
    public sealed class ResolvedStateTests
    {
        const float Delay = RenderClock.Delay;
        const float Tolerance = 0.001f;

        readonly SnapshotBuffer buffer = new SnapshotBuffer();
        readonly RenderClock clock;

        public ResolvedStateTests()
        {
            clock = new RenderClock(buffer);
        }

        EntityState StateOf(int entityId)
        {
            EntityState state;
            Assert.True(buffer.TryGetState(entityId, out state));
            return state;
        }

        static void AssertDiscreteFieldsFrom(uint tick, EntityState state)
        {
            Assert.Equal((int)tick, state.Health);
            Assert.Equal(Snapshots.ActionStateAt(tick), state.ActionState);
            Assert.Equal(Snapshots.TargetAt(tick), state.TargetEntityId);
            Assert.Equal(Snapshots.UnitTypeAt(tick), state.UnitType);
        }

        [Fact]
        public void BetweenSnapshotsDiscreteFieldsComeFromTheEarlierOneAndPositionIsLerped()
        {
            buffer.Push(Snapshots.At(10, 0f), 1.00f);
            buffer.Push(Snapshots.At(11, 1f), 1.05f);
            buffer.Push(Snapshots.At(12, 2f), 1.10f);

            clock.Advance(1.07f + Delay);

            EntityState state = StateOf(1);
            AssertDiscreteFieldsFrom(11, state);
            Assert.Equal(1, state.EntityId);
            Assert.InRange(state.Position.X, 1.4f - Tolerance, 1.4f + Tolerance);
        }

        [Fact]
        public void StateChangeIsNotVisibleUntilTheClockReachesItsSnapshot()
        {
            buffer.Push(Snapshots.At(10, 0f), 1.00f);
            buffer.Push(Snapshots.At(11, 1f), 1.05f);

            float[] before = { 1.001f, 1.02f, 1.04f, 1.049f };
            for (int i = 0; i < before.Length; i++)
            {
                clock.Advance(before[i] + Delay);
                AssertDiscreteFieldsFrom(10, StateOf(1));
                Assert.False(clock.HasReached(11));
            }

            clock.Advance(1.06f + Delay);
            AssertDiscreteFieldsFrom(11, StateOf(1));
            Assert.True(clock.HasReached(11));
        }

        [Fact]
        public void ArrivalOfANewerSnapshotDoesNotChangeWhatIsShown()
        {
            buffer.Push(Snapshots.At(10, 0f), 1.00f);
            clock.Advance(1.10f);
            AssertDiscreteFieldsFrom(10, StateOf(1));

            buffer.Push(Snapshots.At(11, 1f), 1.12f);
            clock.Advance(1.12f);

            EntityState state = StateOf(1);
            AssertDiscreteFieldsFrom(10, state);
            Assert.InRange(state.Position.X, 0.01f, 0.5f);
        }

        [Fact]
        public void HoldsShowTheWholeHeldSample()
        {
            buffer.Push(Snapshots.At(10, 0f), 1.00f);
            buffer.Push(Snapshots.At(11, 1f), 1.05f);

            clock.Advance(0.90f + Delay);
            AssertDiscreteFieldsFrom(10, StateOf(1));
            Assert.Equal(0f, StateOf(1).Position.X);

            clock.Advance(50f);
            AssertDiscreteFieldsFrom(11, StateOf(1));
            Assert.Equal(1f, StateOf(1).Position.X);
        }

        [Fact]
        public void JustSpawnedEntityCarriesTheStateOfItsFirstSnapshot()
        {
            buffer.Push(Snapshots.At(10, 0f), 1.00f);
            buffer.Push(Snapshots.At(11, 1f, 5f), 1.05f);

            clock.Advance(1.025f + Delay);

            EntityState spawned = StateOf(2);
            AssertDiscreteFieldsFrom(11, spawned);
            Assert.Equal(5f, spawned.Position.X);
        }

        [Fact]
        public void ResolvedSetFollowsTheLaterSampleInABracketAndTheHeldSampleOtherwise()
        {
            buffer.Push(Snapshots.At(10, 0f, 0f), 1.00f);
            buffer.Push(Snapshots.At(11, float.NaN, 1f, 1f), 1.05f);
            List<EntityState> resolved = new List<EntityState>();

            clock.Advance(0.90f + Delay);
            buffer.GetResolved(resolved);
            Assert.Equal(new[] { 1, 2 }, Ids(resolved));

            clock.Advance(1.025f + Delay);
            buffer.GetResolved(resolved);
            Assert.Equal(new[] { 2, 3 }, Ids(resolved));
            AssertDiscreteFieldsFrom(10, resolved.Find(s => s.EntityId == 2));

            EntityState removed;
            Assert.False(buffer.TryGetState(1, out removed));
        }

        [Fact]
        public void EmptyBufferResolvesNoStates()
        {
            List<EntityState> resolved = new List<EntityState> { default };

            clock.Advance(1f);
            buffer.GetResolved(resolved);

            Assert.Empty(resolved);
        }

        static int[] Ids(List<EntityState> states)
        {
            int[] ids = new int[states.Count];
            for (int i = 0; i < states.Count; i++)
                ids[i] = states[i].EntityId;
            System.Array.Sort(ids);
            return ids;
        }
    }
}
