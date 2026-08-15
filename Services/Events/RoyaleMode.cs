using System;
using System.Collections.Generic;
using Fusion;
using TidalNexus.StandaloneServer.Data;
using UnityEngine;

namespace TidalNexus.StandaloneServer.Services
{

    internal sealed class RoyaleMode : EventMode
    {
        private EventRoyale _view;

        public RoyaleMode(EventService events)
            : base(events)
        {
        }

        public override EventService.Kind Kind => EventService.Kind.Royale;

        public override string Label => "The battle royale";

        protected override int Sector => 13;

        public override GameObject Prefab(ServerPrefabs prefabs) => prefabs.eventRoyale;

        public override void Bind(NetworkObject spawned)
        {
            _view = spawned.GetComponentInChildren<EventRoyale>(true);
            if (_view == null)
            {
                ServerLog.Warn($"the {Kind} event has no behaviour to drive");
            }
        }

        protected override void SendReport(PlayerRef player,
            List<EventService.Contribution> scores) =>
            Wire.SendRoyaleReport(player, scores);

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

        public override bool Join(PlayerRef player, Account account)
        {
            NetworkObject obj = ServerHub.Runner?.GetPlayerObject(player);
            Player self = obj != null ? obj.GetComponentInChildren<Player>() : null;

            if (_view == null || self == null)
            {
                return false;
            }

            foreach (EventRoyale.RoyalePlayer existing in _view.playerList)
            {
                if (existing != null && existing.player == self)
                {
                    return false;
                }
            }

            _view.playerList.Add(new EventRoyale.RoyalePlayer
            {
                player = self,
                id = account.id,
            });

            PlayerLocalValues local = obj.GetComponentInChildren<PlayerLocalValues>(true);
            if (local != null)
            {
                local.royaleParticipate = true;
            }

            ServerLog.Info($"{account.nickname} joined the royale "
                + $"({_view.playerList.Count} entered)");
            return true;
        }

        public override void Ending()
        {
            try
            {
                if (_view == null)
                {
                    return;
                }

                foreach (EventRoyale.RoyalePlayer entrant in _view.playerList)
                {
                    entrant?.ReturnToWorld();
                }
            }
            catch (Exception e)
            {
                ServerLog.Warn($"could not return royale entrants to the world: {e.Message}");
            }
        }
    }
}
