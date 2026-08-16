using System;
using Fusion;
using TidalNexus.StandaloneServer.Data;

namespace TidalNexus.StandaloneServer
{

    public static class WorldLookup
    {

        public static NetworkObject ObjectOf(PlayerRef player)
        {
            if (ServerHub.Runner == null)
            {
                return null;
            }

            try
            {
                return ServerHub.Runner.GetPlayerObject(player);
            }
            catch (Exception)
            {

                return null;
            }
        }

        public static NetworkObject ObjectOf(Account account)
        {
            PlayerRef player = ServerHub.RefFor(account);
            return player == PlayerRef.None ? null : ObjectOf(player);
        }

        public static Health HealthOf(NetworkObject target)
        {
            if (target == null)
            {
                return null;
            }

            Health direct = target.GetComponent<Health>();
            if (direct != null)
            {
                return direct;
            }

            Player self = target.GetComponent<Player>();
            if (self == null)
            {
                self = target.GetComponentInChildren<Player>(true);
            }

            if (self != null && self.health != null)
            {
                return self.health;
            }

            return target.GetComponentInChildren<Health>(true);
        }

        public static bool IsSpawned(Health health)
        {
            return health != null && health.Object != null && health.Object.IsValid;
        }

        public static int HullOf(Health health)
        {
            return IsSpawned(health) ? health.hull : 0;
        }

        public static int ShieldOf(Health health)
        {
            return IsSpawned(health) ? health.shield : 0;
        }

        public static bool IsAlive(Health health)
        {
            return HullOf(health) > 0;
        }

        public static Health HealthOf(PlayerRef player)
        {
            return HealthOf(ObjectOf(player));
        }

    }
}
