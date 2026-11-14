using System.Collections.Generic;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using UnityEngine;
using UnityEngine.InputSystem;

namespace VisorLite
{
    /// <summary>
    /// The helmet visor (glass + condensation) covers the whole screen with two HDRP/Lit transparent layers, so every
    /// pixel runs the full light loop twice. On weak GPUs that is ~25-40% of the frame. This swaps both layers for
    /// HDRP/Unlit copies (same texture, colour and blending) and keeps the condensation colour in sync with the game.
    /// </summary>
    [BepInPlugin("visorlite", "VisorLite", "1.1.0")]
    public class Plugin : BaseUnityPlugin
    {
        internal static ManualLogSource Log;
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<Key> ToggleKey;
        internal static ConfigEntry<float> GlassBrightness, CondensationBrightness;
        internal static ConfigEntry<bool> SyncWithGame;
        public enum LayerMode { Unlit, Original, Hidden }
        internal static ConfigEntry<LayerMode> GlassMode, CondensationMode;
        internal static bool RuntimeOn;

        private void Awake()
        {
            Log = Logger;
            Enabled = Config.Bind("General", "Enabled", true, "Master switch.");
            ToggleKey = Config.Bind("General", "ToggleKey", Key.F1, "Switch between the cheap and the original visor in game. None = no key.");
            GlassBrightness = Config.Bind("Look", "GlassBrightness", 0.15f, "Colour multiplier of the unlit visor glass.");
            CondensationBrightness = Config.Bind("Look", "CondensationBrightness", 0.03f, "Colour multiplier of the unlit (additive) condensation layer.");
            SyncWithGame = Config.Bind("Look", "SyncCondensationWithGame", true,
                "Copy the game's live visor effects (condensation in rain / water) onto the cheap layers. Off = the cheap visor never changes.");
            GlassMode = Config.Bind("Layers", "GlassMode", LayerMode.Unlit,
                "Unlit = cheap copy (the speed-up), Original = keep the game's expensive glass, Hidden = don't draw it.");
            CondensationMode = Config.Bind("Layers", "CondensationMode", LayerMode.Unlit,
                "Unlit = cheap copy (the speed-up), Original = keep the game's expensive condensation (correct rain look, slower), Hidden = don't draw it.");
            RuntimeOn = Enabled.Value;

            var go = new GameObject("VisorLite");
            go.hideFlags = HideFlags.HideAndDontSave;
            DontDestroyOnLoad(go);
            go.AddComponent<VisorSwapper>();
            Log.LogInfo($"VisorLite loaded ({(RuntimeOn ? "enabled" : "disabled")}, toggle: {ToggleKey.Value})");
        }
    }

    internal class VisorSwapper : MonoBehaviour
    {
        private class Swap
        {
            public Renderer R;
            public Material[] Original;
            public Material[] Cheap;
            public List<(Material src, Material copy, bool additive)> Pairs = new List<(Material, Material, bool)>();
        }

        private static readonly string[] BlendProps =
        {
            "_SurfaceType", "_BlendMode", "_SrcBlend", "_DstBlend", "_AlphaSrcBlend", "_AlphaDstBlend",
            "_ZWrite", "_TransparentZWrite", "_ZTestTransparent", "_CullMode", "_CullModeForward", "_TransparentCullMode",
            "_DoubleSidedEnable", "_EnableBlendModePreserveSpecularLighting", "_AlphaCutoffEnable", "_StencilRef", "_StencilWriteMask"
        };

        private readonly List<Swap> _swaps = new List<Swap>();
        private Material _template;
        private float _nextScan;
        private bool _applied;
        private bool _warned;
        private string _modes;
        private float _nextFullScan;

