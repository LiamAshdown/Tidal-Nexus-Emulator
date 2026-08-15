using System.Collections.Generic;
using UnityEngine;

namespace TidalNexus.StandaloneServer
{

    public sealed class AreaStreamer : MonoBehaviour
    {

        private const float ActivationMargin = 120f;

        private const float DeactivationExtra = 80f;

        private const float CheckInterval = 1f;

        private static readonly int[] SectorPriority =
        {
            13, 1, 8, 0, 2, 3, 4, 6, 5, 7, 9, 10, 11, 12
        };

        private readonly List<Area> _areas = new List<Area>();

        private readonly List<Area> _byPriority = new List<Area>();

        private readonly List<Player> _players = new List<Player>();
        private float _nextCheck;
        private float _scale = 1f;

        public static AreaStreamer Ensure(List<Area> areas, float scale)
        {
            foreach (AreaStreamer stale in
                Object.FindObjectsByType<AreaStreamer>(FindObjectsSortMode.None))
            {
                if (stale != null)
                {
                    Object.Destroy(stale.gameObject);
                }
            }

            var host = new GameObject("AreaStreamer (runtime)");
            Object.DontDestroyOnLoad(host);

            AreaStreamer streamer = host.AddComponent<AreaStreamer>();
            streamer._areas.AddRange(areas);
            streamer._scale = scale;
            streamer.OrderByPriority(areas);

            ServerLog.Info(
                $"area streaming on: {areas.Count} sectors, populated on demand");

            if (streamer._byPriority.Count != SectorPriority.Length)
            {
                ServerLog.Warn(
                    $"only {streamer._byPriority.Count} of {SectorPriority.Length} sectors " +
                    "have a usable boundary box - players in the rest will keep " +
                    "whatever sector they last stood in");
            }

            return streamer;
        }

        private void OrderByPriority(List<Area> areas)
        {
            foreach (int index in SectorPriority)
            {
                foreach (Area area in areas)
                {
                    if (area == null || area.areaIndex != index || !HasBoundaries(area))
                    {
                        continue;
                    }

                    _byPriority.Add(area);
                    break;
                }
            }
        }

        private static bool HasBoundaries(Area area)
        {
            if (area.boundaries == null || area.boundaries.Count == 0)
            {
                return false;
            }

            foreach (BoxCollider box in area.boundaries)
            {
                if (box == null)
                {
                    return false;
                }
            }

            return true;
        }

        private void FixedUpdate()
        {
            if (!ServerHub.Ready || ServerHub.Runner == null)
            {
                return;
            }

            if (Time.time < _nextCheck)
            {
                return;
            }

            _nextCheck = Time.time + CheckInterval;

            _players.Clear();
            foreach (Player p in Object.FindObjectsByType<Player>(FindObjectsSortMode.None))
            {
                if (p != null)
                {
                    _players.Add(p);
                }
            }

            foreach (Player p in _players)
            {
                PublishSector(p);
            }

            if (ServerHub.Npcs == null)
            {
                return;
            }

            foreach (Area area in _areas)
            {
                if (area == null)
                {
                    continue;
                }

                bool populated = ServerHub.Npcs.IsPopulated(area);
                float margin = populated
                    ? ActivationMargin + DeactivationExtra
                    : ActivationMargin;

                bool wanted = AnyPlayerNear(area, margin);

                if (wanted && !populated)
                {
                    int n = ServerHub.Npcs.PopulateArea(area, _scale);
                    ServerLog.Info($"sector {area.areaIndex + 1} entered: +{n} npcs");
                }
                else if (!wanted && populated)
                {
                    int n = ServerHub.Npcs.DespawnArea(area);
                    ServerLog.Info($"sector {area.areaIndex + 1} empty: -{n} npcs");
                }
            }
        }

        private void PublishSector(Player player)
        {
            PlayerNetworkValues values = player.networkValues;

            if (values == null || values.Object == null || !values.Object.IsValid)
            {
                return;
            }

            if (Enums.GetNetworkLayerType(values.networkLayer) !=
                Enums.NetworkLayerType.WorldMap)
            {
                return;
            }

            foreach (Area area in _byPriority)
            {
                if (area != null && area.IsInsideBoundry(player.gameObject))
                {
                    values.currentSector = area.areaIndex;
                    return;
                }
            }
        }

        private bool AnyPlayerNear(Area area, float margin)
        {
            if (area.boundaries == null || area.boundaries.Count == 0)
            {
                return false;
            }

            foreach (Player player in _players)
            {
                Vector3 at = player.transform.position;

                foreach (BoxCollider box in area.boundaries)
                {
                    if (box == null)
                    {
                        continue;
                    }

                    Bounds b = box.bounds;
                    Vector3 c = b.center;
                    Vector3 e = b.extents;

                    float dx = Mathf.Abs(at.x - c.x) - e.x;
                    float dz = Mathf.Abs(at.z - c.z) - e.z;

                    if (dx <= margin && dz <= margin)
                    {
                        return true;
                    }
                }
            }

            return false;
        }
    }
}
