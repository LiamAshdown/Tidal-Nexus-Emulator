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

        public static void SendMissionWindow(PlayerRef player, Account account)
        {
            MissionService missions = ServerHub.Missions;
            if (missions == null)
            {
                return;
            }

            ReliableChannel.SendJson(player, Enums.ReliableData.MissinWindowData,
                new PlayerRPC.MissionWindowData
                {
                    activeMissions = new MissionsResponse { missions = ActiveMissions(account) },
                    availableMissions = new MissionsResponse
                    {
                        missions = missions.Offers(account)
                            .Select(m => new Mission
                            {
                                mission_index = m.index,
                                progress = new int[Math.Max(1, ObjectiveCount(m))],
                                completed = false,
                                update = false,
                            })
                            .ToArray(),
                    },
                });
        }

        public static void SendActiveMissions(PlayerRef player, Account account)
        {
            ReliableChannel.SendJson(player, Enums.ReliableData.ActiveMissionData,
                new MissionsResponse { missions = ActiveMissions(account) });
        }

        private static Mission[] ActiveMissions(Account account)
        {
            var missions = new List<Mission>();

            foreach (ActiveMission m in account.missions)
            {
                if (MissionCatalogue.ById(m.templateId) == null)
                {
                    ServerLog.Warn($"{account.nickname} holds unknown mission "
                        + $"{m.templateId} - not sent, the client cannot resolve it");
                    continue;
                }

                missions.Add(new Mission
                {
                    mission_index = m.templateId,
                    progress = Sized(m.progress, MissionCatalogue.ById(m.templateId)),
                    completed = m.complete,
                    update = false,
                });
            }

            return missions.ToArray();
        }

        private static int[] Sized(int[] progress, MissionData mission)
        {
            int wanted = Math.Max(1, ObjectiveCount(mission));

            if (progress != null && progress.Length == wanted)
            {
                return progress;
            }

            var sized = new int[wanted];
            if (progress != null)
            {
                for (int i = 0; i < wanted && i < progress.Length; i++)
                {
                    sized[i] = progress[i];
                }
            }

            return sized;
        }

        private static int ObjectiveCount(MissionData mission)
        {
            return mission?.objectives?.Count ?? 1;
        }
    }
}
