using System.Collections.Generic;
using Multiplayer.Game;
using Multiplayer.Protocol;
using Multiplayer.Server;
using Xunit;

namespace Multiplayer.Tests
{
    // Runs the real server World and feeds FiringPhase only what a snapshot carries. A derived shot
    // must land on exactly the ticks where the server took health off the target.
    public sealed class FiringPhaseTests
    {
        const float Dt = 1f / NetConfig.TickRate;
        const int PlayerA = 1;
        const int PlayerB = 2;

        readonly World world = new World();
        readonly List<uint> serverShotTicks = new List<uint>();
        uint tick;

        static bool StepFrom(FiringPhase phase, uint stateTick, Entity attacker, Entity target)
        {
            bool known = target != null;
            return phase.Step(stateTick, attacker.ActionState, attacker.Health, attacker.TargetEntityId, known, known ? target.Health : 0);
        }

        // Steps the world one tick at a time and checks every observer against the server.
        void Run(int ticks, Entity attacker, params FiringPhase[] observers)
        {
            for (int i = 0; i < ticks; i++)
            {
                Entity before;
                int healthBefore = world.TryGet(attacker.TargetEntityId, out before) ? before.Health : 0;

                world.Integrate(++tick, Dt);

                Entity target;
                world.TryGet(attacker.TargetEntityId, out target);
                bool serverShot = target != null && target == before && target.Health < healthBefore;
                if (serverShot)
                    serverShotTicks.Add(tick);

                for (int o = 0; o < observers.Length; o++)
                    Assert.Equal(serverShot, StepFrom(observers[o], tick, attacker, target));
            }
        }

        [Theory]
        [InlineData(UnitType.Soldier)]
        [InlineData(UnitType.Tank)]
        public void ShotsLandOnTheTicksHealthDrops(UnitType unitType)
        {
            Entity structure = world.Spawn(PlayerB, UnitType.Structure, new Vec2(0f, 0f));
            Entity attacker = world.Spawn(PlayerA, unitType, new Vec2(-30f, 0f));
            FiringPhase phase = new FiringPhase(unitType, attacker.EntityId, tick);
            world.SetAttackTarget(attacker.EntityId, structure.EntityId);

            Run(600, attacker, phase);

            Assert.InRange(serverShotTicks.Count, 5, 600);
        }

        [Fact]
        public void StaysInPhaseThroughAChase()
        {
            Entity target = world.Spawn(PlayerB, UnitType.Tank, new Vec2(4f, 0f));
            Entity soldier = world.Spawn(PlayerA, UnitType.Soldier, new Vec2(0f, 0f));
            FiringPhase phase = new FiringPhase(UnitType.Soldier, soldier.EntityId, tick);
            world.SetAttackTarget(soldier.EntityId, target.EntityId);
            Run(17, soldier, phase);

            // The slower tank keeps walking out of range, so the soldier alternates between closing
            // and firing and misses the scheduled ticks that fall while it is closing.
            world.SetTarget(target.EntityId, new Vec2(4f, 45f));
            Run(500, soldier, phase);

            Assert.InRange(serverShotTicks.Count, 10, 50);
        }

        [Theory]
        [InlineData(UnitType.Soldier, 23)]
        [InlineData(UnitType.Soldier, 60)]
        [InlineData(UnitType.Tank, 47)]
        [InlineData(UnitType.Tank, 90)]
        public void ObserverJoiningMidFightDerivesTheSameShotTicks(UnitType unitType, int joinAfter)
        {
            Entity structure = world.Spawn(PlayerB, UnitType.Structure, new Vec2(0f, 0f));
            Entity attacker = world.Spawn(PlayerA, unitType, new Vec2(-4f, 0f));
            FiringPhase fromAcquisition = new FiringPhase(unitType, attacker.EntityId, tick);
            world.SetAttackTarget(attacker.EntityId, structure.EntityId);
            Run(joinAfter, attacker, fromAcquisition);
            int shotsBeforeJoin = serverShotTicks.Count;

            FiringPhase lateJoiner = new FiringPhase(unitType, attacker.EntityId, tick);
            Run(300, attacker, fromAcquisition, lateJoiner);

            Assert.InRange(shotsBeforeJoin, 1, 300);
            Assert.InRange(serverShotTicks.Count - shotsBeforeJoin, 5, 300);
        }

