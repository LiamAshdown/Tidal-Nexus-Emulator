using System;
using System.Collections.Generic;
using TidalNexus.StandaloneServer.Core;
using TidalNexus.StandaloneServer.Data;

namespace TidalNexus.StandaloneServer.Services
{

    public sealed class EconomyService
    {

        private const long DefaultPrice = 100;

        private static AreaStation[] _stations;

        private static AreaStation StationObjectFor(StationData data)
        {
            if (data == null)
            {
                return null;
            }

            if (_stations == null || _stations.Length == 0)
            {
                _stations = UnityEngine.Object.FindObjectsByType<AreaStation>(
                    UnityEngine.FindObjectsInactive.Include,
                    UnityEngine.FindObjectsSortMode.None);
            }

            foreach (AreaStation station in _stations)
            {
                if (station != null && station.data == data)
                {
                    return station;
                }
            }

            return null;
        }

        private static StationData StationFor(Account account)
        {
            Fusion.PlayerRef player = ServerHub.RefFor(account);
            if (player == Fusion.PlayerRef.None || ServerHub.Runner == null)
            {
                return null;
            }

            Fusion.NetworkObject obj = ServerHub.Runner.GetPlayerObject(player);
            if (obj == null)
            {
                return null;
            }

            UnityEngine.Vector3 at = obj.transform.position;
            StationData station = GameData.ClosestStation(at);
            AreaStation placed = StationObjectFor(station);
            if (placed == null)
            {
                return null;
            }

            float distance = Enums.Distance2D(placed.transform.position, at);
            if (!TradePricing.InRange(distance))
            {
                ServerLog.Info(
                    $"{account.nickname} is {distance:F1} from {station.name} - too far to trade");
                return null;
            }

            return station;
        }

        public long PriceOf(string itemId)
        {
            CollectableMaterialData material = GameData.Material(itemId);
            return material != null && material.buyPrice > 0 ? material.buyPrice : DefaultPrice;
        }

        public long SellValue(string itemId, int count, StationData station = null)
        {
            CollectableMaterialData material = GameData.Material(itemId);
            if (material == null)
            {
                return TradePricing.Quote(DefaultPrice, count).Total;
            }

            int unit = station != null
                ? GameData.SellPriceAt(material, station)
                : GameData.BestSellPrice(material);

            return TradePricing.QuoteSell(unit, GameData.TradingSellRate, count).Total;
        }

        private static bool TryStationStock(
            CollectableMaterialData material, StationData station, out int rowPrice)
        {
            rowPrice = 0;
            if (material == null || station?.selling == null)
            {
                return false;
            }

            foreach (StationTradeMaterial entry in station.selling)
            {
                if (entry != null && entry.material == material)
                {
                    rowPrice = entry.price;
                    return true;
                }
            }

            return false;
        }

        public int Held(Account account, string itemId)
        {
            foreach (InventoryStack s in account.cargo)
            {
                if (string.Equals(s.itemId, itemId, StringComparison.OrdinalIgnoreCase))
                {
                    return s.count;
                }
            }

            return 0;
        }

        public bool AddCargo(Account account, string itemId, int count)
        {
            if (account == null || string.IsNullOrEmpty(itemId))
            {
                return false;
            }

            if (!CargoHold.Offer(account.CargoUsed, account.cargoMax, count).Fits)
            {
                return false;
            }

            foreach (InventoryStack s in account.cargo)
            {
                if (string.Equals(s.itemId, itemId, StringComparison.OrdinalIgnoreCase))
                {
                    s.count += count;
                    AccountStore.MarkDirty(account);
                    return true;
                }
            }

            account.cargo.Add(new InventoryStack(itemId, count));
            AccountStore.MarkDirty(account);
            return true;
        }

