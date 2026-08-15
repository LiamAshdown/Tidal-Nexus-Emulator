using UnityEngine;

namespace TidalNexus.StandaloneServer
{

    public static class WorldPopulator
    {

        private const int FallbackSectors = 6;

        private const float FallbackRadius = 140f;

        private const int BaseLevel = 1;

        private const int LevelsPerSector = 10;

        private const int NpcsPerSectorFloor = 25;

        private const float PlayPlaneY = 5f;

        private static bool populated;

        public static void Populate()
        {
            if (populated || ServerHub.Npcs == null)
            {
                return;
            }

            populated = true;

            SpawnSingletons();

            ProjectBindings bindings = ProjectBindings.Instance;
            if (bindings == null)
            {
                ServerLog.Warn("no ProjectBindings - world will stay empty");
                return;
            }

            if (bindings.npcPrefabs == null || bindings.npcPrefabs.Length == 0)
            {
                NpcCatalogue catalogue = NpcCatalogue.Load();
                if (catalogue == null || catalogue.prefabs.Length == 0)
                {
                    ServerLog.Warn(
                        "no NPC prefabs available - the world will have no NPCs. " +
                        "Run Tools/Standalone Server/Bind NPC Prefabs.");
                    return;
                }

                bindings.npcPrefabs = catalogue.prefabs;
                ServerLog.Info($"using NpcCatalogue: {catalogue.prefabs.Length} prefabs");
            }

            int perSector = Mathf.Max(NpcsPerSectorFloor, bindings.npcsPerSector);

            Vector3[] discovered = DiscoverSectorCentres();

            if (PopulateAuthoredAreas())
            {
                PopulateOre(discovered, bindings);
                WorldDirector.Ensure();
                ServerLog.Info("world director started");
                return;
            }

            if (bindings.npcSectors != null && bindings.npcSectors.Length > 0)
            {
                for (int i = 0; i < bindings.npcSectors.Length; i++)
                {
                    Transform sector = bindings.npcSectors[i];
                    if (sector == null)
                    {
                        continue;
                    }

                    ServerHub.Npcs.Populate(sector.position, perSector, BaseLevel + i);
                }
            }
            else if (discovered != null)
            {
                ServerLog.Info(
                    $"npcSectors not bound - using the {discovered.Length} sector volumes " +
                    "found in the scene");

                for (int i = 0; i < discovered.Length; i++)
                {

                    int min = i * LevelsPerSector + 1;
                    int max = (i + 1) * LevelsPerSector;
                    ServerHub.Npcs.PopulateBand(discovered[i], perSector, min, max);
                }
            }
            else
            {
                ServerLog.Warn(
                    $"no npcSectors bound and none found in the scene - generating " +
                    $"{FallbackSectors} around the origin so the world is not empty");

                for (int i = 0; i < FallbackSectors; i++)
                {
                    float angle = i * Mathf.PI * 2f / FallbackSectors;
                    var centre = new Vector3(
                        Mathf.Cos(angle) * FallbackRadius,
                        0f,
                        Mathf.Sin(angle) * FallbackRadius);

                    ServerHub.Npcs.Populate(centre, perSector, BaseLevel + i);
                }
            }

            ServerLog.Info($"world populated: {ServerHub.Npcs.Alive} npcs alive");

            PopulateOre(discovered, bindings);

            WorldDirector.Ensure();
            ServerLog.Info("world director started");
        }

        private const int NpcBudget = 700;

        private static bool PopulateAuthoredAreas()
        {
            Area[] all = Object.FindObjectsByType<Area>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);

            if (all == null || all.Length == 0)
            {
                return false;
            }

            var world = new System.Collections.Generic.List<Area>();
            foreach (Area area in all)
            {
                if (area != null && area.layer == Enums.NetworkLayerType.WorldMap)
                {
                    world.Add(area);
                }
            }

            if (world.Count == 0)
            {
                return false;
            }

            world.Sort((a, b) => a.areaIndex.CompareTo(b.areaIndex));

            int authored = 0;
            foreach (Area area in world)
            {
                if (area.npcs == null)
                {
                    continue;
                }

                foreach (AreaNPC entry in area.npcs)
                {
                    if (entry != null && entry.npc != null)
                    {
                        authored += Mathf.Max(1, entry.amount);
                    }
                }
            }

