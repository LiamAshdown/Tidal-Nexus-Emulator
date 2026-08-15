using System.Collections.Generic;
using System.Linq;
using Fusion;
using TidalNexus.StandaloneServer.Data;
using TidalNexus.StandaloneServer.Services;
using UnityEngine;

namespace TidalNexus.StandaloneServer
{
    public static class ServerDispatch
    {

        private const float RefillPerSecond = 60f;
        private const float BurstAllowance = 180f;

        private const float ComplaintInterval = 10f;

        private const float SweepInterval = 60f;
        private const float IdleBudgetSeconds = 300f;

        private sealed class Budget
        {
            public float Tokens;
            public float Stamp;
            public float Complained;
        }

        private static readonly Dictionary<string, Budget> Budgets =
            new Dictionary<string, Budget>();

        private static float _lastSweep;

        private static readonly HashSet<string> Warned = new HashSet<string>();

        private static bool Session(RpcInfo info, out Account acc, out PlayerRef p)
        {
            p = info.Source;
            acc = null;

            if (!ServerHub.Ready)
            {
                return false;
            }

            acc = ServerHub.AccountFor(info);
            if (acc == null)
            {
                return false;
            }

            return WithinBudget(acc);
        }

        private static bool WithinBudget(Account account)
        {
            float now = Time.realtimeSinceStartup;

            if (!Budgets.TryGetValue(account.id, out Budget budget))
            {
                budget = new Budget { Tokens = BurstAllowance, Stamp = now };
                Budgets[account.id] = budget;
            }

            budget.Tokens = Mathf.Min(
                BurstAllowance, budget.Tokens + (now - budget.Stamp) * RefillPerSecond);
            budget.Stamp = now;

            Sweep(now);

            if (budget.Tokens < 1f)
            {

                if (now - budget.Complained >= ComplaintInterval)
                {
                    budget.Complained = now;
                    ServerLog.Warn($"rate limit: dropping requests from {account.nickname}");
                }

                return false;
            }

            budget.Tokens -= 1f;
            return true;
        }

        private static void Sweep(float now)
        {
            if (now - _lastSweep < SweepInterval)
            {
                return;
            }

            _lastSweep = now;

            List<string> stale = null;
            foreach (KeyValuePair<string, Budget> entry in Budgets)
            {
                if (now - entry.Value.Stamp >= IdleBudgetSeconds)
                {
                    (stale ??= new List<string>()).Add(entry.Key);
                }
            }

            if (stale == null)
            {
                return;
            }

            foreach (string id in stale)
            {
                Budgets.Remove(id);
            }
        }

        private static void NotImplemented(string name)
        {
            if (Warned.Add(name))
            {
                ServerLog.Warn("unhandled request: " + name);
            }
        }

        private enum TaskKind
        {
            Pve,
            Pvp,
            Trade
        }

        private static void GrantRandomTask(PlayerRPC self, PlayerRef p, Account acc, TaskKind kind)
        {
            MissionService missions = ServerHub.Missions;
            if (missions == null)
            {
                return;
            }

            List<MissionData> offers = kind switch
            {
                TaskKind.Pve => missions.PveOffers(acc),
                TaskKind.Pvp => missions.PvpOffers(acc),
                _ => missions.TradeOffers(acc),
            };

            if (offers == null || offers.Count == 0)
            {
                ServerLog.Warn($"no {kind} tasks available for level {acc.level}");
                Wire.SendMissionWindow(p, acc);
                return;
            }

            MissionData offer = offers[Random.Range(0, offers.Count)];
            if (!missions.TryAccept(acc, offer.index, out bool limitReached) && limitReached)
            {
                self.RPC_MissionLimitPopup();
                return;
            }

            ServerLog.Info($"{acc.nickname} took {kind} task {offer.index}");
            Wire.SendMissionWindow(p, acc);
        }

        public static void RPC_AcceptClanJoin(PlayerRPC self, RpcInfo info)
        {
            if (!Session(info, out Account acc, out PlayerRef p)) { return; }

            if (ServerHub.Social != null && ServerHub.Social.TryAcceptClanInvite(acc))
            {
                ServerHub.Accounts?.PushState(p, acc);
                Wire.SendClanData(p, acc);
            }
        }

        public static void RPC_AcceptPartyInviteRequest(PlayerRPC self, RpcInfo info)
        {
            if (!Session(info, out Account acc, out PlayerRef p)) { return; }

            ServerHub.Social?.TryAcceptPartyInvite(acc);
        }

        public static void RPC_AchievementSummaryRequest(PlayerRPC self, AchievementData.AchievementType type, RpcInfo info)
        {
            if (!Session(info, out Account acc, out PlayerRef p)) { return; }

            Wire.SendAchievements(p, acc, Enums.ReliableData.AchievementSummary);
        }

        public static void RPC_AchievementsRequest(PlayerRPC self, AchievementData.AchievementCategory category, AchievementData.AchievementType type, RpcInfo info)
        {
            if (!Session(info, out Account acc, out PlayerRef p)) { return; }

            Wire.SendAchievements(p, acc, Enums.ReliableData.AchievementData);
        }

        public static void RPC_AdminBanRequest(PlayerRPC self, string id, RpcInfo info)
        {
            if (!Session(info, out Account acc, out PlayerRef p)) { return; }

            ServerHub.Admin?.Ban(acc, id);
        }