        [Fact]
        public void SwitchingTargetsKeepsFiringOnSchedule()
        {
            Entity first = world.Spawn(PlayerB, UnitType.Structure, new Vec2(0f, 0f));
            Entity second = world.Spawn(PlayerB, UnitType.Tank, new Vec2(-4f, 3f));
            Entity soldier = world.Spawn(PlayerA, UnitType.Soldier, new Vec2(-4f, 0f));
            FiringPhase phase = new FiringPhase(UnitType.Soldier, soldier.EntityId, tick);
            world.SetAttackTarget(soldier.EntityId, first.EntityId);
            Run(27, soldier, phase);
            int firstHealthAtSwitch = first.Health;

            world.SetAttackTarget(soldier.EntityId, second.EntityId);
            Run(200, soldier, phase);

            Assert.Equal(firstHealthAtSwitch, first.Health);
            Assert.InRange(serverShotTicks.Count, 20, 23);
            for (int i = 1; i < serverShotTicks.Count; i++)
                Assert.Equal(10u, serverShotTicks[i] - serverShotTicks[i - 1]);
        }

        [Fact]
        public void KillingShotShowsAndNothingAfterIt()
        {
            Entity victim = world.Spawn(PlayerB, UnitType.Soldier, new Vec2(0f, 0f));
            Entity tank = world.Spawn(PlayerA, UnitType.Tank, new Vec2(-4f, 0f));
            FiringPhase phase = new FiringPhase(UnitType.Tank, tank.EntityId, tick);
            world.SetAttackTarget(tank.EntityId, victim.EntityId);

            Run(400, tank, phase);

            Assert.Equal(0, victim.Health);
            Assert.Equal(2, serverShotTicks.Count);
            Assert.False(phase.Active);
        }

        [Fact]
        public void MoveOrderStopsTheLoop()
        {
            Entity structure = world.Spawn(PlayerB, UnitType.Structure, new Vec2(0f, 0f));
            Entity soldier = world.Spawn(PlayerA, UnitType.Soldier, new Vec2(-4f, 0f));
            FiringPhase phase = new FiringPhase(UnitType.Soldier, soldier.EntityId, tick);
            world.SetAttackTarget(soldier.EntityId, structure.EntityId);
            Run(25, soldier, phase);
            Assert.True(phase.Active);
            int shotsBeforeMove = serverShotTicks.Count;

            world.SetTarget(soldier.EntityId, new Vec2(-40f, 0f));
            Run(200, soldier, phase);

            Assert.InRange(shotsBeforeMove, 2, 3);
            Assert.Equal(shotsBeforeMove, serverShotTicks.Count);
            Assert.False(phase.Active);
        }

        [Fact]
        public void StructuresNeverFire()
        {
            FiringPhase phase = new FiringPhase(UnitType.Structure, 7, 0);

            for (uint t = 1; t <= 200; t++)
                Assert.False(phase.Step(t, ActionState.Attacking, 500, 9, true, 50));

            Assert.False(phase.Active);
        }

        [Theory]
        [InlineData(100u, 102u, false)]
        [InlineData(100u, 103u, true)]
        [InlineData(100u, 109u, true)]
        [InlineData(103u, 112u, false)]
        [InlineData(103u, 113u, true)]
        [InlineData(104u, 190u, true)]
        public void FrameSpanningSeveralTicksShowsTheShotIfOneWasScheduledInIt(uint from, uint to, bool expected)
        {
            // Soldier with id 13 and a 10 tick cooldown fires on ticks ending in 3.
            FiringPhase phase = new FiringPhase(UnitType.Soldier, 13, from - 1);
            phase.Step(from, ActionState.Attacking, 50, 7, true, 500);

            Assert.Equal(expected, phase.Step(to, ActionState.Attacking, 50, 7, true, 500));
        }

        [Fact]
        public void TicksUntilShotCountsDownToTheScheduledTick()
        {
            FiringPhase phase = new FiringPhase(UnitType.Soldier, 13, 100);
            Assert.Equal(10, phase.CooldownTicks);
            Assert.Equal(3, phase.TicksUntilShot);

            phase.Step(102, ActionState.Attacking, 50, 7, true, 500);
            Assert.Equal(1, phase.TicksUntilShot);

            Assert.True(phase.Step(103, ActionState.Attacking, 50, 7, true, 500));
            Assert.Equal(0, phase.TicksUntilShot);

            phase.Step(104, ActionState.Attacking, 50, 7, true, 500);
            Assert.Equal(9, phase.TicksUntilShot);
        }
    }
}
