using System.Collections.Generic;
using Fusion;
using TidalNexus.StandaloneServer.Data;
using UnityEngine;

namespace TidalNexus.StandaloneServer.Services
{

    public sealed class ExtrasService
    {

        private static float Drain => ServerHub.Config?.ExtraDrain ?? 0.1f;

        private static float AutoCollectRange => ServerHub.Config?.AutoCollectRange ?? 15f;

        private const float Interval = 1f;

        public sealed class Carry
        {
            public float Fraction;
            public int LastCargo = -1;
        }

        private float _clock;

        private readonly List<PlayerSession> _players = new List<PlayerSession>();

        public bool Toggle(Account account, PlayerRef player, ExtraData.ExtraType type, bool state)
        {
            if (account == null || type == ExtraData.ExtraType.None)
            {
                return false;
            }

            ExtraData extra = FittedOfType(account, type);
            if (extra == null)
            {
                ServerLog.Info($"{account.nickname} toggled {type} without it fitted");
                return false;
            }

            if (state && account.energy <= 0)
            {
                return false;
            }

            if (type == ExtraData.ExtraType.RepairDrone)
            {
                return state && StartRepairBoost(account, player);
            }

            bool changed = state
                ? !account.activeExtras.Contains((int)type) && Add(account, type)
                : account.activeExtras.Remove((int)type);

            if (!changed)
            {
                return false;
            }

            ApplyFlags(account, player);
            AccountStore.MarkDirty(account);

            ServerLog.Info($"{account.nickname} switched {type} {(state ? "on" : "off")}");
            return true;
        }

        private static bool Add(Account account, ExtraData.ExtraType type)
        {
            account.activeExtras.Add((int)type);
            return true;
        }

        private static bool StartRepairBoost(Account account, PlayerRef player)
        {
            NetworkObject obj = WorldLookup.ObjectOf(player);
            Player self = obj != null ? obj.GetComponentInChildren<Player>() : null;
            if (self == null)
            {
                return false;
            }

            try
            {
                self.StartCoroutine(self.RepairBoost());
            }
            catch (System.Exception e)
            {
                ServerLog.Warn($"could not start repair boost: {e.Message}");
                return false;
            }

            ServerLog.Info($"{account.nickname} triggered a repair boost");
            return true;
        }

        private static bool DrawsEnergy(ExtraData.ExtraType type)
        {
            switch (type)
            {
                case ExtraData.ExtraType.AutoCollect:
                case ExtraData.ExtraType.Sentry:
                case ExtraData.ExtraType.HullConverter:
                case ExtraData.ExtraType.ShieldConverter:
                    return true;

                default:
                    return false;
            }
        }

        public static void ApplyFlags(Account account, PlayerRef player)
        {
            NetworkObject obj = WorldLookup.ObjectOf(player);
            var values = obj != null ? obj.GetComponentInChildren<PlayerNetworkValues>() : null;
            if (values == null)
            {
                return;
            }

            try
            {
                values.isSentryActive =
                    account.activeExtras.Contains((int)ExtraData.ExtraType.Sentry);

                values.isSentryEquipped =
                    FittedOfType(account, ExtraData.ExtraType.Sentry) != null;

                values.isRepairing =
                    FittedOfType(account, ExtraData.ExtraType.RepairDrone) != null;
            }
            catch (System.Exception e)
            {
                ServerLog.Warn($"could not apply extra flags: {e.Message}");
            }
        }

        public void Tick(float deltaTime)
        {
            _clock += deltaTime;
            if (_clock < Interval || ServerHub.Runner == null)
            {
                return;
            }

            float elapsed = _clock;
            _clock = 0f;

            ServerHub.SnapshotSessions(_players);

            foreach (PlayerSession session in _players)
            {
                Account account = session.Account;
                PlayerRef player = session.Player;

                ApplyFlags(account, player);

                if (account.activeExtras.Count > 0)
                {
                    Spend(session, elapsed);
                }

                if (account.activeExtras.Contains((int)ExtraData.ExtraType.AutoCollect))
                {
                    AutoCollect(account, player);
                }

                Convert(session, player);
            }
        }

