using System;
using System.Collections.Generic;
using System.Globalization;
using Fusion;
using TidalNexus.StandaloneServer.Data;
using UnityEngine;

namespace TidalNexus.StandaloneServer.Services
{

    public sealed class CommandService
    {
        private sealed class Command
        {
            public string Name;
            public int Role;
            public string Usage;
            public string Summary;
            public Func<Account, string[], string> Run;
        }

        private readonly List<Command> _commands = new List<Command>();

        public CommandService()
        {
            Add("help", AdminService.RoleNone, "/command help",
                "lists the commands you can use", CmdHelp);

            Add("who", AdminService.RoleNone, "/command who",
                "who is online", CmdWho);

            Add("where", AdminService.RoleNone, "/command where",
                "your sector and position", CmdWhere);

            Add("played", AdminService.RoleNone, "/command played",
                "your level, credits and kills", CmdPlayed);

            Add("whois", AdminService.RoleModerator, "/command whois <name>",
                "an account's level, faction, clan and role", CmdWhois);

            Add("find", AdminService.RoleModerator, "/command find <name>",
                "where a player is", CmdFind);

            Add("mute", AdminService.RoleModerator, "/command mute <name>",
                "mutes a player for an hour", CmdMute);

            Add("unmute", AdminService.RoleModerator, "/command unmute <name>",
                "lifts a mute", CmdUnmute);

            Add("kick", AdminService.RoleModerator, "/command kick <name>",
                "disconnects a player", CmdKick);

            Add("goto", AdminService.RoleModerator, "/command goto <name>",
                "teleports you to a player", CmdGoto);

            Add("bring", AdminService.RoleModerator, "/command bring <name>",
                "teleports a player to you", CmdBring);

            Add("announce", AdminService.RoleModerator, "/command announce <message>",
                "sends a server-wide notification", CmdAnnounce);

            Add("ban", AdminService.RoleAdmin, "/command ban <name>",
                "bans an account", CmdBan);

            Add("unban", AdminService.RoleAdmin, "/command unban <name>",
                "lifts a ban", CmdUnban);

            Add("role", AdminService.RoleAdmin, "/command role <name> <none|tester|mod>",
                "sets an account's role", CmdRole);

            Add("give", AdminService.RoleAdmin,
                "/command give <name> <credits|premium|borax|honour|bp|exp> <amount>",
                "grants currency or experience", CmdGive);

            Add("setlevel", AdminService.RoleAdmin, "/command setlevel <name> <level>",
                "sets an account's level", CmdSetLevel);

            Add("heal", AdminService.RoleAdmin, "/command heal [name]",
                "restores hull and shield", CmdHeal);

            Add("tp", AdminService.RoleAdmin, "/command tp <x> <z>",
                "teleports you to a position", CmdTp);

            Add("event", AdminService.RoleAdmin,
                "/command event <beacon|kraken|royale|stop|status>",
                "starts or stops a world event", CmdEvent);
        }

        private void Add(string name, int role, string usage, string summary,
            Func<Account, string[], string> run)
        {
            _commands.Add(new Command
            {
                Name = name,
                Role = role,
                Usage = usage,
                Summary = summary,
                Run = run,
            });
        }

        public bool TryHandle(Account actor, string message)
        {
            if (actor == null || string.IsNullOrWhiteSpace(message))
            {
                return false;
            }

            string body = Strip(message);
            if (body == null)
            {
                return false;
            }

            string[] parts = body.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
            {
                Reply(actor, "Type /command help for a list.");
                return true;
            }

            Command command = _commands.Find(c =>
                string.Equals(c.Name, parts[0], StringComparison.OrdinalIgnoreCase));

            if (command == null || actor.Role < command.Role)
            {
                Reply(actor, $"Unknown command '{parts[0]}'. Type /command help.");
                return true;
            }

            string[] arguments = new string[parts.Length - 1];
            Array.Copy(parts, 1, arguments, 0, arguments.Length);

            string result;
            try
            {
                result = command.Run(actor, arguments) ?? string.Empty;
            }
            catch (Exception e)
            {
                ServerLog.Warn($"command '{command.Name}' failed: {e}");
                result = "That command failed.";
            }

            if (!string.IsNullOrEmpty(result))
            {
                Reply(actor, result);
            }

            ServerLog.Info($"{actor.nickname} ran /command {body}");
            return true;
        }

