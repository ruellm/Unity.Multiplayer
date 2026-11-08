using System;

namespace Multiplayer.Protocol
{
    // Footprint rules shared by the server's placement checks and the client's placement ghost.
    public static class Footprint
    {
        public const float WorldHalfExtent = 50f;

        public static bool Overlaps(Vec2 a, float radiusA, Vec2 b, float radiusB)
        {
            float sum = radiusA + radiusB;
            float dx = b.X - a.X;
            float dz = b.Z - a.Z;
            return dx * dx + dz * dz < sum * sum;
        }

        public static bool InBounds(Vec2 position, float halfExtent)
        {
            return !float.IsNaN(position.X) && !float.IsNaN(position.Z)
                && Math.Abs(position.X) <= halfExtent && Math.Abs(position.Z) <= halfExtent;
        }
    }
}
