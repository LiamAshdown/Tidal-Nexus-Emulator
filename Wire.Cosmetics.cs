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

        public static void SendTitles(PlayerRef player, Account account)
        {
            ReliableChannel.Send(player, Enums.ReliableData.TitlesData,
                string.Join(",", account.titles));
        }

        public static void SendDesigns(PlayerRef player, Account account)
        {
            ReliableChannel.SendJson(player, Enums.ReliableData.DesignsData,
                new PlayerLocalValues.DesignsData
                {
                    designs = string.Join(",", account.designs),
                    skins = string.Join(",", account.skins),
                    sentryDesigns = string.Join(",", account.sentryDesigns),
                    hpxDesigns = string.Join(",", account.hpxDesigns),
                    spxDesigns = string.Join(",", account.spxDesigns),
                });
        }

        public static void SendExtras(PlayerRef player, Account account)
        {
            List<ExtraData> catalogue = Services.GameData.Data?.extraDatas;
            if (catalogue == null)
            {
                return;
            }

            ServerHub.RpcFor(player)?.RPC_SendExtras(
                JsonUtility.ToJson(Slots(account, catalogue, fitted: true)),
                JsonUtility.ToJson(Slots(account, catalogue, fitted: false)));
        }

        private static ExtraData.ExtraSlotsSerializable Slots(
            Account account, List<ExtraData> catalogue, bool fitted)
        {
            var slots = new ExtraData.ExtraSlotsSerializable();

            foreach (int index in Services.ExtrasService.Listing(account, fitted))
            {
                ExtraData data = catalogue[index];

                int owned = 0;
                foreach (int held in account.extras)
                {
                    if (held == index)
                    {
                        owned++;
                    }
                }

                slots.items.Add(new ExtraData.ExtraSlotItemSerializable
                {
                    id = data.equipmentID,
                    amount = Math.Max(1, owned),
                    state = fitted && account.activeExtras.Contains((int)data.type) ? 1 : 0,
                });
            }

            return slots;
        }
    }
}
