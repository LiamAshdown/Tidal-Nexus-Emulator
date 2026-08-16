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

            bool named = false;

            foreach (Account member in members)
            {
                bool isLeader = leader != null && member.id == leader.id;
                named |= isLeader;

                party.players.Add(new PartyMember
                {
                    id = member.id,
                    nickname = member.nickname,
                    isLeader = isLeader,
                });
            }

            if (party.players.Count > 0 && !named)
            {
                ServerLog.Warn("not sending a party whose leader is not among its members - "
                    + "the arena panel reads that leader every frame and would throw");
                return;
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

        public static readonly Vector3 NotInTheWorld = new Vector3(2000f, 2000f, 2000f);

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

                NetworkObject obj = reference != PlayerRef.None
                    ? ServerHub.Runner.GetPlayerObject(reference)
                    : null;

                payload._items.Add(new PlayerRPC.PartyPositionItem
                {
                    pos = obj != null ? obj.transform.position : NotInTheWorld,
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
