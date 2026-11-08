using System;

namespace Multiplayer.Protocol
{
    public struct UnitDef
    {
        public int MaxHealth;
        public float Speed;
        public float Range;
        public float AttackCooldown;
        public int Damage;
        public float DeathDuration;
        public float FootprintRadius;
    }

    public static class UnitDefs
    {
        static readonly UnitDef HQ = new UnitDef
        {
            MaxHealth = 500,
            Speed = 0f,
            Range = 0f,
            AttackCooldown = 0f,
            Damage = 0,
            DeathDuration = 2f,
            FootprintRadius = 2.5f
        };

        static readonly UnitDef Tower = new UnitDef
        {
            MaxHealth = 300,
            Speed = 0f,
            Range = 10f,
            AttackCooldown = 0.25f,
            Damage = 4,
            DeathDuration = 2f,
            FootprintRadius = 1.5f
        };

        static readonly UnitDef Wall = new UnitDef
        {
            MaxHealth = 400,
            Speed = 0f,
            Range = 0f,
            AttackCooldown = 0f,
            Damage = 0,
            DeathDuration = 2f,
            FootprintRadius = 2.5f
        };

        static readonly UnitDef Soldier = new UnitDef
        {
            MaxHealth = 50,
            Speed = 6f,
            Range = 6f,
            AttackCooldown = 0.5f,
            Damage = 5,
            DeathDuration = 2f,
            FootprintRadius = 0.4f
        };

        static readonly UnitDef Tank = new UnitDef
        {
            MaxHealth = 200,
            Speed = 3f,
            Range = 9f,
            AttackCooldown = 1.5f,
            Damage = 30,
            DeathDuration = 1.5f,
            FootprintRadius = 1.3f
        };

        public static bool TryGet(UnitType type, out UnitDef def)
        {
            switch (type)
            {
                case UnitType.HQ:
                    def = HQ;
                    return true;
                case UnitType.Tower:
                    def = Tower;
                    return true;
                case UnitType.Wall:
                    def = Wall;
                    return true;
                case UnitType.Soldier:
                    def = Soldier;
                    return true;
                case UnitType.Tank:
                    def = Tank;
                    return true;
                default:
                    def = default;
                    return false;
            }
        }

        public static UnitDef Get(UnitType type)
        {
            UnitDef def;
            TryGet(type, out def);
            return def;
        }

        public static int DeathTicks(UnitType type)
        {
            return (int)Math.Round(Get(type).DeathDuration * NetConfig.TickRate);
        }
    }
}
