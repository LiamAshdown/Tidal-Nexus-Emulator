using System;
using System.Collections.Generic;
using Fusion;
using TidalNexus.StandaloneServer.Core;

namespace TidalNexus.StandaloneServer
{

    public static partial class Wire
    {

        public static void SendLeaderboard(
            PlayerRef player, Enums.ReliableData opcode,
            IEnumerable<(string name, long value, int faction)> rows)
        {
            var data = new UIBrackets.LeaderboardData { rows = new List<UIBrackets.LeaderboardRow>() };
            int rank = 1;

            foreach ((string name, long value, int faction) in rows)
            {
                data.rows.Add(new UIBrackets.LeaderboardRow
                {
                    n = name,
                    v = (int)Math.Min(value, int.MaxValue),
                    f = faction,
                    r = rank++,
                });
            }

            ReliableChannel.SendJson(player, opcode, data);
        }

        public static void SendClanLeaderboard(
            PlayerRef player, Enums.ReliableData opcode,
            IEnumerable<(string tag, string name, string leader, long value, int faction, int members)> rows)
        {
            var data = new UIBrackets.ClanLeaderboardData
            {
                rows = new List<UIBrackets.ClanLeaderboardRow>(),
            };
            int rank = 1;

            foreach ((string tag, string name, string leader, long value, int faction, int members) in rows)
            {
                data.rows.Add(new UIBrackets.ClanLeaderboardRow
                {
                    t = tag,
                    n = name,
                    l = leader,
                    v = (int)Math.Min(value, int.MaxValue),
                    f = faction,
                    r = rank++,
                    p = members,
                });
            }

            ReliableChannel.SendJson(player, opcode, data);
        }

        public static void SendBrackets(PlayerRef player)
        {
            IReadOnlyList<BracketCutoff> table = ServerHub.Leaderboards?.Brackets();
            if (table == null)
            {
                return;
            }

            var data = new UIBrackets.BracketData
            {
                rows = new List<UIBrackets.BracketRow>(PrestigeBrackets.Rows),
            };

            foreach (BracketCutoff cutoff in table)
            {
                data.rows.Add(new UIBrackets.BracketRow
                {
                    n = cutoff.Nautilus,
                    s = cutoff.Serran,
                    a = cutoff.Azularis,
                });
            }

            ReliableChannel.SendJson(player, Enums.ReliableData.BracketData, data);
        }
    }
}
