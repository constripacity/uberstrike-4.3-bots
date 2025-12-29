using System;
using System.Numerics;

namespace BotRunner.Networking.Payload
{
    /// <summary>
    /// Matches the UberStrike compression: Vector3 components are scaled by 100 and stored as int16.
    /// This mirrors the ShortVector3 utility in the shipped client.
    /// </summary>
    public readonly struct ShortVector3
    {
        public short X { get; }
        public short Y { get; }
        public short Z { get; }

        public ShortVector3(short x, short y, short z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public static ShortVector3 FromVector(Vector3 value)
        {
            return new ShortVector3(
                (short)Math.Clamp((int)Math.Round(value.X * 100f), short.MinValue, short.MaxValue),
                (short)Math.Clamp((int)Math.Round(value.Y * 100f), short.MinValue, short.MaxValue),
                (short)Math.Clamp((int)Math.Round(value.Z * 100f), short.MinValue, short.MaxValue));
        }

        public Vector3 ToVector3()
        {
            const float scale = 1f / 100f;
            return new Vector3(X * scale, Y * scale, Z * scale);
        }

        public override string ToString() => $"({X}, {Y}, {Z})";
    }
}
