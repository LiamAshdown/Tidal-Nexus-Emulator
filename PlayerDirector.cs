using System.Collections.Generic;
using Fusion;
using TidalNexus.StandaloneServer.Data;
using TidalNexus.StandaloneServer.Services;
using UnityEngine;

namespace TidalNexus.StandaloneServer
{

    public static class PlayerDirector
    {
        private static readonly Dictionary<PlayerRef, NetworkObject> Spawned =
            new Dictionary<PlayerRef, NetworkObject>();

        private const float PlayPlaneY = 5f;

        private const float StationOffset = 4f;

        private static int _joinCounter;

        public static int Count => Spawned.Count;

        public static void Join(NetworkRunner runner, PlayerRef player)
        {
            if (runner == null || !runner.IsServer)
            {
                return;
            }

            if (Spawned.ContainsKey(player))
            {
                ServerLog.Warn($"player {player.PlayerId} joined twice; ignoring");
                return;
            }

            ProjectBindings bindings = ProjectBindings.Instance;
            if (bindings == null || bindings.playerPrefab == null)
            {
                ServerLog.Error(
                    $"player {player.PlayerId} joined but no playerPrefab is bound - " +
                    "set it on ProjectBindings");
                return;
            }

            NetworkObject prefab = bindings.playerPrefab.GetComponent<NetworkObject>();
            if (prefab == null)
            {
                ServerLog.Error("playerPrefab has no NetworkObject component");
                return;
            }

            Data.Account account = Admit(player);
            if (account == null)
            {
                return;
            }

            if (Wire.NeedsCreation(account))
            {
                ServerLog.Info(
                    $"{account.id} needs creation (nickname '{account.nickname}', " +
                    $"faction {account.faction}) - holding the spawn until they pick");
                Wire.SendCreationPrompt(player);
                Wire.SendFactionBalance(player);
                return;
            }

            SpawnFor(runner, player, bindings, prefab);
            Present(player, account);
        }

        public static bool TeleportToBattleZone(PlayerRef player, Data.Account account)
        {
            if (account == null || account.level < BattleZoneLevel)
            {
                ServerLog.Info($"{account?.nickname} is level {account?.level}, "
                    + $"battle zone needs {BattleZoneLevel}");
                return false;
            }

            Vector3 destination = BattleZoneEntry();
            if (destination == Vector3.zero)
            {
                ServerLog.Warn("no battle zone station found");
                return false;
            }

            NetworkObject obj = WorldLookup.ObjectOf(player);
            if (obj == null)
            {
                return false;
            }

            PlaceAt(obj, destination);
            ServerLog.Info($"{account.nickname} teleported to the battle zone");
            return true;
        }

        private const int BattleZoneLevel = 60;

        internal const int BattleZoneArea = 13;

        private static Vector3 BattleZoneEntry()
        {
            AreaStation[] stations = Object.FindObjectsByType<AreaStation>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);

            foreach (AreaStation station in stations)
            {
                if (station?.data != null && station.data.areaIndex == BattleZoneArea)
                {
                    return SpawnBeside(station);
                }
            }

            return Vector3.zero;
        }

        private static Vector3 SpawnBeside(AreaStation station)
        {
            Vector3 spawn = station.transform.position
                + new Vector3(StationOffset, 0f, StationOffset);
            spawn.y = PlayPlaneY;
            return spawn;
        }

        internal static void PlaceAt(NetworkObject obj, Vector3 position)
        {
            if (obj == null)
            {
                return;
            }

            try
            {
                var cc = obj.GetComponent<NetworkCharacterController>();
                if (cc != null)
                {
                    cc.Teleport(position);
                    return;
                }

                obj.transform.position = position;
            }
            catch (System.Exception ex)
            {
                ServerLog.Warn($"could not place player at {position}: {ex.Message}");
            }
        }

        public static void SpawnAfterCreation(PlayerRef player)
        {
            NetworkRunner runner = ServerHub.Runner;
            ProjectBindings bindings = ProjectBindings.Instance;

            if (runner == null || bindings == null || bindings.playerPrefab == null)
            {
                ServerLog.Error("cannot spawn after creation - no runner or playerPrefab");
                return;
            }

            NetworkObject prefab = bindings.playerPrefab.GetComponent<NetworkObject>();
            if (prefab == null)
            {
                ServerLog.Error("playerPrefab has no NetworkObject component");
                return;
            }

            SpawnFor(runner, player, bindings, prefab);
        }