        public static void RPC_AdminDataRequest(PlayerRPC self, RpcInfo info)
        {
            if (!Session(info, out Account acc, out PlayerRef p)) { return; }

            if (ServerHub.Admin == null || !ServerHub.Admin.IsAdmin(acc))
            {
                return;
            }

            List<Account> online = new List<Account>();
            foreach (KeyValuePair<PlayerRef, Account> entry in ServerHub.Online)
            {
                online.Add(entry.Value);
            }

            Wire.SendAdministration(p, online);
        }

        public static void RPC_AdminKickRequest(PlayerRPC self, string id, RpcInfo info)
        {
            if (!Session(info, out Account acc, out PlayerRef p)) { return; }

            ServerHub.Admin?.Kick(acc, id);
        }

        public static void RPC_AdminMapTeleportRequest(PlayerRPC self, Vector3 position, RpcInfo info)
        {
            if (!Session(info, out Account acc, out PlayerRef p)) { return; }

            ServerHub.Admin?.TeleportToPosition(acc, position.x, position.y, position.z);
        }

        public static void RPC_AdminMuteRequest(PlayerRPC self, string id, RpcInfo info)
        {
            if (!Session(info, out Account acc, out PlayerRef p)) { return; }

            ServerHub.Admin?.Mute(acc, id);
        }

        public static void RPC_AdminSummonRequest(PlayerRPC self, string id, RpcInfo info)
        {
            if (!Session(info, out Account acc, out PlayerRef p)) { return; }

            ServerHub.Admin?.Summon(acc, id);
        }

        public static void RPC_AdminTeleportRequest(PlayerRPC self, string id, RpcInfo info)
        {
            if (!Session(info, out Account acc, out PlayerRef p)) { return; }

            ServerHub.Admin?.TeleportTo(acc, id);
        }

        public static void RPC_AdminUnmuteRequest(PlayerRPC self, string id, RpcInfo info)
        {
            if (!Session(info, out Account acc, out PlayerRef p)) { return; }

            ServerHub.Admin?.Unmute(acc, id);
        }

        public static void RPC_ArenaQueueCancel(PlayerRPC self, RpcInfo info)
        {
            if (!Session(info, out Account acc, out PlayerRef p)) { return; }

            ServerHub.Pvp?.CancelArenaQueue(acc);
        }

        public static void RPC_ArenaQueueLeaveRequest(PlayerRPC self, RpcInfo info)
        {
            if (!Session(info, out Account acc, out PlayerRef p)) { return; }

            ServerHub.Pvp?.CancelArenaQueue(acc);
        }

        public static void RPC_ArenaQueueRequest(PlayerRPC self, RpcInfo info)
        {
            if (!Session(info, out Account acc, out PlayerRef p)) { return; }

            ServerHub.Pvp?.QueueForArena(acc);
        }

        public static void RPC_BuyCargo(PlayerRPC self, string items, Vector2 _pos, RpcInfo info)
        {
            if (!Session(info, out Account acc, out PlayerRef p)) { return; }

            if ((ServerHub.Economy?.BuyCargo(acc, items) ?? 0) > 0)
            {
                ServerHub.Accounts?.PushState(p, acc);
            }

            Wire.SendCargo(p, acc);
        }

        public static void RPC_ChangeAmmo(PlayerRPC self, int index, RpcInfo info)
        {
            if (!Session(info, out Account acc, out PlayerRef p)) { return; }

            acc.ammoIndex = index;
            AccountStore.MarkDirty(acc);
        }

        public static void RPC_ChangeBomb(PlayerRPC self, int index, RpcInfo info)
        {
            if (!Session(info, out Account acc, out PlayerRef p)) { return; }

            acc.bombIndex = index;
            AccountStore.MarkDirty(acc);
        }

        public static void RPC_ChangeDecoy(PlayerRPC self, int index, RpcInfo info)
        {
            if (!Session(info, out Account acc, out PlayerRef p)) { return; }

            acc.decoyIndex = index;
            AccountStore.MarkDirty(acc);
        }

        public static void RPC_ChangeFactionRequest(PlayerRPC self, int faction, bool isClan, RpcInfo info)
        {
            if (!Session(info, out Account acc, out PlayerRef p)) { return; }

            if (isClan)
            {
                NotImplemented("RPC_ChangeFactionRequest(clan vote)");
                return;
            }

            if (ServerHub.Accounts != null && ServerHub.Accounts.TryChangeFaction(acc, faction))
            {
                ServerHub.Accounts.PushState(p, acc);
                Wire.SendFactionBalance(p);
            }
            else
            {
                self.RPC_CantJoinFactionPopup();
            }
        }

        public static void RPC_ChangeMine(PlayerRPC self, int index, RpcInfo info)
        {
            if (!Session(info, out Account acc, out PlayerRef p)) { return; }

            acc.mineIndex = index;
            AccountStore.MarkDirty(acc);
        }

        public static void RPC_ChangeNicknameRequest(PlayerRPC self, string nickname, RpcInfo info)
        {
            if (!Session(info, out Account acc, out PlayerRef p)) { return; }

            if (ServerHub.Accounts != null &&
                ServerHub.Accounts.TryChangeNickname(acc, nickname, out _))
            {
                ServerHub.Accounts.PushState(p, acc);
                self.RPC_ChangeNicknameResponse(1);
            }
            else
            {
                self.RPC_ChangeNicknameResponse(0);
            }
        }

