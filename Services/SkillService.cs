using System.Collections.Generic;
using Fusion;
using TidalNexus.StandaloneServer.Data;
using UnityEngine;

namespace TidalNexus.StandaloneServer.Services
{

    public sealed class SkillService
    {

        public sealed class Cooldowns
        {
            public float Slot1;
            public float Slot2;
        }

        private float _clock;

        public void Tick(float deltaTime)
        {
            _clock += deltaTime;
        }

        private static float BatteryFraction => ServerHub.Config?.BatteryFraction ?? 1f;

        public static float DamageBoost => ServerHub.Config?.DamageBoost ?? 1.3f;

        public bool Use(Account account, PlayerRef player, int slot, NetworkId target)
        {
            PlayerSession session = ServerHub.SessionFor(player);
            if (account == null || session == null)
            {
                return false;
            }

            NetworkObject obj = WorldLookup.ObjectOf(player);
            Player self = obj != null ? obj.GetComponentInChildren<Player>() : null;
            if (self == null)
            {
                return false;
            }

            CoreModuleData module = ModuleFor(self);
            if (module == null)
            {

                return false;
            }

            bool first = slot == 1;
            SkillType skill = first ? module.skill1 : module.skill2;
            int cost = first ? module.energyUsage1 : module.energyUsage2;
            float cooldown = first ? module.cooldown1 : module.cooldown2;

            Cooldowns ready = session.State<Cooldowns>();

            if (_clock < (first ? ready.Slot1 : ready.Slot2))
            {
                return false;
            }

            if (account.energy < cost)
            {
                return false;
            }

            if (!Apply(skill, account, player, self, target))
            {
                return false;
            }

            if (first)
            {
                ready.Slot1 = _clock + cooldown;
            }
            else
            {
                ready.Slot2 = _clock + cooldown;
            }

            if (cost > 0)
            {
                account.energy -= cost;
                AccountStore.MarkDirty(account);
                ServerHub.Accounts?.PushState(player, account);
            }

            ServerHub.RpcFor(player)?.RPC_SkillRelay(skill);

            ServerLog.Info($"{account.nickname} used {skill}");
            return true;
        }

        private bool Apply(SkillType skill, Account account, PlayerRef player,
            Player self, NetworkId target)
        {
            switch (skill)
            {
                case SkillType.HullBattery:
                    return Restore(self, account, hull: true);

                case SkillType.ShieldBattery:
                    return Restore(self, account, hull: false);

                case SkillType.DamageBoost:
                    self.StartCoroutine(self.CombatModuleF2());
                    return true;

                case SkillType.Invulnerability:
                    self.StartCoroutine(self.TankModuleF2());
                    return true;

                case SkillType.SpeedBoost:
                    self.StartCoroutine(self.ReconModuleF2());
                    return true;

                case SkillType.Lightweight:
                    self.StartCoroutine(self.TradeModuleF1());
                    return true;

                case SkillType.RadarCloak:
                    self.StartCoroutine(self.ReconModuleF1());
                    return true;

                case SkillType.Cleanse:

                    self.iceStacks?.Clear();
                    self.networkValues.iceStacks = 0;
                    return true;

                case SkillType.Horn:

                    return true;

                case SkillType.SuperTorpedo:
                    return SuperTorpedo(account, player, target);

                default:
                    return false;
            }
        }

        private static bool Restore(Player self, Account account, bool hull)
        {

            Health health = self.health;
            if (!WorldLookup.IsSpawned(health))
            {
                return false;
            }

            if (hull)
            {
                if (health.maxHull <= 0 || health.hull >= health.maxHull)
                {
                    return false;
                }

                health.hull = Mathf.Min(health.maxHull,
                    health.hull + Mathf.RoundToInt(health.maxHull * BatteryFraction));
                account.hull = health.hull;
            }
            else
            {
                if (health.maxShield <= 0 || health.shield >= health.maxShield)
                {
                    return false;
                }

                health.shield = Mathf.Min(health.maxShield,
                    health.shield + Mathf.RoundToInt(health.maxShield * BatteryFraction));
                account.shield = health.shield;
            }

            AccountStore.MarkDirty(account);
            return true;
        }

        private static bool SuperTorpedo(Account account, PlayerRef player, NetworkId target)
        {
            if (!target.IsValid || ServerHub.Runner?.FindObject(target) == null)
            {
                return false;
            }

            PlayerRPC rpc = ServerHub.RpcFor(player);
            if (rpc == null)
            {
                return false;
            }

            int index = account.torpedoIndex > 0 ? account.torpedoIndex : 1;
            rpc.RPC_DropTorpedoRelay(index, false, target, 1f, true, 0);
            return true;
        }

        private static CoreModuleData ModuleFor(Player self)
        {
            ExtraData.ExtraType fitted = self.networkValues.coreModule;
            if (fitted == ExtraData.ExtraType.None)
            {
                return null;
            }

            List<ExtraData> extras = GameData.Data?.extraDatas;
            if (extras == null)
            {
                return null;
            }

            foreach (ExtraData extra in extras)
            {
                if (extra != null && extra.type == fitted && extra is CoreModuleData module)
                {
                    return module;
                }
            }

            return null;
        }
    }
}
