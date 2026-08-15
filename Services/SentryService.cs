using System.Collections.Generic;
using Fusion;
using TidalNexus.StandaloneServer.Data;
using UnityEngine;

namespace TidalNexus.StandaloneServer.Services
{

    public sealed class SentryService
    {

        private const float FireInterval = 0.8f;

        private static int Damage => ServerHub.Config?.SentryDamage ?? 40;

        private static float Range => ServerHub.Config?.SentryRange ?? 20f;

        public sealed class Cadence
        {
            public float NextShot;
        }

        private float _clock;

        private readonly List<PlayerSession> _players = new List<PlayerSession>();

        public void Tick(float deltaTime)
        {
            _clock += deltaTime;

            if (ServerHub.Runner == null)
            {
                return;
            }

            ServerHub.SnapshotSessions(_players);

            foreach (PlayerSession session in _players)
            {
                Account account = session.Account;
                PlayerRef player = session.Player;

                bool active = account.activeExtras.Contains((int)ExtraData.ExtraType.Sentry);

                NetworkObject self = WorldLookup.ObjectOf(player);
                var values = self != null
                    ? self.GetComponentInChildren<PlayerNetworkValues>()
                    : null;

                if (values == null)
                {
                    continue;
                }

                if (!active)
                {
                    Clear(values);
                    continue;
                }

                NetworkObject target = Acquire(player, self);
                if (target == null)
                {
                    Clear(values);
                    continue;
                }

                try
                {
                    values.sentryCannonTarget = target.Id;
                }
                catch (System.Exception e)
                {
                    ServerLog.Warn($"could not publish sentry target: {e.Message}");
                    continue;
                }

                Cadence cadence = session.State<Cadence>();
                if (_clock < cadence.NextShot)
                {
                    continue;
                }

                cadence.NextShot = _clock + FireInterval;
                ServerHub.Combat?.ApplyDamage(self, target, Damage);
            }
        }

        private static NetworkObject Acquire(PlayerRef player, NetworkObject self)
        {
            if (self == null || ServerHub.Combat == null)
            {
                return null;
            }

            Vector3 from = self.transform.position;
            float best = Range * Range;
            NetworkObject found = null;

            foreach (Health health in Object.FindObjectsByType<Health>(
                         FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (health == null || health.hull <= 0 || health.inSafeArea)
                {
                    continue;
                }

                NetworkObject obj = health.GetComponentInParent<NetworkObject>();
                if (obj == null || CombatService.FindOwner(obj) == player)
                {
                    continue;
                }

                float distance = (health.transform.position - from).sqrMagnitude;
                if (distance >= best || !ServerHub.Combat.MayAttack(player, obj))
                {
                    continue;
                }

                best = distance;
                found = obj;
            }

            return found;
        }

        private static void Clear(PlayerNetworkValues values)
        {
            try
            {
                if (values.sentryCannonTarget.IsValid)
                {
                    values.sentryCannonTarget = default;
                }
            }
            catch (System.Exception)
            {

            }
        }
    }
}
