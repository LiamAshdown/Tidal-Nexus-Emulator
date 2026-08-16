using System;
using System.Collections.Generic;
using Fusion;
using TidalNexus.StandaloneServer.Core;
using TidalNexus.StandaloneServer.Data;
using UnityEngine;

namespace TidalNexus.StandaloneServer.Services
{

    public sealed class LeaderboardService
    {
        private const int PageSize = 100;

        public enum Board
        {
            WeeklyFame,
            WeeklyKills,
            WeeklyArena,
            LifetimeFame,
            LifetimeKills,
            LifetimeArena,
            Prestige,
            Achievements,
        }

        public void SendPlayerBoard(
            PlayerRef player, Board board, Enums.ReliableData opcode, string search)
        {
            Func<Account, long> value = PlayerValue(board);
            if (value == null)
            {
                RefusedBoard(board);
                Wire.SendLeaderboard(
                    player, opcode, Array.Empty<(string, long, int)>());
                return;
            }

            var rows = new List<(Account Account, long Value)>();
            foreach (Account a in AccountStore.All)
            {
                if (Matches(a.nickname, search))
                {
                    rows.Add((a, value(a)));
                }
            }

            rows.Sort((x, y) => y.Value.CompareTo(x.Value));

            var page = new List<(string, long, int)>();
            int count = Math.Min(PageSize, rows.Count);
            for (int i = 0; i < count; i++)
            {
                (Account a, long v) = rows[i];
                page.Add((a.nickname, v, a.faction));
            }

            Wire.SendLeaderboard(player, opcode, page);
        }

        public void SendClanBoard(
            PlayerRef player, Board board, Enums.ReliableData opcode, string search)
        {
            Func<Clan, long> value = ClanValue(board);
            if (value == null)
            {
                RefusedBoard(board);
                Wire.SendClanLeaderboard(
                    player, opcode,
                    Array.Empty<(string, string, string, long, int, int)>());
                return;
            }

            var rows = new List<(Clan Clan, long Value)>();
            foreach (Clan c in AccountStore.AllClans)
            {
                if (Matches(c.name, search) || Matches(c.tag, search))
                {
                    rows.Add((c, value(c)));
                }
            }

            rows.Sort((x, y) => y.Value.CompareTo(x.Value));

            var page = new List<(string, string, string, long, int, int)>();
            int count = Math.Min(PageSize, rows.Count);
            for (int i = 0; i < count; i++)
            {
                (Clan c, long v) = rows[i];
                ClanMember leader = c.members.Find(m => m.rank == 2);

                page.Add((c.tag, c.name, leader != null ? leader.nickname : string.Empty,
                          v, c.faction, c.members.Count));
            }

            Wire.SendClanLeaderboard(player, opcode, page);
        }

        private static Func<Account, long> PlayerValue(Board board) =>
            board switch
            {
                Board.WeeklyFame => a => a.weeklyFame,
                Board.LifetimeFame => a => a.lifetimeFame,
                Board.WeeklyKills => a => a.weeklyKills,
                Board.LifetimeKills => a => a.lifetimeKills,
                Board.WeeklyArena => a => a.weeklyArena,
                Board.LifetimeArena => a => a.lifetimeArena,
                Board.Prestige => a => a.prestige,
                Board.Achievements => AchievementPoints,
                _ => null,
            };

        private static Func<Clan, long> ClanValue(Board board) =>
            board switch
            {
                Board.WeeklyFame => c => c.weeklyFame,
                Board.LifetimeFame => c => c.lifetimeFame,
                Board.WeeklyKills => c => c.weeklyKills,
                Board.LifetimeKills => c => c.lifetimeKills,
                Board.Prestige => TotalPrestige,
                Board.Achievements => TotalAchievementPoints,
                _ => null,
            };

        private static void RefusedBoard(Board board) =>
            ServerLog.Warn($"no {board} standing exists to rank; answering with an empty board");

        private static long TotalPrestige(Clan clan)
        {
            long total = 0;
            foreach (ClanMember m in clan.members)
            {
                Account a = AccountStore.Find(m.accountId);
                if (a != null)
                {
                    total += a.prestige;
                }
            }

            return total;
        }

