namespace Multiplayer.Protocol
{
    public enum MessageId : byte
    {
        None = 0,

        Welcome = 1,
        PlayerJoined = 2,
        PlayerLeft = 3,

        SpawnRequest = 10,
        MoveRequest = 11,

        WorldSnapshot = 20
    }
}
