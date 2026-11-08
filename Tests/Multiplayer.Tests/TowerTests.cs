using Multiplayer.Protocol;
using Multiplayer.Server;
using Xunit;

namespace Multiplayer.Tests
{
    public sealed class TowerTests
    {
        const float Dt = 1f / NetConfig.TickRate;
        const int PlayerA = 1;
        const int PlayerB = 2;

        readonly World world = new World();
        readonly float range = UnitDefs.Get(UnitType.Tower).Range;
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

        [Fact]
        public void AcquiresTheNearestEnemyUnitInRangeAndNothingElse()
        {
            Entity tower = world.Spawn(PlayerA, UnitType.Tower, new Vec2(0f, 0f));
            Entity own = world.Spawn(PlayerA, UnitType.Soldier, new Vec2(1f, 0f));
            Entity enemyHQ = world.Spawn(PlayerB, UnitType.HQ, new Vec2(0f, 2f));
            Entity enemyWall = world.Spawn(PlayerB, UnitType.Wall, new Vec2(-2f, 0f));
            Entity far = world.Spawn(PlayerB, UnitType.Soldier, new Vec2(range + 1f, 0f));
            world.Spawn(PlayerB, UnitType.Tank, new Vec2(0f, -6f));
            Entity near = world.Spawn(PlayerB, UnitType.Soldier, new Vec2(4f, 0f));

            Run(1);
            Assert.Equal(near.EntityId, tower.TargetEntityId);
            Assert.Equal(ActionState.Attacking, tower.ActionState);

            Run(AttackSchedule.CooldownTicks(UnitType.Tower) * 3);
            int lost = UnitDefs.Get(UnitType.Soldier).MaxHealth - near.Health;
            Assert.InRange(lost, 12, 16);
            Assert.Equal(0, lost % UnitDefs.Get(UnitType.Tower).Damage);
            Assert.Equal(UnitDefs.Get(UnitType.Soldier).MaxHealth, own.Health);
            Assert.Equal(UnitDefs.Get(UnitType.Soldier).MaxHealth, far.Health);
            Assert.Equal(UnitDefs.Get(UnitType.HQ).MaxHealth, enemyHQ.Health);
            Assert.Equal(UnitDefs.Get(UnitType.Wall).MaxHealth, enemyWall.Health);
            Assert.Equal(0f, tower.Position.X);
            Assert.Equal(0f, tower.Position.Z);
        }

        [Fact]
        public void WithNoEnemyUnitInRangeItIsIdle()
        {
            Entity tower = world.Spawn(PlayerA, UnitType.Tower, new Vec2(0f, 0f));
            world.Spawn(PlayerA, UnitType.Tank, new Vec2(2f, 0f));
            world.Spawn(PlayerB, UnitType.HQ, new Vec2(3f, 0f));
            Entity far = world.Spawn(PlayerB, UnitType.Soldier, new Vec2(0f, range + 5f));

            Run(100);

            Assert.Equal(0, tower.TargetEntityId);
            Assert.Equal(ActionState.Idle, tower.ActionState);
            Assert.Equal(UnitDefs.Get(UnitType.Soldier).MaxHealth, far.Health);
        }

        [Fact]
        public void KeepsItsTargetWhenACloserEnemyAppears()
        {
            Entity tower = world.Spawn(PlayerA, UnitType.Tower, new Vec2(0f, 0f));
            Entity first = world.Spawn(PlayerB, UnitType.Tank, new Vec2(6f, 0f));
            Run(1);
            Assert.Equal(first.EntityId, tower.TargetEntityId);

            Entity closer = world.Spawn(PlayerB, UnitType.Tank, new Vec2(2f, 0f));
            Run(20);

            Assert.Equal(first.EntityId, tower.TargetEntityId);
            Assert.Equal(UnitDefs.Get(UnitType.Tank).MaxHealth, closer.Health);
            Assert.True(first.Health < UnitDefs.Get(UnitType.Tank).MaxHealth);
        }

