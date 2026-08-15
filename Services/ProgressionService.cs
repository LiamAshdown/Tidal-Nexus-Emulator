using System;
using System.Collections.Generic;
using TidalNexus.StandaloneServer.Data;
using UnityEngine;

namespace TidalNexus.StandaloneServer.Services
{

    public sealed class ProgressionService
    {
        public int MaxLevel => GameData.MaxLevel;

        public long ExperienceForLevel(int level) => GameData.ExperienceForLevel(level);

        public int LevelForExperience(long experience) =>
            GameData.LevelForExperience(experience);

        public void LevelProgress(Account account, out long into, out long span)
        {
            long floor = ExperienceForLevel(account.level);
            long ceiling = ExperienceForLevel(Math.Min(account.level + 1, MaxLevel));
            into = Math.Max(0, account.experience - floor);
            span = Math.Max(1, ceiling - floor);
        }

        public int AwardExperience(Account account, long amount)
        {
            if (account == null || amount <= 0)
            {
                return 0;
            }

            amount = (long)Math.Max(1, amount * GameData.ExperienceRate);
            account.experience += amount;
            int newLevel = LevelForExperience(account.experience);
            int gained = Math.Max(0, newLevel - account.level);

            if (gained > 0)
            {
                account.level = newLevel;
                ApplyLevelStats(account);
                ServerLog.Info($"{account.nickname} reached level {newLevel}");

                ServerHub.Achievements?.Evaluate(account);
            }

            AccountStore.MarkDirty(account);
            return gained;
        }

        public void ApplyLevelStats(Account account)
        {
            int baseHull = 3000 + (account.level - 1) * 130;
            int baseShield = 3000 + (account.level - 1) * 130;

            account.hullMax = baseHull + account.hpx * 250;
            account.shieldMax = baseShield + account.spx * 250;
            account.cargoMax = 500 + account.level * 15;

            account.hull = Math.Min(account.hull, account.hullMax);
            account.shield = Math.Min(account.shield, account.shieldMax);
        }

        public bool TryPrestige(Account account)
        {
            if (account == null || account.level < MaxLevel)
            {
                return false;
            }

            account.prestige++;
            account.level = 1;
            account.experience = 0;
            ApplyLevelStats(account);
            AccountStore.MarkDirty(account);
            ServerLog.Info($"{account.nickname} prestiged to {account.prestige}");
            return true;
        }

        public long UpgradeCost(int currentPoints)
        {
            return 5000L + 2500L * currentPoints;
        }

        public bool TryUpgradeHpx(Account account)
        {
            long cost = UpgradeCost(account.hpx);
            if (account.credits < cost)
            {
                return false;
            }

            account.credits -= cost;
            account.hpx++;
            ApplyLevelStats(account);
            AccountStore.MarkDirty(account);
            return true;
        }

        public bool TryUpgradeSpx(Account account)
        {
            long cost = UpgradeCost(account.spx);
            if (account.credits < cost)
            {
                return false;
            }

            account.credits -= cost;
            account.spx++;
            ApplyLevelStats(account);
            AccountStore.MarkDirty(account);
            return true;
        }

        public bool TryUpgradeItem(Account account, int equipmentID, int slot,
            Enums.Currency currency)
        {
            if (account == null || slot < 0 || slot > 9)
            {
                return false;
            }

            if (!FindUpgrade(equipmentID, out SlotTable table, out int digit,
                    out EquipmentData item))
            {
                ServerLog.Warn($"upgrade for unknown equipment {equipmentID}");
                return false;
            }

            if (slot == 0 && digit == 0)
            {
                ServerLog.Warn("refusing to write 0 into slot 0");
                return false;
            }

            int unlocked = GameData.Settings != null
                ? GameData.Settings.GetSlotCount(account.level)
                : 1;

            if (slot >= unlocked)
            {
                ServerLog.Info($"{account.nickname} tried to fit slot {slot} of {unlocked}");
                return false;
            }

            long cost = item.GetPrice(currency);
            if (cost <= 0)
            {
                ServerLog.Warn($"equipment {equipmentID} has no {currency} price");
                return false;
            }

            if (!Charge(account, currency, cost))
            {
                return false;
            }

            table.Set(account, WriteDigit(table.Get(account), slot, digit));

            AccountStore.MarkDirty(account);
            ServerLog.Info($"{account.nickname} upgraded {table.Name} slot {slot} to "
                + $"{item.name} (tier {item.tier}) for {cost} {currency}");
            return true;
        }

