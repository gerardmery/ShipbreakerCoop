using System;

namespace ShipbreakerCoop.Core
{
    /// <summary>Engine-free vector. Converted to/from UnityEngine.Vector3 in the plugin.</summary>
    public struct V3
    {
        public float X, Y, Z;
        public V3(float x, float y, float z) { X = x; Y = y; Z = z; }
        public static readonly V3 Zero = new V3(0, 0, 0);
        public static V3 operator +(V3 a, V3 b) => new V3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static V3 operator -(V3 a, V3 b) => new V3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public static V3 operator *(V3 a, float s) => new V3(a.X * s, a.Y * s, a.Z * s);
        public float Length => (float)Math.Sqrt(X * X + Y * Y + Z * Z);
        public static V3 Lerp(V3 a, V3 b, float t) => a + (b - a) * t;
        public override string ToString() => $"({X:F2}, {Y:F2}, {Z:F2})";
    }

    /// <summary>Engine-free quaternion (x, y, z, w like Unity).</summary>
    public struct Q4
    {
        public float X, Y, Z, W;
        public Q4(float x, float y, float z, float w) { X = x; Y = y; Z = z; W = w; }
        public static readonly Q4 Identity = new Q4(0, 0, 0, 1);

        public static float Dot(Q4 a, Q4 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z + a.W * b.W;

        public Q4 Normalized()
        {
            float n = (float)Math.Sqrt(X * X + Y * Y + Z * Z + W * W);
            if (n < 1e-6f || float.IsNaN(n)) return Identity;
            return new Q4(X / n, Y / n, Z / n, W / n);
        }

        /// <summary>Normalized lerp along the short arc. Good enough at 20 Hz.</summary>
        public static Q4 Nlerp(Q4 a, Q4 b, float t)
        {
            if (Dot(a, b) < 0) b = new Q4(-b.X, -b.Y, -b.Z, -b.W);
            return new Q4(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t,
                          a.Z + (b.Z - a.Z) * t, a.W + (b.W - a.W) * t).Normalized();
        }

        /// <summary>Angle between two rotations in degrees.</summary>
        public static float AngleDeg(Q4 a, Q4 b)
        {
            float d = Math.Min(Math.Abs(Dot(a.Normalized(), b.Normalized())), 1f);
            return (float)(2.0 * Math.Acos(d) * 180.0 / Math.PI);
        }
    }
}