        public bool RemoveCargo(Account account, string itemId, int count)
        {
            if (account == null || count <= 0)
            {
                return false;
            }

            for (int i = 0; i < account.cargo.Count; i++)
            {
                InventoryStack s = account.cargo[i];
                if (!string.Equals(s.itemId, itemId, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (s.count < count)
                {
                    return false;
                }

                s.count -= count;
                if (s.count == 0)
                {
                    account.cargo.RemoveAt(i);
                }

                AccountStore.MarkDirty(account);
                return true;
            }

            return false;
        }

        public long SellCargo(Account account, string itemsJson)
        {
            long earned = 0;

            StationData station = StationFor(account);

            if (station == null)
            {
                ServerLog.Info($"{account.nickname} tried to sell away from a station");
                return 0;
            }

            foreach (KeyValuePair<string, int> item in ParseTradeItems(itemsJson))
            {
                int have = Held(account, item.Key);
                int sell = Math.Min(have, item.Value);
                if (sell <= 0)
                {
                    continue;
                }

                long value = SellValue(item.Key, sell, station);
                if (value <= 0)
                {
                    ServerLog.Info($"  {station.name} does not buy {item.Key}");
                    continue;
                }

                RemoveCargo(account, item.Key, sell);
                earned += value;
                ServerHub.Missions?.OnSold(account, item.Key, sell);
                ServerHub.Achievements?.Bump(account, AchievementService.TradeUnits, sell);
                ServerHub.Achievements?.Bump(account, AchievementService.TradeCredits, value);
                ServerLog.Info($"  sold {sell} x {item.Key} for {value}");
            }

            if (earned > 0)
            {
                account.credits += earned;
                AccountStore.MarkDirty(account);
                ServerLog.Info($"{account.nickname} sold cargo for {earned}");
            }

            return earned;
        }

        public long BuyCargo(Account account, string itemsJson)
        {
            long spent = 0;

            StationData station = StationFor(account);
            if (station == null)
            {
                ServerLog.Info($"{account.nickname} tried to buy away from a station");
                return 0;
            }

            foreach (KeyValuePair<string, int> item in ParseTradeItems(itemsJson))
            {
                CollectableMaterialData material = GameData.Material(item.Key);
                bool stocked = TryStationStock(material, station, out int rowPrice);

                TradeQuote quote = TradePricing.QuoteBuy(
                    stocked, rowPrice, material != null ? material.buyPrice : 0, item.Value);

                if (!quote.Allowed)
                {
                    ServerLog.Info(
                        $"  {station.name} will not sell {item.Value} x {item.Key} " +
                        $"({quote.Refusal})");
                    continue;
                }

                long cost = quote.Total;
                if (cost > account.credits)
                {
                    ServerLog.Info(
                        $"  cannot afford {item.Value} x {item.Key} " +
                        $"({cost} > {account.credits})");
                    continue;
                }

                if (!AddCargo(account, item.Key, item.Value))
                {
                    ServerLog.Info(
                        $"  no room for {item.Value} x {item.Key} " +
                        $"({account.CargoUsed}/{account.cargoMax})");
                    continue;
                }

                account.credits -= cost;
                spent += cost;
                ServerHub.Missions?.OnBought(account, item.Key, item.Value);
                ServerLog.Info($"  bought {item.Value} x {item.Key} for {cost}");
            }

            if (spent > 0)
            {
                AccountStore.MarkDirty(account);
                ServerLog.Info($"{account.nickname} bought cargo for {spent}");
            }

            return spent;
        }

        public void DiscardCargo(Account account, string itemsJson)
        {
            foreach (KeyValuePair<string, int> item in ParseTradeItems(itemsJson))
            {
                RemoveCargo(account, item.Key, item.Value);
            }
        }

        public static IEnumerable<KeyValuePair<string, int>> ParseTradeItems(string json)
        {
            var result = new List<KeyValuePair<string, int>>();
            if (string.IsNullOrWhiteSpace(json))
            {
                return result;
            }

            try
            {
                foreach (System.Text.RegularExpressions.Match m in
                         System.Text.RegularExpressions.Regex.Matches(
                             json,
                             "\"index\"\\s*:\\s*(-?\\d+)\\s*,\\s*\"amount\"\\s*:\\s*(-?\\d+)",
                             System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                {
                    int index = int.Parse(m.Groups[1].Value);
                    int amount = int.Parse(m.Groups[2].Value);
                    if (amount <= 0)
                    {
                        continue;
                    }

                    CollectableMaterialData material = GameData.MaterialByIndex(index);
                    if (material == null)
                    {
                        ServerLog.Warn($"trade: no material with index {index}");
                        continue;
                    }

                    result.Add(new KeyValuePair<string, int>(material.name, amount));
                }
            }
            catch (Exception e)
            {
                ServerLog.Warn($"could not read trade items: {e.Message}");
            }

            return result;
        }

        public static IEnumerable<KeyValuePair<string, int>> ParseItems(string json)
        {
            var result = new List<KeyValuePair<string, int>>();
            if (string.IsNullOrWhiteSpace(json))
            {
                return result;
            }

            try
            {
                foreach (System.Text.RegularExpressions.Match m in
                         System.Text.RegularExpressions.Regex.Matches(
                             json,
                             "\"(?:id|itemId|item|name|key)\"\\s*:\\s*\"([^\"]+)\"\\s*," +
                             "\\s*\"(?:count|amount|qty|quantity|value)\"\\s*:\\s*(\\d+)",
                             System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                {
                    result.Add(new KeyValuePair<string, int>(
                        m.Groups[1].Value, int.Parse(m.Groups[2].Value)));
                }

                if (result.Count == 0)
                {
                    foreach (System.Text.RegularExpressions.Match m in
                             System.Text.RegularExpressions.Regex.Matches(
                                 json, "\"([^\"]+)\"\\s*:\\s*(\\d+)"))
                    {
                        result.Add(new KeyValuePair<string, int>(
                            m.Groups[1].Value, int.Parse(m.Groups[2].Value)));
                    }
                }
            }
            catch (Exception e)
            {
                ServerLog.Warn($"could not parse item list: {e.Message}");
            }

            return result;
        }

        private static float CollectRange =>
            Math.Max(ServerHub.Config?.AutoCollectRange ?? 15f, 15f);

        public bool Collect(Account account, Fusion.NetworkId id, out int unitsTaken, out bool holdFull)
        {
            unitsTaken = 0;
            holdFull = false;

            if (account == null || ServerHub.Runner == null)
            {
                return false;
            }

            Fusion.NetworkObject obj = ServerHub.Runner.FindObject(id);
            if (obj == null)
            {
                ServerLog.Warn($"collect for unknown object {id}");
                return false;
            }

            var collectable = obj.GetComponent<CollectableObject>();
            if (collectable == null)
            {
                ServerLog.Warn($"collect on a non-collectable object {id}");
                return false;
            }

            Fusion.PlayerRef owner = ServerHub.RefFor(account);
            Fusion.NetworkObject ship = owner != Fusion.PlayerRef.None
                ? ServerHub.Runner.GetPlayerObject(owner)
                : null;

            if (ship == null)
            {
                ServerLog.Warn($"collect from {account.nickname} with no ship in the world");
                return false;
            }

            float distance = Enums.Distance2D(obj.transform.position, ship.transform.position);
            if (distance > CollectRange)
            {
                ServerLog.Warn(
                    $"{account.nickname} tried to collect {id} from {distance:F1} away");
                return false;
            }

            var amounts = new List<int>(collectable.materials.Count);
            foreach (CollectableMaterial material in collectable.materials)
            {
                amounts.Add(material?.data != null ? material.amount : 0);
            }

            CollectDecision decision =
                CargoHold.Collect(account.CargoUsed, account.cargoMax, amounts);

            holdFull = decision.HoldFull;
            if (holdFull)
            {
                ServerLog.Info(
                    $"{account.nickname} has no room for {decision.Units} units from {id} " +
                    $"({account.CargoUsed}/{account.cargoMax})");
                return false;
            }

            var looted = new List<Wire.LootLine>();

            foreach (CollectableMaterial material in collectable.materials)
            {

                if (material?.data == null || !CargoHold.Takeable(material.amount))
                {
                    continue;
                }

                if (!AddCargo(account, material.data.name, material.amount))
                {
                    ServerLog.Warn(
                        $"{account.nickname} could not take {material.data.name} - unnamed material");
                    continue;
                }

                unitsTaken += material.amount;

                looted.Add(new Wire.LootLine(
                    global::Log.CollectableLoot, material.amount, material.data.index));

                ServerHub.Missions?.OnCollected(account, material.data.name, material.amount);
                ServerHub.Achievements?.Bump(
                    account, AchievementService.Capsules, material.amount);
            }

            if (unitsTaken > 0)
            {
                ServerLog.Info($"{account.nickname} collected {unitsTaken} units from {id}");
                Wire.SendLootLog(owner, looted.ToArray());
            }

            if (decision.ClearsTheWreck)
            {
                try
                {
                    ServerHub.Runner.Despawn(obj);
                }
                catch (Exception e)
                {
                    ServerLog.Warn($"despawn of collected object failed: {e.Message}");
                }
            }

            return unitsTaken > 0;
        }

        public enum MerchantResult
        {
            Ok,
            BadAmount,
            UnknownItem,
            Locked,
            NotSold,
            CannotAfford,
            NoCounter,
        }

        public MerchantResult TryMerchantBuy(
            Account account, int equipmentId, int amount, Enums.Currency currency)
        {

            const int PerPurchaseLimit = 10000;

            if (account == null || amount <= 0 || amount > PerPurchaseLimit)
            {
                return MerchantResult.BadAmount;
            }

            EquipmentData item = FindEquipment(equipmentId);
            if (item == null)
            {
                return MerchantResult.UnknownItem;
            }

            int level = GameData.LevelForExperience(account.experience);
            if (item.unlockLevel > level)
            {
                return MerchantResult.Locked;
            }

            int unit = item.GetPrice(currency);
            if (unit <= 0)
            {
                return MerchantResult.NotSold;
            }

            long cost = (long)unit * amount;
            if (!Charge(account, currency, cost))
            {
                return MerchantResult.CannotAfford;
            }

            int perPack = item.buyAmount > 0 ? item.buyAmount : 1;

            if (item is EnergyAmmoData)
            {
                account.energy += perPack * amount;
                AccountStore.MarkDirty(account);
                ServerLog.Info($"{account.nickname} bought {amount}x {item.name} "
                    + $"({perPack * amount} energy) for {cost} {currency}");
                return MerchantResult.Ok;
            }

            if (item is ExtraData extra)
            {
                int index = GameData.Data.extraDatas.IndexOf(extra);
                if (index < 0)
                {
                    Refund(account, currency, cost);
                    ServerLog.Warn($"merchant: extra '{item.name}' is not in extraDatas - refunded");
                    return MerchantResult.NoCounter;
                }

                for (int i = 0; i < amount; i++)
                {
                    account.extras.Add(index);
                }

                AccountStore.MarkDirty(account);
                ServerLog.Info($"{account.nickname} bought {amount}x {item.name} "
                    + $"for {cost} {currency}");
                return MerchantResult.Ok;
            }

            long stacked = (long)perPack * amount;

            if (!AddAmmo(account, item.name, (int)Math.Min(stacked, int.MaxValue)))
            {

                Refund(account, currency, cost);
                ServerLog.Warn($"merchant: no counter for '{item.name}' - refunded {cost}");
                return MerchantResult.NoCounter;
            }

            AccountStore.MarkDirty(account);
            ServerLog.Info(
                $"{account.nickname} bought {amount}x {item.name} " +
                $"({perPack * amount} rounds) for {cost} {currency}");

            return MerchantResult.Ok;
        }

        private static EquipmentData FindEquipment(int equipmentId)
        {
            DataManager data = GameData.Data;
            if (data == null)
            {
                return null;
            }

            var lists = new IEnumerable<EquipmentData>[]
            {
                data.weaponAmmoDatas, data.torpedoAmmoDatas, data.energyAmmoDatas,
                data.bombAmmoDatas, data.mineAmmoDatas, data.decoyAmmoDatas,
                data.extraDatas,
            };

            foreach (IEnumerable<EquipmentData> list in lists)
            {
                if (list == null)
                {
                    continue;
                }

                foreach (EquipmentData item in list)
                {
                    if (item != null && item.equipmentID == equipmentId)
                    {
                        return item;
                    }
                }
            }

            return null;
        }

        private static bool Charge(Account account, Enums.Currency currency, long cost)
        {
            switch (currency)
            {
                case Enums.Currency.Credits when account.credits >= cost:
                    account.credits -= cost;
                    return true;

                case Enums.Currency.Borax when account.borax >= cost:
                    account.borax -= (int)cost;
                    return true;
                case Enums.Currency.BattlePoints when account.battlePoints >= cost:
                    account.battlePoints -= (int)cost;
                    return true;
                default:
                    return false;
            }
        }

        private static void Refund(Account account, Enums.Currency currency, long cost)
        {
            switch (currency)
            {
                case Enums.Currency.Credits: account.credits += cost; break;
                case Enums.Currency.Borax: account.borax += (int)cost; break;
                case Enums.Currency.BattlePoints: account.battlePoints += (int)cost; break;
            }
        }

        private const int MaxAmmoHeld = 1_000_000_000;

        private static bool Stack(ref int counter, int rounds)
        {
            if (rounds <= 0)
            {

                return true;
            }

            long total = (long)counter + rounds;
            counter = (int)Math.Min(total, MaxAmmoHeld);
            return true;
        }

        public static bool AddAmmo(Account account, string itemName, int rounds)
        {
            switch (itemName)
            {
                case "WeaponAmmo1": return Stack(ref account.ammo1, rounds);
                case "WeaponAmmo2": return Stack(ref account.ammo2, rounds);
                case "WeaponAmmo3": return Stack(ref account.ammo3, rounds);
                case "WeaponAmmo4": return Stack(ref account.ammo4, rounds);
                case "WeaponAmmo5": return Stack(ref account.ammo5, rounds);

                case "PhotonAmmo1": return Stack(ref account.photonAmmo1, rounds);
                case "PhotonAmmo2": return Stack(ref account.photonAmmo2, rounds);
                case "PhotonAmmo3": return Stack(ref account.photonAmmo3, rounds);
                case "PhotonAmmo4": return Stack(ref account.photonAmmo4, rounds);

                case "TorpedoAmmo1": return Stack(ref account.torpedo1, rounds);
                case "TorpedoAmmo2": return Stack(ref account.torpedo2, rounds);
                case "TorpedoAmmo3": return Stack(ref account.torpedo3, rounds);
                case "TorpedoAmmo4": return Stack(ref account.torpedo4, rounds);
                case "TorpedoAmmoIce": return Stack(ref account.torpedoice, rounds);

                case "PhotonTorpedoAmmo1": return Stack(ref account.photonTorpedo1, rounds);
                case "PhotonTorpedoAmmo2": return Stack(ref account.photonTorpedo2, rounds);
                case "PhotonTorpedoAmmo3": return Stack(ref account.photonTorpedo3, rounds);
                case "PhotonTorpedoAmmo4": return Stack(ref account.photonTorpedo4, rounds);

                case "BombAmmo1": return Stack(ref account.bomb1, rounds);
                case "BombAmmo2": return Stack(ref account.bomb2, rounds);
                case "BombAmmo3": return Stack(ref account.bomb3, rounds);
                case "PhotonBombAmmo1": return Stack(ref account.photonBomb1, rounds);
                case "PhotonBombAmmo2": return Stack(ref account.photonBomb2, rounds);
                case "PhotonBombAmmo3": return Stack(ref account.photonBomb3, rounds);

                case "MineAmmo1": return Stack(ref account.mine1, rounds);
                case "MineAmmo2": return Stack(ref account.mine2, rounds);
                case "MineAmmo3": return Stack(ref account.mine3, rounds);
                case "PhotonMineAmmo1": return Stack(ref account.photonMine1, rounds);
                case "PhotonMineAmmo2": return Stack(ref account.photonMine2, rounds);
                case "PhotonMineAmmo3": return Stack(ref account.photonMine3, rounds);

                case "DecoyAmmo1": return Stack(ref account.decoy1, rounds);
                case "DecoyAmmo2": return Stack(ref account.decoy2, rounds);
                case "DecoyAmmo3": return Stack(ref account.decoy3, rounds);

                default: return false;
            }
        }

        private static bool? _storeGrants;

        private static bool StoreGrantsAllowed
        {
            get
            {
                if (_storeGrants == null)
                {
                    string raw = Environment.GetEnvironmentVariable("TN_STORE_GRANTS");
                    _storeGrants =
                        string.Equals(raw, "1", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(raw, "true", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(raw, "yes", StringComparison.OrdinalIgnoreCase);
                }

                return _storeGrants.Value;
            }
        }

        private static bool RefuseGrant(Account account, string what)
        {
            ServerLog.Warn(
                $"refused {what} for {account?.nickname ?? "?"} - no payment backend exists; " +
                "set TN_STORE_GRANTS=1 to allow store grants on a development server");
            return false;
        }

        private const int MaxVipGrantDays = 365;
        private const long MaxVipHorizonSeconds = 3650L * 86400L;
        private const int MaxBoraxGrant = 100000;
        private const long FounderPackPremium = 5000;

        public bool GrantVip(Account account, int days)
        {
            if (account == null)
            {
                return false;
            }

            if (!StoreGrantsAllowed)
            {
                return RefuseGrant(account, $"{days}d of VIP");
            }

            if (days <= 0 || days > MaxVipGrantDays)
            {
                ServerLog.Warn($"refused a VIP grant of {days}d for {account.nickname}");
                return false;
            }

            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            long from = Math.Max(account.vipUntilUnix, now);

            long until = Math.Min(from + (long)days * 86400L, now + MaxVipHorizonSeconds);
            if (until <= account.vipUntilUnix)
            {
                ServerLog.Info($"{account.nickname} is already at the VIP ceiling");
                return false;
            }

            account.vipUntilUnix = until;
            AccountStore.MarkDirty(account);
            ServerLog.Info($"{account.nickname} VIP for {days}d");
            return true;
        }

        public bool GrantBorax(Account account, int amount)
        {
            if (account == null)
            {
                return false;
            }

            if (!StoreGrantsAllowed)
            {
                return RefuseGrant(account, $"{amount} borax");
            }

            if (amount <= 0 || amount > MaxBoraxGrant)
            {
                ServerLog.Warn($"refused a borax grant of {amount} for {account.nickname}");
                return false;
            }

            long total = (long)account.borax + amount;
            account.borax = (int)Math.Min(total, Account.MaxBorax);
            AccountStore.MarkDirty(account);
            ServerLog.Info($"{account.nickname} granted {amount} borax");
            return true;
        }

        public bool GrantFounderPack(Account account)
        {
            if (account == null || account.founderPack)
            {
                return false;
            }

            if (!StoreGrantsAllowed)
            {
                return RefuseGrant(account, "the founder pack");
            }

            account.founderPack = true;
            account.premium += FounderPackPremium;
            GrantVip(account, 30);
            ServerHub.Progression?.GrantTitleNamed(account, "Founder");
            AccountStore.MarkDirty(account);
            return true;
        }

        public bool TryClaimVipReward(Account account)
        {
            if (!account.IsVip)
            {
                return false;
            }

            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            if (now - account.lastVipRewardUnix < 86400)
            {
                return false;
            }

            account.lastVipRewardUnix = now;
            account.credits += 25000;
            account.premium += 100;
            AccountStore.MarkDirty(account);
            return true;
        }
    }
}
