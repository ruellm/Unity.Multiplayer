using System.Collections.Generic;
using Multiplayer.Protocol;
using Multiplayer.Server;
using Xunit;

namespace Multiplayer.Tests
{
    public sealed class WorldDamageTests
    {
        const float Dt = 1f / NetConfig.TickRate;
        const int PlayerA = 1;
        const int PlayerB = 2;

        readonly World world = new World();
        uint tick;

        void Run(int ticks)
        {
            for (int i = 0; i < ticks; i++)
                world.Integrate(++tick, Dt);
        }

        // Ticks on which the target lost health, with the amount lost.
        List<KeyValuePair<uint, int>> RunAndRecordHits(int ticks, Entity target)
        {
            List<KeyValuePair<uint, int>> hits = new List<KeyValuePair<uint, int>>();
            for (int i = 0; i < ticks; i++)
            {
                int before = target.Health;
                world.Integrate(++tick, Dt);
                if (target.Health < before)
                    hits.Add(new KeyValuePair<uint, int>(tick, before - target.Health));
            }
            return hits;
        }

        Entity AttackerInRangeOf(UnitType unitType, Entity target)
        {
            Entity attacker = world.Spawn(PlayerA, unitType, new Vec2(target.Position.X - 4f, target.Position.Z));
            world.SetAttackTarget(attacker.EntityId, target.EntityId);
            return attacker;
        }

        [Theory]
        [InlineData(UnitType.Soldier, 0)]
        [InlineData(UnitType.Soldier, 3)]
        [InlineData(UnitType.Soldier, 7)]
        [InlineData(UnitType.Tank, 0)]
        [InlineData(UnitType.Tank, 11)]
        [InlineData(UnitType.Tank, 29)]
        public void ShotsLandOnlyOnTheScheduledTicksWhateverTickTheFightStarts(UnitType unitType, int startDelay)
        {
            int cooldown = AttackSchedule.CooldownTicks(unitType);
            int damage = UnitDefs.Get(unitType).Damage;
            Entity structure = world.Spawn(PlayerB, UnitType.Structure, new Vec2(0f, 0f));
            Run(startDelay);
            Entity attacker = AttackerInRangeOf(unitType, structure);

            List<KeyValuePair<uint, int>> hits = RunAndRecordHits(cooldown * 6, structure);

            Assert.Equal(6, hits.Count);
            for (int i = 0; i < hits.Count; i++)
            {
                Assert.Equal((uint)(attacker.EntityId % cooldown), hits[i].Key % (uint)cooldown);
                Assert.Equal(damage, hits[i].Value);
                if (i > 0)
                    Assert.Equal((uint)cooldown, hits[i].Key - hits[i - 1].Key);
            }
        }

        [Fact]
        public void TankHitsHarderAndLessOftenThanSoldier()
        {
            Assert.Equal(10, AttackSchedule.CooldownTicks(UnitType.Soldier));
            Assert.Equal(30, AttackSchedule.CooldownTicks(UnitType.Tank));
            Assert.True(UnitDefs.Get(UnitType.Tank).Damage > UnitDefs.Get(UnitType.Soldier).Damage);
        }

        [Fact]
        public void StructuresAreNeverScheduledToFire()
        {
            int cooldown = AttackSchedule.CooldownTicks(UnitType.Structure);

            Assert.Equal(0, cooldown);
            for (uint t = 0; t < 100; t++)
                Assert.False(AttackSchedule.Fires(t, 5, cooldown));
        }

        [Fact]
        public void NoDamageWhileApproaching()
        {
            Entity structure = world.Spawn(PlayerB, UnitType.Structure, new Vec2(0f, 0f));
            Entity soldier = world.Spawn(PlayerA, UnitType.Soldier, new Vec2(-30f, 0f));
            world.SetAttackTarget(soldier.EntityId, structure.EntityId);

            int approachTicks = 0;
            while (soldier.ActionState != ActionState.Attacking)
            {
                Assert.Equal(500, structure.Health);
                world.Integrate(++tick, Dt);
                approachTicks++;
                Assert.InRange(approachTicks, 1, 1000);
            }

            // The tick that first reports Attacking may itself be a scheduled one.
            Assert.InRange(approachTicks, 40, 1000);
            int onArrival = structure.Health;
            Assert.InRange(onArrival, 495, 500);

            Run(AttackSchedule.CooldownTicks(UnitType.Soldier));
            Assert.Equal(onArrival - 5, structure.Health);
        }

        [Fact]
        public void HealthClampsAtZero()
        {
            Entity victim = world.Spawn(PlayerB, UnitType.Soldier, new Vec2(0f, 0f));
            AttackerInRangeOf(UnitType.Tank, victim);

            List<KeyValuePair<uint, int>> hits = RunAndRecordHits(AttackSchedule.CooldownTicks(UnitType.Tank) * 2, victim);

            Assert.Equal(0, victim.Health);
            Assert.Equal(2, hits.Count);
            Assert.Equal(30, hits[0].Value);
            Assert.Equal(20, hits[1].Value);
        }

        [Fact]
        public void SwitchingTargetsKeepsTheSameSchedule()
        {
            int cooldown = AttackSchedule.CooldownTicks(UnitType.Soldier);
            Entity first = world.Spawn(PlayerB, UnitType.Structure, new Vec2(0f, 0f));
            Entity second = world.Spawn(PlayerB, UnitType.Tank, new Vec2(-4f, 3f));
            Entity soldier = AttackerInRangeOf(UnitType.Soldier, first);
            List<KeyValuePair<uint, int>> firstHits = RunAndRecordHits(cooldown * 2 + 4, first);
            int firstHealthAtSwitch = first.Health;

            world.SetAttackTarget(soldier.EntityId, second.EntityId);
            List<KeyValuePair<uint, int>> secondHits = RunAndRecordHits(cooldown * 2, second);

            Assert.InRange(firstHits.Count, 2, 3);
            Assert.Equal(2, secondHits.Count);
            Assert.Equal(firstHealthAtSwitch, first.Health);
            Assert.Equal((uint)cooldown, secondHits[0].Key - firstHits[firstHits.Count - 1].Key);
        }

        [Fact]
        public void MovingAwayStopsTheDamage()
        {
            int cooldown = AttackSchedule.CooldownTicks(UnitType.Soldier);
            Entity structure = world.Spawn(PlayerB, UnitType.Structure, new Vec2(0f, 0f));
            Entity soldier = AttackerInRangeOf(UnitType.Soldier, structure);
            Run(cooldown);
            Assert.Equal(495, structure.Health);

            world.SetTarget(soldier.EntityId, new Vec2(-40f, 0f));
            Run(cooldown * 5);

            Assert.Equal(495, structure.Health);
        }
    }
}