        [Fact]
        public void SwitchesWhenTheTargetDies()
        {
            Entity tower = world.Spawn(PlayerA, UnitType.Tower, new Vec2(0f, 0f));
            Entity victim = world.Spawn(PlayerB, UnitType.Soldier, new Vec2(4f, 0f));
            Entity next = world.Spawn(PlayerB, UnitType.Tank, new Vec2(7f, 0f));
            Run(1);
            Assert.Equal(victim.EntityId, tower.TargetEntityId);

            RunUntilDying(victim);
            Run(1);

            Assert.Equal(0, victim.Health);
            Assert.Equal(next.EntityId, tower.TargetEntityId);
            Assert.Equal(ActionState.Attacking, tower.ActionState);
        }

        [Fact]
        public void SwitchesWhenTheTargetLeavesRange()
        {
            Entity tower = world.Spawn(PlayerA, UnitType.Tower, new Vec2(0f, 0f));
            Entity runner = world.Spawn(PlayerB, UnitType.Soldier, new Vec2(4f, 0f));
            Entity next = world.Spawn(PlayerB, UnitType.Tank, new Vec2(7f, 0f));
            Run(1);
            Assert.Equal(runner.EntityId, tower.TargetEntityId);

            world.SetTarget(runner.EntityId, new Vec2(40f, 0f));
            Run(60);

            Assert.True(runner.Position.X > range);
            Assert.Equal(next.EntityId, tower.TargetEntityId);
        }

        [Fact]
        public void LosingItsOnlyTargetLeavesItIdle()
        {
            Entity tower = world.Spawn(PlayerA, UnitType.Tower, new Vec2(0f, 0f));
            Entity runner = world.Spawn(PlayerB, UnitType.Tank, new Vec2(4f, 0f));
            Run(1);
            Assert.Equal(runner.EntityId, tower.TargetEntityId);

            world.SetTarget(runner.EntityId, new Vec2(40f, 0f));
            Run(200);

            Assert.Equal(0, tower.TargetEntityId);
            Assert.Equal(ActionState.Idle, tower.ActionState);
        }

        [Fact]
        public void HQAndWallNeverAttack()
        {
            Entity hq = world.Spawn(PlayerA, UnitType.HQ, new Vec2(0f, 0f));
            Entity wall = world.Spawn(PlayerA, UnitType.Wall, new Vec2(3f, 0f));
            Entity enemy = world.Spawn(PlayerB, UnitType.Soldier, new Vec2(1.5f, 1f));

            Run(100);

            Assert.Equal(UnitDefs.Get(UnitType.Soldier).MaxHealth, enemy.Health);
            Assert.Equal(0, hq.TargetEntityId);
            Assert.Equal(0, wall.TargetEntityId);
            Assert.Equal(ActionState.Idle, hq.ActionState);
            Assert.Equal(ActionState.Idle, wall.ActionState);
        }

        [Fact]
        public void StructuresIgnoreMoveAndAttackOrders()
        {
            Entity tower = world.Spawn(PlayerA, UnitType.Tower, new Vec2(0f, 0f));
            Entity wall = world.Spawn(PlayerA, UnitType.Wall, new Vec2(2f, 0f));
            Entity enemy = world.Spawn(PlayerB, UnitType.Soldier, new Vec2(range + 10f, 0f));

            world.SetTarget(tower.EntityId, new Vec2(5f, 0f));
            world.SetAttackTarget(tower.EntityId, enemy.EntityId);
            world.SetAttackTarget(wall.EntityId, enemy.EntityId);
            Run(10);

            Assert.Equal(0f, tower.Position.X);
            Assert.Equal(0, tower.TargetEntityId);
            Assert.Equal(ActionState.Idle, tower.ActionState);
            Assert.Equal(0, wall.TargetEntityId);
            Assert.Equal(ActionState.Idle, wall.ActionState);
        }
    }
}
