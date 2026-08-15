using System.Collections.Generic;
using TidalNexus.StandaloneServer.Core;
using TidalNexus.StandaloneServer.Data;
using TidalNexus.StandaloneServer.Services;

namespace TidalNexus.StandaloneServer
{

    public static class RewardService
    {

        public static void PayOut(NPCBehaviour npc, RewardPurse? purse = null)
        {
            if (npc?.data == null || npc.attackers == null)
            {
                return;
            }

            List<NPCBehaviour.Attackers> ledger = npc.attackers;
            var damage = new List<int>(ledger.Count);

            foreach (NPCBehaviour.Attackers entry in ledger)
            {
                damage.Add(entry?.player != null ? entry.damage : 0);
            }

            IReadOnlyList<RewardShare> shares = KillReward.Split(
                damage,
                purse ?? new RewardPurse(
                    npc.data.creditReward,
                    npc.data.boraxReward,
                    npc.data.experienceReward,
                    npc.data.fameReward));

            for (int i = 0; i < shares.Count; i++)
            {
                Grant(ledger[i]?.player, shares[i]);
            }
        }

        private static void Grant(Player player, RewardShare share)
        {
            if (player == null)
            {
                return;
            }

            if (share.Payout.IsEmpty)
            {
                if (share.RoundedToNothing)
                {

                    ServerLog.Info(
                        $"reward: {share.Fraction:P0} share was too small to pay out");
                }

                return;
            }

            int credits = share.Payout.Credits;
            int borax = share.Payout.Borax;
            int experience = share.Payout.Experience;
            int fame = share.Payout.Fame;

            Fusion.PlayerRef owner = CombatService.FindOwner(player.Object);
            Account account = ServerHub.AccountFor(owner);

            if (account != null)
            {
                account.credits += credits;

                account.borax = (int)System.Math.Min(
                    (long)account.borax + borax, Account.MaxBorax);

                account.experience += experience;
                account.lifetimeFame += fame;
                account.weeklyFame += fame;
                AccountStore.MarkDirty(account);
            }
            else
            {
                ServerLog.Warn("reward earned but no account to bank it in");
            }

            try
            {
                if (player.localValues != null && account != null)
                {

                    player.localValues.credits =
                        (int)System.Math.Min(account.credits, int.MaxValue);
                    player.localValues.borax =
                        (int)System.Math.Min(account.borax, int.MaxValue);
                    player.localValues.fame =
                        (int)System.Math.Min(account.weeklyFame, int.MaxValue);
                    player.localValues.totalFame =
                        (int)System.Math.Min(account.lifetimeFame, int.MaxValue);
                }

                if (player.networkValues != null && experience > 0 && account != null)
                {
                    player.networkValues.experience =
                        (int)System.Math.Min(account.experience, int.MaxValue);
                }
            }
            catch (System.Exception ex)
            {
                ServerLog.Warn($"could not mirror reward onto the player: {ex.Message}");
            }

            if (owner != Fusion.PlayerRef.None)
            {
                Wire.SendLootLog(owner,
                    new Wire.LootLine(global::Log.Credits, credits),
                    new Wire.LootLine(global::Log.Borax, borax),
                    new Wire.LootLine(global::Log.Experience, experience),
                    new Wire.LootLine(global::Log.Fame, fame));
            }

            ServerLog.Info(
                $"reward: +{credits} credits, +{borax} borax, +{experience} xp, " +
                $"+{fame} fame ({share.Fraction:P0} share)");
        }
    }
}