        public static void RPC_ChangeTorpedo(PlayerRPC self, int index, RpcInfo info)
        {
            if (!Session(info, out Account acc, out PlayerRef p)) { return; }

            acc.torpedoIndex = index;
            AccountStore.MarkDirty(acc);
        }

        public static void RPC_ChatChannelRefresh(PlayerRPC self, string _channel, RpcInfo info)
        {

            Session(info, out Account _, out PlayerRef _);
        }

        public static void RPC_ClanBannerChangeRequest(PlayerRPC self, string design, RpcInfo info)
        {
            if (!Session(info, out Account acc, out PlayerRef p)) { return; }

            ServerHub.Social?.SetClanBanner(acc, design);
            Wire.SendClanData(p, acc);
        }

        public static void RPC_ClanDescriptionRequest(PlayerRPC self, string text, RpcInfo info)
        {
            if (!Session(info, out Account acc, out PlayerRef p)) { return; }

            ServerHub.Social?.SetClanDescription(acc, text);
            Wire.SendClanData(p, acc);
        }

        public static void RPC_ClanFactionVoteNo(PlayerRPC self, RpcInfo info)
        {
            if (!Session(info, out Account acc, out PlayerRef p)) { return; }

            if (ServerHub.Social != null && ServerHub.Social.CastFactionVote(acc, inFavour: false))
            {
                ServerHub.Social.PushClanToMembers(AccountStore.FindClan(acc.clanTag));
            }
        }

        public static void RPC_ClanFactionVoteStart(PlayerRPC self, RpcInfo info)
        {
            if (!Session(info, out Account acc, out PlayerRef p)) { return; }

            if (ServerHub.Social != null && ServerHub.Social.TryStartFactionVote(acc))
            {
                ServerHub.Social.PushClanToMembers(AccountStore.FindClan(acc.clanTag));
            }
        }

        public static void RPC_ClanFactionVoteYes(PlayerRPC self, RpcInfo info)
        {
            if (!Session(info, out Account acc, out PlayerRef p)) { return; }

            if (ServerHub.Social != null && ServerHub.Social.CastFactionVote(acc, inFavour: true))
            {
                ServerHub.Social.PushClanToMembers(AccountStore.FindClan(acc.clanTag));
            }
        }

        public static void RPC_ClanInviteRequest(PlayerRPC self, string nickname, RpcInfo info)
        {
            if (!Session(info, out Account acc, out PlayerRef p)) { return; }

            ServerHub.Social?.TryInviteToClan(acc, nickname);
        }

        public static void RPC_ClanSearch_Request(PlayerRPC self, string search, RpcInfo info)
        {
            if (!Session(info, out Account acc, out PlayerRef p)) { return; }

            Wire.SendClanSearch(p, ServerHub.Social?.SearchClans(search) ?? Enumerable.Empty<Clan>());
        }

        public static void RPC_ClanWar_Request(PlayerRPC self, string tag, RpcInfo info)
        {
            if (!Session(info, out Account acc, out PlayerRef p)) { return; }

            self.RPC_ClanWarResponse(
                ServerHub.Social != null && ServerHub.Social.DeclareWar(acc, tag) ? 1 : 0);
        }

        public static void RPC_Collect(PlayerRPC self, NetworkId id, RpcInfo info)
        {
            if (!Session(info, out Account acc, out PlayerRef p)) { return; }

            if (ServerHub.Economy == null)
            {
                return;
            }

            if (ServerHub.Economy.Collect(acc, id, out _, out bool holdFull))
            {
                Wire.SendCargo(p, acc);
            }

            if (holdFull)
            {
                self.RPC_SendFullCargoLog();
            }
        }

        public static void RPC_DeclinePartyInviteRequest(PlayerRPC self, string inviterName, string invitedName, RpcInfo info)
        {
            if (!Session(info, out Account acc, out PlayerRef p)) { return; }

            ServerHub.Social?.DeclinePartyInvite(acc);
        }

        public static void RPC_DesignChangeRequest(PlayerRPC self, int designIndex, int skinIndex, RpcInfo info)
        {
            if (!Session(info, out Account acc, out PlayerRef p)) { return; }

            if (ServerHub.Progression != null &&
                ServerHub.Progression.TrySetDesign(acc, designIndex, skinIndex))
            {
                ServerHub.Accounts?.PushState(p, acc);
            }
        }

        public static void RPC_DiscargCargo(PlayerRPC self, string items, RpcInfo info)
        {
            if (!Session(info, out Account acc, out PlayerRef p)) { return; }

            ServerHub.Economy?.DiscardCargo(acc, items);
            Wire.SendCargo(p, acc);
        }

        public static void RPC_DropBomb(PlayerRPC self, RpcInfo info)
        {
            if (!Session(info, out Account acc, out PlayerRef p)) { return; }

            ServerHub.Ordnance?.DropBomb(acc, p);
        }

        public static void RPC_DropDecoy(PlayerRPC self, NetworkId id, RpcInfo info)
        {
            if (!Session(info, out Account acc, out PlayerRef p)) { return; }

            ServerHub.Ordnance?.DropDecoy(self, acc, p, id);
        }

        public static void RPC_DropMine(PlayerRPC self, RpcInfo info)
        {
            if (!Session(info, out Account acc, out PlayerRef p)) { return; }

            ServerHub.Ordnance?.DropMine(acc, p);
        }