        private static string Strip(string message)
        {
            string trimmed = message.Trim();

            foreach (string prefix in new[] { "/command", "/server" })
            {
                if (!trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (trimmed.Length == prefix.Length)
                {
                    return string.Empty;
                }

                if (trimmed[prefix.Length] == ' ')
                {
                    return trimmed.Substring(prefix.Length + 1).Trim();
                }
            }

            return null;
        }

        private string CmdHelp(Account actor, string[] args)
        {
            var batch = new List<string>
            {
                $"Commands available to you ({RoleName(actor.Role)}):",
            };

            foreach (Command c in _commands)
            {
                if (actor.Role < c.Role)
                {
                    continue;
                }

                batch.Add($"  {c.Usage} - {c.Summary}");

                if (batch.Count >= 5)
                {
                    Reply(actor, string.Join("\n", batch));
                    batch.Clear();
                }
            }

            if (batch.Count > 0)
            {
                Reply(actor, string.Join("\n", batch));
            }

            return string.Empty;
        }

        private string CmdWho(Account actor, string[] args)
        {
            int count = 0;
            var names = new List<string>();

            foreach (KeyValuePair<PlayerRef, Account> kv in ServerHub.Online)
            {
                count++;
                if (names.Count < 20)
                {
                    names.Add(kv.Value.nickname);
                }
            }

            string list = names.Count == 0 ? "nobody" : string.Join(", ", names);
            return count > names.Count
                ? $"{count} online: {list} and {count - names.Count} more"
                : $"{count} online: {list}";
        }

        private string CmdWhere(Account actor, string[] args)
        {
            NetworkObject obj = WorldLookup.ObjectOf(actor);
            if (obj == null)
            {
                return "You are not in the world.";
            }

            Vector3 p = obj.transform.position;
            int sector = SectorAt(p);
            string where = sector >= 0 ? $"sector {sector + 1}" : "open water";
            return $"{where}, at {(int)p.x}, {(int)p.z}";
        }

        private string CmdPlayed(Account actor, string[] args)
        {
            return $"Level {actor.level}, {actor.credits} credits, "
                + $"{actor.premium} premium, {actor.lifetimeKills} kills, "
                + $"{actor.deaths} deaths.";
        }

        private string CmdWhois(Account actor, string[] args)
        {
            if (args.Length < 1)
            {
                return "Usage: /command whois <name>";
            }

            Account target = Find(args[0]);
            if (target == null)
            {
                return $"No account called '{args[0]}'.";
            }

            string clan = string.IsNullOrEmpty(target.clanTag) ? "no clan" : $"[{target.clanTag}]";
            string state = target.banned ? ", banned" : target.IsMuted ? ", muted" : string.Empty;

            return $"{target.nickname}: level {target.level}, faction {target.faction}, "
                + $"{clan}, {RoleName(target.Role)}{state}.";
        }

        private string CmdFind(Account actor, string[] args)
        {
            if (args.Length < 1)
            {
                return "Usage: /command find <name>";
            }

            Account target = Find(args[0]);
            NetworkObject obj = WorldLookup.ObjectOf(target);
            if (target == null)
            {
                return $"No account called '{args[0]}'.";
            }

            if (obj == null)
            {
                return $"{target.nickname} is offline.";
            }

            Vector3 p = obj.transform.position;
            int sector = SectorAt(p);
            string where = sector >= 0 ? $"sector {sector + 1}" : "open water";
            return $"{target.nickname} is in {where}, at {(int)p.x}, {(int)p.z}.";
        }

        private string CmdMute(Account actor, string[] args)
        {
            if (args.Length < 1)
            {
                return "Usage: /command mute <name>";
            }

            Account target = Find(args[0]);
            ServerHub.Admin?.Mute(actor, args[0]);

            return target != null && target.IsMuted
                ? $"{target.nickname} is muted for an hour."
                : "That player cannot be muted.";
        }

        private string CmdUnmute(Account actor, string[] args)
        {
            if (args.Length < 1)
            {
                return "Usage: /command unmute <name>";
            }

            Account target = Find(args[0]);
            ServerHub.Admin?.Unmute(actor, args[0]);

            return target != null && !target.IsMuted
                ? $"{target.nickname} is no longer muted."
                : "That player cannot be unmuted.";
        }

        private string CmdKick(Account actor, string[] args)
        {
            if (args.Length < 1)
            {
                return "Usage: /command kick <name>";
            }

            Account target = Find(args[0]);
            if (target == null)
            {
                return $"No account called '{args[0]}'.";
            }

            bool online = ServerHub.RefFor(target) != PlayerRef.None;
            ServerHub.Admin?.Kick(actor, args[0]);

            return online ? $"Kicked {target.nickname}." : $"{target.nickname} is offline.";
        }

        private string CmdGoto(Account actor, string[] args)
        {
            if (args.Length < 1)
            {
                return "Usage: /command goto <name>";
            }

            Account target = Find(args[0]);
            if (WorldLookup.ObjectOf(target) == null)
            {
                return $"'{args[0]}' is not in the world.";
            }

            ServerHub.Admin?.TeleportTo(actor, args[0]);
            return $"Teleported to {target.nickname}.";
        }

        private string CmdBring(Account actor, string[] args)
        {
            if (args.Length < 1)
            {
                return "Usage: /command bring <name>";
            }

            Account target = Find(args[0]);
            if (WorldLookup.ObjectOf(target) == null)
            {
                return $"'{args[0]}' is not in the world.";
            }

            ServerHub.Admin?.Summon(actor, args[0]);
            return $"Brought {target.nickname} to you.";
        }

        private string CmdAnnounce(Account actor, string[] args)
        {
            if (args.Length < 1)
            {
                return "Usage: /command announce <message>";
            }

            ServerHub.Admin?.Announce(actor, string.Join(" ", args));
            return string.Empty;
        }

        private string CmdBan(Account actor, string[] args)
        {
            if (args.Length < 1)
            {
                return "Usage: /command ban <name>";
            }

            Account target = Find(args[0]);
            if (target == null)
            {
                return $"No account called '{args[0]}'.";
            }

            ServerHub.Admin?.Ban(actor, args[0]);
            return target.banned ? $"Banned {target.nickname}." : "That account cannot be banned.";
        }

        private string CmdUnban(Account actor, string[] args)
        {
            if (args.Length < 1)
            {
                return "Usage: /command unban <name>";
            }

            bool ok = ServerHub.Admin != null && ServerHub.Admin.Unban(actor, args[0]);
            return ok ? $"Unbanned {args[0]}." : $"No account called '{args[0]}'.";
        }

        private string CmdRole(Account actor, string[] args)
        {
            if (args.Length < 2)
            {
                return "Usage: /command role <name> <none|tester|mod>";
            }

            int role = RoleValue(args[1]);
            if (role < 0)
            {
                return "Role must be none, tester or mod.";
            }

            bool ok = ServerHub.Admin != null && ServerHub.Admin.SetRole(actor, args[0], role);

            return ok
                ? $"{args[0]} is now {RoleName(role)}."
                : "That role change was refused.";
        }

        private string CmdGive(Account actor, string[] args)
        {
            if (args.Length < 3)
            {
                return "Usage: /command give <name> <credits|premium|borax|honour|bp|exp> <amount>";
            }

            Account target = Find(args[0]);
            if (target == null)
            {
                return $"No account called '{args[0]}'.";
            }

            if (!long.TryParse(args[2], NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out long amount))
            {
                return $"'{args[2]}' is not a number.";
            }

            switch (args[1].ToLowerInvariant())
            {
                case "credits":
                    target.credits = Clamp(target.credits + amount);
                    break;
                case "premium":
                    target.premium = Clamp(target.premium + amount);
                    break;
                case "borax":
                    target.borax = (int)Clamp(target.borax + amount, int.MaxValue);
                    break;
                case "honour":
                case "honor":
                    target.honour = (int)Clamp(target.honour + amount, int.MaxValue);
                    break;
                case "bp":
                case "battlepoints":
                    target.battlePoints = (int)Clamp(target.battlePoints + amount, int.MaxValue);
                    break;
                case "exp":
                case "experience":
                    ServerHub.Progression?.AwardExperience(target, amount);
                    break;
                default:
                    return "Give credits, premium, borax, honour, bp or exp.";
            }

            AccountStore.MarkDirty(target);
            PushTo(target);
            return $"Gave {target.nickname} {amount} {args[1].ToLowerInvariant()}.";
        }

        private string CmdSetLevel(Account actor, string[] args)
        {
            if (args.Length < 2)
            {
                return "Usage: /command setlevel <name> <level>";
            }

            Account target = Find(args[0]);
            if (target == null)
            {
                return $"No account called '{args[0]}'.";
            }

            if (!int.TryParse(args[1], NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out int level) || level < 1)
            {
                return $"'{args[1]}' is not a level.";
            }

            long floor = level <= 1 ? 0 : GameData.ExperienceForLevel(level - 1);

            target.experience = (int)Clamp(floor, int.MaxValue);
            target.level = level;

            ServerHub.Progression?.ApplyLevelStats(target);
            AccountStore.MarkDirty(target);
            PushTo(target);

            return $"{target.nickname} is now level {level}.";
        }

        private string CmdHeal(Account actor, string[] args)
        {
            Account target = args.Length >= 1 ? Find(args[0]) : actor;
            if (target == null)
            {
                return $"No account called '{args[0]}'.";
            }

            if (ServerHub.Combat == null)
            {
                return "Repair is unavailable.";
            }

            ServerHub.Combat.RepairHull(target, 1f);
            ServerHub.Combat.RepairShield(target, 1f);
            AccountStore.MarkDirty(target);
            PushTo(target);

            return $"Repaired {target.nickname}.";
        }

        private string CmdTp(Account actor, string[] args)
        {
            if (args.Length < 2)
            {
                return "Usage: /command tp <x> <z>";
            }

            if (!float.TryParse(args[0], NumberStyles.Float,
                    CultureInfo.InvariantCulture, out float x)
                || !float.TryParse(args[1], NumberStyles.Float,
                    CultureInfo.InvariantCulture, out float z))
            {
                return "X and Z must be numbers.";
            }

            NetworkObject obj = WorldLookup.ObjectOf(actor);
            if (obj == null)
            {
                return "You are not in the world.";
            }

            ServerHub.Admin?.TeleportToPosition(actor, x, obj.transform.position.y, z);
            return $"Teleported to {(int)x}, {(int)z}.";
        }

        private string CmdEvent(Account actor, string[] args)
        {
            EventService events = ServerHub.Events;
            if (events == null)
            {
                return "Events are unavailable.";
            }

            if (args.Length < 1 || string.Equals(args[0], "status",
                    StringComparison.OrdinalIgnoreCase))
            {
                return events.IsRunning
                    ? $"{events.Running} is running."
                    : "No event is running.";
            }

            if (string.Equals(args[0], "stop", StringComparison.OrdinalIgnoreCase))
            {
                return events.Stop("stopped by an admin")
                    ? "Event stopped."
                    : "No event is running.";
            }

            EventService.Kind kind = EventService.KindNamed(args[0]);
            if (kind == EventService.Kind.None)
            {
                return "Start beacon, kraken or royale - or stop.";
            }

            return events.Start(kind)
                ? $"{kind} event starting."
                : "That event could not start - one may already be running.";
        }

        private static void Reply(Account actor, string message)
        {
            PlayerRef p = ServerHub.RefFor(actor);
            if (p == PlayerRef.None)
            {
                return;
            }

            ServerHub.RpcFor(p)?.RPC_ReceiveChatMessage(
                Enums.Faction.None,
                "null",
                "Server",
                message,
                ChatChannel.Log,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                false,
                true,
                Enums.NetworkLayerType.WorldMap);
        }

        private static Account Find(string idOrName)
        {
            if (string.IsNullOrWhiteSpace(idOrName))
            {
                return null;
            }

            return AccountStore.Find(idOrName)
                ?? AccountStore.FindByNickname(idOrName)
                ?? AccountStore.Find(Wire.ServerAccountId(idOrName));
        }

        private static void PushTo(Account account)
        {
            PlayerRef p = ServerHub.RefFor(account);
            if (p != PlayerRef.None)
            {
                ServerHub.Accounts?.PushState(p, account);
            }
        }

        private static int SectorAt(Vector3 position)
        {
            Area[] areas = UnityEngine.Object.FindObjectsByType<Area>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);

            Area nearest = null;
            float best = float.MaxValue;

            foreach (Area area in areas)
            {
                if (area == null)
                {
                    continue;
                }

                float d = (area.transform.position - position).sqrMagnitude;
                if (d < best)
                {
                    best = d;
                    nearest = area;
                }
            }

            return nearest != null ? nearest.areaIndex : -1;
        }

        private static long Clamp(long value, long ceiling = long.MaxValue)
        {
            return value < 0 ? 0 : value > ceiling ? ceiling : value;
        }

        private static string RoleName(int role)
        {
            return role switch
            {
                AdminService.RoleAdmin => "Admin",
                AdminService.RoleModerator => "Moderator",
                AdminService.RoleTester => "Tester",
                _ => "Player",
            };
        }

        private static int RoleValue(string name)
        {
            return (name ?? string.Empty).ToLowerInvariant() switch
            {
                "none" or "player" => AdminService.RoleNone,
                "tester" or "internaltester" => AdminService.RoleTester,
                "mod" or "moderator" => AdminService.RoleModerator,
                _ => -1,
            };
        }
    }
}
