namespace Multiplayer.Protocol
{
    public enum MessageId : byte
    {
        None = 0,

        Welcome = 1,
        PlayerJoined = 2,
        PlayerLeft = 3,

        MoveRequest = 11,
        PlaceStructureRequest = 12,
        BuildUnitRequest = 13,
        AttackRequest = 14,

        WorldSnapshot = 20,
        GameEvent = 21
    }
}
