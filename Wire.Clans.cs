using System;
using System.Collections.Generic;
using System.Linq;
using Fusion;
using TidalNexus.StandaloneServer.Data;
using TidalNexus.StandaloneServer.Services;
using UnityEngine;

namespace TidalNexus.StandaloneServer
{

    public static partial class Wire
    {
        public static void SendClanData(PlayerRef player, Account account)
        {
            Clan clan = AccountStore.FindClan(account.clanTag);
            if (clan == null)
            {

                ServerLog.Warn($"clan data for {account.nickname}: no clan "
                    + $"'{account.clanTag}' in the store");
                return;
            }

            ServerLog.Info($"clan data -> {account.nickname}: [{clan.tag}] "
                + $"{clan.name}, {clan.members.Count} members");

            ReliableChannel.SendJson(player, Enums.ReliableData.ClanData, ClanDataFor(clan));
        }

        public static UIClans.ClanData ClanDataFor(Clan clan)
        {
            var data = new UIClans.ClanData
            {
                name = clan.name,
                tag = clan.tag,
                description = clan.description,
                banner = clan.banner,
                totalFame = (int)clan.lifetimeFame,
                weeklyFame = (int)clan.weeklyFame,
                totalKills = (int)clan.lifetimeKills,
                weeklyKills = (int)clan.weeklyKills,
                factionChange = clan.factionChangeVote,
                members = new List<UIClans.ClanMember>(),
            };

            foreach (Data.ClanMember member in clan.members)
            {
                Account a = AccountStore.Find(member.accountId);

                data.members.Add(new UIClans.ClanMember
                {
                    id = ClientAccountId(member.accountId),
                    name = member.nickname,
                    rank = ClientRank(member.rank),
                    isVoted = clan.factionChangeVote &&
                              clan.factionVoters.Contains(member.accountId),
                    isOnline = a != null && ServerHub.RefFor(a) != PlayerRef.None,
                    experience = a != null ? (int)Math.Min(a.experience, int.MaxValue) : 0,
                    fame = a != null ? (int)a.weeklyFame : 0,
                    totalfame = a != null ? (int)a.lifetimeFame : 0,
                    kills = a != null ? (int)a.weeklyKills : 0,
                    totalkills = a != null ? (int)a.lifetimeKills : 0,
                    prestige = a?.prestige ?? 0,
                    lastOnlineString = a != null
                        ? DateTimeOffset.FromUnixTimeSeconds(a.lastSeenUnix).UtcDateTime
                            .ToString("yyyy-MM-dd HH:mm:ss",
                                System.Globalization.CultureInfo.InvariantCulture)
                        : string.Empty,
                });
            }

            return data;
        }

        public static string ClientAccountId(string accountId)
        {
            const string prefix = "steam-";

            return !string.IsNullOrEmpty(accountId) &&
                   accountId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                ? accountId.Substring(prefix.Length)
                : accountId;
        }

        public static string ServerAccountId(string clientId)
        {
            if (string.IsNullOrEmpty(clientId))
            {
                return clientId;
            }

            return AccountStore.Find(clientId) != null ? clientId : "steam-" + clientId;
        }

        public static int ClientRank(int rank)
        {
            switch (rank)
            {
                case 2: return (int)Enums.ClanRank.Leader;
                case 1: return (int)Enums.ClanRank.Officer;
                case 0: return (int)Enums.ClanRank.Member;
                default: return (int)Enums.ClanRank.None;
            }
        }

        public static void SendClanSearch(PlayerRef player, IEnumerable<Clan> clans)
        {
            var search = new Enums.ClanSearchData { rows = new List<Enums.ClanSearchRow>() };

            foreach (Clan clan in clans)
            {
                Data.ClanMember leader = clan.members.FirstOrDefault(m => m.rank == 2);

                search.rows.Add(new Enums.ClanSearchRow
                {
                    tag = clan.tag,
                    name = clan.name,
                    faction = clan.faction,
                    count = clan.members.Count,
                    leader = leader != null ? leader.nickname : string.Empty,
                    ended = false,
                });
            }

            ReliableChannel.SendJson(player, Enums.ReliableData.ClanSearchData, search);
        }

        public static void SendClanWars(PlayerRef player, Account account)
        {
            var database = new Enums.ClanWarDatabase { clanWarList = new List<Enums.ClanWar>() };

            Clan own = AccountStore.FindClan(account.clanTag);
            if (own?.wars != null)
            {
                foreach (string enemyTag in own.wars)
                {
                    Clan enemy = AccountStore.FindClan(enemyTag);

                    database.clanWarList.Add(new Enums.ClanWar
                    {
                        clan1 = own.tag,
                        clan2 = enemyTag,
                        clan1Name = own.name,
                        clan2Name = enemy != null ? enemy.name : enemyTag,
                        clan1point = (int)Math.Min(own.weeklyKills, int.MaxValue),
                        clan2point = enemy != null
                            ? (int)Math.Min(enemy.weeklyKills, int.MaxValue)
                            : 0,
                        timer = string.Empty,
                    });
                }
            }

            ReliableChannel.SendJson(player, Enums.ReliableData.ClanWarData, database);
        }
    }
}
