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
        public static void SendAdministration(PlayerRef player, IEnumerable<Account> online)
        {
            var data = new AdministrationData { data = new List<AdministrationItemData>() };

            foreach (Account a in online)
            {
                data.data.Add(new AdministrationItemData
                {
                    id = a.id,
                    nickName = a.nickname,
                    permission = (Enums.Permission)a.Role,
                });
            }

            ReliableChannel.SendJson(player, Enums.ReliableData.Administration, data);
        }

        public static void SendKick(PlayerRef player)
        {
            ReliableChannel.Send(player, Enums.ReliableData.Kick);
        }

        public static void SendBan(PlayerRef player, long untilUnix)
        {
            ReliableChannel.SendUntil(player, Enums.ReliableData.Ban, untilUnix);
        }

        public static void SendMute(PlayerRef player, long untilUnix)
        {
            ReliableChannel.SendUntil(player, Enums.ReliableData.Mute, untilUnix);
        }
    }
}
