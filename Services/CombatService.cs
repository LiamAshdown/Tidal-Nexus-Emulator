using System;
using System.Collections.Generic;
using Fusion;
using TidalNexus.StandaloneServer.Core;
using TidalNexus.StandaloneServer.Data;
using UnityEngine;

namespace TidalNexus.StandaloneServer.Services
{

    public sealed class CombatService
    {

        private const float FireInterval = 1.0f;

        private const float CombatMemory = 20f;

        public sealed class Engagement
        {

            public bool Firing;

            public NetworkObject Target;
            public float NextShot;

            public void Begin(NetworkObject target, float now)
            {
                Firing = true;
                Target = target;
                NextShot = now;
            }

            public void Clear()
            {
                Firing = false;
                Target = null;
                NextShot = 0f;
            }
        }

        private readonly struct AttackerFlag : IEquatable<AttackerFlag>
        {
            public readonly NetworkId Victim;
            public readonly NetworkId Attacker;

            public AttackerFlag(NetworkId victim, NetworkId attacker)
            {
                Victim = victim;
                Attacker = attacker;
            }

            public bool Equals(AttackerFlag other) =>
                Victim == other.Victim && Attacker == other.Attacker;

            public override bool Equals(object obj) =>
                obj is AttackerFlag other && Equals(other);

            public override int GetHashCode() =>
                unchecked((int)(Victim.Raw * 397u ^ Attacker.Raw));
        }

        private readonly Dictionary<AttackerFlag, float> _attackerExpiry =
            new Dictionary<AttackerFlag, float>();

        private readonly List<PlayerSession> _players = new List<PlayerSession>();

        private readonly List<AttackerFlag> _lapsed = new List<AttackerFlag>();

        private readonly List<AttackerFlag> _cleared = new List<AttackerFlag>();

        private float _clock;

        public void Tick(float deltaTime)
        {
            _clock += deltaTime;

            if (ServerHub.Runner == null)
            {
                return;
            }

            ServerHub.SnapshotSessions(_players);

            foreach (PlayerSession session in _players)
            {

                Engagement engagement = session.Peek<Engagement>();
                if (engagement == null || !engagement.Firing)
                {
                    continue;
                }

                PlayerRef shooter = session.Player;

                NetworkObject shooterObj = WorldLookup.ObjectOf(shooter);
                if (shooterObj == null || engagement.Target == null)
                {
                    Disengage(session, engagement);
                    continue;
                }

                if (!InCannonRange(shooterObj, engagement.Target))
                {
                    ServerHub.RpcFor(shooter)?.RPC_SendOutOfRange();
                    Disengage(session, engagement);
                    continue;
                }

                if (_clock < engagement.NextShot)
                {
                    continue;
                }

                Account shooterAccount = session.Account;

                if (!TakeRound(shooterAccount, shooter))
                {
                    Disengage(session, engagement);
                    continue;
                }

                engagement.NextShot = _clock + FireInterval;
                ApplyDamage(shooterObj, engagement.Target,
                    DamageOf(shooterAccount, shooterObj, engagement.Target));
            }

            ExpireAttackers();
        }

        public static bool InCannonRange(Vector3 from, Vector3 to)
        {
            return WeaponRange.InRange(
                from.x, from.y, from.z, to.x, to.y, to.z, WeaponRange.Cannon);
        }

        private static bool InCannonRange(NetworkObject shooter, NetworkObject target)
        {
            return InCannonRange(shooter.transform.position, target.transform.position);
        }

        private static void Disengage(PlayerSession session, Engagement engagement)
        {
            engagement.Clear();

            ServerHub.RpcFor(session.Player)?.RPC_SendCannonStop();

            AccountStore.MarkDirty(session.Account);
        }

        public void MarkTarget(PlayerRef player, NetworkId targetId)
        {
            if (ServerHub.Runner == null)
            {
                return;
            }

            NetworkObject obj = WorldLookup.ObjectOf(player);
            var local = obj != null ? obj.GetComponentInChildren<PlayerLocalValues>() : null;
            if (local == null)
            {
                return;
            }

            local.currentMarkedTarget =
                targetId.IsValid && ServerHub.Runner.FindObject(targetId) != null
                    ? targetId
                    : default;
        }

