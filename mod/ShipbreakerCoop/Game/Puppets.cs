using System.Collections.Generic;
using ShipbreakerCoop.Core;
using UnityEngine;

namespace ShipbreakerCoop.Game
{
    /// <summary>
    /// One puppet per remote player. M1: a primitive suit (body, helmet, visor, backpack) built at runtime.
    /// No colliders: the ghost does not touch the world yet (that is milestone 2).
    /// TODO: swap in the game's own suit mesh (PRF_Shipbreaker_BodysuitMesh_HabVersion) loaded at runtime.
    /// </summary>
    internal sealed class Puppet
    {
        static readonly Color[] sColors =
        {
            new Color(1.00f, 0.55f, 0.10f), // 1 orange (host)
            new Color(0.20f, 0.70f, 1.00f), // 2 blue
            new Color(0.40f, 0.90f, 0.30f), // 3 green
            new Color(0.95f, 0.30f, 0.60f), // 4 pink
        };

        public readonly byte Id;
        public readonly GameObject Root;
        readonly Transform mHead;
        public string Name;
        public bool Visible { get; private set; }
        public Vector3 Position => Root.transform.position;
        public Vector3 HeadPosition => mHead.position;

        public Puppet(byte id, string name)
        {
            Id = id; Name = name;
            var color = sColors[(id - 1) % sColors.Length];
            Root = new GameObject($"CoopPuppet_{id}");
            Object.DontDestroyOnLoad(Root);

            Part(PrimitiveType.Capsule, Root.transform, new Vector3(0, -0.35f, 0), new Vector3(0.55f, 0.6f, 0.45f), color);
            Part(PrimitiveType.Cube, Root.transform, new Vector3(0, -0.15f, -0.3f), new Vector3(0.45f, 0.55f, 0.2f), color * 0.6f); // backpack
            mHead = new GameObject("Head").transform;
            mHead.SetParent(Root.transform, false);
            mHead.localPosition = new Vector3(0, 0.45f, 0);
            Part(PrimitiveType.Sphere, mHead, Vector3.zero, Vector3.one * 0.42f, new Color(0.85f, 0.85f, 0.85f));
            Part(PrimitiveType.Cube, mHead, new Vector3(0, 0.02f, 0.17f), new Vector3(0.3f, 0.16f, 0.1f), color, emissive: true); // visor
            SetVisible(false);
        }

        static void Part(PrimitiveType type, Transform parent, Vector3 pos, Vector3 scale, Color color, bool emissive = false)
        {
            var go = GameObject.CreatePrimitive(type);
            Object.Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            var r = go.GetComponent<Renderer>();
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            // CreatePrimitive's default material is built-in pipeline and HDRP draws nothing with it
            // (seen in the lab screenshot: label, no body). Use HDRP materials instead.
            var m = emissive ? MakeUnlit(color) : MakeLit(color);
            if (m != null) r.sharedMaterial = m;
        }

        static Material sLitTemplate;
        static readonly HashSet<Material> sOwn = new HashSet<Material>();

        /// <summary>HDRP/Lit: clone a material the game already set up (keywords and passes valid),
        /// strip its textures and tint it. Falls back to a fresh HDRP/Lit material.</summary>
        static Material MakeLit(Color color)
        {
            if (sLitTemplate == null)
            {
                foreach (var rend in Object.FindObjectsOfType<MeshRenderer>())
                {
                    var sm = rend.sharedMaterial;
                    if (sm != null && !sOwn.Contains(sm) && sm.shader != null && sm.shader.name == "HDRP/Lit")
                    { sLitTemplate = sm; break; }
                }
                if (sLitTemplate != null) Plugin.Log.LogInfo($"puppet material cloned from '{sLitTemplate.name}'");
            }
            Material m;
            if (sLitTemplate != null) m = new Material(sLitTemplate);
            else
            {
                var sh = Shader.Find("HDRP/Lit");
                if (sh == null) { Plugin.Log.LogWarning("HDRP/Lit shader not found"); return null; }
                m = new Material(sh);
            }
            foreach (var tex in new[] { "_BaseColorMap", "_NormalMap", "_MaskMap", "_EmissiveColorMap", "_DetailMap", "_CoatMaskMap" })
                if (m.HasProperty(tex)) m.SetTexture(tex, null);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
            if (m.HasProperty("_EmissiveColor")) m.SetColor("_EmissiveColor", Color.black);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0.2f);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.4f);
            sOwn.Add(m);
            return m;
        }

        static Material MakeUnlit(Color color)
        {
            var sh = Shader.Find("HDRP/Unlit");
            if (sh == null) return MakeLit(color);
            var m = new Material(sh);
            if (m.HasProperty("_UnlitColor")) m.SetColor("_UnlitColor", color);
            return m;
        }

        public void Apply(in PoseSample p)
        {
            Root.transform.SetPositionAndRotation(p.Pos.ToUnity(), p.Rot.ToUnity());
            mHead.rotation = p.Look.ToUnity();
        }

        public void SetVisible(bool v)
        {
            if (v == Visible && Root.activeSelf == v) return;
            Visible = v; Root.SetActive(v);
        }

        public void Destroy() => Object.Destroy(Root);
    }

    internal sealed class PuppetManager
    {
        readonly Dictionary<byte, Puppet> mPuppets = new Dictionary<byte, Puppet>();
        public IEnumerable<Puppet> All => mPuppets.Values;

        public void Update(CoopSession session, double now, string localScene)
        {
            var remotes = session.Remotes;
            foreach (var kv in remotes)
            {
                var rp = kv.Value;
                if (!mPuppets.TryGetValue(rp.Id, out var pup)) { pup = new Puppet(rp.Id, rp.Name); mPuppets[rp.Id] = pup; }
                pup.Name = rp.Name;
                if (!rp.Buffer.Sample(now, out var pose)) { pup.SetVisible(false); continue; }
                bool sameScene = pose.Scene == LocalPlayerSource.AnyScene || (pose.Scene.Length > 0 && pose.Scene == localScene);
                bool show = pose.HasPlayer && sameScene && now - rp.LastStateTime < 3.0;
                pup.SetVisible(show);
                if (show) pup.Apply(pose);
            }
            List<byte> gone = null;
            foreach (var id in mPuppets.Keys) if (!remotes.ContainsKey(id)) (gone ?? (gone = new List<byte>())).Add(id);
            if (gone != null) foreach (var id in gone) { mPuppets[id].Destroy(); mPuppets.Remove(id); }
        }

        public void Clear()
        {
            foreach (var p in mPuppets.Values) p.Destroy();
            mPuppets.Clear();
        }

        /// <summary>Names over heads (IMGUI works in any render pipeline).</summary>
        public void DrawLabels(GUIStyle style)
        {
            var cam = Camera.main;
            if (cam == null) return;
            foreach (var p in mPuppets.Values)
            {
                if (!p.Visible) continue;
                var w = p.HeadPosition + Vector3.up * 0.45f;
                var s = cam.WorldToScreenPoint(w);
                if (s.z <= 0) continue;
                float dist = Vector3.Distance(cam.transform.position, p.Position);
                var text = $"{p.Name}  {dist:F0} m";
                var size = style.CalcSize(new GUIContent(text));
                GUI.Label(new Rect(s.x - size.x / 2, Screen.height - s.y - size.y, size.x, size.y), text, style);
            }
        }
    }
}
