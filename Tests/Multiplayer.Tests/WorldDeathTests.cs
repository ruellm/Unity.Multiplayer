using Multiplayer.Protocol;
using Multiplayer.Server;
using Xunit;

namespace Multiplayer.Tests
{
    public sealed class WorldDeathTests
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

        void RunUntilDying(Entity entity)
        {
            int guard = 0;
            while (entity.ActionState != ActionState.Dying)
            {
                world.Integrate(++tick, Dt);
                Assert.InRange(++guard, 1, 5000);
            }
        }

        Entity AttackerInRangeOf(UnitType unitType, Entity target)
        {
            Entity attacker = world.Spawn(PlayerA, unitType, new Vec2(target.Position.X - 4f, target.Position.Z));
            world.SetAttackTarget(attacker.EntityId, target.EntityId);
            return attacker;
        }

        bool Exists(Entity entity)
        {
            Entity found;
            return world.TryGet(entity.EntityId, out found);
        }

        [Fact]
        public void ZeroHealthMakesTheEntityDyingAndEmitsEntityDiedOnThatTick()
        {
            Entity victim = world.Spawn(PlayerB, UnitType.Soldier, new Vec2(0f, 0f));
            AttackerInRangeOf(UnitType.Tank, victim);

            RunUntilDying(victim);

            Assert.Equal(0, victim.Health);
            Assert.Equal(tick, victim.DeathTick);
            GameEventMessage died = Assert.Single(world.Events);
            Assert.Equal((byte)GameEventType.EntityDied, died.EventType);
            Assert.Equal(tick, died.Tick);
            Assert.Equal(victim.EntityId, died.EntityDied.EntityId);
            Assert.Equal((byte)DeathCause.Shot, died.EntityDied.Cause);
        }

        [Theory]
        [InlineData(UnitType.Soldier)]
        [InlineData(UnitType.Tank)]
        [InlineData(UnitType.Structure)]
        public void DyingEntityStaysForItsDeathDurationThenIsRemoved(UnitType victimType)
        {
            Entity victim = world.Spawn(PlayerB, victimType, new Vec2(0f, 0f));
            AttackerInRangeOf(UnitType.Tank, victim);
            RunUntilDying(victim);
            int deathTicks = UnitDefs.DeathTicks(victimType);

            Run(deathTicks - 1);
            Assert.True(Exists(victim));
            Assert.Equal(ActionState.Dying, victim.ActionState);

            Run(1);
            Assert.False(Exists(victim));
            Assert.Equal(victim.DeathTick + (uint)deathTicks, tick);
        }

        [Fact]
        public void StructureEmitsExplosionAtItsPositionWhenItsCollapseEnds()
        {
            Entity structure = world.Spawn(PlayerB, UnitType.Structure, new Vec2(7f, -3f));
            AttackerInRangeOf(UnitType.Tank, structure);
            RunUntilDying(structure);
            world.Events.Clear();

            Run(UnitDefs.DeathTicks(UnitType.Structure) - 1);
            Assert.Empty(world.Events);

            Run(1);
            GameEventMessage explosion = Assert.Single(world.Events);
            Assert.Equal((byte)GameEventType.Explosion, explosion.EventType);
            Assert.Equal(tick, explosion.Tick);
            Assert.Equal(7f, explosion.Explosion.Position.X);
            Assert.Equal(-3f, explosion.Explosion.Position.Z);
            Assert.Equal((byte)ExplosionType.StructureDestroyed, explosion.Explosion.ExplosionType);
        }

        [Fact]
        public void UnitDeathEmitsNoExplosion()
        {
            Entity victim = world.Spawn(PlayerB, UnitType.Soldier, new Vec2(0f, 0f));
            AttackerInRangeOf(UnitType.Tank, victim);
            RunUntilDying(victim);
            world.Events.Clear();

            Run(UnitDefs.DeathTicks(UnitType.Soldier) + 5);

            Assert.False(Exists(victim));
            Assert.Empty(world.Events);
        }

        [Fact]
        public void AttackersClearADyingTargetAndGoIdle()
        {
            Entity victim = world.Spawn(PlayerB, UnitType.Soldier, new Vec2(0f, 0f));
            Entity tank = AttackerInRangeOf(UnitType.Tank, victim);
            Entity soldier = AttackerInRangeOf(UnitType.Soldier, victim);
            RunUntilDying(victim);

            Run(1);

            Assert.True(Exists(victim));
            Assert.Equal(0, tank.TargetEntityId);
            Assert.Equal(ActionState.Idle, tank.ActionState);
            Assert.Equal(0, soldier.TargetEntityId);
            Assert.Equal(ActionState.Idle, soldier.ActionState);
        }

        [Fact]
        public void DyingEntityCannotBeTargetedAndTakesNoDamage()
        {
            Entity victim = world.Spawn(PlayerB, UnitType.Soldier, new Vec2(0f, 0f));
            Entity tank = AttackerInRangeOf(UnitType.Tank, victim);
            RunUntilDying(victim);
            world.Events.Clear();

            world.SetAttackTarget(tank.EntityId, victim.EntityId);
            Run(UnitDefs.DeathTicks(UnitType.Soldier) - 1);

            Assert.Equal(0, tank.TargetEntityId);
            Assert.Equal(ActionState.Idle, tank.ActionState);
            Assert.Equal(0, victim.Health);
            Assert.Empty(world.Events);
        }

        [Fact]
        public void DyingEntityDealsNoDamageAndDoesNotMove()
        {
            Entity structure = world.Spawn(PlayerA, UnitType.Structure, new Vec2(-4f, 4f));
            Entity victim = world.Spawn(PlayerB, UnitType.Soldier, new Vec2(0f, 0f));
            world.SetAttackTarget(victim.EntityId, structure.EntityId);
            AttackerInRangeOf(UnitType.Tank, victim);
            RunUntilDying(victim);
            int structureHealth = structure.Health;
            Vec2 at = victim.Position;

            world.SetTarget(victim.EntityId, new Vec2(20f, 20f));
            Run(UnitDefs.DeathTicks(UnitType.Soldier) - 1);

            Assert.InRange(structureHealth, 400, 499);
            Assert.Equal(structureHealth, structure.Health);
            Assert.Equal(ActionState.Dying, victim.ActionState);
            Assert.Equal(at.X, victim.Position.X);
            Assert.Equal(at.Z, victim.Position.Z);
        }

        [Fact]
        public void KillingAStructureLeavesItsOwnersUnits()
        {
            Entity structure = world.Spawn(PlayerB, UnitType.Structure, new Vec2(0f, 0f));
            Entity soldier = world.Spawn(PlayerB, UnitType.Soldier, new Vec2(20f, 20f));
            Entity tank = world.Spawn(PlayerB, UnitType.Tank, new Vec2(22f, 20f));
            AttackerInRangeOf(UnitType.Tank, structure);

            RunUntilDying(structure);
            Run(UnitDefs.DeathTicks(UnitType.Structure) + 5);

            Assert.False(Exists(structure));
            Assert.False(world.HasStructure(PlayerB));
            Assert.True(Exists(soldier));
            Assert.True(Exists(tank));
            Assert.Equal(2, world.CountByOwner(PlayerB));
        }
    }
}
