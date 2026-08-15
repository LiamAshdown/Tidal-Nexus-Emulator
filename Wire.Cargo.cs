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

        public static void SendCargo(PlayerRef player, Account account)
        {
            var cargo = new SerializableCargo { items = new List<CargoItem>() };

            List<InventoryStack> unknown = null;

            foreach (InventoryStack stack in account.cargo)
            {
                CollectableMaterialData material = GameData.Material(stack.itemId);
                if (material == null)
                {
                    (unknown ??= new List<InventoryStack>()).Add(stack);
                    continue;
                }

                cargo.items.Add(new CargoItem { index = material.index, amount = stack.count });
            }

            if (unknown != null)
            {
                foreach (InventoryStack stack in unknown)
                {
                    ServerLog.Warn($"purging unknown cargo item '{stack.itemId}'");
                    account.cargo.Remove(stack);
                }

                AccountStore.MarkDirty(account);
            }

            ReliableChannel.SendJson(player, Enums.ReliableData.CargoData, cargo);

            try
            {
                Fusion.NetworkObject obj = ServerHub.Runner != null
                    ? ServerHub.Runner.GetPlayerObject(player)
                    : null;

                var local = obj != null
                    ? obj.GetComponentInChildren<PlayerLocalValues>()
                    : null;

                if (local != null)
                {
                    local.cargoAmount = account.CargoUsed;
                }
            }
            catch (System.Exception ex)
            {
                ServerLog.Warn($"could not update the cargo readout: {ex.Message}");
            }
        }
    }
}