        public void SetTarget(PlayerRef shooter, NetworkId targetId)
        {
            if (ServerHub.Runner == null)
            {
                return;
            }

            if (!targetId.IsValid)
            {
                StopFiring(shooter);
                return;
            }

            NetworkObject target = ServerHub.Runner.FindObject(targetId);
            if (target == null)
            {
                StopFiring(shooter);
                return;
            }

            NetworkObject shooterObj = WorldLookup.ObjectOf(shooter);
            if (shooterObj == null)
            {
                return;
            }

            if (!InCannonRange(shooterObj, target))
            {
                ServerHub.RpcFor(shooter)?.RPC_SendOutOfRange();
                return;
            }

            if (!MayAttack(shooter, target))
            {
                ServerHub.RpcFor(shooter)?.RPC_SendCannonStop();
                return;
            }

            PlayerSession session = ServerHub.SessionFor(shooter);
            if (session == null)
            {
                return;
            }

            session.State<Engagement>().Begin(target, _clock);

            PublishTarget(shooter, targetId);
        }

        public void StopFiring(PlayerRef shooter)
        {

            ServerHub.SessionFor(shooter)?.Peek<Engagement>()?.Clear();
            PublishTarget(shooter, default);
        }

        private void PublishTarget(PlayerRef shooter, NetworkId targetId)
        {
            if (ServerHub.Runner == null)
            {
                return;
            }

            try
            {
                NetworkObject obj = WorldLookup.ObjectOf(shooter);
                var player = obj != null ? obj.GetComponent<Player>() : null;

                if (player != null && player.networkValues != null)
                {
                    player.networkValues.currentAttackingTarget = targetId;
                }
            }
            catch (Exception e)
            {
                ServerLog.Warn($"could not publish target: {e.Message}");
            }
        }

        public bool MayAttack(PlayerRef shooter, NetworkObject target)
        {
            var targetPlayer = target.GetComponent<Player>();
            if (targetPlayer == null)
            {
                return true;
            }

            Account attacker = ServerHub.AccountFor(shooter);
            PlayerRef targetRef = FindOwner(target);
            Account defender = ServerHub.AccountFor(targetRef);

            if (attacker == null || defender == null)
            {
                return false;
            }

            if (attacker.faction != 0 && attacker.faction == defender.faction)
            {
                return false;
            }

            return ServerHub.Pvp == null || ServerHub.Pvp.MayFight(attacker, defender);
        }

        public int DamageOf(Account account, NetworkObject shooter, NetworkObject target)
        {
            var self = shooter != null ? shooter.GetComponentInChildren<Player>() : null;
            if (self == null)
            {
                return 50;
            }

            bool versusPlayer = target != null && FindOwner(target) != PlayerRef.None;
            int damage = versusPlayer ? self.pvpDamage : self.pveDamage;

            if (damage <= 0)
            {

                damage = WeaponDamageOf(account);
            }

            if (account != null && HasBuff(account, buff => buff.isCombatF2))
            {
                damage = Mathf.RoundToInt(damage * SkillService.DamageBoost);
            }

            return damage;
        }

        private static bool TakeRound(Account account, PlayerRef shooter)
        {
            if (account == null)
            {
                return false;
            }

            int index = account.ammoIndex > 0 ? account.ammoIndex : 1;

            switch (index)
            {
                case 1: if (account.ammo1 <= 0) { return false; } account.ammo1--; break;
                case 2: if (account.ammo2 <= 0) { return false; } account.ammo2--; break;
                case 3: if (account.ammo3 <= 0) { return false; } account.ammo3--; break;
                case 4: if (account.ammo4 <= 0) { return false; } account.ammo4--; break;
                case 5: if (account.ammo5 <= 0) { return false; } account.ammo5--; break;
                case 6: if (account.photonAmmo1 <= 0) { return false; } account.photonAmmo1--; break;
                case 7: if (account.photonAmmo2 <= 0) { return false; } account.photonAmmo2--; break;
                case 8: if (account.photonAmmo3 <= 0) { return false; } account.photonAmmo3--; break;
                case 9: if (account.photonAmmo4 <= 0) { return false; } account.photonAmmo4--; break;
                default: return false;
            }

            MirrorAmmo(account, shooter, index);
            return true;
        }

        private static void MirrorAmmo(Account account, PlayerRef shooter, int index)
        {
            if (ServerHub.Runner == null)
            {
                return;
            }

            NetworkObject obj = WorldLookup.ObjectOf(shooter);
            var local = obj != null ? obj.GetComponentInChildren<PlayerLocalValues>() : null;
            if (local == null)
            {
                return;
            }

            try
            {
                switch (index)
                {
                    case 1: local.ammo1 = account.ammo1; break;
                    case 2: local.ammo2 = account.ammo2; break;
                    case 3: local.ammo3 = account.ammo3; break;
                    case 4: local.ammo4 = account.ammo4; break;
                    case 5: local.ammo5 = account.ammo5; break;
                    case 6: local.photonAmmo1 = account.photonAmmo1; break;
                    case 7: local.photonAmmo2 = account.photonAmmo2; break;
                    case 8: local.photonAmmo3 = account.photonAmmo3; break;
                    case 9: local.photonAmmo4 = account.photonAmmo4; break;
                }
            }
            catch (Exception e)
            {
                ServerLog.Warn($"could not mirror ammo: {e.Message}");
            }
        }

