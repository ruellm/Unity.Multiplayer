namespace Multiplayer.Protocol
{
    public enum ActionState : byte
    {
        Idle = 0,
        Moving = 1,
        MovingToAttack = 2,
        Attacking = 3,
        Dying = 4
    }
}
