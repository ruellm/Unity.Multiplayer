using System;

namespace Multiplayer.Protocol
{
    public static class MovementMath
    {
        public static Vec2 Step(Vec2 current, Vec2 target, float speed, float dt)
        {
            float dx = target.X - current.X;
            float dz = target.Z - current.Z;
            float distance = (float)Math.Sqrt(dx * dx + dz * dz);
            float travel = speed * dt;

            if (travel >= distance)
                return target;

            float scale = travel / distance;
            return new Vec2(current.X + dx * scale, current.Z + dz * scale);
        }
    }
}
