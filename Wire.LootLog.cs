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

        public readonly struct LootLine
        {
            public readonly global::Log Type;
            public readonly int Amount;
            public readonly int Index;

            public LootLine(global::Log type, int amount, int index = 0)
            {
                Type = type;
                Amount = amount;
                Index = index;
            }
        }

        public static void SendLootLog(PlayerRef player, params LootLine[] lines)
        {
            if (lines == null || lines.Length == 0)
            {
                return;
            }

            var sb = new System.Text.StringBuilder("[");
            bool empty = true;

            foreach (LootLine line in lines)
            {

                if (line.Amount == 0)
                {
                    continue;
                }

                if (!empty)
                {
                    sb.Append(',');
                }

                empty = false;
                sb.Append("{\"t\":").Append((int)line.Type)
                  .Append(",\"i\":").Append(line.Index)
                  .Append(",\"a\":").Append(line.Amount)
                  .Append('}');
            }

            if (empty)
            {
                return;
            }

            ReliableChannel.Send(player, Enums.ReliableData.LootLog, sb.Append(']').ToString());
        }
    }
}
