using UnityEngine;

namespace TidalNexus.StandaloneServer
{

    public sealed class OreCatalogue : ScriptableObject
    {

        public const string ResourcePath = "OreCatalogue";

        [Tooltip("Prefabs carrying CollectableObject and a NetworkObject.")]
        public GameObject[] prefabs = new GameObject[0];

        private static OreCatalogue cached;

        public static OreCatalogue Load()
        {
            if (cached != null)
            {
                return cached;
            }

            cached = Resources.Load<OreCatalogue>(ResourcePath);

            if (cached == null)
            {
                ServerLog.Warn(
                    $"no {ResourcePath} in Resources - run " +
                    "Tools > Standalone Server > Bind Ore Prefabs. No ore will spawn.");
            }

            return cached;
        }

        public GameObject Any()
        {
            if (prefabs == null || prefabs.Length == 0)
            {
                return null;
            }

            for (int attempt = 0; attempt < 4; attempt++)
            {
                GameObject candidate = prefabs[Random.Range(0, prefabs.Length)];
                if (candidate != null)
                {
                    return candidate;
                }
            }

            foreach (GameObject prefab in prefabs)
            {
                if (prefab != null)
                {
                    return prefab;
                }
            }

            return null;
        }
    }
}
