using System;
using System.Collections.Generic;
using System.Linq;
using Fusion;
using TidalNexus.StandaloneServer.Core;
using TidalNexus.StandaloneServer.Data;
using TidalNexus.StandaloneServer.Services;
using UnityEngine;

namespace TidalNexus.StandaloneServer
{

    public static partial class Wire
    {

        public static void SendBeaconReport(PlayerRef player,
            List<Services.EventService.Contribution> scores,
            BeaconFaction winner = BeaconFaction.None)
        {
            var data = new EventBZBeacon.BeaconStats();

            foreach (Services.EventService.Contribution c in Rows(scores))
            {
                data.stats.Add(new EventBZBeacon.BeaconStat
                {
                    n = c.Name,
                    t = ClanTag(c),
                    m = ExtraData.ExtraType.None,
                    f = c.Faction,
                    d = c.Damage,
                    c = c.Captures,
                    k = c.Kills,
                    o = c.Deaths,
                    p = c.Points,
                });
            }

            data.winner = FactionField(
                data.stats.Count > 0 ? winner : BeaconFaction.None);

            ReliableChannel.SendJson(player, Enums.ReliableData.BeaconReport, data);
        }

        private static int FactionField(BeaconFaction faction) =>
            faction == BeaconFaction.None ? (int)Enums.Faction.None : (int)faction;

        public static void SendKrakenReport(PlayerRef player,
            List<Services.EventService.Contribution> scores)
        {
            var data = new EventKraken.KrakenStats();

            foreach (Services.EventService.Contribution c in Rows(scores))
            {
                data.stats.Add(new EventKraken.KrakenStat
                {
                    playerName = c.Name,
                    clanTag = ClanTag(c),
                    damage = c.Damage,
                    tank = 0,
                    deaths = c.Deaths,
                    points = c.Points,
                    faction = c.Faction,
                });
            }

            ReliableChannel.SendJson(player, Enums.ReliableData.KrakenReport, data);
        }

        public static void SendRoyaleReport(PlayerRef player,
            List<Services.EventService.Contribution> scores)
        {
            var data = new EventRoyale.RoyaleStats();

            foreach (Services.EventService.Contribution c in Rows(scores))
            {
                data.stats.Add(new EventRoyale.RoyaleStat
                {
                    playerName = c.Name,
                    clanTag = ClanTag(c),
                    faction = c.Faction,
                    rank = 0,
                    damage = c.Damage,
                    kills = c.Kills,
                    exp = c.Points,
                    collection = 0,
                    skills = 0,
                    firstKill = false,
                    firstDeath = false,
                });
            }

            ReliableChannel.SendJson(player, Enums.ReliableData.RoyaleReport, data);
        }

        private static List<Services.EventService.Contribution> Rows(
            List<Services.EventService.Contribution> scores) =>
            scores ?? new List<Services.EventService.Contribution>();

        private static string ClanTag(Services.EventService.Contribution c) =>
            string.IsNullOrEmpty(c.Clan) ? "null" : c.Clan;
    }
}
