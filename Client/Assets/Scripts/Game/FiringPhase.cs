using Multiplayer.Protocol;

namespace Multiplayer.Game
{
    // Decides when a unit visibly fires, from replicated state and AttackSchedule alone. The only
    // history it keeps is the last tick it saw and the target's health on that tick.
    public sealed class FiringPhase
    {
        readonly int entityId;
        readonly int cooldownTicks;
        uint lastStateTick;
        int trackedTarget;
        int lastTargetHealth;
        bool active;

        public FiringPhase(UnitType unitType, int ownerEntityId, uint stateTick)
        {
            entityId = ownerEntityId;
            cooldownTicks = AttackSchedule.CooldownTicks(unitType);
            lastStateTick = stateTick;
        }

        public int CooldownTicks
        {
            get { return cooldownTicks; }
        }

        // Whether the unit was in a state to fire on the last step.
        public bool Active
        {
            get { return active; }
        }

        public int TicksUntilShot
        {
            get { return cooldownTicks > 0 ? AttackSchedule.TicksUntilShot(lastStateTick, entityId, cooldownTicks) : 0; }
        }

        public bool Step(uint stateTick, ActionState state, int health, int targetEntityId, bool targetKnown, int targetHealth)
        {
            if (targetEntityId != trackedTarget)
            {
                trackedTarget = targetEntityId;
                lastTargetHealth = targetKnown ? targetHealth : 0;
            }

            // The target's health from before this tick, so the shot that takes it to zero still shows.
            active = cooldownTicks > 0
                && state == ActionState.Attacking
                && health > 0
                && targetKnown
                && lastTargetHealth > 0;

            // A frame can span several ticks. The shot shows if any of them was a firing tick.
            bool shot = false;
            if (active && stateTick > lastStateTick)
            {
                uint elapsed = stateTick - lastStateTick;
                shot = elapsed >= (uint)cooldownTicks
                    || (uint)AttackSchedule.TicksUntilShot(lastStateTick + 1, entityId, cooldownTicks) < elapsed;
            }

            lastStateTick = stateTick;
            if (targetKnown)
                lastTargetHealth = targetHealth;

            return shot;
        }
    }
}
