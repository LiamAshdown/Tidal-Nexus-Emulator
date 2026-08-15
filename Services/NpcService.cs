using System;
using System.Collections.Generic;
using Fusion;
using TidalNexus.StandaloneServer.Core;
using UnityEngine;

namespace TidalNexus.StandaloneServer.Services
{

    public sealed class NpcService
    {

        private const float DeathLinger = 0.75f;

        private const float MinimumRespawn = 12f;

        private const float NoDataRespawn = 30f;

        private sealed class Npc
        {
            public NetworkObject Object;

            public NPCBehaviour Behaviour;

            public Vector3 Home;
            public int Level;

            public NPCData Data;

            public RewardPurse? Reward;

            public Area Area;

            public GameObject Prefab;

            public bool Dead;

            public bool EventOwned;

            public float DespawnAt;

            public float RespawnAt;
            public bool Despawned;
        }

        private readonly List<Npc> _npcs = new List<Npc>();

        private readonly Dictionary<NPCBehaviour, Npc> _byBehaviour =
            new Dictionary<NPCBehaviour, Npc>();

        private readonly List<Npc> _dying = new List<Npc>();

        public int Alive
        {
            get
            {
                int n = 0;
                foreach (Npc npc in _npcs)
                {
                    if (!npc.Dead)
                    {
                        n++;
                    }
                }

                return n;
            }
        }

        public void PopulateBand(Vector3 centre, int count, int minLevel, int maxLevel,
            float spread = 70f)
        {
            for (int i = 0; i < count; i++)
            {
                Populate(centre, 1, UnityEngine.Random.Range(minLevel, maxLevel + 1), spread);
            }
        }

        public int PopulateArea(Area area, float scale)
        {
            if (area == null || area.npcs == null)
            {
                return 0;
            }

            int spawned = 0;

            foreach (AreaNPC entry in area.npcs)
            {
                if (entry == null || entry.npc == null || entry.npc.prefab == null)
                {
                    continue;
                }

                int want = Mathf.Max(1, Mathf.RoundToInt(entry.amount * scale));

                for (int i = 0; i < want; i++)
                {

                    int index = _nextIndex++;

                    Vector3 position;
                    try
                    {
                        position = area.GetRandomPosition(index);
                    }
                    catch (Exception)
                    {

                        continue;
                    }

                    if (SpawnAuthored(entry.npc, area, position, index, entry) != null)
                    {
                        spawned++;
                    }
                }
            }

            spawned += SpawnBosses(area);

            return spawned;
        }

        private int SpawnBosses(Area area)
        {
            int spawned = 0;

            foreach (NPCData boss in BossesFor(area))
            {
                int index = _nextIndex++;

                Vector3 position;
                try
                {
                    position = area.GetRandomPosition(index);
                }
                catch (Exception)
                {
                    continue;
                }

                if (SpawnAuthored(boss, area, position, index) != null)
                {
                    spawned++;
                    ServerLog.Info($"boss {boss.name} (level {boss.level}) "
                        + $"spawned in sector {area.areaIndex + 1}");
                }
            }

            return spawned;
        }

        private Dictionary<int, List<NPCData>> _bossesByArea;

        private IEnumerable<NPCData> BossesFor(Area area)
        {
            _bossesByArea ??= MapBosses();

            return _bossesByArea.TryGetValue(area.areaIndex, out List<NPCData> list)
                ? list
                : System.Linq.Enumerable.Empty<NPCData>();
        }

        private static Dictionary<int, List<NPCData>> MapBosses()
        {
            var map = new Dictionary<int, List<NPCData>>();

            List<NPCData> catalogue = GameData.Data?.npcs;
            if (catalogue == null)
            {
                return map;
            }

            var sectorLevel = new Dictionary<int, int>();
            foreach (Area area in UnityEngine.Object.FindObjectsByType<Area>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (area?.npcs == null)
                {
                    continue;
                }

                int top = 0;
                foreach (AreaNPC entry in area.npcs)
                {
                    if (entry?.npc != null && !IsBoss(entry.npc) && entry.npc.level > top)
                    {
                        top = entry.npc.level;
                    }
                }

                if (top > 0)
                {
                    sectorLevel[area.areaIndex] = top;
                }
            }

            if (sectorLevel.Count == 0)
            {
                ServerLog.Warn("no sector levels found - bosses cannot be placed");
                return map;
            }

            foreach (NPCData boss in catalogue)
            {
                if (boss == null || !IsBoss(boss) || IsKrakenPart(boss))
                {
                    continue;
                }

                int best = -1;
                int bestGap = int.MaxValue;

                foreach (KeyValuePair<int, int> sector in sectorLevel)
                {
                    int gap = Mathf.Abs(sector.Value - boss.level);
                    if (gap < bestGap)
                    {
                        bestGap = gap;
                        best = sector.Key;
                    }
                }

                if (best < 0)
                {
                    continue;
                }

                if (!map.TryGetValue(best, out List<NPCData> list))
                {
                    list = new List<NPCData>();
                    map[best] = list;
                }

                list.Add(boss);
            }

            int total = 0;
            var summary = new System.Text.StringBuilder();

            foreach (KeyValuePair<int, List<NPCData>> sector in map)
            {
                total += sector.Value.Count;
                summary.Append($" s{sector.Key + 1}(lvl {sectorLevel[sector.Key]}):");

                foreach (NPCData boss in sector.Value)
                {
                    summary.Append(' ').Append(boss.level);
                }
            }

            ServerLog.Info($"bosses: {total} placed across {map.Count} sectors -{summary}");
            return map;
        }

