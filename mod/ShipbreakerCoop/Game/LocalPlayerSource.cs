using BBI.Unity.Game;
using ShipbreakerCoop.Core;
using UnityEngine;

namespace ShipbreakerCoop.Game
{
    /// <summary>Reads the local player's pose. Player is a MonoBehaviour + Rigidbody (decomp Player.cs).</summary>
    internal sealed class LocalPlayerSource
    {
        public const string AnyScene = "*";
        Player mPlayer;
        Rigidbody mBody;
        float mNextSearch;
        public string Fake; // "circle" = lab mode, see CoopOptions

        public Player Player => mPlayer;

        /// <summary>Scene of the local player, or "" when there is none (menus, loading).</summary>
        public string Scene => mPlayer != null ? mPlayer.gameObject.scene.name : "";

        public PlayerStateMsg Capture()
        {
            if (mPlayer == null && Time.unscaledTime >= mNextSearch)
            {
                mNextSearch = Time.unscaledTime + 1f;
                mPlayer = Object.FindObjectOfType<Player>();
                mBody = mPlayer != null ? mPlayer.GetComponent<Rigidbody>() : null;
                if (mPlayer != null) Plugin.Log.LogInfo($"local player found in scene '{Scene}'");
            }

            var cam = Camera.main;
            if (Fake == "circle") return FakeCircle(cam);

            if (mPlayer == null) return new PlayerStateMsg { Scene = "", Rot = Q4.Identity, Look = Q4.Identity };
            var t = mPlayer.transform;
            return new PlayerStateMsg
            {
                Scene = Scene,
                Pos = (mBody != null ? mBody.position : t.position).ToNet(),
                Vel = (mBody != null ? mBody.velocity : Vector3.zero).ToNet(),
                Rot = t.rotation.ToNet(),
                Look = (cam != null ? cam.transform.rotation : t.rotation).ToNet(),
                Flags = PlayerStateMsg.FlagHasPlayer,
            };
        }

        /// <summary>A fake player 4 m in front of the camera, circling with radius 1.5 m at ~0.5 rev/s.
        /// Scene "*" = visible everywhere. Lets one person (or a bot) check the whole pipe.</summary>
        PlayerStateMsg FakeCircle(Camera cam)
        {
            Vector3 center = cam != null ? cam.transform.position + cam.transform.forward * 4f : Vector3.zero;
            float a = Time.unscaledTime * Mathf.PI;
            var offset = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * 1.5f;
            var vel = new Vector3(-Mathf.Sin(a), 0, Mathf.Cos(a)) * 1.5f * Mathf.PI;
            var rot = Quaternion.LookRotation(vel.normalized, Vector3.up);
            return new PlayerStateMsg
            {
                Scene = AnyScene, Pos = (center + offset).ToNet(), Vel = vel.ToNet(),
                Rot = rot.ToNet(), Look = rot.ToNet(), Flags = PlayerStateMsg.FlagHasPlayer,
            };
        }
    }
}