        private static long TotalAchievementPoints(Clan clan)
        {
            long total = 0;
            foreach (ClanMember m in clan.members)
            {
                Account a = AccountStore.Find(m.accountId);
                if (a != null)
                {
                    total += AchievementPoints(a);
                }
            }

            return total;
        }

        private static long AchievementPoints(Account account) =>
            ServerHub.Achievements?.PointsOf(account) ?? 0;

        private static bool Matches(string field, string search) =>
            string.IsNullOrEmpty(search) ||
            (field != null &&
             field.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0);

        private const float BracketRebuildInterval = 1f;

        private IReadOnlyList<BracketCutoff> _brackets;
        private float _bracketsBuiltAt;

        public IReadOnlyList<BracketCutoff> Brackets()
        {
            float now = Time.realtimeSinceStartup;

            if (_brackets != null && now - _bracketsBuiltAt < BracketRebuildInterval)
            {
                return _brackets;
            }

            var population = new List<FameStanding>();
            foreach (Account a in AccountStore.All)
            {
                if (a != null)
                {
                    population.Add(new FameStanding(
                        AccountService.CoreFaction(a.faction), a.weeklyFame));
                }
            }

            _brackets = PrestigeBrackets.Table(population);
            _bracketsBuiltAt = now;
            return _brackets;
        }

        private float _rolloverClock;

        public void Tick(float deltaTime)
        {
            _rolloverClock += deltaTime;
            if (_rolloverClock < 60f)
            {
                return;
            }

            _rolloverClock = 0f;

            long lastReset = ServerState.Current.lastWeeklyResetUnix;
            long boundary = WeekStartUnix(DateTime.UtcNow);

            if (lastReset >= boundary)
            {
                return;
            }

            ResetWeekly();
            ServerState.Current.lastWeeklyResetUnix =
                DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            ServerState.Save();
        }

        private static long WeekStartUnix(DateTime now)
        {

            DateTime shifted = now.AddHours(-8);

            int daysSinceMonday = ((int)shifted.DayOfWeek + 6) % 7;

            DateTime weekStart = shifted.Date
                .AddDays(-daysSinceMonday)
                .AddHours(8);

            return new DateTimeOffset(weekStart, TimeSpan.Zero).ToUnixTimeSeconds();
        }

        public void ResetWeekly()
        {
            AwardPrestige();

            foreach (Account a in AccountStore.All)
            {
                a.weeklyFame = 0;
                a.weeklyKills = 0;
                a.weeklyArena = 0;
            }

            foreach (Clan c in AccountStore.AllClans)
            {
                c.weeklyFame = 0;
                c.weeklyKills = 0;
            }

            _brackets = null;

            AccountStore.SaveAll();
            ServerLog.Info("weekly leaderboards reset");
        }

        private static void AwardPrestige()
        {
            var byFaction = new Dictionary<int, List<Account>>();

            foreach (Account a in AccountStore.All)
            {
                if (a == null)
                {
                    continue;
                }

                if (!byFaction.TryGetValue(a.faction, out List<Account> members))
                {
                    members = new List<Account>();
                    byFaction[a.faction] = members;
                }

                members.Add(a);
            }

            int awarded = 0;

            foreach (KeyValuePair<int, List<Account>> kv in byFaction)
            {
                List<Account> ranked = kv.Value.FindAll(a => a.weeklyFame > 0);
                ranked.Sort((x, y) => y.weeklyFame.CompareTo(x.weeklyFame));
                List<long> fame = ranked.ConvertAll(a => a.weeklyFame);

                foreach (Account a in kv.Value)
                {
                    int position = BoardPosition.Of(fame, a.weeklyFame);
                    int bracket = PrestigeRanks.BracketFor(
                        position, ranked.Count, PrestigeBrackets.PercentilesPerMille);

                    int before = a.prestige;
                    a.prestige = PrestigeRanks.AfterWeeklyReset(a.prestige, bracket);
                    a.lastFamePosition = position;
                    a.lastFameBracket = bracket;
                    a.lastWeeklyFame = a.weeklyFame;
                    a.lastWeeklyKills = a.weeklyKills;

                    if (a.prestige != before)
                    {
                        awarded++;
                    }
                }
            }

            ServerLog.Info($"prestige settled for {awarded} account(s) across "
                + $"{byFaction.Count} faction(s)");
        }
    }
}
