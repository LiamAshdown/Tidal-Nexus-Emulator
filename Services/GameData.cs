using UnityEngine;

namespace TidalNexus.StandaloneServer.Services
{

    public static class GameData
    {
        private const string RatesPath = "ServerRateSettings";
        private const string SettingsPath = "GameSettings";

        private static ServerRateSettingData _rates;
        private static bool _searched;

        private static GameSettingsData _settings;
        private static bool _settingsSearched;

        public static GameSettingsData Settings
        {
            get
            {
                if (_settings != null || _settingsSearched)
                {
                    return _settings;
                }

                _settingsSearched = true;
                _settings = Resources.Load<GameSettingsData>(SettingsPath);

                if (_settings == null)
                {
                    GameSettingsData[] found =
                        Resources.FindObjectsOfTypeAll<GameSettingsData>();
                    if (found != null && found.Length > 0)
                    {
                        _settings = found[0];
                    }
                }

                if (_settings == null)
                {
                    ServerLog.Warn("GameSettings not found - repair prices approximated");
                }

                return _settings;
            }
        }

        public static float HullRepairPrice =>
            Settings != null && Settings.hullRepairPrice > 0f
                ? Settings.hullRepairPrice
                : 0.0002f;

        public static float ShieldRepairPrice =>
            Settings != null && Settings.shieldRepairPrice > 0f
                ? Settings.shieldRepairPrice
                : 0.05f;

        public static ServerRateSettingData Rates
        {
            get
            {
                if (_rates != null || _searched)
                {
                    return _rates;
                }

                _searched = true;
                _rates = Resources.Load<ServerRateSettingData>(RatesPath);

                if (_rates == null)
                {

                    ServerRateSettingData[] found =
                        Resources.FindObjectsOfTypeAll<ServerRateSettingData>();
                    if (found != null && found.Length > 0)
                    {
                        _rates = found[0];
                    }
                }

                if (_rates != null)
                {
                    ServerLog.Info(
                        $"rate settings loaded: {_rates.levels?.Count ?? 0} levels, " +
                        $"xp x{_rates.experienceDropRate}, credits x{_rates.creditsDropRate}, " +
                        $"npc loot x{_rates.npcLootDropRate}");
                }
                else
                {
                    ServerLog.Warn(
                        "ServerRateSettings not found - falling back to approximated " +
                        "progression and economy numbers");
                }

                return _rates;
            }
        }

        public static bool HasRates => Rates != null && Rates.levels != null &&
                                       Rates.levels.Count > 0;

        public static long ExperienceForLevel(int level)
        {
            if (!HasRates)
            {

                long n = Mathf.Max(0, level);
                return 1000L * n * n + 1500L * n;
            }

            int clamped = Mathf.Clamp(level, 1, Rates.levels.Count);
            return Rates.levels[clamped - 1];
        }

        public static int LevelForExperience(long experience)
        {
            if (!HasRates)
            {
                for (int level = 100; level >= 1; level--)
                {
                    if (experience >= ExperienceForLevel(level - 1))
                    {
                        return level;
                    }
                }

                return 1;
            }

            try
            {
                return Rates.GetLevelByExperience(
                    (int)Mathf.Clamp(experience, 0, int.MaxValue));
            }
            catch (System.Exception e)
            {
                ServerLog.Warn($"level lookup failed: {e.Message}");
                return 1;
            }
        }

        public static int MaxLevel => HasRates ? Rates.levels.Count : 100;

        public static ServerRateSettingData.ProgressionData ProgressionFor(int level)
        {
            if (Rates?.levelProgressionData == null)
            {
                return null;
            }

            int index = level - 2;
            return index >= 0 && index < Rates.levelProgressionData.Count
                ? Rates.levelProgressionData[index]
                : null;
        }

        public static float ExperienceRate => Rates != null ? Rates.experienceDropRate : 1f;

        public static float CreditsRate => Rates != null ? Rates.creditsDropRate : 1f;

        public static float BoraxRate => Rates != null ? Rates.boraxDropRate : 1f;

        public static float NpcLootRate => Rates != null ? Rates.npcLootDropRate : 1f;

        public static float CapsuleLootRate => Rates != null ? Rates.capsuleLootDropRate : 1f;

        public static float TradingSellRate => Rates != null ? Rates.tradingSellRate : 1f;

