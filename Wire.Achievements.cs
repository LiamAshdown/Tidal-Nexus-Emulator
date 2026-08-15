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

        public static void SendAchievements(
            PlayerRef player, Account account, Enums.ReliableData opcode)
        {
            var summary = new AchievementSummary
            {
                achievements = new List<PlayerAchievement>(),
                lastAchievements = string.Empty,
                achievementPoints = 0,
            };

            AchievementService achievements = ServerHub.Achievements;
            if (achievements == null)
            {
                ReliableChannel.SendJson(player, opcode, summary);
                return;
            }

            bool isSummary = opcode == Enums.ReliableData.AchievementSummary;

            foreach (AchievementData achievement in achievements.All)
            {
                AchievementData.AchievementCategory? summarises =
                    AchievementService.Summarises(achievement.category);

                if (isSummary != summarises.HasValue)
                {
                    continue;
                }

                int progress = summarises.HasValue
                    ? achievements.EarnedTiersIn(account, summarises.Value)
                    : (int)Math.Min(achievements.ProgressOf(account, achievement.index),
                        int.MaxValue);

                summary.achievements.Add(new PlayerAchievement
                {
                    i = achievement.index,
                    p = progress,
                });
            }

            summary.achievementPoints = achievements.PointsOf(account);

            if (isSummary)
            {
                summary.lastAchievements = achievements.RecentTokens(account);
            }

            ReliableChannel.SendJson(player, opcode, summary);
        }
    }
}