        private static bool IsBoss(NPCData data) =>
            !string.IsNullOrEmpty(data.name) &&
            data.name.IndexOf("BOSS", StringComparison.OrdinalIgnoreCase) >= 0;

        public static bool IsKrakenPart(NPCData data) =>
            data != null && !string.IsNullOrEmpty(data.name) &&
            (data.name.IndexOf("Kraken", StringComparison.OrdinalIgnoreCase) >= 0 ||
             data.name.IndexOf("Tentacle", StringComparison.OrdinalIgnoreCase) >= 0);

        public static bool IsBossKill(NPCData data) => data != null && IsBoss(data);

        public NPCBehaviour PlaceForEvent(string name, Vector3 at, float healthScale = 1f)
        {
            NPCData data = NamedNpc(name);
            if (data == null)
            {
                ServerLog.Warn($"no authored NPC named \"{name}\" - "
                    + "the event will be missing a part");
                return null;
            }

            Npc npc = SpawnAuthored(data, null, at, _nextIndex++, healthScale: healthScale);
            if (npc == null)
            {
                return null;
            }

            RewardPurse reward = EventReward(data, healthScale);

            npc.EventOwned = true;
            npc.Reward = reward;

            if (npc.Object != null)
            {
                var look = npc.Object.GetComponent<LookAtMovementDirection>();
                if (look != null)
                {
                    UnityEngine.Object.Destroy(look);
                }
            }

            ServerLog.Info($"{data.name} placed for the event at "
                + $"{(int)at.x},{(int)at.z} with "
                + $"{ScaledHealth(data.hull, healthScale)} hull, paying "
                + $"{reward.Credits} credits and {reward.Borax} borax");

            return npc.Behaviour;
        }

        private static RewardPurse EventReward(NPCData data, float healthScale)
        {
            int units = Mathf.CeilToInt(
                (ScaledHealth(data.hull, healthScale)
                    + ScaledHealth(data.shield, healthScale)) / 125f);

            int credits = Mathf.RoundToInt(units * 15f * 1.5f);

            return new RewardPurse(
                credits,
                Mathf.RoundToInt(credits / 2000f),
                data.experienceReward,
                data.fameReward);
        }

        private static int ScaledHealth(int authored, float scale) =>
            Mathf.RoundToInt(authored * scale);

        public int ClearEventNpcs()
        {
            int removed = 0;

            for (int i = _npcs.Count - 1; i >= 0; i--)
            {
                Npc npc = _npcs[i];
                if (!npc.EventOwned)
                {
                    continue;
                }

                if (npc.Object != null)
                {
                    try
                    {
                        ServerHub.Runner?.Despawn(npc.Object);
                    }
                    catch (Exception)
                    {

                    }
                }

                npc.Object = null;
                Forget(npc);
                removed++;
            }

            return removed;
        }

        private static NPCData NamedNpc(string name)
        {
            List<NPCData> catalogue = GameData.Data?.npcs;
            if (catalogue == null || string.IsNullOrEmpty(name))
            {
                return null;
            }

            foreach (NPCData data in catalogue)
            {
                if (data != null &&
                    string.Equals(data.name, name, StringComparison.OrdinalIgnoreCase))
                {
                    return data;
                }
            }

            string wanted = LettersAndDigits(name);

            foreach (NPCData data in catalogue)
            {
                if (data != null && string.Equals(
                        LettersAndDigits(data.name), wanted, StringComparison.OrdinalIgnoreCase))
                {
                    return data;
                }
            }

            return null;
        }

        private static string LettersAndDigits(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            var builder = new System.Text.StringBuilder(value.Length);

            foreach (char c in value)
            {
                if (char.IsLetterOrDigit(c))
                {
                    builder.Append(c);
                }
            }

            return builder.ToString();
        }

        private int _nextIndex = 1;

        public bool IsPopulated(Area area) =>
            area != null && _byArea.ContainsKey(area);

        private readonly Dictionary<Area, List<Npc>> _byArea =
            new Dictionary<Area, List<Npc>>();

