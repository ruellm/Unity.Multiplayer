using System;

namespace Multiplayer.Protocol
{
    // Yaw on the wire: one byte, 256 steps around the circle.
    public static class YawByte
    {
        public static byte FromDegrees(float degrees)
        {
            float turns = degrees / 360f;
            turns -= (float)Math.Floor(turns);
            return (byte)((int)Math.Round(turns * 256f) & 0xFF);
        }

        public static float ToDegrees(byte yaw)
        {
            return yaw * (360f / 256f);
        }
    }
}