            float scale = authored > NpcBudget ? (float)NpcBudget / authored : 1f;

            ServerLog.Info(
                $"{world.Count} authored world areas " +
                $"({authored} npcs authored, scale {scale:0.00}), " +
                $"DepthManager={(DepthManager.instance != null ? "present" : "MISSING - depths will diverge")}");

            AreaStreamer.Ensure(world, scale);
            return true;
        }

        private const int OrePerSector = 30;

        private const float OreSpread = 90f;

        private static void PopulateOre(Vector3[] sectors, ProjectBindings bindings)
        {
            OreCatalogue catalogue = OreCatalogue.Load();
            if (catalogue == null || catalogue.prefabs == null || catalogue.prefabs.Length == 0)
            {
                ServerLog.Warn("no ore catalogue - the world will have nothing to mine");
                return;
            }

            Vector3[] centres = sectors;

            if (centres == null)
            {

                centres = new Vector3[FallbackSectors];
                for (int i = 0; i < FallbackSectors; i++)
                {
                    float angle = i * Mathf.PI * 2f / FallbackSectors;
                    centres[i] = new Vector3(
                        Mathf.Cos(angle) * FallbackRadius, PlayPlaneY, Mathf.Sin(angle) * FallbackRadius);
                }
            }

            int spawned = 0;

            foreach (Vector3 centre in centres)
            {
                for (int i = 0; i < OrePerSector; i++)
                {
                    GameObject prefab = catalogue.Any();
                    if (prefab == null)
                    {
                        continue;
                    }

                    Vector2 offset = Random.insideUnitCircle * OreSpread;
                    var position = new Vector3(
                        centre.x + offset.x, PlayPlaneY, centre.z + offset.y);

                    if (CollectableSpawn.At(prefab, position) != null)
                    {
                        spawned++;
                    }
                }
            }

            int wanted = centres.Length * OrePerSector;
            if (spawned == wanted)
            {
                ServerLog.Info($"ore populated: {spawned} nodes across {centres.Length} sectors");
            }
            else
            {
                ServerLog.Warn(
                    $"ore: only {spawned}/{wanted} nodes spawned - check the collectable " +
                    "prefabs are in Fusion's NetworkPrefabTable");
            }
        }

        private static Vector3[] DiscoverSectorCentres()
        {
            EnvironmentParalax paralax = Object.FindFirstObjectByType<EnvironmentParalax>(
                FindObjectsInactive.Include);

            if (paralax == null || paralax.colliders == null)
            {
                return null;
            }

            var centres = new System.Collections.Generic.List<Vector3>();
            foreach (Collider sector in paralax.colliders)
            {
                if (sector == null)
                {
                    continue;
                }

                Vector3 centre = sector.bounds.center;
                centre.y = PlayPlaneY;
                centres.Add(centre);
            }

            return centres.Count > 0 ? centres.ToArray() : null;
        }

        private static void SpawnSingletons()
        {
            ServerPrefabs prefabs = ServerPrefabs.Load();
            if (prefabs == null || prefabs.globalTimer == null)
            {
                ServerLog.Error(
                    "no GlobalTimer prefab bound - every NPC will throw on the client " +
                    "when it spawns. Run Tools/Standalone Server/Bind NPC Prefabs.");
                return;
            }

            var netPrefab = prefabs.globalTimer.GetComponent<Fusion.NetworkObject>();
            if (netPrefab == null || ServerHub.Runner == null)
            {
                return;
            }

            try
            {
                Fusion.NetworkObject spawned =
                    ServerHub.Runner.Spawn(netPrefab, Vector3.zero, Quaternion.identity);

                if (spawned == null)
                {
                    ServerLog.Error("GlobalTimer spawn returned null");
                    return;
                }

                ServerLog.Info("spawned GlobalTimer");
            }
            catch (System.Exception e)
            {
                ServerLog.Error($"GlobalTimer spawn failed: {e.Message}");
            }
        }

        public static void Reset()
        {
            populated = false;
        }
    }
}