        public static void SpawnFor(
            NetworkRunner runner, PlayerRef player, ProjectBindings bindings, NetworkObject prefab)
        {
            if (Spawned.ContainsKey(player))
            {
                return;
            }

            Data.Account stored = AccountStore.Find(IdentityOf(player));

            Vector3 position = ResolveSpawn(stored, bindings, _joinCounter++, out int sector);

            try
            {
                NetworkObject spawned = runner.Spawn(
                    prefab, position, Quaternion.identity, player);

                if (spawned == null)
                {
                    ServerLog.Error($"spawn returned null for player {player.PlayerId}");
                    return;
                }

                Spawned[player] = spawned;
                runner.SetPlayerObject(player, spawned);

                var values = spawned.GetComponentInChildren<PlayerNetworkValues>();
                if (values != null)
                {
                    values.playerRef = player;

                    values.speed = stored != null
                        ? AccountService.TurbineSpeed(stored)
                        : AccountService.TurbineSpeed(new Data.Account());

                    if (sector >= 0)
                    {
                        values.currentSector = sector;
                    }
                }
                else
                {
                    ServerLog.Warn(
                        $"player {player.PlayerId} has no PlayerNetworkValues - " +
                        "they will be stuck unable to move");
                }

                SeedState(spawned, bindings);
                PlaceAt(spawned, position);

                ServerLog.Info(
                    $"player {player.PlayerId} spawned at {position} " +
                    $"({Spawned.Count} online)");
            }
            catch (System.Exception ex)
            {
                ServerLog.Error($"spawn failed for player {player.PlayerId}: {ex}");
            }
        }

        private static Data.Account Admit(PlayerRef player)
        {
            AccountService accounts = ServerHub.Accounts;
            if (accounts == null)
            {
                ServerLog.Warn("player joined before ServerHub.Boot; no account bound");
                return null;
            }

            string id = IdentityOf(player);

            if (!IsAllowed(id))
            {
                ServerLog.Warn($"refusing {id} - not on the allow list");
                Wire.SendNoServerAccess(player);

                ServerHub.DisconnectAfterFlush(player, $"{id} is not on the allow list");
                return null;
            }

            Data.Account account = accounts.Login(player, id, string.Empty);

            if (account == null)
            {

                Data.Account banned = AccountStore.Find(id);
                Wire.SendBan(player, banned?.BanExpiryUnix ?? Data.Account.PermanentBan);

                ServerHub.DisconnectAfterFlush(player, $"{id} is banned");
                return null;
            }

            return account;
        }

