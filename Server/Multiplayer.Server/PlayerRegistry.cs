using System.Collections.Generic;

namespace Multiplayer.Server
{
    public struct PlayerColor
    {
        public byte R;
        public byte G;
        public byte B;

        public PlayerColor(byte r, byte g, byte b)
        {
            R = r;
            G = g;
            B = b;
        }

        public override string ToString()
        {
            return "#" + R.ToString("X2") + G.ToString("X2") + B.ToString("X2");
        }
    }

    public sealed class PlayerInfo
    {
        public int PlayerId;
        public PlayerColor Color;
    }

    public sealed class PlayerRegistry
    {
        static readonly PlayerColor[] Palette =
        {
            new PlayerColor(51, 115, 230),
            new PlayerColor(230, 64, 64),
            new PlayerColor(64, 191, 90),
            new PlayerColor(240, 200, 40),
            new PlayerColor(180, 80, 220),
            new PlayerColor(40, 200, 210),
            new PlayerColor(245, 130, 40),
            new PlayerColor(240, 240, 240)
        };

        readonly Dictionary<int, PlayerInfo> byPeerId = new Dictionary<int, PlayerInfo>();
        int nextPlayerId = 1;

        public int Count
        {
            get { return byPeerId.Count; }
        }

        public IEnumerable<PlayerInfo> Players
        {
            get { return byPeerId.Values; }
        }

        public PlayerInfo Add(int peerId)
        {
            int playerId = nextPlayerId++;
            PlayerInfo info = new PlayerInfo
            {
                PlayerId = playerId,
                Color = Palette[(playerId - 1) % Palette.Length]
            };
            byPeerId[peerId] = info;
            return info;
        }

        public bool TryGet(int peerId, out PlayerInfo info)
        {
            return byPeerId.TryGetValue(peerId, out info);
        }

        public bool Remove(int peerId, out PlayerInfo info)
        {
            if (!byPeerId.TryGetValue(peerId, out info))
                return false;

            byPeerId.Remove(peerId);
            return true;
        }
    }
}
