using System;
using System.Text;
using Fusion;
using TidalNexus.StandaloneServer.Data;
using UnityEngine;

namespace TidalNexus.StandaloneServer.Services
{

    public sealed class AdminService
    {
        private const long MuteSeconds = 3600;

        public const int RoleNone = 0;
        public const int RoleTester = 5;
        public const int RoleModerator = 9;
        public const int RoleAdmin = 10;

        public bool IsAdmin(Account account) => Has(account, RoleAdmin);

        public bool Has(Account account, int role) => account != null && account.Role >= role;

        private bool MayActOn(Account actor, Account target, int minimumRole)
        {
            if (!Has(actor, minimumRole) || target == null)
            {
                return false;
            }

            if (string.Equals(actor.id, target.id, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return actor.Role > target.Role;
        }

        public void Kick(Account actor, string targetId)
        {
            Account target = Resolve(targetId);
            if (!MayActOn(actor, target, RoleModerator))
            {
                return;
            }

            PlayerRef p = ServerHub.RefFor(target);
            if (p != PlayerRef.None && ServerHub.Runner != null)
            {

                Wire.SendKick(p);
                DropAfterNotice(p);
                ServerLog.Info($"{actor.nickname} kicked {target.nickname}");
            }
        }

        public void Mute(Account actor, string targetId)
        {
            Account target = Resolve(targetId);
            if (!MayActOn(actor, target, RoleModerator))
            {
                return;
            }

            target.mutedUntilUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + MuteSeconds;
            AccountStore.SaveNow(target);

            PlayerRef p = ServerHub.RefFor(target);
            if (p != PlayerRef.None)
            {
                Wire.SendMute(p, target.mutedUntilUnix);
            }

            ServerLog.Info($"{actor.nickname} muted {target.nickname}");
        }

        public void Unmute(Account actor, string targetId)
        {

            Account target = Resolve(targetId);
            if (!Has(actor, RoleModerator) || target == null)
            {
                return;
            }

            target.mutedUntilUnix = 0;
            AccountStore.SaveNow(target);
        }

        public void Ban(Account actor, string targetId)
        {
            Account target = Resolve(targetId);
            if (!MayActOn(actor, target, RoleAdmin))
            {
                return;
            }

            target.banned = true;
            target.bannedUntilUnix = Account.PermanentBan;

            AccountStore.SaveNow(target);

            PlayerRef p = ServerHub.RefFor(target);
            if (p != PlayerRef.None && ServerHub.Runner != null)
            {

                Wire.SendBan(p, target.BanExpiryUnix);
                DropAfterNotice(p);
            }

            ServerLog.Info($"{actor.nickname} banned {target.nickname}");
        }

        private const float NoticeSeconds = 3f;

        private static void DropAfterNotice(PlayerRef player)
        {
            ServerHub.DisconnectAfterFlush(player, "moderation");
        }

        public void TeleportTo(Account actor, string targetId)
        {
            if (!Has(actor, RoleModerator))
            {
                return;
            }

            Account target = Resolve(targetId);
            NetworkObject targetObj = WorldLookup.ObjectOf(target);
            NetworkObject actorObj = WorldLookup.ObjectOf(actor);

            if (targetObj == null || actorObj == null)
            {
                return;
            }

            PlayerDirector.PlaceAt(actorObj,
                targetObj.transform.position + new Vector3(3f, 0f, 0f));
        }

        public void Summon(Account actor, string targetId)
        {
            Account target = Resolve(targetId);
            if (!MayActOn(actor, target, RoleModerator))
            {
                return;
            }

            NetworkObject targetObj = WorldLookup.ObjectOf(target);
            NetworkObject actorObj = WorldLookup.ObjectOf(actor);

            if (targetObj == null || actorObj == null)
            {
                return;
            }

            PlayerDirector.PlaceAt(targetObj,
                actorObj.transform.position + new Vector3(3f, 0f, 0f));
        }

        public void TeleportToPosition(Account actor, float x, float y, float z)
        {
            if (!Has(actor, RoleModerator))
            {
                return;
            }

            NetworkObject obj = WorldLookup.ObjectOf(actor);
            if (obj != null)
            {
                PlayerDirector.PlaceAt(obj, new Vector3(x, y, z));
            }
        }

        public string Report(Account reporter, string nickname, string reason)
        {
            if (reporter == null)
            {
                return "error";
            }

            try
            {
                string line =
                    $"{DateTimeOffset.UtcNow:u}\t{Cell(reporter.nickname)}\t{Cell(nickname)}\t" +
                    $"{Cell(reason)}\n";

                System.IO.File.AppendAllText(
                    System.IO.Path.Combine(ServerPaths.DataRoot, "reports.tsv"), line);

                return "ok";
            }
            catch (Exception e)
            {
                ServerLog.Warn($"could not record report: {e.Message}");
                return "error";
            }
        }

        private static string Cell(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            var sb = new StringBuilder(value.Length);

            foreach (char c in value)
            {
                sb.Append(char.IsControl(c) ? ' ' : c);
            }

            return sb.ToString();
        }

        public bool Unban(Account actor, string targetId)
        {
            Account target = Resolve(targetId);
            if (!Has(actor, RoleAdmin) || target == null)
            {
                return false;
            }

            target.banned = false;
            target.bannedUntilUnix = 0;
            AccountStore.SaveNow(target);
            ServerLog.Info($"{actor.nickname} unbanned {target.nickname}");
            return true;
        }

        public bool SetRole(Account actor, string targetId, int role)
        {
            if (!Has(actor, RoleAdmin))
            {
                return false;
            }

            if (role != RoleNone && role != RoleTester
                && role != RoleModerator && role != RoleAdmin)
            {
                return false;
            }

            Account target = Resolve(targetId);
            if (target == null || role >= actor.Role)
            {
                return false;
            }

            if (string.Equals(actor.id, target.id, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (target.Role >= actor.Role)
            {
                return false;
            }

            target.Role = role;
            AccountStore.SaveNow(target);
            ServerLog.Info($"{actor.nickname} set {target.nickname}'s role to {role}");

            PlayerRef p = ServerHub.RefFor(target);
            if (p != PlayerRef.None)
            {
                ServerHub.Accounts?.PushState(p, target);
            }

            return true;
        }

        public void Announce(Account actor, string message)
        {
            if (!Has(actor, RoleModerator) || string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            Broadcast(message);
            ServerLog.Info($"{actor.nickname} announced: {message}");
        }

        public static void Broadcast(string message)
        {
            foreach (System.Collections.Generic.KeyValuePair<PlayerRef, Account> kv
                     in ServerHub.Online)
            {
                ServerHub.RpcFor(kv.Key)?.RPC_ReceiveChatMessage(
                    Enums.Faction.None,
                    "null",
                    "Server",
                    message,
                    ChatChannel.Server,
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    false,
                    true,
                    Enums.NetworkLayerType.WorldMap);
            }
        }

        private static Account Resolve(string idOrName)
        {
            return AccountStore.Find(idOrName) ?? AccountStore.FindByNickname(idOrName);
        }
    }
}
