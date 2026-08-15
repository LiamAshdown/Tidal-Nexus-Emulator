using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEngine;

namespace TidalNexus.StandaloneServer
{

    public static class RealShaderInjector
    {

        private const string DefaultGamePath =
            @"C:\Program Files (x86)\Steam\steamapps\common\Tidal Nexus Online\Tidal Nexus Online_Data\StreamingAssets\aa";

        private static readonly string[] BundleOrder =
        {
            "server_assets_all",
        };

        private static readonly List<AssetBundle> Held = new List<AssetBundle>();
        private static bool done;

        private static Dictionary<string, Shader> realShaders;
        private static Dictionary<string, string> authoredShaders;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        public static void Inject()
        {
            if (done)
            {
                return;
            }

            done = true;

            var host = new GameObject("~RealShaderInjector");
            host.hideFlags = HideFlags.HideAndDontSave;
            Object.DontDestroyOnLoad(host);
            host.AddComponent<Sweeper>();

            LoadOnce();
        }

        private static void LoadOnce()
        {
            string root = System.Environment.GetEnvironmentVariable("TIDALNEXUS_GAME_AA");
            if (string.IsNullOrEmpty(root))
            {
                root = DefaultGamePath;
            }

            if (!Directory.Exists(root))
            {
                Debug.Log($"[RealShader] no shipped client at {root} - " +
                          "keeping the reconstructed shaders");
                return;
            }

            realShaders = LoadRealShaders(root);
            authoredShaders = LoadAuthoredShaderMap();

            Debug.Log($"[RealShader] {realShaders.Count} real shaders, " +
                      $"{authoredShaders.Count} authored mappings");

            var wanted = new Dictionary<string, int>();
            foreach (string shader in authoredShaders.Values)
            {
                if (string.IsNullOrEmpty(shader) ||
                    shader.StartsWith("HDRP/") ||
                    realShaders.ContainsKey(shader))
                {
                    continue;
                }

                wanted.TryGetValue(shader, out int n);
                wanted[shader] = n + 1;
            }

            var missing = new List<KeyValuePair<string, int>>(wanted);
            missing.Sort((a, b) => b.Value.CompareTo(a.Value));

            foreach (KeyValuePair<string, int> entry in missing)
            {
                Debug.Log($"[RealShader] MISSING {entry.Value,4} materials -> {entry.Key}");
            }
        }

        public static int Sweep()
        {
            if (realShaders == null || realShaders.Count == 0)
            {
                return 0;
            }

            int rebound = 0;
            var perShader = new Dictionary<string, int>();

            foreach (Material m in Resources.FindObjectsOfTypeAll<Material>())
            {
                if (m == null || m.shader == null)
                {
                    continue;
                }

                Shader genuine = null;

                if (authoredShaders != null &&
                    authoredShaders.TryGetValue(CleanName(m.name), out string original) &&
                    realShaders.TryGetValue(original, out Shader fromManifest))
                {
                    genuine = fromManifest;
                }
                else if (realShaders.TryGetValue(m.shader.name, out Shader direct))
                {
                    genuine = direct;
                }

                if (genuine == null || m.shader == genuine)
                {
                    continue;
                }

                m.shader = genuine;
                rebound++;

                perShader.TryGetValue(genuine.name, out int n);
                perShader[genuine.name] = n + 1;
            }

            if (rebound > 0)
            {
                foreach (var kv in perShader)
                {
                    Debug.Log($"[RealShader]   {kv.Value,4} materials -> real {kv.Key}");
                }

                Debug.Log($"[RealShader] {rebound} materials switched to the shipped " +
                          "client's compiled shaders");
            }

            return rebound;
        }

        private sealed class Sweeper : MonoBehaviour
        {
            private float next;
            private float deadline;
            private int total;

            private const float BurstSeconds = 180f;

            private const float BurstInterval = 3f;

            private const float IdleInterval = 15f;

            private bool announced;

            private void Start()
            {
                deadline = Time.realtimeSinceStartup + BurstSeconds;
            }

            private void Update()
            {
                if (Time.realtimeSinceStartup < next)
                {
                    return;
                }

                bool startingUp = Time.realtimeSinceStartup <= deadline;

                next = Time.realtimeSinceStartup + (startingUp ? BurstInterval : IdleInterval);
                total += Sweep();

                if (!startingUp && !announced)
                {
                    announced = true;
                    Debug.Log($"[RealShader] start-up sweeping done, {total} materials so far; " +
                              $"continuing every {IdleInterval:0}s for streamed-in models");
                }
            }
        }

        private static string CleanName(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return string.Empty;
            }

            int i = name.IndexOf(" (Instance)", System.StringComparison.Ordinal);
            return i >= 0 ? name.Substring(0, i) : name;
        }

        private static Dictionary<string, string> LoadAuthoredShaderMap()
        {
            var map = new Dictionary<string, string>();
            string path = Path.Combine(Application.streamingAssetsPath, "real-shader-map.json");

            if (!File.Exists(path))
            {
                Debug.LogWarning($"[RealShader] no authored-shader map at {path} - " +
                                 "only materials still on their original shader can be matched");
                return map;
            }

            foreach (Match match in Regex.Matches(File.ReadAllText(path),
                         "\\{\"m\":\"(.*?)\",\"s\":\"(.*?)\"\\}"))
            {
                map[match.Groups[1].Value] = match.Groups[2].Value;
            }

            Debug.Log($"[RealShader] authored-shader map: {map.Count} materials");
            return map;
        }

        private static Dictionary<string, Shader> LoadRealShaders(string root)
        {
            var real = new Dictionary<string, Shader>();

            foreach (string prefix in BundleOrder)
            {
                string[] matches = Directory.GetFiles(root, prefix + "*.bundle",
                    SearchOption.AllDirectories);
                if (matches.Length == 0)
                {
                    continue;
                }

                AssetBundle bundle;
                try
                {
                    bundle = AssetBundle.LoadFromFile(matches[0]);
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning($"[RealShader] {prefix} failed to load: {e.Message}");
                    continue;
                }

                if (bundle == null)
                {
                    continue;
                }

                var known = new HashSet<Shader>(Resources.FindObjectsOfTypeAll<Shader>());

                int before = real.Count;
                bundle.LoadAllAssets();

                foreach (Shader shader in Resources.FindObjectsOfTypeAll<Shader>())
                {
                    if (shader == null || known.Contains(shader))
                    {
                        continue;
                    }

                    string name = shader.name;

                    if (string.IsNullOrEmpty(name) ||
                        name.StartsWith("Hidden/") ||
                        name.StartsWith("HDRP/") ||
                        real.ContainsKey(name))
                    {
                        continue;
                    }

                    real[name] = shader;
                }

                Held.Add(bundle);

                Debug.Log($"[RealShader] {Path.GetFileName(matches[0])}: " +
                          $"+{real.Count - before} shaders");
            }

            var names = new List<string>(real.Keys);
            names.Sort();
            Debug.Log("[RealShader] harvested: " + string.Join(", ", names));

            return real;
        }
    }
}
