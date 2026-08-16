using System;
using System.Collections.Generic;
using System.Text;
using TidalNexus.StandaloneServer.Data;

namespace TidalNexus.StandaloneServer.Services
{

    public sealed class MissionService
    {

        public const int MaxActive = 10;

        private const int OffersPerType = 3;

        private const int Listed = 25;

        public int DropUncompletable(Account account)
        {
            if (account == null || account.missions.Count == 0)
            {
                return 0;
            }

            int removed = account.missions.RemoveAll(active =>
            {
                MissionData mission = MissionCatalogue.ById(active.templateId);
                return mission != null && !MissionCatalogue.IsCompletable(mission);
            });

            if (removed > 0)
            {
                AccountStore.MarkDirty(account);
                ServerLog.Warn($"released {removed} held mission(s) from {account.nickname} "
                    + "that no server hook can advance");
            }

            return removed;
        }

        public List<MissionData> Offers(Account account)
        {
            DropUncompletable(account);

            var held = new HashSet<int>();

            if (account != null)
            {
                foreach (ActiveMission active in account.missions)
                {
                    held.Add(active.templateId);
                }
            }

            var result = new List<MissionData>();

            foreach (MissionData mission in MissionCatalogue.Available(account, Listed + held.Count))
            {
                if (!held.Contains(mission.index))
                {
                    result.Add(mission);
                }

                if (result.Count >= Listed)
                {
                    break;
                }
            }

            return result;
        }

        public List<MissionData> PveOffers(Account account) =>
            MissionCatalogue.PveTasks(account, OffersPerType);

        public List<MissionData> TradeOffers(Account account) =>
            MissionCatalogue.TradeTasks(account, OffersPerType);

        public List<MissionData> PvpOffers(Account account) =>
            MissionCatalogue.PvpTasks(account, OffersPerType);

        public bool TryAccept(Account account, int missionIndex, out bool limitReached)
        {
            limitReached = false;

            if (account == null)
            {
                return false;
            }

            DropUncompletable(account);

            if (account.missions.Count >= MaxActive)
            {
                limitReached = true;
                return false;
            }

            MissionData mission = MissionCatalogue.ById(missionIndex);
            if (mission == null)
            {
                ServerLog.Warn($"accept for unknown mission {missionIndex}");
                return false;
            }

            if (!MissionCatalogue.IsCompletable(mission))
            {
                ServerLog.Warn(
                    $"{account.nickname} tried to accept mission {mission.index} " +
                    $"\"{mission.missionName}\" - no server hook advances its objectives");
                return false;
            }

            foreach (ActiveMission m in account.missions)
            {
                if (m.templateId == mission.index)
                {
                    return false;
                }
            }

            if (MissionCatalogue.IsCompleted(mission, account))
            {
                ServerLog.Warn(
                    $"{account.nickname} tried to re-accept completed mission " +
                    $"{mission.index} \"{mission.missionName}\"");
                return false;
            }

            account.missions.Add(new ActiveMission
            {
                templateId = mission.index,
                progress = new int[Math.Max(1, mission.objectives?.Count ?? 1)],
                target = MissionCatalogue.TargetsOf(mission),
                acceptedUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                complete = false,
            });

            AccountStore.MarkDirty(account);
            ServerLog.Info($"{account.nickname} accepted \"{mission.missionName}\"");
            return true;
        }

        public bool TryCancel(Account account, int missionIndex)
        {
            if (account == null)
            {
                return false;
            }

            int removed = account.missions.RemoveAll(m => m.templateId == missionIndex);
            if (removed > 0)
            {
                AccountStore.MarkDirty(account);
            }

            return removed > 0;
        }