        public static void RPC_DropTorpedo(PlayerRPC self, NetworkId id, RpcInfo info)
        {
            if (!Session(info, out Account acc, out PlayerRef p)) { return; }

            ServerHub.Ordnance?.DropTorpedo(self, acc, p, id);
        }

        public static void RPC_ExtraEquipRequest(PlayerRPC self, int index, RpcInfo info)
        {
            if (!Session(info, out Account acc, out PlayerRef p)) { return; }

            List<int> owned = ExtrasService.Listing(acc, fitted: false);
            if (index < 0 || index >= owned.Count)
            {
                ServerLog.Info($"equip: {acc.nickname} sent slot {index} of {owned.Count}");
                return;
            }

            if (ServerHub.Progression != null &&
                ServerHub.Progression.TryEquipExtra(acc, owned[index]))
            {
                Wire.SendExtras(p, acc);
                ServerHub.Accounts?.PushState(p, acc);
            }
        }

        public static void RPC_ExtraUnequipRequest(PlayerRPC self, int index, RpcInfo info)
        {
            if (!Session(info, out Account acc, out PlayerRef p)) { return; }

            List<int> fitted = ExtrasService.Listing(acc, fitted: true);
            if (index < 0 || index >= fitted.Count)
            {
                ServerLog.Info($"unequip: {acc.nickname} sent slot {index} of {fitted.Count}");
                return;
            }

            if (ServerHub.Progression != null &&
                ServerHub.Progression.TryUnequipExtra(acc, fitted[index]))
            {
                Wire.SendExtras(p, acc);
                ServerHub.Accounts?.PushState(p, acc);
            }
        }

        public static void RPC_FactionBalanceRequest(PlayerRPC self, RpcInfo info)
        {
            if (!Session(info, out Account acc, out PlayerRef p)) { return; }

            Wire.SendFactionBalance(p);
        }

        public static void RPC_GetCargoRequest(PlayerRPC self, RpcInfo info)
        {
            if (!Session(info, out Account acc, out PlayerRef p)) { return; }

            Wire.SendCargo(p, acc);
        }

        public static void RPC_HpxChangeRequest(PlayerRPC self, int designIndex, RpcInfo info)
        {
            if (!Session(info, out Account acc, out PlayerRef p)) { return; }

            if (ServerHub.Progression != null &&
                ServerHub.Progression.TrySetHpxDesign(acc, designIndex))
            {
                ServerHub.Accounts?.PushState(p, acc);
            }
        }

        public static void RPC_InitialDataRequest(PlayerRPC self, RpcInfo info)
        {
            if (!Session(info, out Account acc, out PlayerRef p)) { return; }

            ServerLog.Info($"initial data requested by {acc.id}");
            PlayerDirector.Present(p, acc);
        }

        public static void RPC_JoinRoyale(PlayerRPC self, RpcInfo info)
        {
            if (!Session(info, out Account acc, out PlayerRef p)) { return; }

            ServerHub.Events?.JoinRoyale(p, acc);
        }

        public static void RPC_LeaveClanRequest(PlayerRPC self, RpcInfo info)
        {
            if (!Session(info, out Account acc, out PlayerRef p)) { return; }

            ServerHub.Social?.LeaveClan(acc);
            ServerHub.Accounts?.PushState(p, acc);
            self.RPC_LeaveClanResponse();
        }

        public static void RPC_MerchantBuyRequest(PlayerRPC self, int itemID, int amount, Enums.Currency currency, RpcInfo info)
        {
            if (!Session(info, out Account acc, out PlayerRef p)) { return; }

            if (ServerHub.Economy == null)
            {
                self.RPC_MerchantBuyLimit();
                return;
            }

            EconomyService.MerchantResult result =
                ServerHub.Economy.TryMerchantBuy(acc, itemID, amount, currency);

            if (result != EconomyService.MerchantResult.Ok)
            {
                ServerLog.Info(
                    $"merchant: {acc.nickname} buy {itemID} x{amount} in {currency} refused - {result}");
                self.RPC_MerchantBuyLimit();
                return;
            }

            ServerHub.Accounts?.PushState(p, acc);
            Wire.SendExtras(p, acc);
            self.RPC_MerchantBuySuccess();
        }

        public static void RPC_PartyDisbandRequest(PlayerRPC self, RpcInfo info)
        {
            if (!Session(info, out Account acc, out PlayerRef p)) { return; }

            ServerHub.Social?.DisbandParty(acc);
        }

        public static void RPC_PartyInviteRequest(PlayerRPC self, string nickname, RpcInfo info)
        {
            if (!Session(info, out Account acc, out PlayerRef p)) { return; }

            ServerHub.Social?.TryInviteToParty(acc, nickname);
        }

        public static void RPC_PartyKickRequest(PlayerRPC self, string nickname, RpcInfo info)
        {
            if (!Session(info, out Account acc, out PlayerRef p)) { return; }

            ServerHub.Social?.TryKickFromParty(acc, nickname);
        }

        public static void RPC_PartyLeaveRequest(PlayerRPC self, RpcInfo info)
        {
            if (!Session(info, out Account acc, out PlayerRef p)) { return; }

            ServerHub.Social?.LeaveParty(acc);
        }

