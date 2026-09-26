using System.Collections.Generic;
using Multiplayer.Protocol;
using UnityEngine;

namespace Multiplayer.Game
{
    public sealed class PlayerRoster
    {
        static readonly Color UnknownColor = new Color(0.5f, 0.5f, 0.5f);

        readonly Dictionary<int, Color> colors = new Dictionary<int, Color>();

        public int Count
        {
            get { return colors.Count; }
        }

        public void Add(PlayerJoinedMessage joined)
        {
            colors[joined.PlayerId] = new Color32(joined.R, joined.G, joined.B, 255);
        }

        public void Remove(int playerId)
        {
            colors.Remove(playerId);
        }

        public void Clear()
        {
            colors.Clear();
        }

        public Color GetColor(int playerId)
        {
            Color color;
            return colors.TryGetValue(playerId, out color) ? color : UnknownColor;
        }
    }
}