        private static int WeaponDamageOf(Account account)
        {
            if (account == null)
            {
                return 0;
            }

            List<WeaponData> weapons = GameData.Data?.weaponDatas;
            if (weapons == null || weapons.Count == 0)
            {
                return 0;
            }

            int slots = GameData.Settings != null
                ? GameData.Settings.GetSlotCount(account.level)
                : 1;

            string digits = account.weaponSlots.ToString();
            int total = 0;

            for (int i = 0; i < slots && i < digits.Length; i++)
            {
                int index = digits[i] - '0';
                if (index >= 0 && index < weapons.Count && weapons[index] != null)
                {
                    total += weapons[index].damage;
                }
            }

            return total;
        }

        private static bool IsInvulnerable(Health health)
        {
            try
            {
                PlayerNetworkValues values = health.player != null
                    ? health.player.networkValues
                    : null;

                return values != null && values.isTankF2;
            }
            catch (Exception)
            {

                return false;
            }
        }

        private static bool HasBuff(Account account, Func<PlayerNetworkValues, bool> test)
        {
            PlayerRef p = ServerHub.RefFor(account);
            if (p == PlayerRef.None || ServerHub.Runner == null)
            {
                return false;
            }

            NetworkObject obj = WorldLookup.ObjectOf(p);
            var values = obj != null ? obj.GetComponentInChildren<PlayerNetworkValues>() : null;

            try
            {
                return values != null && test(values);
            }
            catch (Exception)
            {
                return false;
            }
        }

        public bool ApplyDamage(NetworkObject attacker, NetworkObject target, int amount)
        {

            Health health = WorldLookup.HealthOf(target);

            if (amount <= 0 || !WorldLookup.IsSpawned(health))
            {
                return false;
            }

            if (IsInvulnerable(health))
            {
                return false;
            }

            DamageOutcome hit = DamageResolution.Resolve(
                new HitPoints(WorldLookup.ShieldOf(health), WorldLookup.HullOf(health)),
                amount);

            if (hit.AbsorbedByShield > 0)
            {
                health.shield = hit.After.Shield;
            }

            if (hit.DealtToHull > 0)
            {
                health.hull = hit.After.Hull;
            }

            var npc = target.GetComponent<NPCBehaviour>();
            if (npc != null && hit.Landed)
            {
                var shooter = attacker != null ? attacker.GetComponent<Player>() : null;
                if (shooter != null)
                {

                    NpcDamageLedger.Credit(npc, shooter, hit.TotalDealt);

                    ServerHub.Events?.NoteDamage(attacker, npc, hit.TotalDealt);
                }
            }

            PlayerRef targetRef = FindOwner(target);

            if (targetRef != PlayerRef.None && attacker != null)
            {
                ServerHub.RpcFor(targetRef)?.RPC_SendAttacker_True(attacker.Id);

                _attackerExpiry[new AttackerFlag(target.Id, attacker.Id)] =
                    _clock + CombatMemory;
            }

            if (hit.Fatal)
            {
                OnKilled(attacker, target);
                return true;
            }

            return false;
        }