        public static void RPC_PartyPromoteRequest(PlayerRPC self, string nickname, RpcInfo info)
        {
            if (!Session(info, out Account acc, out PlayerRef p)) { return; }

            ServerHub.Social?.TryPromoteParty(acc, nickname);
        }

        public static void RPC_RejectClanJoin(PlayerRPC self, string inviterName, string invitedName, RpcInfo info)
        {
            if (!Session(info, out Account acc, out PlayerRef p)) { return; }

            ServerHub.Social?.DeclineClanInvite(acc);
        }

        public static void RPC_RepairHull(PlayerRPC self, float percentage, Vector2 _pos, RpcInfo info)
        {
            if (!Session(info, out Account acc, out PlayerRef p)) { return; }

            ServerHub.Combat?.RepairHull(acc, percentage);
        }

        public static void RPC_RepairShield(PlayerRPC self, float percentage, Vector2 _pos, RpcInfo info)
        {
            if (!Session(info, out Account acc, out PlayerRef p)) { return; }

            ServerHub.Combat?.RepairShield(acc, percentage);
        }

        public static void RPC_ReportPlayer(PlayerRPC self, string nickname, string reason, RpcInfo info)
        {
            if (!Session(info, out Account acc, out PlayerRef p)) { return; }

            self.RPC_ReportPlayerResponse(ServerHub.Admin?.Report(acc, nickname, reason) ?? "error");
        }

        public static void RPC_RequestAcceptMission(PlayerRPC self, int missionIndex, RpcInfo info)
        {
            if (!Session(info, out Account acc, out PlayerRef p)) { return; }

            if (ServerHub.Missions != null &&
                !ServerHub.Missions.TryAccept(acc, missionIndex, out bool limitReached) &&
                limitReached)
            {
                self.RPC_MissionLimitPopup();
                return;
            }

            Wire.SendMissionWindow(p, acc);
        }

        public static void RPC_RequestBeaconReport(PlayerRPC self, RpcInfo info)
        {
            if (!Session(info, out Account acc, out PlayerRef p)) { return; }

            if (ServerHub.Events != null)
            {
                Wire.SendBeaconReport(p, ServerHub.Events.ReportFor(EventService.Kind.Beacon));
            }
        }

        public static void RPC_RequestBorax(PlayerRPC self, int amount, RpcInfo info)
        {
            if (!Session(info, out Account acc, out PlayerRef p)) { return; }

            ServerHub.Economy?.GrantBorax(acc, amount);
            ServerHub.Accounts?.PushState(p, acc);
        }

        public static void RPC_RequestCancelMission(PlayerRPC self, int missionIndex, RpcInfo info)
        {
            if (!Session(info, out Account acc, out PlayerRef p)) { return; }

            ServerHub.Missions?.TryCancel(acc, missionIndex);
            Wire.SendMissionWindow(p, acc);
        }

        public static void RPC_RequestClanKick(PlayerRPC self, string kickedId, RpcInfo info)
        {
            if (!Session(info, out Account acc, out PlayerRef p)) { return; }

            if (ServerHub.Social != null &&
                ServerHub.Social.TryKickFromClan(acc, Wire.ServerAccountId(kickedId)))
            {
                self.RPC_SuccessClanKick();
                Wire.SendClanData(p, acc);
            }
            else
            {
                self.RPC_FailClanKick();
            }
        }

        public static void RPC_RequestClanPromote(PlayerRPC self, string promotedId, int promotionRank, RpcInfo info)
        {
            if (!Session(info, out Account acc, out PlayerRef p)) { return; }

            if (ServerHub.Social != null &&
                ServerHub.Social.TryPromote(acc, Wire.ServerAccountId(promotedId), promotionRank))
            {
                ServerHub.Social.PushClanToMembers(AccountStore.FindClan(acc.clanTag));
            }
        }

        public static void RPC_RequestCompleteMission(PlayerRPC self, int missionIndex, RpcInfo info)
        {
            if (!Session(info, out Account acc, out PlayerRef p)) { return; }

            ServerHub.Missions?.TryComplete(acc, missionIndex);
            ServerHub.Accounts?.PushState(p, acc);
            Wire.SendMissionWindow(p, acc);
        }

        public static void RPC_RequestDesignsRefresh(PlayerRPC self, RpcInfo info)
        {
            if (!Session(info, out Account acc, out PlayerRef p)) { return; }

            Wire.SendDesigns(p, acc);
        }

        public static void RPC_RequestFounderPack(PlayerRPC self, int tier, RpcInfo info)
        {
            if (!Session(info, out Account acc, out PlayerRef p)) { return; }

            ServerHub.Economy?.GrantFounderPack(acc);
            ServerHub.Accounts?.PushState(p, acc);
            Wire.SendTitles(p, acc);
            self.RPC_PurchaseSuccess();
        }

        public static void RPC_RequestKrakenReport(PlayerRPC self, RpcInfo info)
        {
            if (!Session(info, out Account acc, out PlayerRef p)) { return; }

            if (ServerHub.Events != null)
            {
                Wire.SendKrakenReport(p, ServerHub.Events.ReportFor(EventService.Kind.Kraken));
            }
        }

        public static void RPC_RequestMissionWindow(PlayerRPC self, RpcInfo info)
        {
            if (!Session(info, out Account acc, out PlayerRef p)) { return; }

            Wire.SendMissionWindow(p, acc);
        }

