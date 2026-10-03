using System;
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
                        Vec2 actual;
                        Assert.Equal(reference.TryGetPosition(id, out expected), buffer.TryGetPosition(id, out actual));
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

                    Vec2 drawn;
                    Assert.True(buffer.TryGetPosition(4, out drawn));
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

        static void Run(int seed, Action<WorldSnapshotMessage, float> receive, Action<float> frame)
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
