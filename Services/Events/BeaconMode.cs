using System;
using System.Collections.Generic;
using Fusion;
using TidalNexus.StandaloneServer.Core;
using TidalNexus.StandaloneServer.Data;
using UnityEngine;

namespace TidalNexus.StandaloneServer.Services
{

    internal sealed class BeaconMode : EventMode
    {

        private const float CaptureRange = 5f;

        private readonly List<BZBeacon> _beacons = new List<BZBeacon>();

        private readonly HashSet<string> _participated = new HashSet<string>();

        private EventBZBeacon _view;

        public BeaconMode(EventService events)
            : base(events)
        {
        }

        public override EventService.Kind Kind => EventService.Kind.Beacon;

        public override string Label => "The beacon assault";

        protected override int Sector => 13;

        public override GameObject Prefab(ServerPrefabs prefabs) => prefabs.eventBeacon;

        public override void Bind(NetworkObject spawned)
        {
            _view = spawned.GetComponentInChildren<EventBZBeacon>(true);
            if (_view == null)
            {
                ServerLog.Warn($"the {Kind} event has no behaviour to drive");
            }

            _beacons.Clear();
            _participated.Clear();
            _beacons.AddRange(spawned.GetComponentsInChildren<BZBeacon>(true));
            if (_beacons.Count == 0)
            {
                ServerLog.Warn("the beacon event spawned with no beacons");
            }

            if (_view != null)
            {
                _view.nautilusPoints = 0f;
                _view.serranPoints = 0f;
                _view.azularisPoints = 0f;
            }
        }

        protected override void SendReport(PlayerRef player,
            List<EventService.Contribution> scores) =>
            Wire.SendBeaconReport(player, scores);

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
        }

        public override void Advance(float deltaTime, float timeLeft)
        {
            float captureRate = ServerHub.Config?.BeaconCaptureRate ?? 4f;
            float pointRate = ServerHub.Config?.BeaconPointRate ?? 1f;
            float range = CaptureRange * CaptureRange;

            foreach (BZBeacon beacon in _beacons)
            {
                if (beacon == null)
                {
                    continue;
                }

                var present = new int[3];

                var anyOf = new Account[3];

                foreach (KeyValuePair<PlayerRef, Account> kv in ServerHub.Online)
                {
                    NetworkObject obj = ServerHub.Runner.GetPlayerObject(kv.Key);
                    if (obj == null)
                    {
                        continue;
                    }

                    if ((obj.transform.position - beacon.transform.position).sqrMagnitude > range)
                    {
                        continue;
                    }

                    int faction = kv.Value.faction;
                    if (faction >= 0 && faction < 3)
                    {
                        present[faction]++;
                        anyOf[faction] = kv.Value;
                        NoteParticipation(kv.Value);
                    }
                }

                BeaconTick tick = BeaconCapture.Advance(
                    new BeaconState(beacon.capturePercentage,
                        CoreFaction(beacon.ownership),
                        CoreFaction(beacon.currentDominantFaction)),
                    new BeaconCrowd(present[0], present[1], present[2]),
                    captureRate,
                    deltaTime);

                beacon.capturePercentage = tick.Percentage;
                beacon.ownership = OwnershipOf(tick.Owner);
                beacon.currentDominantFaction = DominantOf(tick.Dominant);

                if (tick.Captured && tick.Owner != BeaconFaction.None)
                {

                    Account taker = anyOf[(int)tick.Owner];

                    Events.Credit(taker, capture: true);
                    ServerHub.Missions?.OnBeacon(taker, MissionObjectiveType.BeaconCapture);

                    AdminService.Broadcast(
                        $"A beacon has fallen to {FactionName((int)tick.Owner)}.");
                }

                if (beacon.ownership != Ownership.None)
                {
                    AddFactionPoints((int)beacon.ownership - 1, pointRate * deltaTime);
                }
            }
        }

        public override void NoteKill(Account killer, Account victim)
        {
            base.NoteKill(killer, victim);

            if (killer == null)
            {
                return;
            }

            ServerHub.Missions?.OnBeacon(killer, MissionObjectiveType.BeaconKill);
            NoteParticipation(killer);
        }

        public override void Award(Dictionary<string, EventService.Contribution> scores)
        {
            base.Award(scores);

            int winner = WinningFaction();
            if (winner < 0)
            {
                return;
            }

            foreach (KeyValuePair<string, EventService.Contribution> kv in scores)
            {
                if (kv.Value.Faction != winner)
                {
                    continue;
                }

                Account account = AccountStore.Find(kv.Key);
                if (account != null)
                {
                    ServerHub.Missions?.OnBeacon(account, MissionObjectiveType.BeaconWin);
                }
            }
        }

        private void NoteParticipation(Account account)
        {
            if (account == null || !_participated.Add(account.id))
            {
                return;
            }

            ServerHub.Missions?.OnBeacon(account, MissionObjectiveType.BeaconJoin);
        }

        private int WinningFaction()
        {
            if (_view == null)
            {
                return -1;
            }

            float[] points;

            try
            {
                points = new[]
                {
                    _view.nautilusPoints, _view.serranPoints, _view.azularisPoints,
                };
            }
            catch (Exception e)
            {
                ServerLog.Warn($"could not read the {Kind} event's scores: {e.Message}");
                return -1;
            }

            int best = -1;
            int tied = 0;

            for (int i = 0; i < points.Length; i++)
            {
                if (best < 0 || points[i] > points[best])
                {
                    best = i;
                    tied = 1;
                }
                else if (points[i] == points[best])
                {
                    tied++;
                }
            }

            return best >= 0 && tied == 1 && points[best] > 0f ? best : -1;
        }

        private static BeaconFaction CoreFaction(Ownership ownership) =>
            ownership == Ownership.None
                ? BeaconFaction.None
                : (BeaconFaction)((int)ownership - 1);

        private static BeaconFaction CoreFaction(Enums.Faction faction) =>
            faction == Enums.Faction.None
                ? BeaconFaction.None
                : (BeaconFaction)(int)faction;

        private static Ownership OwnershipOf(BeaconFaction faction) =>
            faction == BeaconFaction.None
                ? Ownership.None
                : (Ownership)((int)faction + 1);

        private static Enums.Faction DominantOf(BeaconFaction faction) =>
            faction == BeaconFaction.None
                ? Enums.Faction.None
                : (Enums.Faction)(int)faction;

        private void AddFactionPoints(int faction, float amount)
        {
            if (_view == null)
            {
                return;
            }

            switch (faction)
            {
                case 0:
                    _view.nautilusPoints += amount;
                    break;
                case 1:
                    _view.serranPoints += amount;
                    break;
                case 2:
                    _view.azularisPoints += amount;
                    break;
            }
        }

        private static string FactionName(int faction)
        {
            return faction switch
            {
                0 => "Nautilus",
                1 => "Serran",
                2 => "Azularis",
                _ => "nobody",
            };
        }
    }
}