        public static void RPC_RequestPvPFlag_Off(PlayerRPC self, RpcInfo info)
        {
            if (!Session(info, out Account acc, out PlayerRef p)) { return; }

            ServerHub.Pvp?.FlagOff(acc);
        }

        public static void RPC_RequestPvPFlag_On(PlayerRPC self, RpcInfo info)
        {
            if (!Session(info, out Account acc, out PlayerRef p)) { return; }

            ServerHub.Pvp?.FlagOn(acc);
        }

        public static void RPC_RequestPveTask(PlayerRPC self, RpcInfo info)
        {
            if (!Session(info, out Account acc, out PlayerRef p)) { return; }

            GrantRandomTask(self, p, acc, TaskKind.Pve);
        }

        public static void RPC_RequestPvpTask(PlayerRPC self, RpcInfo info)
        {
            if (!Session(info, out Account acc, out PlayerRef p)) { return; }

            GrantRandomTask(self, p, acc, TaskKind.Pvp);
        }

        public static void RPC_RequestRoyaleReport(PlayerRPC self, RpcInfo info)
        {
            if (!Session(info, out Account acc, out PlayerRef p)) { return; }

            if (ServerHub.Events != null)
            {
                Wire.SendRoyaleReport(p, ServerHub.Events.ReportFor(EventService.Kind.Royale));
            }
        }

        public static void RPC_RequestTitlesRefresh(PlayerRPC self, RpcInfo info)
        {
            if (!Session(info, out Account acc, out PlayerRef p)) { return; }

            Wire.SendTitles(p, acc);
        }

        public static void RPC_RequestTradeTask(PlayerRPC self, RpcInfo info)
        {
            if (!Session(info, out Account acc, out PlayerRef p)) { return; }

            GrantRandomTask(self, p, acc, TaskKind.Trade);
        }

        public static void RPC_RequestTranslation(PlayerRPC self, string message, int messageIndex, string language, RpcInfo info)
        {
            if (!Session(info, out Account acc, out PlayerRef p)) { return; }

            self.RPC_RequestTranslationResponse(message, messageIndex);
        }

        public static void RPC_RequestVip(PlayerRPC self, RpcInfo info)
        {
            if (!Session(info, out Account acc, out PlayerRef p)) { return; }

            ServerHub.Economy?.GrantVip(acc, 30);
            ServerHub.Accounts?.PushState(p, acc);
            self.RPC_PurchaseSuccess();
        }

        public static void RPC_RequestVipReward(PlayerRPC self, RpcInfo info)
        {
            if (!Session(info, out Account acc, out PlayerRef p)) { return; }

            if (ServerHub.Economy != null && ServerHub.Economy.TryClaimVipReward(acc))
            {
                ServerHub.Accounts?.PushState(p, acc);
                self.RPC_PurchaseSuccess();
            }
        }

        public static void RPC_Respawn(PlayerRPC self, RpcInfo info)
        {
            if (!Session(info, out Account acc, out PlayerRef p)) { return; }

            ServerHub.Combat?.Respawn(p);
        }

        public static void RPC_RotationResponse(PlayerRPC self, int vector, RpcInfo info)
        {
            if (!Session(info, out Account acc, out PlayerRef p)) { return; }

            ServerHub.AntiBot?.OnResponse(acc, p, vector);
        }

        public static void RPC_SellCargo(PlayerRPC self, string items, Vector2 _pos, RpcInfo info)
        {
            if (!Session(info, out Account acc, out PlayerRef p)) { return; }

            if ((ServerHub.Economy?.SellCargo(acc, items) ?? 0) > 0)
            {
                ServerHub.Accounts?.PushState(p, acc);
            }

            Wire.SendCargo(p, acc);
        }

        public static void RPC_SendArenaDataRequest(PlayerRPC self, RpcInfo info)
        {
            if (!Session(info, out Account acc, out PlayerRef p)) { return; }

            Wire.SendArena(p, acc);
        }

        public static void RPC_SendBracketDataRequest(PlayerRPC self, RpcInfo info)
        {
            if (!Session(info, out Account acc, out PlayerRef p)) { return; }

            Wire.SendBrackets(p);
        }

        public static void RPC_SendChatMessageRequest(PlayerRPC self, string message, ChatChannel channel, string target, string languageCode, RpcInfo info)
        {
            if (!Session(info, out Account acc, out PlayerRef p)) { return; }

            if (ServerHub.Commands != null && ServerHub.Commands.TryHandle(acc, message))
            {
                return;
            }

            if (acc.IsMuted)
            {
                Wire.SendMute(p, acc.mutedUntilUnix);
                return;
            }

            ServerHub.Social?.Say(acc, (int)channel, message, target, languageCode);
        }

        public static void RPC_SendClanCreationRequest(PlayerRPC self, string tag, string name, RpcInfo info)
        {
            if (!Session(info, out Account acc, out PlayerRef p)) { return; }

            bool badTag = false;
            bool badName = false;

            if (ServerHub.Social != null &&
                ServerHub.Social.TryCreateClan(acc, tag, name, out badTag, out badName))
            {
                ServerHub.Accounts?.PushState(p, acc);
                self.RPC_SendClanCreationSuccess(acc.clanTag);
                Wire.SendClanData(p, acc);
            }
            else if (badTag)
            {
                self.RPC_SendClanCreationFailTag();
            }
            else if (badName)
            {
                self.RPC_SendClanCreationFailName();
            }
        }