        public int DespawnArea(Area area)
        {
            if (area == null || !_byArea.TryGetValue(area, out List<Npc> list))
            {
                return 0;
            }

            int removed = 0;

            foreach (Npc npc in list)
            {
                if (npc.Object != null)
                {
                    try
                    {
                        ServerHub.Runner?.Despawn(npc.Object);
                        removed++;
                    }
                    catch (Exception)
                    {

                    }
                }

                npc.Object = null;
                Forget(npc);
            }

            _byArea.Remove(area);
            return removed;
        }

        private void Forget(Npc npc)
        {
            _npcs.Remove(npc);
            _dying.Remove(npc);

            if (!ReferenceEquals(npc.Behaviour, null))
            {
                _byBehaviour.Remove(npc.Behaviour);
            }
        }

        private Npc SpawnAuthored(NPCData data, Area area, Vector3 position, int index,
            AreaNPC entry = null, float healthScale = 1f)
        {
            if (data == null || data.prefab == null)
            {
                return null;
            }

            NetworkObject netPrefab = data.prefab.GetComponent<NetworkObject>();
            if (netPrefab == null || ServerHub.Runner == null)
            {
                return null;
            }

            try
            {
                NetworkObject obj = ServerHub.Runner.Spawn(
                    netPrefab, position, Quaternion.identity);
                if (obj == null)
                {
                    return null;
                }

                var behaviour = obj.GetComponent<NPCBehaviour>();
                if (behaviour != null)
                {
                    behaviour.data = data;

                    behaviour.index = index;

                    if (area != null)
                    {
                        behaviour.areaIndex = area.areaIndex;
                        behaviour.area = area;
                        behaviour.areaNPC = entry;

                        behaviour.GetPatrolPoints();
                        behaviour.isSet = true;
                    }
                    else
                    {

                        behaviour.areaIndex = -1;
                    }
                }

                var health = obj.GetComponent<Health>();
                if (health != null)
                {

                    int hull = ScaledHealth(data.hull, healthScale);
                    int shield = ScaledHealth(data.shield, healthScale);

                    health.maxHull = hull;
                    health.hull = hull;
                    health.maxShield = shield;
                    health.shield = shield;
                }

                var npc = new Npc
                {
                    Object = obj,
                    Behaviour = behaviour,
                    Home = position,
                    Level = data.level,
                    Data = data,
                    Area = area,
                };

                Register(npc);

                if (area == null)
                {
                    return npc;
                }

                AssignPatrol(obj, position);

                if (!_byArea.TryGetValue(area, out List<Npc> list))
                {
                    list = new List<Npc>();
                    _byArea[area] = list;
                }

                list.Add(npc);
                return npc;
            }
            catch (Exception e)
            {
                ServerLog.Warn($"npc spawn failed ({data.name}): {e.Message}");
                return null;
            }
        }

        public void Populate(Vector3 centre, int count, int level, float spread = 70f)
        {
            ProjectBindings bindings = ProjectBindings.Instance;
            if (bindings == null || bindings.npcPrefabs == null || bindings.npcPrefabs.Length == 0)
            {
                ServerLog.Warn("no npcPrefabs bound - sector will stay empty");
                return;
            }

            NpcCatalogue catalogue = NpcCatalogue.Load();
            int spawned = 0;

            for (int i = 0; i < count; i++)
            {
                GameObject prefab = catalogue != null
                    ? catalogue.ForLevel(level)
                    : bindings.npcPrefabs[UnityEngine.Random.Range(0, bindings.npcPrefabs.Length)];

                if (prefab == null)
                {
                    continue;
                }

                Vector2 offset = UnityEngine.Random.insideUnitCircle * spread;
                Vector3 position = centre + new Vector3(offset.x, 0f, offset.y);

                if (Spawn(prefab, position, level) != null)
                {
                    spawned++;
                }
            }

            if (spawned == count)
            {
                ServerLog.Info($"populated sector at {centre}: {spawned} npcs (level {level})");
            }
            else
            {
                ServerLog.Warn(
                    $"sector at {centre}: only {spawned}/{count} npcs spawned (level {level}) - " +
                    "check the prefabs are in Fusion's NetworkPrefabTable");
            }
        }

