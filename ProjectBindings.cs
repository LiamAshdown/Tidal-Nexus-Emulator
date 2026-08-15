using UnityEngine;

namespace TidalNexus.StandaloneServer
{

    public sealed class ProjectBindings : MonoBehaviour
    {
        public static ProjectBindings Instance { get; private set; }

        [Header("Player")]
        [Tooltip("The networked player prefab spawned for each joining peer. " +
                 "This is the one thing the server cannot start without.")]
        public GameObject playerPrefab;

        [Tooltip("Where players appear. If empty, WorldSpawnFallback is used.")]
        public Transform[] spawnPoints;

        [Tooltip("Used when no spawn points are assigned.")]
        public Vector3 worldSpawnFallback = new Vector3(0f, 5f, 0f);

        [Header("World")]
        [Tooltip("Build index of the scene holding the world (areas, stations, " +
                 "pooling). The server loads this after the runner starts.")]
        public int worldSceneIndex = 1;

        [Header("NPCs")]
        [Tooltip("Networked NPC prefabs the server spawns to populate sectors. " +
                 "One is picked at random per spawn. Leave empty to run a world " +
                 "with no NPCs rather than fail to start.")]
        public GameObject[] npcPrefabs = new GameObject[0];

        [Tooltip("How many NPCs each populated sector holds.")]
        public int npcsPerSector = 25;

        [Tooltip("Sector centres to populate. Empty means only the spawn area.")]
        public Transform[] npcSectors = new Transform[0];

        [Header("PvP")]
        [Tooltip("Where RPC_TeleportToBZ sends players. Falls back to a fixed " +
                 "offset from the origin when unset.")]
        public Transform battleZone;

        [Header("Starting state")]
        [Tooltip("Credits given to a player with no saved state.")]
        public int startingCredits = 10000;

        [Tooltip("Borax given to a player with no saved state.")]
        public int startingBorax = 1000;

        private void Awake()
        {
            Instance = this;

            if (playerPrefab == null)
            {
                ServerLog.Error(
                    "ProjectBindings.playerPrefab is not set. Joining peers will " +
                    "connect but never get a ship.");
            }
        }

        public Vector3 NextSpawnPosition(int index)
        {
            if (spawnPoints == null || spawnPoints.Length == 0)
            {
                return worldSpawnFallback;
            }

            Transform point = spawnPoints[Mathf.Abs(index) % spawnPoints.Length];
            return point != null ? point.position : worldSpawnFallback;
        }

        public static int WorldSceneIndex =>
            Instance != null ? Instance.worldSceneIndex : 1;
    }
}