        private void OnKilled(NetworkObject killer, NetworkObject victim)
        {
            PlayerRef killerRef = FindOwner(killer);
            PlayerRef victimRef = FindOwner(victim);

            Account killerAccount = ServerHub.AccountFor(killerRef);
            Account victimAccount = ServerHub.AccountFor(victimRef);

            if (victimAccount != null)
            {
                victimAccount.deaths++;

                ServerHub.Events?.NoteKill(
                    ReferenceEquals(killerAccount, victimAccount) ? null : killerAccount,
                    victimAccount);

                victimAccount.cargo.Clear();
                AccountStore.MarkDirty(victimAccount);
                StopFiring(victimRef);
                ClearAttackersOf(victimRef);

                Wire.SendCargo(victimRef, victimAccount);
            }
            else
            {

            }

            if (killerAccount != null)
            {
                killerAccount.lifetimeKills++;
                killerAccount.weeklyKills++;

                long fame = victimAccount != null ? 250 : 40;
                killerAccount.lifetimeFame += fame;
                killerAccount.weeklyFame += fame;

                ServerHub.Progression?.AwardExperience(
                    killerAccount, victimAccount != null ? 900 : 220);

                var deadNpc = victim != null ? victim.GetComponentInChildren<NPCBehaviour>() : null;
                ServerHub.Missions?.OnKill(
                    killerAccount, deadNpc != null ? deadNpc.data : null, victimAccount == null);

                if (victimAccount != null && !ReferenceEquals(killerAccount, victimAccount))
                {
                    if (victimAccount.faction != killerAccount.faction)
                    {
                        ServerHub.Missions?.OnFactionKill(killerAccount);
                    }

                    if (ServerHub.Pvp != null && ServerHub.Pvp.InBattleZone(killerAccount))
                    {
                        ServerHub.Missions?.OnBattleZoneKill(killerAccount);
                    }
                }

                if (victimAccount == null && deadNpc != null)
                {
                    ServerHub.Events?.NoteNpcKill(killerAccount, deadNpc);
                }

                ServerHub.Achievements?.Bump(killerAccount, victimAccount != null
                    ? AchievementService.PvpKills
                    : AchievementService.NpcKills);

                if (victimAccount == null && deadNpc != null &&
                    NpcService.IsBossKill(deadNpc.data))
                {
                    ServerHub.Achievements?.Bump(killerAccount, AchievementService.BossKills);
                    ServerHub.Achievements?.Bump(
                        killerAccount, AchievementService.BossKeyFor(deadNpc.data.name));
                }
                AccountStore.MarkDirty(killerAccount);
            }

            if (victimRef != PlayerRef.None)
            {
                ServerLog.Info(
                    $"{victimAccount?.nickname ?? "?"} killed by " +
                    $"{killerAccount?.nickname ?? "?"}");
            }
        }

        public void Respawn(PlayerRef player)
        {
            Account account = ServerHub.AccountFor(player);
            if (account == null || ServerHub.Runner == null)
            {
                return;
            }

            NetworkObject obj = WorldLookup.ObjectOf(player);
            if (obj == null)
            {
                return;
            }

            ServerHub.Progression?.ApplyLevelStats(account);

            Health health = WorldLookup.HealthOf(obj);
            if (health != null)
            {
                health.hull = health.maxHull > 0 ? health.maxHull : account.hullMax;
                health.shield = health.maxShield > 0 ? health.maxShield : account.shieldMax;
                account.hull = health.hull;
                account.shield = health.shield;
            }

            Vector3 home = PlayerDirector.HomeStation(account.faction);

            if (home == Vector3.zero)
            {
                ProjectBindings bindings = ProjectBindings.Instance;
                home = bindings != null
                    ? bindings.NextSpawnPosition(0)
                    : new Vector3(0f, 5f, 0f);
            }

            obj.transform.position = home;

            account.x = home.x;
            account.y = home.y;
            account.z = home.z;

            StopFiring(player);

            ClearAttackersOf(player);

            AccountStore.MarkDirty(account);

            ServerLog.Info($"{account.nickname} respawned at {home}");
        }

        public void PlayerDied(PlayerRef victim)
        {
            Account account = ServerHub.AccountFor(victim);
            if (account == null)
            {
                return;
            }

            account.deaths++;

            ServerHub.Events?.NoteKill(null, account);

            account.cargo.Clear();
            AccountStore.MarkDirty(account);

            StopFiring(victim);
            ClearAttackersOf(victim);
            Wire.SendCargo(victim, account);

            ServerLog.Info($"{account.nickname} was killed");
        }

        public bool RepairHull(Account account, float targetFraction)
        {
            return Repair(account, targetFraction, hull: true);
        }

        public bool RepairShield(Account account, float targetFraction)
        {
            return Repair(account, targetFraction, hull: false);
        }

