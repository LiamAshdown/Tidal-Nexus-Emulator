using UnityEngine;

namespace TidalNexus.StandaloneServer
{

    public sealed class LootCatalogue : ScriptableObject
    {
        [Tooltip("Capsule_Sector1..14, in sector order.")]
        public GameObject[] capsules = new GameObject[0];

        [Tooltip("Used when no sector capsule matches.")]
        public GameObject fallback;

        public GameObject ForLevel(int level)
        {
            if (capsules != null && capsules.Length > 0)
            {
                int index = Mathf.Clamp((level - 1) / 10, 0, capsules.Length - 1);
                if (capsules[index] != null)
                {
                    return capsules[index];
                }
            }

            return fallback;
        }
    }
}