        public bool TryComplete(Account account, int missionIndex)
        {
            if (account == null)
            {
                return false;
            }

            ActiveMission active = account.missions.Find(m => m.templateId == missionIndex);
            if (active == null)
            {
                return false;
            }

            MissionData mission = MissionCatalogue.ById(missionIndex);

            if (!IsSatisfied(active, mission))
            {
                if (active.complete)
                {
                    active.complete = false;
                    Publish(account);
                }

                return false;
            }

            account.missions.Remove(active);

            foreach (MissionReward reward in MissionCatalogue.RewardsFor(mission, account))
            {
                Pay(account, reward);
            }

            if (mission?.achievementRewards != null)
            {
                foreach (AchievementData achievement in mission.achievementRewards)
                {
                    ServerHub.Achievements?.Award(account, achievement, tier: 0);
                }
            }

            string token = MissionCatalogue.CompletionToken(missionIndex);
            if (!account.achievements.Contains(token))
            {
                account.achievements.Add(token);
            }

            AccountStore.MarkDirty(account);

            ServerHub.Achievements?.Bump(account, AchievementService.MissionsDone);

            Fusion.PlayerRef p = ServerHub.RefFor(account);
            if (p != Fusion.PlayerRef.None)
            {
                ServerHub.Accounts?.PushState(p, account);
            }

            ServerLog.Info(
                $"{account.nickname} completed \"{mission?.missionName ?? missionIndex.ToString()}\"");
            return true;
        }

        private void Pay(Account account, MissionReward reward)
        {
            switch (reward.type)
            {
                case MissionRewardType.Credits:
                    account.credits += reward.amount;
                    break;

                case MissionRewardType.Borax:
                    account.borax += reward.amount;
                    break;

                case MissionRewardType.Experience:
                    ServerHub.Progression?.AwardExperience(account, reward.amount);
                    break;

                case MissionRewardType.BattlePoints:
                    account.battlePoints += reward.amount;
                    break;

                case MissionRewardType.Fame:
                    account.lifetimeFame += reward.amount;
                    account.weeklyFame += reward.amount;
                    break;

                case MissionRewardType.Ammo:

                    if (reward.ammo != null &&
                        !EconomyService.AddAmmo(account, reward.ammo.name, reward.amount))
                    {
                        ServerLog.Warn($"mission ammo reward \"{reward.ammo.name}\" "
                            + "has no matching counter - not credited");
                    }

                    break;

                case MissionRewardType.Unlockable:
                    ServerHub.Progression?.GrantUnlockable(account, reward.unlockable);
                    break;
            }
        }

        public void OnKill(Account account, NPCData npc, bool wasNpc, Account victim = null)
        {
            if (!wasNpc)
            {
                Advance(account, MissionObjectiveType.PlayerKill, 1, _ => true);

                if (victim != null && !ReferenceEquals(account, victim) &&
                    FittedModuleObjective(victim) is MissionObjectiveType module)
                {
                    Advance(account, module, 1, _ => true);
                }

                return;
            }

            Advance(account, MissionObjectiveType.NpcKill, 1,
                objective => Wants(objective.GetNPC(), npc));
        }

        private static MissionObjectiveType? FittedModuleObjective(Account victim)
        {
            List<ExtraData> catalogue = GameData.Data?.extraDatas;
            if (victim?.equippedExtras == null || catalogue == null)
            {
                return null;
            }

            foreach (int index in victim.equippedExtras)
            {
                if (index < 0 || index >= catalogue.Count ||
                    !(catalogue[index] is CoreModuleData fitted))
                {
                    continue;
                }

                switch (fitted.type)
                {
                    case ExtraData.ExtraType.CombatModule:
                        return MissionObjectiveType.DestroyCombat;

                    case ExtraData.ExtraType.ReconModule:
                        return MissionObjectiveType.DestroyRecon;

                    case ExtraData.ExtraType.TankModule:
                        return MissionObjectiveType.DestroyTank;

                    case ExtraData.ExtraType.TradeModule:
                        return MissionObjectiveType.DestroyTrade;
                }
            }

            return null;
        }