        private static void Spend(PlayerSession session, float elapsed)
        {
            Account account = session.Account;
            PlayerRef player = session.Player;

            int draining = 0;
            foreach (int type in account.activeExtras)
            {
                if (DrawsEnergy((ExtraData.ExtraType)type))
                {
                    draining++;
                }
            }

            if (draining == 0)
            {
                return;
            }

            Carry carry = session.State<Carry>();

            float carried = carry.Fraction + Drain * draining * elapsed;

            int whole = Mathf.FloorToInt(carried);
            carry.Fraction = carried - whole;

            if (whole <= 0)
            {
                return;
            }

            account.energy = Mathf.Max(0, account.energy - whole);

            if (account.energy <= 0)
            {
                account.activeExtras.RemoveAll(
                    type => DrawsEnergy((ExtraData.ExtraType)type));
                ApplyFlags(account, player);
                ServerLog.Info($"{account.nickname} ran out of energy - extras off");
            }

            AccountStore.MarkDirty(account);
            ServerHub.Accounts?.PushState(player, account);
        }

        private static void AutoCollect(Account account, PlayerRef player)
        {
            NetworkObject self = WorldLookup.ObjectOf(player);
            if (self == null || ServerHub.Economy == null)
            {
                return;
            }

            Vector3 at = self.transform.position;
            float range = AutoCollectRange * AutoCollectRange;

            foreach (CollectableObject loot in Object.FindObjectsByType<CollectableObject>(
                         FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (loot == null)
                {
                    continue;
                }

                var obj = loot.GetComponentInParent<NetworkObject>();
                if (obj == null || (loot.transform.position - at).sqrMagnitude > range)
                {
                    continue;
                }

                if (ServerHub.Economy.Collect(account, obj.Id, out _, out bool full))
                {
                    Wire.SendCargo(player, account);
                }

                if (full)
                {

                    return;
                }
            }
        }

        private static void Convert(PlayerSession session, PlayerRef player)
        {
            Account account = session.Account;
            Carry carry = session.State<Carry>();

            int used = account.CargoUsed;
            int previous = carry.LastCargo;
            carry.LastCargo = used;

            if (previous < 0 || used <= previous || account.activeExtras.Count == 0)
            {
                return;
            }

            NetworkObject obj = WorldLookup.ObjectOf(player);
            Player self = obj != null ? obj.GetComponentInChildren<Player>() : null;
            if (self?.health == null)
            {
                return;
            }

            foreach (int type in account.activeExtras)
            {
                if (!(FittedOfType(account, (ExtraData.ExtraType)type) is ConverterData converter))
                {
                    continue;
                }

                bool hull = converter.converterType == ConverterType.Hull;
                int current = hull ? self.health.hull : self.health.shield;
                int max = hull ? self.health.maxHull : self.health.maxShield;

                if (max <= 0 || current >= max)
                {
                    continue;
                }

                int restored = Mathf.Max(1, Mathf.RoundToInt(max * converter.repairPercentage));

                if (hull)
                {
                    self.health.hull = Mathf.Min(max, current + restored);
                    account.hull = self.health.hull;
                }
                else
                {
                    self.health.shield = Mathf.Min(max, current + restored);
                    account.shield = self.health.shield;
                }

                AccountStore.MarkDirty(account);
            }
        }

        public static List<int> Listing(Account account, bool fitted)
        {
            var result = new List<int>();
            if (account == null)
            {
                return result;
            }

            List<ExtraData> catalogue = GameData.Data?.extraDatas;
            if (catalogue == null)
            {
                return result;
            }

            var remaining = new List<int>(account.equippedExtras);
            var seen = new HashSet<int>();

            foreach (int index in account.extras)
            {
                if (index < 0 || index >= catalogue.Count || catalogue[index] == null)
                {
                    continue;
                }

                bool isFitted = remaining.Remove(index);

                if (isFitted == fitted && seen.Add(index))
                {
                    result.Add(index);
                }
            }

            return result;
        }

        private static ExtraData FittedOfType(Account account, ExtraData.ExtraType type)
        {
            List<ExtraData> catalogue = GameData.Data?.extraDatas;
            if (catalogue == null)
            {
                return null;
            }

            foreach (int index in account.equippedExtras)
            {
                if (index < 0 || index >= catalogue.Count)
                {
                    continue;
                }

                ExtraData extra = catalogue[index];
                if (extra != null && extra.type == type)
                {
                    return extra;
                }
            }

            return null;
        }
    }
}