        private bool Repair(Account account, float targetFraction, bool hull)
        {
            if (account == null)
            {
                return false;
            }

            Health health = WorldLookup.HealthOf(ServerHub.RefFor(account));
            if (health == null)
            {
                ServerLog.Warn($"repair: no Health for {account.nickname}");
                return false;
            }

            int current = hull ? health.hull : health.shield;
            int max = hull ? health.maxHull : health.maxShield;
            if (max <= 0)
            {
                return false;
            }

            int points = Mathf.RoundToInt(max * Mathf.Clamp01(targetFraction)) - current;
            if (points <= 0)
            {
                return false;
            }

            float price = hull ? GameData.HullRepairPrice : GameData.ShieldRepairPrice;
            float discount = account.IsVip ? 0.25f : 1f;
            int cost = Mathf.Max(1, Mathf.RoundToInt(points * price * discount));

            string currency = hull ? "borax" : "credits";
            long held = hull ? account.borax : account.credits;
            if (held < cost)
            {
                ServerLog.Info($"repair refused for {account.nickname}: {points} "
                    + $"{(hull ? "hull" : "shield")} costs {cost} {currency}, holds {held}");
                return false;
            }

            if (hull)
            {
                account.borax -= cost;
                health.hull = current + points;
                account.hull = health.hull;
                account.hullMax = max;
            }
            else
            {
                account.credits -= cost;
                health.shield = current + points;
                account.shield = health.shield;
                account.shieldMax = max;
            }

            AccountStore.MarkDirty(account);
            ServerHub.Accounts?.PushWallet(ServerHub.RefFor(account), account);

            ServerLog.Info($"{account.nickname} repaired {points} "
                + $"{(hull ? "hull" : "shield")} for {cost} {currency}");
            return true;
        }

        public bool UseRepairBattery(Account account)
        {
            if (account == null)
            {
                return false;
            }

            RepairBatteryData battery = FittedBattery(account);
            if (battery == null)
            {
                ServerLog.Info($"{account.nickname} used a repair battery with none fitted");
                return false;
            }

            Health health = WorldLookup.HealthOf(ServerHub.RefFor(account));
            if (health == null)
            {
                return false;
            }

            bool hull = battery.repairType == RepairType.Hull;
            int max = hull ? health.maxHull : health.maxShield;
            int current = hull ? health.hull : health.shield;
            if (max <= 0 || current >= max)
            {
                return false;
            }

            int points = Mathf.Max(1, Mathf.RoundToInt(max * battery.repairPercentage));
            int healed = Math.Min(max, current + points);

            if (hull)
            {
                health.hull = healed;
                account.hull = healed;
                account.hullMax = max;
            }
            else
            {
                health.shield = healed;
                account.shield = healed;
                account.shieldMax = max;
            }

            AccountStore.MarkDirty(account);
            ServerLog.Info($"{account.nickname} used {battery.name}: "
                + $"+{healed - current} {(hull ? "hull" : "shield")}");
            return true;
        }

        private static RepairBatteryData FittedBattery(Account account)
        {
            DataManager data = GameData.Data;
            if (data == null || data.extraDatas == null)
            {
                return null;
            }

            foreach (int index in account.equippedExtras)
            {
                if (index < 0 || index >= data.extraDatas.Count)
                {
                    continue;
                }

                if (data.extraDatas[index] is RepairBatteryData battery)
                {
                    return battery;
                }
            }

            return null;
        }

        private void ClearAttackersOf(PlayerRef victim)
        {
            if (victim == PlayerRef.None || _attackerExpiry.Count == 0 ||
                ServerHub.Runner == null)
            {
                return;
            }

            _cleared.Clear();
            foreach (KeyValuePair<AttackerFlag, float> kv in _attackerExpiry)
            {

                NetworkObject obj = ServerHub.Runner.FindObject(kv.Key.Victim);
                if (obj != null && FindOwner(obj) == victim)
                {
                    _cleared.Add(kv.Key);
                }
            }

            PlayerRPC rpc = ServerHub.RpcFor(victim);

            foreach (AttackerFlag flag in _cleared)
            {
                _attackerExpiry.Remove(flag);
                rpc?.RPC_SendAttacker_False(flag.Attacker);
            }

            _cleared.Clear();
        }

        private void ExpireAttackers()
        {
            if (_attackerExpiry.Count == 0)
            {
                return;
            }

            _lapsed.Clear();
            foreach (KeyValuePair<AttackerFlag, float> kv in _attackerExpiry)
            {
                if (_clock >= kv.Value)
                {
                    _lapsed.Add(kv.Key);
                }
            }

            foreach (AttackerFlag flag in _lapsed)
            {
                _attackerExpiry.Remove(flag);

                NetworkObject victim = ServerHub.Runner?.FindObject(flag.Victim);
                if (victim == null)
                {
                    continue;
                }

                PlayerRef owner = FindOwner(victim);
                if (owner != PlayerRef.None)
                {
                    ServerHub.RpcFor(owner)?.RPC_SendAttacker_False(flag.Attacker);
                }
            }
        }

        public static PlayerRef FindOwner(NetworkObject obj)
        {
            return obj != null && obj.InputAuthority.IsRealPlayer
                ? obj.InputAuthority
                : PlayerRef.None;
        }
    }
}
