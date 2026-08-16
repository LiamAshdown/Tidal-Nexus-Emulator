using Fusion;
using TidalNexus.StandaloneServer.Services;
using UnityEngine;

namespace TidalNexus.StandaloneServer
{

    public static class LootService
    {

        private const string CapsuleResource = "LootCatalogue";

        private static LootCatalogue _cached;
        private static bool _looked;

        public static void Drop(NPCBehaviour npc, int level)
        {
            if (npc == null || ServerHub.Runner == null)
            {
                return;
            }

            LootCatalogue catalogue = Load();
            if (catalogue == null)
            {
                return;
            }

            GameObject prefab = catalogue.fallback;
            if (prefab == null)
            {
                ServerLog.Warn(
                    "no wreck prefab in the LootCatalogue - run " +
                    "Tools > Standalone Server > Bind Loot Prefabs. Kills will drop nothing.");
                return;
            }

            try
            {

                Vector3 at = npc.transform.position;

                NetworkObject spawned = CollectableSpawn.At(prefab, at, IndexOf(npc.data));

                if (spawned == null)
                {
                    ServerLog.Warn(
                        $"loot drop for level {level} returned null - check {prefab.name} " +
                        "is in Fusion's NetworkPrefabTable");
                    return;
                }

                var collectable = spawned.GetComponent<CollectableObject>();
                if (collectable != null && npc.data != null)
                {
                    collectable.materials = npc.data.GetLoot();
                }

                LootOwnership.Claim(spawned.Id, TopAttacker(npc));

                int units = 0;
                if (collectable != null && collectable.materials != null)
                {
                    foreach (CollectableMaterial material in collectable.materials)
                    {
                        if (material != null)
                        {
                            units += material.amount;
                        }
                    }
                }

                ServerLog.Info(
                    $"dropped {prefab.name} at {at} (level {level}, {units} units of cargo)");
            }
            catch (System.Exception ex)
            {
                ServerLog.Warn($"could not drop loot: {ex.Message}");
            }
        }

        private static string TopAttacker(NPCBehaviour npc)
        {
            if (npc == null || npc.attackers == null)
            {
                return null;
            }

            Player best = null;
            int bestDamage = 0;

            foreach (NPCBehaviour.Attackers entry in npc.attackers)
            {
                if (entry?.player == null || entry.damage <= bestDamage)
                {
                    continue;
                }

                best = entry.player;
                bestDamage = entry.damage;
            }

            if (best == null)
            {
                return null;
            }

            PlayerRef who = Services.CombatService.FindOwner(best.Object);
            Data.Account account = ServerHub.AccountFor(who);

            return account?.id;
        }

        private static int IndexOf(NPCData data)
        {
            if (data == null || GameData.Data == null || GameData.Data.npcs == null)
            {
                return -1;
            }

            return GameData.Data.npcs.IndexOf(data);
        }

        private static LootCatalogue Load()
        {
            if (_looked)
            {
                return _cached;
            }

            _looked = true;
            _cached = Resources.Load<LootCatalogue>(CapsuleResource);

            if (_cached == null)
            {
                ServerLog.Warn(
                    $"no {CapsuleResource} in Resources - run " +
                    "Tools > Standalone Server > Bind Loot Prefabs. Kills will drop nothing.");
            }

            return _cached;
        }
    }

}