        private Npc Spawn(GameObject prefab, Vector3 position, int level)
        {
            NetworkObject netPrefab = prefab.GetComponent<NetworkObject>();
            if (netPrefab == null || ServerHub.Runner == null)
            {
                return null;
            }

            try
            {
                NetworkObject obj = ServerHub.Runner.Spawn(
                    netPrefab, position, Quaternion.identity);
                if (obj == null)
                {
                    return null;
                }

                var health = obj.GetComponent<Health>();
                if (health != null)
                {

                    int hp = GameData.NpcHealth(level);
                    health.maxHull = hp;
                    health.hull = hp;
                    health.maxShield = hp / 2;
                    health.shield = hp / 2;
                }

                AssignPatrol(obj, position);

                var npc = new Npc
                {
                    Object = obj,
                    Behaviour = obj.GetComponent<NPCBehaviour>(),
                    Home = position,
                    Level = level,
                    Prefab = prefab,
                };

                Register(npc);
                return npc;
            }
            catch (Exception e)
            {
                ServerLog.Warn($"npc spawn failed: {e.Message}");
                return null;
            }
        }

        private void Register(Npc npc)
        {
            _npcs.Add(npc);

            if (!ReferenceEquals(npc.Behaviour, null))
            {
                _byBehaviour[npc.Behaviour] = npc;
            }
        }

        private const float PatrolRadius = 55f;

        private const int PatrolPoints = 4;

        private static void AssignPatrol(NetworkObject obj, Vector3 home)
        {
            var behaviour = obj.GetComponent<NPCBehaviour>();
            if (behaviour == null)
            {
                return;
            }

            if (behaviour.assignedPatrolPositions == null)
            {
                behaviour.assignedPatrolPositions = new System.Collections.Generic.List<Vector3>();
            }
            else
            {
                behaviour.assignedPatrolPositions.Clear();
            }

            for (int i = 0; i < PatrolPoints; i++)
            {
                Vector2 offset = UnityEngine.Random.insideUnitCircle * PatrolRadius;
                behaviour.assignedPatrolPositions.Add(
                    new Vector3(home.x + offset.x, home.y, home.z + offset.y));
            }
        }

        public void Tick(float deltaTime)
        {
            if (ServerHub.Runner == null || _dying.Count == 0)
            {
                return;
            }

            float now = Time.time;

            for (int i = _dying.Count - 1; i >= 0; i--)
            {
                Npc npc = _dying[i];
                if (StepDeath(npc, now))
                {
                    Forget(npc);
                    Replace(npc);
                }
            }
        }

        private bool StepDeath(Npc npc, float now)
        {
            if (!npc.Despawned)
            {
                if (now < npc.DespawnAt)
                {
                    return false;
                }

                npc.Despawned = true;

                try
                {
                    if (npc.Object != null)
                    {
                        ServerHub.Runner?.Despawn(npc.Object);
                    }
                }
                catch (Exception e)
                {
                    ServerLog.Warn($"npc despawn failed: {e.Message}");
                }

                npc.Object = null;
                return false;
            }

            return now >= npc.RespawnAt;
        }

        private void Replace(Npc npc)
        {

            if (npc.EventOwned)
            {
                return;
            }

            if (npc.Data != null && npc.Area != null)
            {
                if (SpawnAuthored(npc.Data, npc.Area, npc.Home, _nextIndex++) == null)
                {
                    ServerLog.Warn($"respawn failed for {npc.Data.name}");
                }

                return;
            }

            if (npc.Prefab != null)
            {
                Spawn(npc.Prefab, npc.Home, npc.Level);
            }
        }

        public void Died(NPCBehaviour behaviour)
        {
            if (ReferenceEquals(behaviour, null))
            {
                return;
            }

            if (!_byBehaviour.TryGetValue(behaviour, out Npc npc))
            {
                npc = new Npc
                {
                    Behaviour = behaviour,
                    Object = behaviour.Object,
                    Home = behaviour.transform.position,
                };

                _byBehaviour[behaviour] = npc;
            }

            if (npc.Dead)
            {
                return;
            }

            npc.Dead = true;

            Health health = behaviour.health;
            if (health != null && WorldLookup.HullOf(health) != 0)
            {
                health.hull = 0;
            }

            NPCData data = behaviour.data;

            float respawn = Mathf.Max(
                MinimumRespawn, data != null ? data.respawnTime : NoDataRespawn);

            npc.DespawnAt = Time.time + DeathLinger;
            npc.Despawned = false;
            npc.RespawnAt = Time.time + respawn;

            _dying.Add(npc);

            RewardService.PayOut(behaviour, npc.Reward);

            LootService.Drop(behaviour, data != null ? data.level : 1);

            ServerLog.Info(
                $"{(data != null ? data.name : "npc")} killed, respawning in {respawn:F0}s");

            try
            {
                behaviour.attackers?.Clear();
            }
            catch
            {

            }
        }

        public void Clear()
        {
            foreach (Npc npc in _npcs)
            {
                if (npc.Object != null)
                {
                    try
                    {
                        ServerHub.Runner?.Despawn(npc.Object);
                    }
                    catch
                    {

                    }
                }
            }

            _npcs.Clear();
            _byArea.Clear();
            _byBehaviour.Clear();
            _dying.Clear();
        }
    }
}
