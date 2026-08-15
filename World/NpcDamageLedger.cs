using System.Collections.Generic;
using UnityEngine;

namespace TidalNexus.StandaloneServer
{

    public static class NpcDamageLedger
    {

        private const float AttackerTimeout = 60f;

        public static void DamagePlayer(Player player, int amount)
        {
            if (player == null || amount <= 0 || !WorldLookup.IsAlive(player.health))
            {
                return;
            }

            Health health = player.health;
            int remaining = amount;

            if (health.shield > 0)
            {
                int absorbed = Mathf.Min(health.shield, remaining);
                health.shield -= absorbed;
                remaining -= absorbed;
            }

            if (remaining > 0)
            {
                health.hull = Mathf.Max(0, WorldLookup.HullOf(health) - remaining);
            }
        }

        public static void Credit(NPCBehaviour npc, Player source, int amount)
        {
            RecordAttacker(npc, source, amount);
        }

        private static void RecordAttacker(NPCBehaviour npc, Player source, int amount)
        {
            if (npc == null || source == null)
            {
                return;
            }

            try
            {
                npc.attackers ??= new List<NPCBehaviour.Attackers>();

                foreach (NPCBehaviour.Attackers entry in npc.attackers)
                {
                    if (entry != null && entry.player == source)
                    {

                        entry.damage = (int)System.Math.Min(
                            (long)entry.damage + amount, int.MaxValue);
                        entry.timer = AttackerTimeout;
                        return;
                    }
                }

                npc.attackers.Add(new NPCBehaviour.Attackers
                {
                    player = source,
                    damage = amount,
                    timer = AttackerTimeout,
                });
            }
            catch (System.Exception ex)
            {
                ServerLog.Warn($"could not record attacker: {ex.Message}");
            }
        }

        public static void TickLedger(NPCBehaviour npc, float delta)
        {
            if (npc?.attackers == null)
            {
                return;
            }

            for (int i = npc.attackers.Count - 1; i >= 0; i--)
            {
                NPCBehaviour.Attackers entry = npc.attackers[i];

                if (entry == null || entry.player == null)
                {
                    npc.attackers.RemoveAt(i);
                    continue;
                }

                entry.timer -= delta;
                if (entry.timer <= 0f)
                {
                    npc.attackers.RemoveAt(i);
                }
            }
        }
    }
}
