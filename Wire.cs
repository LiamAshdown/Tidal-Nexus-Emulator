using System;
using Fusion;
using TidalNexus.StandaloneServer.Core;
using TidalNexus.StandaloneServer.Data;
using TidalNexus.StandaloneServer.Services;
using UnityEngine;

namespace TidalNexus.StandaloneServer
{

    public static partial class Wire
    {

        public static void SendInitialState(PlayerRef player, Account account)
        {
            if (account == null)
            {
                return;
            }

            SendCargo(player, account);
            SendTitles(player, account);
            SendDesigns(player, account);
            SendExtras(player, account);
            SendMissionWindow(player, account);
            SendFactionBalance(player);

            if (!string.IsNullOrEmpty(account.clanTag))
            {
                SendClanData(player, account);
            }
        }

        public static bool NeedsCreation(Account account)
        {
            return account == null ||
                   string.IsNullOrEmpty(account.nickname) ||
                   account.faction == AccountService.FactionNone;
        }

        public static void SendCreationPrompt(PlayerRef player)
        {
            ReliableChannel.Send(player, Enums.ReliableData.Creation);
        }

        public static void SendFactionBalance(PlayerRef player)
        {
            AccountService accounts = ServerHub.Accounts;
            if (accounts == null)
            {
                return;
            }

            FactionBalanceReport report = FactionBalance.Report(accounts.Census());

            ReliableChannel.SendJson(player, Enums.ReliableData.FactionBalance,
                new FactionBalanceData
                {
                    faction0 = report.Nautilus,
                    faction1 = report.Serran,
                    faction2 = report.Azularis,
                    totalPlayers = report.TotalPlayers,
                });
        }

        public static void SendDisconnect(PlayerRef player)
        {
            ReliableChannel.Send(player, Enums.ReliableData.Disconnect);
        }

        public static void SendNoServerAccess(PlayerRef player)
        {
            ReliableChannel.Send(player, Enums.ReliableData.NoServerAccess);
        }
    }
}
