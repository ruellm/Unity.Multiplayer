using System;
using System.Collections.Generic;
using Multiplayer.Protocol;
using Multiplayer.Server;
using Xunit;

namespace Multiplayer.Tests
{
    public sealed class WorldFootprintTests
    {
        const float HalfExtent = 50f;
        const int PlayerA = 1;
        const int PlayerB = 2;

        readonly World world = new World();

        static float Radius(UnitType type)
        {
            return UnitDefs.Get(type).FootprintRadius;
        }

        static float Distance(Vec2 a, Vec2 b)
        {
            float dx = a.X - b.X;
            float dz = a.Z - b.Z;
            return (float)Math.Sqrt(dx * dx + dz * dz);
        }

        [Theory]
        [InlineData(UnitType.Soldier)]
        [InlineData(UnitType.Tank)]
        public void ProducedUnitsSpawnOutsideTheHQAndClearOfEachOther(UnitType unitType)
        {
            Entity hq = world.Spawn(PlayerA, UnitType.HQ, new Vec2(5f, -3f));
            float minFromHQ = Radius(UnitType.HQ) + Radius(unitType);
            List<Entity> spawned = new List<Entity>();

            for (int i = 0; i < 12; i++)
            {
                Vec2 point;
                Assert.True(world.TryFindSpawnPoint(hq, unitType, HalfExtent, out point));
                Assert.True(Distance(point, hq.Position) >= minFromHQ);
                spawned.Add(world.Spawn(PlayerA, unitType, point));
            }

            for (int i = 0; i < spawned.Count; i++)
            {
                for (int j = i + 1; j < spawned.Count; j++)
                    Assert.True(Distance(spawned[i].Position, spawned[j].Position) >= 2f * Radius(unitType) - 0.0001f);
            }
        }

        [Fact]
        public void SpawnSearchSkipsSlotsTakenByOtherUnits()
        {
            Entity hq = world.Spawn(PlayerA, UnitType.HQ, new Vec2(0f, 0f));
            Entity soldier = world.Spawn(PlayerA, UnitType.Soldier, new Vec2(3.9f, 0f));

            Vec2 point;
            Assert.True(world.TryFindSpawnPoint(hq, UnitType.Tank, HalfExtent, out point));

            Assert.True(Distance(point, soldier.Position) >= Radius(UnitType.Soldier) + Radius(UnitType.Tank));
            Assert.True(Distance(point, hq.Position) >= Radius(UnitType.HQ) + Radius(UnitType.Tank));
        }

        [Theory]
        [InlineData(UnitType.Tower, UnitType.HQ)]
        [InlineData(UnitType.Wall, UnitType.Wall)]
        [InlineData(UnitType.HQ, UnitType.Soldier)]
        [InlineData(UnitType.Tower, UnitType.Tank)]
        public void PlacementIsRejectedInsideTheRadiusSumAndAcceptedJustOutside(UnitType placing, UnitType existing)
        {
            world.Spawn(PlayerB, existing, new Vec2(0f, 0f));
            float sum = Radius(placing) + Radius(existing);

            Assert.False(world.IsFree(new Vec2(sum - 0.01f, 0f), placing));
            Assert.False(world.IsFree(new Vec2(0f, -(sum - 0.01f)), placing));
            Assert.True(world.IsFree(new Vec2(sum + 0.01f, 0f), placing));
            Assert.True(world.IsFree(new Vec2(0f, -(sum + 0.01f)), placing));
        }

        [Theory]
        [InlineData(UnitType.Tower)]
        [InlineData(UnitType.Wall)]
        public void TowerAndWallNeedTheOwnersHQ(UnitType type)
        {
            Vec2 at = new Vec2(10f, 10f);
            Assert.Equal(PlacementError.NeedsHQ, world.CheckPlacement(PlayerA, type, at));

            world.Spawn(PlayerB, UnitType.HQ, new Vec2(-10f, -10f));
            Assert.Equal(PlacementError.NeedsHQ, world.CheckPlacement(PlayerA, type, at));

            world.Spawn(PlayerA, UnitType.HQ, new Vec2(0f, 0f));
            Assert.Equal(PlacementError.None, world.CheckPlacement(PlayerA, type, at));
        }

        [Fact]
        public void PlacementRulesRejectInOrder()
        {
            Assert.Equal(PlacementError.None, world.CheckPlacement(PlayerA, UnitType.HQ, new Vec2(0f, 0f)));
            world.Spawn(PlayerA, UnitType.HQ, new Vec2(0f, 0f));

            Assert.Equal(PlacementError.AlreadyHasHQ, world.CheckPlacement(PlayerA, UnitType.HQ, new Vec2(20f, 20f)));
            Assert.Equal(PlacementError.NotAStructure, world.CheckPlacement(PlayerA, UnitType.Soldier, new Vec2(20f, 20f)));
            Assert.Equal(PlacementError.OutOfBounds, world.CheckPlacement(PlayerA, UnitType.Wall, new Vec2(Footprint.WorldHalfExtent + 0.5f, 0f)));
            Assert.Equal(PlacementError.OutOfBounds, world.CheckPlacement(PlayerA, UnitType.Wall, new Vec2(float.NaN, 0f)));
            Assert.Equal(PlacementError.Overlaps, world.CheckPlacement(PlayerA, UnitType.Tower, new Vec2(3f, 0f)));
            Assert.Equal(PlacementError.None, world.CheckPlacement(PlayerA, UnitType.Tower, new Vec2(4.5f, 0f)));
        }

        [Fact]
        public void EmptyGroundIsFreeForEveryType()
        {
            world.Spawn(PlayerB, UnitType.HQ, new Vec2(0f, 0f));

            Assert.True(world.IsFree(new Vec2(20f, 20f), UnitType.HQ));
            Assert.True(world.IsFree(new Vec2(20f, 20f), UnitType.Wall));
            Assert.True(world.IsFree(new Vec2(20f, 20f), UnitType.Tower));
        }
    }
}