        private sealed class SlotTable
        {
            public string Name;
            public Func<DataManager, IReadOnlyList<EquipmentData>> Items;
            public Func<Account, long> Get;
            public Action<Account, long> Set;
        }

        private static readonly SlotTable[] Tables =
        {
            new SlotTable
            {
                Name = "Weapon",
                Items = d => d.weaponDatas,
                Get = a => a.weaponSlots,
                Set = (a, v) => a.weaponSlots = v,
            },
            new SlotTable
            {
                Name = "Hull",
                Items = d => d.hullDatas,
                Get = a => a.hullSlots,
                Set = (a, v) => a.hullSlots = v,
            },
            new SlotTable
            {
                Name = "Shield",
                Items = d => d.shieldDatas,
                Get = a => a.shieldSlots,
                Set = (a, v) => a.shieldSlots = v,
            },
            new SlotTable
            {
                Name = "Turbine",
                Items = d => d.turbineDatas,
                Get = a => a.turbineSlots,
                Set = (a, v) => a.turbineSlots = v,
            },
        };

        private static bool FindUpgrade(int equipmentID, out SlotTable table,
            out int digit, out EquipmentData item)
        {
            table = null;
            digit = 0;
            item = null;

            DataManager data = GameData.Data;
            if (data == null)
            {
                return false;
            }

            foreach (SlotTable candidate in Tables)
            {
                IReadOnlyList<EquipmentData> items = candidate.Items(data);
                if (items == null)
                {
                    continue;
                }

                for (int i = 0; i < items.Count; i++)
                {
                    if (items[i] == null || items[i].equipmentID != equipmentID)
                    {
                        continue;
                    }

                    table = candidate;
                    digit = i;
                    item = items[i];
                    return true;
                }
            }

            return false;
        }

        private static long WriteDigit(long slots, int slot, int digit)
        {
            string text = slots.ToString();

            if (text.Length < 10)
            {
                text = text.PadRight(10, '0');
            }

            var chars = text.ToCharArray();
            chars[slot] = (char)('0' + Mathf.Clamp(digit, 0, 9));

            return long.TryParse(new string(chars), out long parsed) ? parsed : slots;
        }

        private static bool Charge(Account account, Enums.Currency currency, long cost)
        {
            switch (currency)
            {
                case Enums.Currency.Borax:
                    if (account.borax < cost) { return false; }
                    account.borax -= (int)cost;
                    return true;

                case Enums.Currency.BattlePoints:
                    if (account.battlePoints < cost) { return false; }
                    account.battlePoints -= (int)cost;
                    return true;

                default:
                    if (account.credits < cost) { return false; }
                    account.credits -= cost;
                    return true;
            }
        }

        public bool TryEquipExtra(Account account, int index)
        {
            if (!account.extras.Contains(index) || account.equippedExtras.Contains(index))
            {
                return false;
            }

            int slots = 2 + account.level / 20;
            if (account.equippedExtras.Count >= slots)
            {
                return false;
            }

            account.equippedExtras.Add(index);
            AccountStore.MarkDirty(account);
            return true;
        }

        public bool TryUnequipExtra(Account account, int index)
        {
            bool removed = account.equippedExtras.Remove(index);
            if (removed)
            {
                AccountStore.MarkDirty(account);
            }

            return removed;
        }

