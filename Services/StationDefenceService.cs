using System.Collections.Generic;
using Fusion;
using TidalNexus.StandaloneServer.Data;
using UnityEngine;

namespace TidalNexus.StandaloneServer.Services
{

    public sealed class StationDefenceService
    {

        private const float FireInterval = 0.8f;

        private const float RetargetInterval = 0.5f;

        private const float RescanInterval = 15f;

        private const float RangeSquared = 100f;

        private static int Damage => ServerHub.Config?.StationDamage ?? 120;

        private readonly List<CannonController> _stations = new List<CannonController>();

        private readonly Dictionary<CannonBehaviour, PlayerRef> _engaged =
            new Dictionary<CannonBehaviour, PlayerRef>();

        private float _clock;
        private float _nextShot;
        private float _nextRetarget;
        private float _nextRescan;

        public void Tick(float deltaTime)
        {
            if (ServerHub.Runner == null)
            {
                return;
            }

            _clock += deltaTime;

            if (_clock >= _nextRescan)
            {
                _nextRescan = _clock + RescanInterval;
                _stations.Clear();
                _stations.AddRange(Object.FindObjectsByType<CannonController>(
                    FindObjectsInactive.Include, FindObjectsSortMode.None));
            }

            if (_stations.Count == 0)
            {
                return;
            }

            try
            {
                if (_clock >= _nextRetarget)
                {
                    _nextRetarget = _clock + RetargetInterval;
                    Retarget();
                }

                if (_clock >= _nextShot)
                {
                    _nextShot = _clock + FireInterval;
                    Fire();
                }
            }
            catch (System.Exception e)
            {
                ServerLog.Warn($"station defence pass failed: {e.Message}");
            }
        }

        private void Retarget()
        {
            _engaged.Clear();

            foreach (CannonController station in _stations)
            {
                if (station == null)
                {
                    continue;
                }

                List<CannonBehaviour> cannons = CannonsOf(station);
                if (cannons.Count == 0)
                {
                    continue;
                }

                Vector3 centre = station.transform.position;

                foreach (KeyValuePair<PlayerRef, Account> kv in ServerHub.Online)
                {

                    if (kv.Value == null || kv.Value.faction == (int)station.faction)
                    {
                        continue;
                    }

                    NetworkObject obj = WorldLookup.ObjectOf(kv.Key);
                    if (obj == null || !obj.gameObject.activeSelf)
                    {
                        continue;
                    }

                    Vector3 where = obj.transform.position;
                    if ((where - centre).sqrMagnitude >= RangeSquared)
                    {
                        continue;
                    }

                    Health health = WorldLookup.HealthOf(obj);
                    if (!WorldLookup.IsAlive(health) || health.inSafeArea)
                    {
                        continue;
                    }

                    CannonBehaviour closest = null;
                    float best = float.MaxValue;

                    foreach (CannonBehaviour cannon in cannons)
                    {
                        if (cannon == null || _engaged.ContainsKey(cannon))
                        {
                            continue;
                        }

                        float d = (cannon.transform.position - where).sqrMagnitude;
                        if (d < best)
                        {
                            best = d;
                            closest = cannon;
                        }
                    }

                    if (closest != null)
                    {
                        _engaged[closest] = kv.Key;
                    }
                }
            }
        }

        private void Fire()
        {
            if (_engaged.Count == 0)
            {
                return;
            }

            foreach (KeyValuePair<CannonBehaviour, PlayerRef> kv in _engaged)
            {

                NetworkObject obj = WorldLookup.ObjectOf(kv.Value);
                if (obj == null)
                {
                    continue;
                }

                Health health = WorldLookup.HealthOf(obj);

                if (!WorldLookup.IsAlive(health) || health.inSafeArea)
                {
                    continue;
                }

                ServerHub.Combat?.ApplyDamage(null, health.Object, Damage);
            }
        }

        private static List<CannonBehaviour> CannonsOf(CannonController station)
        {
            if (station.cannons != null && station.cannons.Count > 0)
            {
                return station.cannons;
            }

            station.cannons =
                new List<CannonBehaviour>(station.GetComponentsInChildren<CannonBehaviour>(true));

            return station.cannons;
        }
    }
}