        public static void RPC_SendClanDataRequest(PlayerRPC self, RpcInfo info)
        {
            if (!Session(info, out Account acc, out PlayerRef p)) { return; }

            ServerLog.Info($"clan data requested by {acc.nickname} (tag '{acc.clanTag}')");
            Wire.SendClanData(p, acc);
        }

        public static void RPC_SendClanWarDataRequest(PlayerRPC self, RpcInfo info)
        {
            if (!Session(info, out Account acc, out PlayerRef p)) { return; }

            Wire.SendClanWars(p, acc);
        }

        public static void RPC_SendLeaderboard_Achievement_Request(PlayerRPC self, string search, RpcInfo info)
        {
            if (!Session(info, out Account acc, out PlayerRef p)) { return; }

            ServerHub.Leaderboards?.SendPlayerBoard(
                p, LeaderboardService.Board.Achievements,
                Enums.ReliableData.Leaderboard_Achievement, search);
        }

        public static void RPC_SendLeaderboard_ClanAchievement_Request(PlayerRPC self, string search, RpcInfo info)
        {
            if (!Session(info, out Account acc, out PlayerRef p)) { return; }

            ServerHub.Leaderboards?.SendClanBoard(
                p, LeaderboardService.Board.Achievements,
                Enums.ReliableData.Leaderboard_ClanAchievement, search);
        }

        public static void RPC_SendLeaderboard_ClanLifetimeFame_Request(PlayerRPC self, string search, RpcInfo info)
        {
            if (!Session(info, out Account acc, out PlayerRef p)) { return; }

            ServerHub.Leaderboards?.SendClanBoard(
                p, LeaderboardService.Board.LifetimeFame,
                Enums.ReliableData.Leaderboard_ClanLifetimeFame, search);
        }

        public static void RPC_SendLeaderboard_ClanLifetimeKills_Request(PlayerRPC self, string search, RpcInfo info)
        {
            if (!Session(info, out Account acc, out PlayerRef p)) { return; }

            ServerHub.Leaderboards?.SendClanBoard(
                p, LeaderboardService.Board.LifetimeKills,
                Enums.ReliableData.Leaderboard_ClanLifetimeKills, search);
        }

        public static void RPC_SendLeaderboard_ClanPrestige_Request(PlayerRPC self, string search, RpcInfo info)
        {
            if (!Session(info, out Account acc, out PlayerRef p)) { return; }

            ServerHub.Leaderboards?.SendClanBoard(
                p, LeaderboardService.Board.Prestige,
                Enums.ReliableData.Leaderboard_ClanPrestige, search);
        }

        public static void RPC_SendLeaderboard_ClanWeeklyFame_Request(PlayerRPC self, string search, RpcInfo info)
        {
            if (!Session(info, out Account acc, out PlayerRef p)) { return; }

            ServerHub.Leaderboards?.SendClanBoard(
                p, LeaderboardService.Board.WeeklyFame,
                Enums.ReliableData.Leaderboard_ClanWeeklyFame, search);
        }

        public static void RPC_SendLeaderboard_ClanWeeklyKills_Request(PlayerRPC self, string search, RpcInfo info)
        {
            if (!Session(info, out Account acc, out PlayerRef p)) { return; }

            ServerHub.Leaderboards?.SendClanBoard(
                p, LeaderboardService.Board.WeeklyKills,
                Enums.ReliableData.Leaderboard_ClanWeeklyKills, search);
        }

        public static void RPC_SendLeaderboard_LifetimeArena_Request(PlayerRPC self, string search, RpcInfo info)
        {
            if (!Session(info, out Account acc, out PlayerRef p)) { return; }

            ServerHub.Leaderboards?.SendPlayerBoard(
                p, LeaderboardService.Board.LifetimeArena,
                Enums.ReliableData.Leaderboard_LifetimeArena, search);
        }

        public static void RPC_SendLeaderboard_LifetimeFame_Request(PlayerRPC self, string search, RpcInfo info)
        {
            if (!Session(info, out Account acc, out PlayerRef p)) { return; }

            ServerHub.Leaderboards?.SendPlayerBoard(
                p, LeaderboardService.Board.LifetimeFame,
                Enums.ReliableData.Leaderboard_LifetimeFame, search);
        }

        public static void RPC_SendLeaderboard_LifetimeKills_Request(PlayerRPC self, string search, RpcInfo info)
        {
            if (!Session(info, out Account acc, out PlayerRef p)) { return; }

            ServerHub.Leaderboards?.SendPlayerBoard(
                p, LeaderboardService.Board.LifetimeKills,
                Enums.ReliableData.Leaderboard_LifetimeKills, search);
        }

        public static void RPC_SendLeaderboard_Prestige_Request(PlayerRPC self, string search, RpcInfo info)
        {
            if (!Session(info, out Account acc, out PlayerRef p)) { return; }

            ServerHub.Leaderboards?.SendPlayerBoard(
                p, LeaderboardService.Board.Prestige,
                Enums.ReliableData.Leaderboard_Prestige, search);
        }

        public static void RPC_SendLeaderboard_WeeklyArena_Request(PlayerRPC self, string search, RpcInfo info)
        {
            if (!Session(info, out Account acc, out PlayerRef p)) { return; }

            ServerHub.Leaderboards?.SendPlayerBoard(
                p, LeaderboardService.Board.WeeklyArena,
                Enums.ReliableData.Leaderboard_WeeklyArena, search);
        }

