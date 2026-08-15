using UnityEngine;

namespace TidalNexus.StandaloneServer
{

    public sealed class ServerPrefabs : ScriptableObject
    {
        public const string ResourcePath = "ServerPrefabs";

        [Tooltip("GlobalTimer.prefab - networked clock the NPC and boss code reads.")]
        public GameObject globalTimer;

        [Tooltip("Event prefabs, spawned only when their event is enabled.")]
        public GameObject eventKraken;
        public GameObject eventRoyale;
        public GameObject eventBeacon;

        private static ServerPrefabs cached;

        public static ServerPrefabs Load()
        {
            if (cached != null)
            {
                return cached;
            }

            cached = Resources.Load<ServerPrefabs>(ResourcePath);
            if (cached == null)
            {
                ServerLog.Warn(
                    $"no {ResourcePath} in Resources - run " +
                    "Tools/Standalone Server/Bind NPC Prefabs");
            }

            return cached;
        }
    }
}
