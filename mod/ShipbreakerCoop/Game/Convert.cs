using ShipbreakerCoop.Core;
using UnityEngine;

namespace ShipbreakerCoop.Game
{
    internal static class Conv
    {
        public static V3 ToNet(this Vector3 v) => new V3(v.x, v.y, v.z);
        public static Q4 ToNet(this Quaternion q) => new Q4(q.x, q.y, q.z, q.w);
        public static Vector3 ToUnity(this V3 v) => new Vector3(v.X, v.Y, v.Z);
        public static Quaternion ToUnity(this Q4 q) => new Quaternion(q.X, q.Y, q.Z, q.W);
    }
}
