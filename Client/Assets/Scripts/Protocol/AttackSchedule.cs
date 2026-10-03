using System;

namespace Multiplayer.Protocol
{
    // When an attacking entity fires is a pure function of the absolute tick and its id. The server
    // applies damage with it and the client animates with it, so no per-shot state crosses the wire
    // and an observer needs no history to agree.
    public static class AttackSchedule
    {
        public static int CooldownTicks(UnitType unitType)
        {
            return (int)Math.Round(UnitDefs.Get(unitType).AttackCooldown * NetConfig.TickRate);
        }

        public static bool Fires(uint tick, int entityId, int cooldownTicks)
        {
            return cooldownTicks > 0 && TicksUntilShot(tick, entityId, cooldownTicks) == 0;
        }

        // 0 on a firing tick. Meaningless when cooldownTicks is not positive.
        public static int TicksUntilShot(uint tick, int entityId, int cooldownTicks)
        {
            uint period = (uint)cooldownTicks;
            uint phase = (uint)entityId % period;
            return (int)((phase + period - tick % period) % period);
        }
    }
}