        public static long CreditsPerKill(int level)
        {
            ServerRateSettingData.ProgressionData row = ProgressionFor(level + 1);
            ServerRateSettingData.ProgressionData previous = ProgressionFor(level);

            if (row == null)
            {
                return (long)(150 * CreditsRate);
            }

            int kills = row.totalNPCKills - (previous?.totalNPCKills ?? 0);
            int credits = row.totalCredits - (previous?.totalCredits ?? 0);

            if (kills <= 0)
            {
                return (long)(150 * CreditsRate);
            }

            return (long)Mathf.Max(1f, credits / (float)kills * CreditsRate);
        }

        public static int NpcHealth(int level)
        {
            if (Rates?.npcHealthCurve == null)
            {
                return 400 + level * 150;
            }

            float t = Mathf.Clamp01(level / (float)Mathf.Max(1, MaxLevel));
            return Mathf.Max(50, Mathf.RoundToInt(Rates.npcHealthCurve.Evaluate(t)));
        }

        public static int CannonDamage(int tier)
        {
            if (Rates?.cannonDamageCurve == null)
            {
                return 60 + tier * 20;
            }

            float t = Mathf.Clamp01(tier / 7f);
            return Mathf.Max(1, Mathf.RoundToInt(Rates.cannonDamageCurve.Evaluate(t)));
        }

        private static DataManager _dataManager;
        private static bool _searchedData;

        public static DataManager Data
        {
            get
            {
                if (_dataManager != null || _searchedData)
                {
                    return _dataManager;
                }

                _searchedData = true;
                _dataManager = Rates != null ? Rates.dataManager : null;

                if (_dataManager == null)
                {
                    DataManager[] found = Resources.FindObjectsOfTypeAll<DataManager>();
                    if (found != null && found.Length > 0)
                    {
                        _dataManager = found[0];
                    }
                }

                if (_dataManager != null)
                {
                    ServerLog.Info(
                        $"item catalogue: {_dataManager.collectableDatas?.Count ?? 0} materials, " +
                        $"{_dataManager.weaponDatas?.Count ?? 0} weapons, " +
                        $"{_dataManager.stationDatas?.Count ?? 0} stations");
                }
                else
                {
                    ServerLog.Warn("DataManager not found - item prices will be approximated");
                }

                return _dataManager;
            }
        }

        public static CollectableMaterialData Material(string name)
        {
            if (Data?.collectableDatas == null || string.IsNullOrEmpty(name))
            {
                return null;
            }

            foreach (CollectableMaterialData m in Data.collectableDatas)
            {
                if (m != null &&
                    string.Equals(m.name, name, System.StringComparison.OrdinalIgnoreCase))
                {
                    return m;
                }
            }

            return null;
        }

        public static CollectableMaterialData MaterialByIndex(int index)
        {
            if (Data?.collectableDatas == null)
            {
                return null;
            }

            foreach (CollectableMaterialData m in Data.collectableDatas)
            {
                if (m != null && m.index == index)
                {
                    return m;
                }
            }

            return null;
        }

        private static AreaStation[] _stations;

        public static StationData ClosestStation(Vector3 position)
        {
            if (_stations == null || _stations.Length == 0)
            {
                _stations = Object.FindObjectsByType<AreaStation>(
                    FindObjectsInactive.Include, FindObjectsSortMode.None);
            }

            AreaStation best = null;
            float bestDistance = float.MaxValue;

            foreach (AreaStation station in _stations)
            {
                if (station == null || station.data == null)
                {
                    continue;
                }

                float distance = Enums.Distance2D(station.transform.position, position);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = station;
                }
            }

            return best != null ? best.data : null;
        }

        public static int SellPriceAt(CollectableMaterialData material, StationData station)
        {
            if (material?.sellStationPrices == null)
            {
                return 0;
            }

            foreach (MaterialSellStation entry in material.sellStationPrices)
            {
                if (entry != null && entry.stationData == station)
                {
                    return entry.price;
                }
            }

            return 0;
        }

        public static int BestSellPrice(CollectableMaterialData material)
        {
            if (material?.sellStationPrices == null || material.sellStationPrices.Count == 0)
            {
                return material != null ? material.buyPrice : 0;
            }

            int best = 0;
            foreach (MaterialSellStation entry in material.sellStationPrices)
            {
                if (entry != null && entry.price > best)
                {
                    best = entry.price;
                }
            }

            return best;
        }
    }
}