        public bool TrySetTitle(Account account, int title)
        {

            if (title >= 0)
            {
                TitleData data = TitleById(title);
                if (data == null || !Unlocks.Meets(data, account))
                {
                    ServerLog.Info($"{account.nickname} cannot wear title {title}");
                    return false;
                }
            }

            account.title = title;
            AccountStore.MarkDirty(account);
            return true;
        }

        private static TitleData TitleById(int index)
        {
            List<TitleData> titles = GameData.Data?.titles;
            if (titles == null)
            {
                return null;
            }

            foreach (TitleData title in titles)
            {
                if (title != null && title.index == index)
                {
                    return title;
                }
            }

            return null;
        }

        public void GrantUnlockable(Account account, Unlockable unlockable)
        {
            if (account == null || unlockable == null)
            {
                return;
            }

            switch (unlockable)
            {
                case TitleData title:
                    Add(account.titles, title.index, account, $"title \"{title.title}\"");
                    break;
                case SkinData skin:
                    Add(account.skins, skin.index, account, "a skin");
                    break;
                case ShipData ship:
                    Add(account.designs, ship.index, account, "a design");
                    break;
                case SentryDesignData sentry:
                    Add(account.sentryDesigns, sentry.index, account, "a sentry design");
                    break;
                case HpxDesignData hpx:
                    Add(account.hpxDesigns, hpx.index, account, "an HPX design");
                    break;
                case SpxDesignData spx:
                    Add(account.spxDesigns, spx.index, account, "an SPX design");
                    break;
                default:
                    ServerLog.Info($"unhandled unlockable reward {unlockable.name}");
                    break;
            }
        }

        private static void Add(List<int> owned, int index, Account account, string what)
        {
            if (index < 0 || owned.Contains(index))
            {
                return;
            }

            owned.Add(index);
            AccountStore.MarkDirty(account);
            ServerLog.Info($"{account.nickname} earned {what}");
        }

        public bool TrySetDesign(Account account, int design, int skin = -1)
        {
            bool changed = false;

            if (design >= 0 && account.designs.Contains(design))
            {
                account.design = design;
                changed = true;
            }

            if (skin >= 0 && account.skins.Contains(skin))
            {
                account.skin = skin;
                changed = true;
            }

            if (changed)
            {
                AccountStore.MarkDirty(account);
            }

            return changed;
        }

        public bool TrySetSentryDesign(Account account, int design)
        {
            if (!account.sentryDesigns.Contains(design))
            {
                return false;
            }

            account.sentryDesign = design;
            AccountStore.MarkDirty(account);
            return true;
        }

        public bool TrySetHpxDesign(Account account, int design)
        {
            if (!account.hpxDesigns.Contains(design))
            {
                return false;
            }

            account.hpxDesign = design;
            AccountStore.MarkDirty(account);
            return true;
        }

        public bool TrySetSpxDesign(Account account, int design)
        {
            if (!account.spxDesigns.Contains(design))
            {
                return false;
            }

            account.spxDesign = design;
            AccountStore.MarkDirty(account);
            return true;
        }

        public void Grant(Account account, int title = -1, int design = -1)
        {
            if (title >= 0 && !account.titles.Contains(title))
            {
                account.titles.Add(title);
            }

            if (design >= 0 && !account.designs.Contains(design))
            {
                account.designs.Add(design);
            }

            AccountStore.MarkDirty(account);
        }

        public bool GrantTitleNamed(Account account, string titleName)
        {
            if (account == null || string.IsNullOrEmpty(titleName) ||
                GameData.Data?.titles == null)
            {
                return false;
            }

            foreach (TitleData candidate in GameData.Data.titles)
            {
                if (candidate != null &&
                    string.Equals(candidate.title, titleName,
                        System.StringComparison.OrdinalIgnoreCase))
                {
                    Grant(account, title: candidate.index);
                    return true;
                }
            }

            ServerLog.Warn($"no shipped title named '{titleName}'");
            return false;
        }
    }
}
