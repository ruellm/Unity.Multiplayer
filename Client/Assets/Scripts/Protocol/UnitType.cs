namespace Multiplayer.Protocol
{
    public enum UnitType : byte
    {
        HQ = 1,
        Soldier = 2,
        Tank = 3,
        Tower = 4,
        Wall = 5
    }

    public static class UnitTypeExtensions
    {
        public static bool IsStructure(this UnitType type)
        {
            return type == UnitType.HQ || type == UnitType.Tower || type == UnitType.Wall;
        }
    }
}
