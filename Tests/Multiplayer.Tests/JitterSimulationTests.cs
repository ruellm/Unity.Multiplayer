using System;
using System.Collections.Generic;
using Multiplayer.Game;
using Multiplayer.Protocol;
using Xunit;

namespace Multiplayer.Tests
{
    // Drives the pipeline with a randomized feed: frame and arrival jitter, packet loss, replayed
    // stale ticks, a mid-run spawn and server death. Entity 1 sits at x == 0.1 * tick and entity 4
    // at x == tick, so drawn positions say which tick the units are drawn at.
    public sealed class JitterSimulationTests
    {
        const int Frames = 20000;
        const int ServerDeathFrame = 15000;
        const uint SpawnTick = 40;
        const float Tolerance = 0.001f;

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(4)]
        [InlineData(5)]
        public void PositionsAreLinearInTickDespiteArrivalJitter(int seed)
        {
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
                    Assert.True(buffer.TryGetState(1, out state));
                    float expected = 0.1f * clock.RenderTick;
                    Assert.InRange(state.Position.X, expected - Tolerance, expected + Tolerance);
                    checkedFrames++;
                });

            Assert.InRange(checkedFrames, Frames / 2, Frames);
        }

        // Between holds, the render tick advances with local time at one tick per tick length,
        // whatever the arrival spacing was. The smoothed offset may drift by a millisecond or so.
        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(4)]
        [InlineData(5)]
        public void RenderTickAdvancesAtTickRateDespiteArrivalJitter(int seed)
        {
            const float RateTolerance = 0.004f;
            SnapshotBuffer buffer = new SnapshotBuffer();
            RenderClock clock = new RenderClock(buffer);
            bool previousValid = false;
            float previousTick = 0f;
            float previousTime = 0f;
            int checkedFrames = 0;

            Run(seed,
                (snapshot, time) => buffer.Push(snapshot, time),
                time =>
                {
                    bool valid = clock.Advance(time) && !clock.Holding;
                    if (valid && previousValid)
                    {
                        float advanced = (clock.RenderTick - previousTick) * (float)SnapshotBuffer.TickLength;
                        float elapsed = time - previousTime;
                        Assert.True(advanced >= 0f);
                        Assert.InRange(advanced, elapsed - RateTolerance, elapsed + RateTolerance);
                        checkedFrames++;
                    }

                    previousValid = valid;
                    previousTick = clock.RenderTick;
                    previousTime = time;
                });

            Assert.InRange(checkedFrames, Frames / 2, Frames);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(4)]
        [InlineData(5)]
        public void RenderTickMatchesDrawnPositions(int seed)
        {
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

        // Oracle: the newest buffered tick at or below the render tick, else the oldest buffered.
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
            int checkedFrames = 0;

            Run(seed,
                (snapshot, time) =>
                {
                    if (buffer.Push(snapshot, time))
                        acceptedTicks.Add(snapshot.Tick);
                },
                time =>
                {
                    if (!clock.Advance(time))
                        return;

                    double renderTick = clock.RenderTickFor(time);
                    int oldest = Math.Max(0, acceptedTicks.Count - BufferCapacity);
                    int index = acceptedTicks.Count - 1;
                    while (index > oldest && acceptedTicks[index] > renderTick)
                        index--;
                    uint expected = acceptedTicks[index];

                    EntityState state;
                    Assert.True(buffer.TryGetState(1, out state));
                    Assert.Equal((int)expected, state.Health);
                    Assert.Equal(Snapshots.ActionStateAt(expected), state.ActionState);
                    Assert.Equal(Snapshots.TargetAt(expected), state.TargetEntityId);
                    Assert.Equal(Snapshots.UnitTypeAt(expected), state.UnitType);
                    Assert.True(clock.HasReached(expected));
                    Assert.Equal(expected, clock.StateTick);

                    checkedFrames++;
                });

            Assert.InRange(checkedFrames, Frames / 2, Frames);
        }

        // Ticks leave the server on a fixed 50 ms schedule. Each one arrives 20 ms later give or take
        // 20 ms, one in ten is lost, and one in fifteen is replaced by a stale replay.
        static void Run(int seed, Action<WorldSnapshotMessage, float> receive, Action<float> frame)
        {
            Random rng = new Random(seed);
            float time = 0f;
            uint tick = 1;
            float nextArrival = ArrivalOf(tick, rng);

            for (int i = 0; i < Frames; i++)
            {
                time += 0.004f + (float)rng.NextDouble() * 0.03f;

                while (i < ServerDeathFrame && time >= nextArrival)
                {
                    uint current = tick;
                    tick++;
                    nextArrival = ArrivalOf(tick, rng);
                    if (rng.Next(10) == 0)
                        continue;

                    uint sent = rng.Next(15) == 0 && current > 3 ? current - 2 : current;
                    float spawned = sent > SpawnTick ? sent * 0.3f : float.NaN;
                    receive(Snapshots.At(sent, sent * 0.1f, -sent * 0.25f, spawned, sent), time);
                }

                frame(time);
            }
        }

        static float ArrivalOf(uint tick, Random rng)
        {
            return tick * 0.05f + 0.02f + ((float)rng.NextDouble() - 0.5f) * 0.04f;
        }
    }
}