        private static bool IsAllowed(string id)
        {
            string list = ServerHub.Config?.AllowList;
            if (string.IsNullOrWhiteSpace(list))
            {
                return true;
            }

            foreach (string entry in list.Split(','))
            {
                if (string.Equals(entry.Trim(), id, System.StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static string IdentityOf(PlayerRef player)
        {
            try
            {
                byte[] token = ServerHub.Runner?.GetPlayerConnectionToken(player);
                if (token != null && token.Length > 0)
                {
                    string raw = System.Text.Encoding.UTF8.GetString(token);
                    int slash = raw.IndexOf('/');
                    string steamId = slash > 0 ? raw.Substring(0, slash) : raw;

                    if (!string.IsNullOrWhiteSpace(steamId))
                    {
                        return "steam-" + steamId.Trim();
                    }
                }
            }
            catch (System.Exception e)
            {
                ServerLog.Warn($"could not read connection token: {e.Message}");
            }

            ServerLog.Info($"peer {player.PlayerId} sent no identity token");
            return "peer-" + player.PlayerId;
        }

        public static void Present(PlayerRef player, Data.Account account)
        {
            AccountService accounts = ServerHub.Accounts;
            if (accounts == null || account == null)
            {
                return;
            }

            accounts.PushState(player, account);
            SeedFaction(player, account);
            Wire.SendInitialState(player, account);

            ServerHub.Social?.PushParty(account);
        }

        private static void SeedFaction(PlayerRef player, Data.Account account)
        {
            if (!Spawned.TryGetValue(player, out NetworkObject obj) || obj == null)
            {
                return;
            }

            try
            {
                var body = obj.GetComponent<Player>();
                if (body != null && body.networkValues != null)
                {
                    body.networkValues.faction = (Enums.Faction)account.faction;
                }
            }
            catch (System.Exception ex)
            {
                ServerLog.Warn($"could not publish faction for {account.nickname}: {ex.Message}");
            }
        }

        public static void Leave(NetworkRunner runner, PlayerRef player)
        {
            if (Spawned.TryGetValue(player, out NetworkObject obj))
            {

                SavePosition(player, obj);

                try
                {
                    if (obj != null && runner != null)
                    {
                        runner.Despawn(obj);
                    }
                }
                catch (System.Exception ex)
                {
                    ServerLog.Warn($"despawn failed for player {player.PlayerId}: {ex.Message}");
                }

                Spawned.Remove(player);
            }

            ServerHub.Accounts?.Logout(player);

            ServerLog.Info($"player {player.PlayerId} left ({Spawned.Count} online)");
        }

        public static void SendHome(PlayerRef player, Data.Account account)
        {
            if (account == null)
            {
                return;
            }

            Vector3 home = HomeStation(account.faction);
            if (home == Vector3.zero)
            {
                ServerLog.Warn(
                    $"no station found for faction {account.faction} - " +
                    $"{account.nickname} stays where they are");
                return;
            }

            account.x = home.x;
            account.y = home.y;
            account.z = home.z;

            if (Spawned.TryGetValue(player, out NetworkObject obj) && obj != null)
            {
                try
                {

                    PlaceAt(obj, home);
                }
                catch (System.Exception ex)
                {
                    ServerLog.Warn($"could not move {account.nickname} home: {ex.Message}");
                }
            }

            ServerLog.Info($"{account.nickname} sent home to {home}");
        }

        private static void SavePosition(PlayerRef player, NetworkObject obj)
        {
            if (obj == null)
            {
                return;
            }

            Data.Account account = ServerHub.AccountFor(player);
            if (account == null)
            {
                return;
            }

            try
            {
                Vector3 at = obj.transform.position;

                if (IsUnset(at.x, at.z))
                {
                    return;
                }

                account.x = at.x;
                account.y = at.y;
                account.z = at.z;
                AccountStore.MarkDirty(account);

                ServerLog.Info($"{account.nickname} logged out at {at}");
            }
            catch (System.Exception ex)
            {
                ServerLog.Warn($"could not save position for {account.nickname}: {ex.Message}");
            }
        }

        private static void SeedState(NetworkObject spawned, ProjectBindings bindings)
        {
            var player = spawned.GetComponent<Player>();
            if (player == null)
            {
                ServerLog.Warn("spawned object has no Player component; state not seeded");
                return;
            }

            try
            {
                if (player.localValues != null)
                {
                    player.localValues.credits = bindings.startingCredits;
                    player.localValues.borax = bindings.startingBorax;
                }
            }
            catch (System.Exception ex)
            {
                ServerLog.Warn($"could not seed currency: {ex.Message}");
            }

            try
            {
                if (player.health != null)
                {
                    if (player.health.maxHull > 0)
                    {
                        player.health.hull = player.health.maxHull;
                    }

                    if (player.health.maxShield > 0)
                    {
                        player.health.shield = player.health.maxShield;
                    }
                }
            }
            catch (System.Exception ex)
            {
                ServerLog.Warn($"could not seed health: {ex.Message}");
            }

            SeedProgression(player);
        }

        private static Vector3 ResolveSpawn(
            Account account, ProjectBindings bindings, int joinIndex, out int sector)
        {
            sector = -1;

            if (account != null && !IsUnset(account.x, account.z))
            {
                return new Vector3(account.x, account.y, account.z);
            }

            if (account != null)
            {
                AreaStation station = FactionStation(account.faction);
                if (station != null)
                {

                    Vector3 spawn = SpawnBeside(station);
                    sector = station.data.areaIndex;

                    account.x = spawn.x;
                    account.y = spawn.y;
                    account.z = spawn.z;
                    AccountStore.MarkDirty(account);

                    ServerLog.Info(
                        $"{account.nickname} is new - starting at the faction " +
                        $"{account.faction} station {spawn} in sector {sector + 1}");

                    return spawn;
                }

                ServerLog.Warn(
                    $"no station found for faction {account.faction} - falling back " +
                    "to the bound spawn points, which are outside every sector: " +
                    $"{account.nickname} will read as sector 1 until they enter one");
            }

            return bindings.NextSpawnPosition(joinIndex);
        }

        private static bool IsUnset(float x, float z)
        {
            return Mathf.Abs(x) < 0.01f && Mathf.Abs(z) < 0.01f;
        }

        public static Vector3 HomeStation(int faction)
        {
            AreaStation station = FactionStation(faction);
            return station != null ? SpawnBeside(station) : Vector3.zero;
        }

        private static AreaStation FactionStation(int faction)
        {
            AreaStation[] stations = Object.FindObjectsByType<AreaStation>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);

            AreaStation best = null;

            foreach (AreaStation station in stations)
            {
                if (station == null || station.data == null)
                {
                    continue;
                }

                if ((int)station.data.faction != faction)
                {
                    continue;
                }

                if (station.data.areaIndex == BattleZoneArea)
                {
                    continue;
                }

                if (best == null || station.data.level < best.data.level)
                {
                    best = station;
                }
            }

            return best;
        }

        private static void SeedProgression(Player player)
        {
            try
            {
                if (player.networkValues != null)
                {
                    player.networkValues.experience = 0;
                    player.networkValues.prestige = 0;

                    player.networkValues.clanBanner = "0,0,0,0,0,0,0,0";
                }
            }
            catch (System.Exception ex)
            {
                ServerLog.Warn($"could not seed progression: {ex.Message}");
            }

            try
            {
                if (player.localValues != null)
                {
                    player.localValues.cargoAmount = 0;
                    player.localValues.fame = 0;
                    player.localValues.totalFame = 0;
                    player.localValues.totalKills = 0;
                    player.localValues.repairDrones = 0;

                    player.localValues.lastArenaPos = Data.Account.NeverPlacedArena;
                }
            }
            catch (System.Exception ex)
            {
                ServerLog.Warn($"could not seed local counters: {ex.Message}");
            }

        }

        public static void Reset()
        {
            Spawned.Clear();
            _joinCounter = 0;
            ServerHub.EndAllSessions();
        }
    }
}
