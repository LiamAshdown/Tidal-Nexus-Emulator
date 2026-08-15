using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace TidalNexus.StandaloneServer
{

    public sealed class RuntimeShaderFix : MonoBehaviour
    {
        private const float FirstScanDelay = 0.5f;
        private const float ScanInterval = 2f;

        private static readonly string[] Fine =
        {
            "Standard", "Sprites/", "UI/", "Particles/", "Legacy Shaders/",
            "Unlit/", "Mobile/", "Skybox/", "TextMeshPro/", "Oktay/UI",
            "FakeFogUI", "Hidden/",
        };

        private static readonly string[] AlbedoNames =
        {
            "_BaseColorMap", "_Albedo", "_MainTex", "_BaseMap", "_Diffuse",
            "_ColorMap", "_AlbedoMap", "_MainTexture", "_Texture",
        };

        private static readonly string[] NormalNames =
        {
            "_NormalMap", "_Normal", "_BumpMap", "_Bump",
        };

        private static readonly string[] EmissionNames =
        {
            "_EmissiveColorMap", "_EmissionMap", "_Emissive",
        };

        private static readonly string[] ColorNames =
        {
            "_BaseColor", "_Color", "_MainColor", "_TintColor",
        };

        private static readonly HashSet<int> Handled = new HashSet<int>();

        private static readonly string[] VfxWords =
        {
            "shapes/", "fakelight", "light", "glow", "beam", "trail", "smoke",
            "flame", "fire", "spark", "flare", "laser", "explos", "turbine",
            "water", "bubble", "particle", "vfx", "aura", "ray", "decal",
        };

        private static Shader _standard;
        private static Shader _unlit;
        private static int _fixedCount;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {

            if (ServerBootstrap.Instance != null)
            {
                return;
            }

            _standard = Shader.Find("Standard");
            _unlit = Shader.Find("Sprites/Default");
            if (_standard == null)
            {
                Debug.LogWarning("[ShaderFix] Standard shader not found - not starting");
                return;
            }

            var host = new GameObject("~RuntimeShaderFix");
            host.hideFlags = HideFlags.HideAndDontSave;
            DontDestroyOnLoad(host);
            host.AddComponent<RuntimeShaderFix>();
        }

        private void Start()
        {
            InvokeRepeating(nameof(Scan), FirstScanDelay, ScanInterval);
        }

        private void Scan()
        {
            Renderer[] renderers = FindObjectsByType<Renderer>(FindObjectsSortMode.None);
            int before = _fixedCount;

            foreach (Renderer r in renderers)
            {
                Material[] materials = r.sharedMaterials;
                for (int i = 0; i < materials.Length; i++)
                {
                    TryFix(materials[i]);
                }
            }

            if (_fixedCount != before)
            {
                Debug.Log($"[ShaderFix] rebound {_fixedCount - before} material(s) " +
                          $"({_fixedCount} total) across {renderers.Length} renderers");
            }
        }

        private static void TryFix(Material m)
        {
            if (m == null || m.shader == null)
            {
                return;
            }

            if (!Handled.Add(m.GetInstanceID()))
            {
                return;
            }

            string name = m.shader.name;
            foreach (string prefix in Fine)
            {
                if (name.StartsWith(prefix, System.StringComparison.Ordinal))
                {
                    return;
                }
            }

            Texture albedo = First(m, AlbedoNames, out Vector2 scale, out Vector2 offset);
            Texture normal = First(m, NormalNames, out _, out _);
            Texture emission = First(m, EmissionNames, out _, out _);
            Color tint = FirstColor(m, ColorNames);

            bool isEffect = _unlit != null &&
                            (albedo == null ||
                             VfxWords.Any(w => name.ToLowerInvariant().Contains(w)));

            Shader target = isEffect ? _unlit : _standard;

            if (albedo == null && !isEffect)
            {
                Debug.LogWarning($"[ShaderFix] '{m.name}' ({name}): no albedo found. " +
                                 $"Texture properties: {DescribeTextures(m)}");
            }

            Debug.Log($"[ShaderFix] '{m.name}' ({name}) -> {target.name}" +
                      (albedo != null ? $", albedo={albedo.name}" : ", no texture"));

            m.shader = target;

            if (albedo != null)
            {
                m.SetTexture("_MainTex", albedo);
                m.SetTextureScale("_MainTex", scale);
                m.SetTextureOffset("_MainTex", offset);
            }

            if (tint.a <= 0.01f)
            {
                tint.a = 1f;
            }
            m.SetColor("_Color", tint);

            if (target == _standard)
            {
                if (normal != null)
                {
                    m.SetTexture("_BumpMap", normal);
                    m.EnableKeyword("_NORMALMAP");
                }

                if (emission != null)
                {
                    m.SetTexture("_EmissionMap", emission);
                    m.SetColor("_EmissionColor", Color.white);
                    m.EnableKeyword("_EMISSION");
                }
            }

            _fixedCount++;
        }

        private static Texture First(Material m, string[] names, out Vector2 scale, out Vector2 offset)
        {
            scale = Vector2.one;
            offset = Vector2.zero;

            foreach (string n in names)
            {
                if (!m.HasProperty(n))
                {
                    continue;
                }

                Texture t = m.GetTexture(n);
                if (t != null)
                {
                    scale = m.GetTextureScale(n);
                    offset = m.GetTextureOffset(n);
                    return t;
                }
            }

            if (names != AlbedoNames)
            {
                return null;
            }

            string best = null;
            int bestScore = int.MinValue;
            Shader shader = m.shader;
            int count = shader.GetPropertyCount();

            for (int i = 0; i < count; i++)
            {
                if (shader.GetPropertyType(i) != UnityEngine.Rendering.ShaderPropertyType.Texture)
                {
                    continue;
                }

                string prop = shader.GetPropertyName(i);
                Texture t = m.GetTexture(prop);
                if (t == null)
                {
                    continue;
                }

                int score = ScoreAlbedoName(prop);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = prop;
                }
            }

            if (best == null)
            {
                return null;
            }

            scale = m.GetTextureScale(best);
            offset = m.GetTextureOffset(best);
            Debug.Log($"[ShaderFix] '{m.name}' ({shader.name}): albedo resolved to {best}");
            return m.GetTexture(best);
        }

        private static int ScoreAlbedoName(string prop)
        {
            string p = prop.ToLowerInvariant();

            if (p.Contains("albedo") || p.Contains("basecolor") || p.Contains("diffuse")) return 100;
            if (p.Contains("maintex") || p.Contains("basemap")) return 90;
            if (p.Contains("color") || p.Contains("colour")) return 70;
            if (p.Contains("tex") && !p.Contains("detail")) return 40;

            if (p.Contains("normal") || p.Contains("bump")) return -100;
            if (p.Contains("mask") || p.Contains("metal") || p.Contains("smooth") ||
                p.Contains("rough") || p.Contains("occl") || p.Contains("height") ||
                p.Contains("emiss") || p.Contains("detail") || p.Contains("pattern")) return -50;

            return 0;
        }

        private static string DescribeTextures(Material m)
        {
            Shader shader = m.shader;
            int count = shader.GetPropertyCount();
            var parts = new List<string>();

            for (int i = 0; i < count; i++)
            {
                if (shader.GetPropertyType(i) != UnityEngine.Rendering.ShaderPropertyType.Texture)
                {
                    continue;
                }

                string prop = shader.GetPropertyName(i);
                parts.Add(m.GetTexture(prop) != null ? prop + "=SET" : prop);
            }

            return parts.Count == 0 ? "(none declared)" : string.Join(", ", parts);
        }

        private static Color FirstColor(Material m, string[] names)
        {
            foreach (string n in names)
            {
                if (m.HasProperty(n))
                {
                    return m.GetColor(n);
                }
            }

            return Color.white;
        }
    }
}