        public void OnFactionKill(Account account) =>
            Advance(account, MissionObjectiveType.FactionKill, 1, _ => true);

        public void OnBattleZoneKill(Account account) =>
            Advance(account, MissionObjectiveType.BzKill, 1, _ => true);

        public void OnKrakenKill(Account account) =>
            Advance(account, MissionObjectiveType.KrakenKill, 1, _ => true);

        public void OnCollected(Account account, string material, int units) =>
            Advance(account, MissionObjectiveType.Collection, units,
                objective => WantsMaterial(objective, account, material));

        public void OnSold(Account account, string material, int units) =>
            Advance(account, MissionObjectiveType.MaterialSell, units,
                objective => WantsMaterial(objective, account, material));

        public void OnBought(Account account, string material, int units) =>
            Advance(account, MissionObjectiveType.MaterialBuy, units,
                objective => WantsMaterial(objective, account, material));

        public void OnBeacon(Account account, MissionObjectiveType kind) =>
            Advance(account, kind, 1, _ => true);

        private static bool Wants(NPCData wanted, NPCData killed)
        {
            if (wanted == null)
            {
                return true;
            }

            return killed != null &&
                   (ReferenceEquals(wanted, killed) || wanted.name == killed.name);
        }

        private static bool WantsMaterial(MissionObjective objective, Account account,
            string material)
        {
            CollectableMaterialData wanted =
                objective.GetMaterial((Enums.Faction)(account?.faction ?? 3));

            return wanted == null || wanted.name == material;
        }

        private static bool IsSatisfied(ActiveMission active, MissionData mission)
        {
            if (mission?.objectives != null)
            {
                Resize(active, mission);
            }

            for (int i = 0; i < active.target.Length; i++)
            {
                int done = i < active.progress.Length ? active.progress[i] : 0;

                if (done < active.target[i])
                {
                    return false;
                }
            }

            return true;
        }

        private static void Resize(ActiveMission active, MissionData mission)
        {
            int count = Math.Max(1, mission.objectives.Count);
            if (active.target.Length == count && active.progress.Length == count)
            {
                return;
            }

            var target = new int[count];
            var progress = new int[count];

            for (int i = 0; i < count; i++)
            {
                target[i] = i < mission.objectives.Count
                    ? MissionCatalogue.TargetOfObjective(mission.objectives[i])
                    : 1;

                if (i < active.progress.Length)
                {
                    progress[i] = active.progress[i];
                }
            }

            active.target = target;
            active.progress = progress;
        }

        private void Advance(Account account, MissionObjectiveType type, int amount,
            Func<MissionObjective, bool> matches)
        {
            if (account == null || amount <= 0 || account.missions.Count == 0)
            {
                return;
            }

            bool changed = false;

            foreach (ActiveMission active in account.missions)
            {
                MissionData mission = MissionCatalogue.ById(active.templateId);
                if (mission?.objectives == null)
                {
                    continue;
                }

                Resize(active, mission);

                bool advanced = false;
                for (int i = 0; i < mission.objectives.Count; i++)
                {
                    MissionObjective objective = mission.objectives[i];
                    if (objective == null || objective.type != type || !matches(objective))
                    {
                        continue;
                    }

                    if (active.progress[i] >= active.target[i])
                    {
                        continue;
                    }

                    active.progress[i] = Math.Min(active.target[i], active.progress[i] + amount);
                    advanced = true;
                }

                if (!advanced)
                {
                    continue;
                }

                active.complete = IsSatisfied(active, mission);
                changed = true;
            }

            if (changed)
            {
                Publish(account);
            }
        }

        private static void Publish(Account account)
        {
            AccountStore.MarkDirty(account);

            Fusion.PlayerRef p = ServerHub.RefFor(account);
            if (p != Fusion.PlayerRef.None)
            {
                Wire.SendActiveMissions(p, account);
            }
        }
    }
}