        public static void RPC_SendLeaderboard_WeeklyFame_Request(PlayerRPC self, string search, RpcInfo info)
        {
            if (!Session(info, out Account acc, out PlayerRef p)) { return; }

            ServerHub.Leaderboards?.SendPlayerBoard(
                p, LeaderboardService.Board.WeeklyFame,
                Enums.ReliableData.Leaderboard_WeeklyFame, search);
        }

        public static void RPC_SendLeaderboard_WeeklyKills_Request(PlayerRPC self, string search, RpcInfo info)
        {
            if (!Session(info, out Account acc, out PlayerRef p)) { return; }

            ServerHub.Leaderboards?.SendPlayerBoard(
                p, LeaderboardService.Board.WeeklyKills,
                Enums.ReliableData.Leaderboard_WeeklyKills, search);
        }

        public static void RPC_SendPartyPing(PlayerRPC self, string position, RpcInfo info)
        {
            if (!Session(info, out Account acc, out PlayerRef p)) { return; }

            if (ServerHub.Social == null)
            {
                return;
            }

            foreach (Account member in ServerHub.Social.PartyMembers(acc))
            {
                if (member == acc)
                {
                    continue;
                }

                PlayerRef to = ServerHub.RefFor(member);
                if (to != PlayerRef.None)
                {
                    Wire.SendPartyPing(to, position);
                }
            }
        }

        public static void RPC_SendPvPDataRequest(PlayerRPC self, RpcInfo info)
        {
            if (!Session(info, out Account acc, out PlayerRef p)) { return; }

            Wire.SendPrestige(p, acc);
        }

        public static void RPC_SendTarget(PlayerRPC self, NetworkId id, Vector2 _pos, RpcInfo info)
        {
            if (!Session(info, out Account acc, out PlayerRef p)) { return; }

            ServerHub.Combat?.SetTarget(p, id);
        }

        public static void RPC_SendTargetMark(PlayerRPC self, NetworkId id, RpcInfo info)
        {
            if (!Session(info, out Account acc, out PlayerRef p)) { return; }

            ServerHub.Combat?.MarkTarget(p, id);
        }

        public static void RPC_SentryChangeRequest(PlayerRPC self, int designIndex, RpcInfo info)
        {
            if (!Session(info, out Account acc, out PlayerRef p)) { return; }

            if (ServerHub.Progression != null &&
                ServerHub.Progression.TrySetSentryDesign(acc, designIndex))
            {
                ServerHub.Accounts?.PushState(p, acc);
            }
        }

        public static void RPC_SpxChangeRequest(PlayerRPC self, int designIndex, RpcInfo info)
        {
            if (!Session(info, out Account acc, out PlayerRef p)) { return; }

            if (ServerHub.Progression != null &&
                ServerHub.Progression.TrySetSpxDesign(acc, designIndex))
            {
                ServerHub.Accounts?.PushState(p, acc);
            }
        }

        public static void RPC_TeleportToBZ(PlayerRPC self, RpcInfo info)
        {
            if (!Session(info, out Account acc, out PlayerRef p)) { return; }

            if (PlayerDirector.TeleportToBattleZone(p, acc))
            {
                ServerHub.Accounts?.PushState(p, acc);
            }
        }

        public static void RPC_TitleChangeRequest(PlayerRPC self, int index, RpcInfo info)
        {
            if (!Session(info, out Account acc, out PlayerRef p)) { return; }

            if (ServerHub.Progression != null && ServerHub.Progression.TrySetTitle(acc, index))
            {
                ServerHub.Accounts?.PushState(p, acc);
            }
        }

        public static void RPC_ToggleExtra(PlayerRPC self, ExtraData.ExtraType _type, bool state, RpcInfo info)
        {
            if (!Session(info, out Account acc, out PlayerRef p)) { return; }

            ServerHub.Extras?.Toggle(acc, p, _type, state);
        }

        public static void RPC_UpgradeItem(PlayerRPC self, int equipmentID, int slot, Enums.Currency currency, RpcInfo info)
        {
            if (!Session(info, out Account acc, out PlayerRef p)) { return; }

            if (ServerHub.Progression != null &&
                ServerHub.Progression.TryUpgradeItem(acc, equipmentID, slot, currency))
            {
                ServerHub.Accounts?.PushState(p, acc);
                self.RPC_UpgradeSuccess();
                return;
            }

            ServerLog.Warn(
                $"upgrade refused for {acc.nickname}: item {equipmentID} slot {slot} in {currency}");
            self.RPC_MerchantBuyLimit();
        }

        public static void RPC_UseRepairBattery(PlayerRPC self, RpcInfo info)
        {
            if (!Session(info, out Account acc, out PlayerRef p)) { return; }

            ServerHub.Combat?.UseRepairBattery(acc);
        }

        public static void RPC_UseSkill1(PlayerRPC self, RpcInfo info)
        {
            if (!Session(info, out Account acc, out PlayerRef p)) { return; }

            ServerHub.Skills?.Use(acc, p, 1, default);
        }

        public static void RPC_UseSkill2(PlayerRPC self, NetworkId target, RpcInfo info)
        {
            if (!Session(info, out Account acc, out PlayerRef p)) { return; }

            ServerHub.Skills?.Use(acc, p, 2, target);
        }
    }
}
