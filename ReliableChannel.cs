using System;
using System.Text;
using Fusion;
using Fusion.Sockets;
using TidalNexus.StandaloneServer.Data;
using TidalNexus.StandaloneServer.Services;
using UnityEngine;

namespace TidalNexus.StandaloneServer
{

    public static class ReliableChannel
    {

        private static readonly ReliableKey Key = default(ReliableKey);

        public static void Send(PlayerRef player, Enums.ReliableData opcode)
        {
            Dispatch(player, opcode, Array.Empty<byte>());
        }

        public static void Send(PlayerRef player, Enums.ReliableData opcode, string payload)
        {
            Dispatch(player, opcode,
                string.IsNullOrEmpty(payload)
                    ? Array.Empty<byte>()
                    : Encoding.UTF8.GetBytes(payload));
        }

        public static void SendJson<T>(PlayerRef player, Enums.ReliableData opcode, T dto)
        {
            if (dto == null)
            {
                return;
            }

            Send(player, opcode, JsonUtility.ToJson(dto));
        }

        public static void SendUntil(PlayerRef player, Enums.ReliableData opcode, long unixSeconds)
        {
            Send(player, opcode,
                DateTimeOffset.FromUnixTimeSeconds(unixSeconds).UtcDateTime
                    .ToString("yyyy-MM-dd HH:mm:ss",
                        System.Globalization.CultureInfo.InvariantCulture));
        }

        public static void Broadcast(Enums.ReliableData opcode, string payload)
        {
            foreach (System.Collections.Generic.KeyValuePair<PlayerRef, Account> kv
                     in ServerHub.Online)
            {
                Send(kv.Key, opcode, payload);
            }
        }

        private static void Dispatch(PlayerRef player, Enums.ReliableData opcode, byte[] payload)
        {
            NetworkRunner runner = ServerHub.Runner;
            if (runner == null || !runner.IsRunning || player == PlayerRef.None)
            {
                return;
            }

            try
            {
                var frame = new byte[payload.Length + 1];
                frame[0] = (byte)opcode;
                if (payload.Length > 0)
                {
                    Buffer.BlockCopy(payload, 0, frame, 1, payload.Length);
                }

                runner.SendReliableDataToPlayer(player, Key, frame);
            }
            catch (Exception e)
            {
                ServerLog.Warn($"reliable send {opcode} failed: {e.Message}");
            }
        }

        public static void Receive(NetworkRunner runner, PlayerRef player, ReadOnlySpan<byte> data)
        {
            if (data.Length == 0)
            {
                return;
            }

            var opcode = (Enums.ReliableData)data[0];
            byte[] payload = data.Length > 1 ? data.Slice(1).ToArray() : Array.Empty<byte>();

            switch (opcode)
            {
                case Enums.ReliableData.CreationRequest:
                    OnCreationRequest(player, payload);
                    break;

                default:
                    ServerLog.Warn($"unexpected reliable opcode {(byte)opcode} ({opcode}) from {player}");
                    break;
            }
        }

        private static void OnCreationRequest(PlayerRef player, byte[] payload)
        {
            Account account = ServerHub.AccountFor(player);
            if (account == null)
            {
                ServerLog.Warn($"creation request from unbound player {player}");
                return;
            }

            if (!Wire.NeedsCreation(account))
            {
                ServerLog.Warn(
                    $"ignoring a creation request from {account.nickname} [{account.id}] - " +
                    "that character already exists");
                return;
            }

            UICreate.CreatingInfo info;
            try
            {
                info = SerializationUtility.DeserializeObject<UICreate.CreatingInfo>(payload);
            }
            catch (Exception e)
            {
                ServerLog.Warn($"malformed creation request from {player}: {e.Message}");
                Send(player, Enums.ReliableData.CreationFail);
                return;
            }

            if (info == null)
            {
                Send(player, Enums.ReliableData.CreationFail);
                return;
            }

            AccountService accounts = ServerHub.Accounts;
            if (accounts == null)
            {
                return;
            }

            ServerLog.Info(
                $"creation request from {player}: nickname '{info.nickname}' " +
                $"faction {info.faction}");

            string nickname = AccountService.Sanitise(info.nickname);
            if (nickname.Length < 3 || AccountStore.NicknameTaken(nickname, account.id))
            {
                ServerLog.Info($"creation rejected: nickname '{nickname}' too short or taken");
                Send(player, Enums.ReliableData.CreationFail);
                return;
            }

            if (!accounts.CanJoinFaction((int)info.faction))
            {
                ServerLog.Info($"creation rejected: faction {info.faction} is closed");
                Send(player, Enums.ReliableData.CreationFactionFail);
                return;
            }

            account.nickname = nickname;
            accounts.TryChangeFaction(account, (int)info.faction);

            PlayerDirector.SendHome(player, account);

            AccountStore.SaveNow(account);

            ServerLog.Info($"created {account.nickname} in faction {account.faction}");

            PlayerDirector.SpawnAfterCreation(player);

            PlayerDirector.Present(player, account);
        }
    }
}