        private void LateUpdate()
        {
            var kb = Keyboard.current;
            var key = Plugin.ToggleKey.Value;
            if (kb != null && key != Key.None && kb[key].wasPressedThisFrame)
            {
                Plugin.RuntimeOn = !Plugin.RuntimeOn;
                Plugin.Log.LogInfo($"VisorLite {(Plugin.RuntimeOn ? "ON" : "OFF")}");
                try { ShowTip(Plugin.RuntimeOn ? "ON" : "OFF"); } catch { /* game API changed: the tip is optional */ }
            }

            // helmet renderers can be destroyed/recreated on scene changes
            _swaps.RemoveAll(s => s.R == null);
            if (Time.unscaledTime >= _nextScan)
            {
                _nextScan = Time.unscaledTime + 2f;
                Scan();
            }

            bool want = Plugin.RuntimeOn;
            if (want != _applied)
            {
                foreach (var s in _swaps) s.R.sharedMaterials = want ? s.Cheap : s.Original;
                _applied = want;
            }

            // layer modes changed in game (LethalConfig): rebuild the copies
            string modes = $"{Plugin.GlassMode.Value}|{Plugin.CondensationMode.Value}";
            if (modes != _modes)
            {
                _modes = modes;
                foreach (var s in _swaps) { BuildCheap(s); if (_applied) s.R.sharedMaterials = s.Cheap; }
            }

            if (!_applied) return;
            // the game animates the condensation (rain, water...) on the original material: mirror it
            if (!Plugin.SyncWithGame.Value) return;
            float gk = Plugin.GlassBrightness.Value, ck = Plugin.CondensationBrightness.Value;
            foreach (var s in _swaps)
            {
                if (!SameMaterials(s.R.sharedMaterials, s.Cheap))
                {
                    // something (the game) reassigned materials: take them as the new originals
                    s.Original = s.R.sharedMaterials;
                    BuildCheap(s);
                    s.R.sharedMaterials = s.Cheap;
                }
                foreach (var (src, copy, additive) in s.Pairs)
                {
                    if (src == null || copy == null) continue;
                    SetColor(copy, src, additive ? ck : gk);
                }
            }
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static void ShowTip(string body)
        {
            if (HUDManager.Instance != null) HUDManager.Instance.DisplayTip("VisorLite", body);
        }

        private static bool SameMaterials(Material[] a, Material[] b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (!ReferenceEquals(a[i], b[i])) return false;
            return true;
        }

        private void Scan()
        {
            if (_template == null)
            {
                // any transparent HDRP/Unlit material the game ships has the right shader variant compiled.
                // Prefer the "plainest" one: no emission (a blue flame glows blue!), no distortion.
                _template = Resources.FindObjectsOfTypeAll<Material>()
                    .Where(m => m != null && m.shader != null && m.shader.name == "HDRP/Unlit" && m.HasProperty("_SurfaceType")
                                && m.GetFloat("_SurfaceType") == 1f && !m.name.Contains("VisorLite"))
                    .OrderBy(TemplateScore).FirstOrDefault();
                if (_template == null)
                {
                    if (!_warned) { _warned = true; Plugin.Log.LogWarning("No transparent HDRP/Unlit material loaded yet, will retry"); }
                    return;
                }
                Plugin.Log.LogInfo($"Using '{_template.name}' as unlit template");
            }

            // cheap path: the helmet lives under Systems/Rendering/PlayerHUDHelmetModel
            var root = GameObject.Find("PlayerHUDHelmetModel");
            IEnumerable<Renderer> candidates;
            if (root != null) candidates = root.GetComponentsInChildren<Renderer>(true);
            else
            {
                if (Time.unscaledTime < _nextFullScan) return;
                _nextFullScan = Time.unscaledTime + 10f;
                candidates = FindObjectsOfType<Renderer>().Where(r => InHelmet(r.transform));
            }
            foreach (var r in candidates)
            {
                if (_swaps.Any(s => s.R == r)) continue;
                if (!r.sharedMaterials.Any(m => m != null && m.renderQueue >= 2501 && m.shader.name == "HDRP/Lit")) continue;
                var s = new Swap { R = r, Original = r.sharedMaterials };
                BuildCheap(s);
                _swaps.Add(s);
                if (_applied) r.sharedMaterials = s.Cheap;
                Plugin.Log.LogInfo($"Visor layer found: {r.name} ({string.Join(", ", s.Pairs.Select(p => p.src.name))})");
            }
        }

        private void BuildCheap(Swap s)
        {
            s.Pairs.Clear();
            s.Cheap = new Material[s.Original.Length];
            for (int i = 0; i < s.Original.Length; i++)
            {
                var src = s.Original[i];
                if (src == null || src.renderQueue < 2501 || src.shader.name != "HDRP/Lit") { s.Cheap[i] = src; continue; }
                bool additive = src.HasProperty("_BlendMode") && src.GetFloat("_BlendMode") == 1f;
                var mode = additive ? Plugin.CondensationMode.Value : Plugin.GlassMode.Value;
                if (mode == Plugin.LayerMode.Original) { s.Cheap[i] = src; continue; }
                var m = new Material(_template) { name = src.name + " (VisorLite)" };
                Neutralize(m);
                var tex = src.HasProperty("_BaseColorMap") ? src.GetTexture("_BaseColorMap") : null;
                m.SetTexture("_UnlitColorMap", tex);
                if (tex != null) m.SetTextureScale("_UnlitColorMap", src.GetTextureScale("_BaseColorMap"));
                foreach (var p in BlendProps)
                    if (src.HasProperty(p) && m.HasProperty(p)) m.SetFloat(p, src.GetFloat(p));
                m.renderQueue = src.renderQueue;
                if (mode == Plugin.LayerMode.Hidden) { m.SetColor("_UnlitColor", new Color(0, 0, 0, 0)); s.Cheap[i] = m; continue; }
                SetColor(m, src, additive ? Plugin.CondensationBrightness.Value : Plugin.GlassBrightness.Value);
                s.Pairs.Add((src, m, additive));
                s.Cheap[i] = m;
            }
        }

        private static void SetColor(Material copy, Material src, float k)
        {
            var c = src.GetColor("_BaseColor");
            copy.SetColor("_UnlitColor", new Color(c.r * k, c.g * k, c.b * k, c.a));
        }

        /// <summary>Strip everything the template brought along that the visor must not have.</summary>
        private static void Neutralize(Material m)
        {
            if (m.HasProperty("_EmissiveColor")) m.SetColor("_EmissiveColor", Color.black);
            if (m.HasProperty("_EmissiveColorLDR")) m.SetColor("_EmissiveColorLDR", Color.black);
            if (m.HasProperty("_EmissiveIntensity")) m.SetFloat("_EmissiveIntensity", 0f);
            if (m.HasProperty("_EmissiveColorMap")) m.SetTexture("_EmissiveColorMap", null);
            m.DisableKeyword("_EMISSIVE_COLOR_MAP");
            if (m.HasProperty("_DistortionEnable")) m.SetFloat("_DistortionEnable", 0f);
            if (m.HasProperty("_DistortionOnly")) m.SetFloat("_DistortionOnly", 0f);
            m.DisableKeyword("_DISTORTION");
            if (m.HasProperty("_AlphaCutoffEnable")) m.SetFloat("_AlphaCutoffEnable", 0f);
            m.DisableKeyword("_ALPHATEST_ON");
        }

        private static float TemplateScore(Material m)
        {
            float score = 0;
            if (m.HasProperty("_EmissiveColor")) { var e = m.GetColor("_EmissiveColor"); score += e.r + e.g + e.b; }
            if (m.HasProperty("_EmissiveColorMap") && m.GetTexture("_EmissiveColorMap") != null) score += 10;
            if (m.IsKeywordEnabled("_EMISSIVE_COLOR_MAP")) score += 10;
            if (m.HasProperty("_DistortionEnable") && m.GetFloat("_DistortionEnable") > 0) score += 10;
            if (m.name.IndexOf("flame", System.StringComparison.OrdinalIgnoreCase) >= 0) score += 5;
            return score;
        }

        private static bool InHelmet(Transform t)
        {
            for (; t != null; t = t.parent) if (t.name == "PlayerHUDHelmetModel") return true;
            return false;
        }
    }
}
