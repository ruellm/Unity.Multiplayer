using System;
using System.Collections.Generic;
using Multiplayer.Game;
using Multiplayer.Protocol;
using Xunit;

namespace Multiplayer.Tests
{
    // Drives the pre-RenderClock pipeline and the current one with the same randomized feed:
    // frame and arrival jitter, packet loss, replayed stale ticks, a mid-run spawn and server death.
    public sealed class JitterSimulationTests
    {
        const int Frames = 20000;
        const int ServerDeathFrame = 15000;
        const uint SpawnTick = 40;

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(4)]
        [InlineData(5)]
        public void PositionsMatchReferenceBitForBit(int seed)
        {
            ReferenceSnapshotBuffer reference = new ReferenceSnapshotBuffer();
            uint referenceLastTick = 0;
            SnapshotBuffer buffer = new SnapshotBuffer();
            RenderClock clock = new RenderClock(buffer);

            Run(seed,
                (snapshot, time) =>
                {
                    if (snapshot.Tick > referenceLastTick)
                    {
                        referenceLastTick = snapshot.Tick;
                        reference.Push(snapshot, time);
                    }

                    buffer.Push(snapshot, time);
                },
                time =>
                {
                    bool expectedResolved = reference.Resolve(time - RenderClock.Delay);
                    Assert.Equal(expectedResolved, clock.Advance(time));

                    for (int id = 1; id <= 3; id++)
                    {
                        Vec2 expected;
                        EntityState state;
                        Assert.Equal(reference.TryGetPosition(id, out expected), buffer.TryGetState(id, out state));
                        Vec2 actual = state.Position;
                        Assert.Equal(BitConverter.SingleToInt32Bits(expected.X), BitConverter.SingleToInt32Bits(actual.X));
                        Assert.Equal(BitConverter.SingleToInt32Bits(expected.Z), BitConverter.SingleToInt32Bits(actual.Z));
                    }
                });
        }

        // Entity 4 sits at x == tick, so its drawn position is the tick the units are drawn at.
        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(4)]
        [InlineData(5)]
        public void RenderTickMatchesDrawnPositions(int seed)
        {
            const float Tolerance = 0.001f;
            SnapshotBuffer buffer = new SnapshotBuffer();
            RenderClock clock = new RenderClock(buffer);
            int checkedFrames = 0;

            Run(seed,
                (snapshot, time) => buffer.Push(snapshot, time),
                time =>
                {
                    if (!clock.Advance(time))
                        return;

                    EntityState state;
                    Assert.True(buffer.TryGetState(4, out state));
                    Vec2 drawn = state.Position;
                    Assert.InRange(clock.RenderTick, drawn.X - Tolerance, drawn.X + Tolerance);

                    float whole = (float)Math.Floor(drawn.X);
                    float fraction = drawn.X - whole;
                    if (fraction > Tolerance && fraction < 1f - Tolerance)
                    {
                        Assert.True(clock.HasReached((uint)whole));
                        Assert.False(clock.HasReached((uint)whole + 1));
                    }

                    checkedFrames++;
                });

            Assert.InRange(checkedFrames, Frames / 2, Frames);
        }

        // Oracle: the newest buffered snapshot received at or before render time, else the oldest buffered.
        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(4)]
        [InlineData(5)]
        public void DiscreteStateComesFromTheSnapshotTheClockHasReached(int seed)
        {
            const int BufferCapacity = 8;
            SnapshotBuffer buffer = new SnapshotBuffer();
            RenderClock clock = new RenderClock(buffer);
            List<uint> acceptedTicks = new List<uint>();
            List<float> acceptedTimes = new List<float>();
            int checkedFrames = 0;

            Run(seed,
                (snapshot, time) =>
                {
                    if (!buffer.Push(snapshot, time))
                        return;

                    acceptedTicks.Add(snapshot.Tick);
                    acceptedTimes.Add(time);
                },
                time =>
                {
                    if (!clock.Advance(time))
                        return;

                    int oldest = Math.Max(0, acceptedTicks.Count - BufferCapacity);
                    int index = acceptedTicks.Count - 1;
                    while (index > oldest && acceptedTimes[index] > clock.RenderTime)
                        index--;
                    uint expected = acceptedTicks[index];

                    EntityState state;
                    Assert.True(buffer.TryGetState(1, out state));
                    Assert.Equal((int)expected, state.Health);
                    Assert.Equal(Snapshots.ActionStateAt(expected), state.ActionState);
                    Assert.Equal(Snapshots.TargetAt(expected), state.TargetEntityId);
                    Assert.Equal(Snapshots.UnitTypeAt(expected), state.UnitType);
                    Assert.True(clock.HasReached(expected));

                    checkedFrames++;
                });

            Assert.InRange(checkedFrames, Frames / 2, Frames);
        }

        static void Run(int seed,Action<WorldSnapshotMessage, float> receive, Action<float> frame)
        {
            Random rng = new Random(seed);
            float time = 0f;
            uint tick = 0;
            float nextSnapshot = 0.05f;

            for (int i = 0; i < Frames; i++)
            {
                time += 0.004f + (float)rng.NextDouble() * 0.03f;

                while (i < ServerDeathFrame && time >= nextSnapshot)
                {
                    tick++;
                    nextSnapshot += 0.05f + ((float)rng.NextDouble() - 0.5f) * 0.04f;
                    if (rng.Next(10) == 0)
                        continue;

                    uint sent = rng.Next(15) == 0 && tick > 3 ? tick - 2 : tick;
                    float spawned = sent > SpawnTick ? sent * 0.3f : float.NaN;
                    receive(Snapshots.At(sent, sent * 0.1f, -sent * 0.25f, spawned, sent), time);
                }

                frame(time);
            }
        }
    }
}
