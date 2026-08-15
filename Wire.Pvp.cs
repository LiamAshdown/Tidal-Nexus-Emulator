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
        public static void SendPrestige(PlayerRef player, Account account)
        {
            ReliableChannel.SendJson(player, Enums.ReliableData.PvPData,
                new UIPrestige.PrestigeData
                {
                    prestige = account.prestige,
                    kills = (int)account.weeklyKills,
                    lifetimeKills = (int)account.lifetimeKills,
                    lifetimeFame = (int)account.lifetimeFame,
                    lastFame = (int)account.weeklyFame,
                });
        }

        public static void SendArena(PlayerRef player, Account account)
        {
            ReliableChannel.SendJson(player, Enums.ReliableData.ArenaData,
                new UIArena.ArenaData
                {
                    rating = (int)account.weeklyArena,
                    ranking = 0,
                });
        }
    }
}
