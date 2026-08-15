using UnityEngine;

namespace TidalNexus.StandaloneServer
{

    public sealed class NpcCatalogue : ScriptableObject
    {

        public const string ResourcePath = "NpcCatalogue";

        [Tooltip("Prefabs carrying NPCBehaviour and a NetworkObject.")]
        public GameObject[] prefabs = new GameObject[0];

        public int[] levels = new int[0];

        private static NpcCatalogue cached;

        public static NpcCatalogue Load()
        {
            if (cached != null)
            {
                return cached;
            }

            cached = Resources.Load<NpcCatalogue>(ResourcePath);
            if (cached == null)
            {
                ServerLog.Warn(
                    $"no {ResourcePath} in Resources - run " +
                    "Tools/Standalone Server/Bind NPC Prefabs");
            }

            return cached;
        }

        public GameObject ForLevel(int level)
        {
            if (prefabs == null || prefabs.Length == 0)
            {
                return null;
            }

            int best = -1;
            int bestDistance = int.MaxValue;

            for (int i = 0; i < prefabs.Length; i++)
            {
                if (prefabs[i] == null)
                {
                    continue;
                }

                int npcLevel = i < levels.Length ? levels[i] : 1;
                int distance = Mathf.Abs(npcLevel - level);

                if (distance < bestDistance ||
                    (distance == bestDistance && Random.value < 0.5f))
                {
                    best = i;
                    bestDistance = distance;
                }
            }

            return best >= 0 ? prefabs[best] : null;
        }
    }
}
