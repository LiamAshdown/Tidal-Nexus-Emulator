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

        public static void SendParty(PlayerRef player, IEnumerable<Account> members, Account leader)
        {
            var party = new Party
            {
                players = new List<PartyMember>(),
                invited = new List<Player>(),
            };

            foreach (Account member in members)
            {
                party.players.Add(new PartyMember
                {
                    id = member.id,
                    nickname = member.nickname,
                    isLeader = leader != null && member.id == leader.id,
                });
            }

            ReliableChannel.SendJson(player, Enums.ReliableData.PartyData, party);
        }

        public static void SendPartyCleared(PlayerRef player)
        {
            ReliableChannel.Send(player, Enums.ReliableData.PartyData, string.Empty);
        }

        public static void SendPartyPing(PlayerRef player, string pingJson)
        {
            ReliableChannel.Send(player, Enums.ReliableData.PartyPing, pingJson);
        }

        public static void SendPartyPositions(PlayerRef player, IEnumerable<Account> members)
        {
            if (members == null || ServerHub.Runner == null)
            {
                return;
            }

            var payload = new PlayerRPC.PartyPositions
            {
                _items = new List<PlayerRPC.PartyPositionItem>(),
            };

            foreach (Account member in members)
            {
                PlayerRef reference = ServerHub.RefFor(member);
                if (reference == PlayerRef.None)
                {
                    continue;
                }

                NetworkObject obj = ServerHub.Runner.GetPlayerObject(reference);
                if (obj == null)
                {
                    continue;
                }

                payload._items.Add(new PlayerRPC.PartyPositionItem
                {
                    pos = obj.transform.position,
                    name = member.nickname,
                });
            }

            if (payload._items.Count < 2)
            {
                return;
            }

            ReliableChannel.SendJson(player, Enums.ReliableData.PartyPositions, payload);
        }
    }
}
