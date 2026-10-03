using System;
using Multiplayer.Protocol;
using Multiplayer.Server;
using Xunit;

namespace Multiplayer.Tests
{
    public sealed class WorldAttackTests
    {
        const float Dt = 1f / NetConfig.TickRate;
        const int PlayerA = 1;
        const int PlayerB = 2;

        readonly World world = new World();
        uint tick;

        static float Distance(Entity a, Entity b)
        {
            float dx = a.Position.X - b.Position.X;
            float dz = a.Position.Z - b.Position.Z;
            return (float)Math.Sqrt(dx * dx + dz * dz);
        }

        void Run(int ticks)
        {
            for (int i = 0; i < ticks; i++)
                world.Integrate(++tick, Dt);
        }

        [Fact]
        public void NewEntityIsIdleWithNoTarget()
        {
            Entity soldier = world.Spawn(PlayerA, UnitType.Soldier, new Vec2(0f, 0f));

            Run(1);

            Assert.Equal(ActionState.Idle, soldier.ActionState);
            Assert.Equal(0, soldier.TargetEntityId);
        }

        [Fact]
        public void MoveOrderReportsMovingThenIdle()
        {
            Entity soldier = world.Spawn(PlayerA, UnitType.Soldier, new Vec2(0f, 0f));
            world.SetTarget(soldier.EntityId, new Vec2(3f, 0f));

            Run(1);
            Assert.Equal(ActionState.Moving, soldier.ActionState);

            Run(40);
            Assert.Equal(ActionState.Idle, soldier.ActionState);
            Assert.Equal(3f, soldier.Position.X);
        }

        [Theory]
        [InlineData(UnitType.Soldier)]
        [InlineData(UnitType.Tank)]
        public void AttackerApproachesAndStopsJustInsideRange(UnitType unitType)
        {
            UnitDef def = UnitDefs.Get(unitType);
            Entity attacker = world.Spawn(PlayerA, unitType, new Vec2(0f, 0f));
            Entity target = world.Spawn(PlayerB, UnitType.Soldier, new Vec2(30f, 0f));
            world.SetAttackTarget(attacker.EntityId, target.EntityId);

            Run(1);
            Assert.Equal(ActionState.MovingToAttack, attacker.ActionState);
            Assert.InRange(attacker.Position.X, 0.001f, 30f);

            Run(400);
            Assert.Equal(ActionState.Attacking, attacker.ActionState);
            Assert.Equal(target.EntityId, attacker.TargetEntityId);
            Assert.InRange(Distance(attacker, target), def.Range - def.Speed * Dt - 0.001f, def.Range);

            Vec2 held = attacker.Position;
            Run(20);
            Assert.Equal(held.X, attacker.Position.X);
            Assert.Equal(held.Z, attacker.Position.Z);
        }

        [Fact]
        public void TargetAlreadyInRangeIsAttackedWithoutMoving()
        {
            Entity attacker = world.Spawn(PlayerA, UnitType.Soldier, new Vec2(0f, 0f));
            Entity target = world.Spawn(PlayerB, UnitType.Tank, new Vec2(4f, 0f));
            world.SetAttackTarget(attacker.EntityId, target.EntityId);

            Run(5);

            Assert.Equal(ActionState.Attacking, attacker.ActionState);
            Assert.Equal(0f, attacker.Position.X);
        }

        [Fact]
        public void AttackerFollowsAMovingTargetAndHoldsRange()
        {
            float range = UnitDefs.Get(UnitType.Soldier).Range;
            Entity attacker = world.Spawn(PlayerA, UnitType.Soldier, new Vec2(0f, 0f));
            Entity target = world.Spawn(PlayerB, UnitType.Tank, new Vec2(4f, 0f));
            world.SetAttackTarget(attacker.EntityId, target.EntityId);
            Run(5);

            // The soldier outruns the tank, so it alternates between closing and holding.
            world.SetTarget(target.EntityId, new Vec2(4f, 30f));
            int chasingTicks = 0;
            for (int i = 0; i < 400; i++)
            {
                world.Integrate(++tick, Dt);
                if (attacker.ActionState == ActionState.MovingToAttack)
                    chasingTicks++;

                Assert.InRange(Distance(attacker, target), 0f, range + UnitDefs.Get(UnitType.Tank).Speed * Dt);
            }

            Assert.InRange(chasingTicks, 1, 399);
            Assert.Equal(ActionState.Idle, target.ActionState);
            Assert.Equal(ActionState.Attacking, attacker.ActionState);
            Assert.InRange(Distance(attacker, target), 0f, range);
            Assert.InRange(attacker.Position.Z, 20f, 30f);
        }

        [Fact]
        public void RemovedTargetClearsTheOrderAndGoesIdle()
        {
            Entity attacker = world.Spawn(PlayerA, UnitType.Soldier, new Vec2(0f, 0f));
            Entity target = world.Spawn(PlayerB, UnitType.Soldier, new Vec2(30f, 0f));
            world.SetAttackTarget(attacker.EntityId, target.EntityId);
            Run(10);

            world.RemoveByOwner(PlayerB);
            Vec2 at = attacker.Position;
            Run(5);

            Assert.Equal(ActionState.Idle, attacker.ActionState);
            Assert.Equal(0, attacker.TargetEntityId);
            Assert.Equal(at.X, attacker.Position.X);
        }

        [Fact]
        public void MoveOrderClearsTheAttackTarget()
        {
            Entity attacker = world.Spawn(PlayerA, UnitType.Soldier, new Vec2(0f, 0f));
            Entity target = world.Spawn(PlayerB, UnitType.Soldier, new Vec2(4f, 0f));
            world.SetAttackTarget(attacker.EntityId, target.EntityId);
            Run(5);

            world.SetTarget(attacker.EntityId, new Vec2(0f, -10f));
            Run(1);

            Assert.Equal(0, attacker.TargetEntityId);
            Assert.Equal(ActionState.Moving, attacker.ActionState);

            Run(100);
            Assert.Equal(ActionState.Idle, attacker.ActionState);
            Assert.Equal(-10f, attacker.Position.Z);
        }

        [Fact]
        public void AttackOrderClearsTheMoveTarget()
        {
            Entity attacker = world.Spawn(PlayerA, UnitType.Soldier, new Vec2(0f, 0f));
            Entity target = world.Spawn(PlayerB, UnitType.Soldier, new Vec2(4f, 0f));
            world.SetTarget(attacker.EntityId, new Vec2(0f, -10f));
            Run(2);

            world.SetAttackTarget(attacker.EntityId, target.EntityId);
            Vec2 at = attacker.Position;
            Run(20);

            Assert.Equal(ActionState.Attacking, attacker.ActionState);
            Assert.Equal(at.Z, attacker.Position.Z);

            world.RemoveByOwner(PlayerB);
            Run(20);
            Assert.Equal(ActionState.Idle, attacker.ActionState);
            Assert.Equal(at.Z, attacker.Position.Z);
        }
    }
}
