using System;
using System.Collections.Generic;
using Fusion;
using TidalNexus.StandaloneServer.Data;
using UnityEngine;

namespace TidalNexus.StandaloneServer.Services
{

    internal sealed class KrakenMode : EventMode
    {

        private const string HeadData = "(100) [BOSS] Kraken";

        private const int TentacleCount = 6;

        private static string TentacleData(int index) =>
            $"(100) [BOSS] Tentacle {index + 1}";

        private const string HeadAnchorName = "Head";

        private const string TentacleAnchorPrefix = "Tentacle ";

        private const float TentacleRingRadius = 13f;

        private const float TentacleRingDepth = -10f;

        private EventKraken _view;

        public KrakenMode(EventService events)
            : base(events)
        {
        }

        public override EventService.Kind Kind => EventService.Kind.Kraken;

        public override string Label => "The Kraken";

        protected override int Sector => 8;

        public override GameObject Prefab(ServerPrefabs prefabs) => prefabs.eventKraken;

        public override void Bind(NetworkObject spawned)
        {
            _view = spawned.GetComponentInChildren<EventKraken>(true);
            if (_view == null)
            {
                ServerLog.Warn($"the {Kind} event has no behaviour to drive");
                return;
            }

            BindAnchors(spawned.transform);
        }

        private void BindAnchors(Transform root)
        {
            var byName = new Dictionary<string, Transform>();

            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {

                if (t != null && !byName.ContainsKey(t.name))
                {
                    byName[t.name] = t;
                }
            }

            int found = 0;

            if (_view.head == null)
            {
                _view.head = byName.TryGetValue(HeadAnchorName, out Transform head)
                    ? head
                    : Anchor(root, HeadAnchorName, Vector3.zero);
            }

            if (_view.head != null)
            {
                found++;
            }

            _view.tentacles ??= new List<Transform>();

            while (_view.tentacles.Count < TentacleCount)
            {
                _view.tentacles.Add(null);
            }

            for (int i = 0; i < TentacleCount; i++)
            {
                if (_view.tentacles[i] != null)
                {
                    found++;
                    continue;
                }

                if (byName.TryGetValue(TentacleAnchorPrefix + (i + 1), out Transform bone))
                {
                    _view.tentacles[i] = bone;
                    found++;
                    continue;
                }

                float angle = Mathf.Deg2Rad * 60f * (i + 1);
                _view.tentacles[i] = Anchor(root, TentacleAnchorPrefix + (i + 1),
                    new Vector3(
                        Mathf.Sin(angle) * TentacleRingRadius,
                        TentacleRingDepth,
                        Mathf.Cos(angle) * TentacleRingRadius));
            }

            if (found < TentacleCount + 1)
            {
                ServerLog.Warn($"the Kraken model is missing {TentacleCount + 1 - found} "
                    + "of its 7 anchor bones - those parts stand on a ring instead");
            }
        }

        private static Transform Anchor(Transform parent, string name, Vector3 localPosition)
        {
            var host = new GameObject(name);
            host.transform.SetParent(parent, false);
            host.transform.localPosition = localPosition;
            return host.transform;
        }

        protected override void SendReport(PlayerRef player,
            List<EventService.Contribution> scores) =>
            Wire.SendKrakenReport(player, scores);

        public override void PublishTimer(float timeLeft)
        {
            if (_view == null)
            {
                return;
            }

            try
            {
                _view.timeLeft = timeLeft;
            }
            catch (Exception e)
            {
                CouldNotSet("timeLeft", e);
            }
        }

        public override void Begin()
        {
            if (_view == null)
            {
                return;
            }

            try
            {
                _view.started = true;
            }
            catch (Exception e)
            {
                CouldNotSet("started", e);
            }

            Summon();
        }

        private void Summon()
        {
            NpcService npcs = ServerHub.Npcs;
            if (npcs == null)
            {
                ServerLog.Warn("no npc service - the Kraken has no body");
                return;
            }

            float scale = HullScale();
            int placed = 0;

            if (npcs.PlaceForEvent(HeadData, At(_view.head), scale) != null)
            {
                placed++;
            }

            for (int i = 0; i < TentacleCount; i++)
            {
                Transform anchor = _view.tentacles != null && i < _view.tentacles.Count
                    ? _view.tentacles[i]
                    : null;

                if (npcs.PlaceForEvent(TentacleData(i), At(anchor), scale) != null)
                {
                    placed++;
                }
            }

            if (placed == 0)
            {
                ServerLog.Warn("the Kraken has no body - there is nothing to fight");
                return;
            }

            ServerLog.Info($"kraken body: {placed}/{TentacleCount + 1} parts placed "
                + $"at tier {Tier()}");
        }

        private Vector3 At(Transform anchor) =>
            anchor != null ? anchor.position : _view.transform.position;

        private float HullScale() => (1 << (Tier() - 1)) * 0.25f;

        private int Tier()
        {
            try
            {
                return _view != null ? Mathf.Clamp(_view.tier, 1, 8) : 1;
            }
            catch (Exception)
            {
                return 1;
            }
        }

        public override void Ending()
        {
            int removed = ServerHub.Npcs?.ClearEventNpcs() ?? 0;
            if (removed > 0)
            {
                ServerLog.Info($"kraken body: {removed} parts removed");
            }
        }

        public override void NoteDamage(Account attacker, NPCBehaviour target, int amount)
        {
            if (!NpcService.IsKrakenPart(target.data))
            {
                return;
            }

            Events.Credit(attacker, damage: amount);
        }

        public override void NoteNpcKill(Account killer, NPCBehaviour target)
        {
            if (!NpcService.IsKrakenPart(target.data))
            {
                return;
            }

            ServerHub.Missions?.OnKrakenKill(killer);
        }

        public override void Advance(float deltaTime, float timeLeft)
        {
            if (_view == null || Duration <= 0f)
            {
                return;
            }

            float elapsed = Duration - timeLeft;
            int phase = Mathf.Clamp((int)(elapsed / (Duration / 3f)) + 1, 1, 3);

            try
            {
                _view.currentPhase = phase;
            }
            catch (Exception e)
            {
                CouldNotSet("currentPhase", e);
            }
        }
    }
}
