using System;
using UnityEngine;

namespace TidalNexus.StandaloneServer
{

    public sealed class ServerConfig
    {
        public string AppId = string.Empty;
        public string SessionName = "tidalnexus-local";
        public ushort Port = 27015;
        public int SceneIndex = -1;
        public int MaxPlayers = 32;

        public float BlastRadius = 8f;
        public float MineTrigger = 5f;
        public float BombFuse = 3f;
        public float MineLifetime = 300f;

        public float CaptchaInterval;

        public float BatteryFraction = 1f;
        public float DamageBoost = 1.5f;
        public float ExtraDrain = 0.1f;
        public float AutoCollectRange = 15f;

        public int SentryDamage = 40;
        public float SentryRange = 20f;

        public int StationDamage = 120;

        public float BeaconCaptureRate = 4f;
        public float BeaconPointRate = 1f;

        public int EventCreditsPerPoint = 100;

        public bool EventsEnabled = true;

        public string StartEvent = string.Empty;

        public string AllowList = string.Empty;

        public static ServerConfig FromEnvironment(ServerConfig defaults = null)
        {
            var config = new ServerConfig();

            if (defaults != null)
            {
                config.AppId = defaults.AppId ?? string.Empty;
                config.SessionName = string.IsNullOrWhiteSpace(defaults.SessionName)
                    ? config.SessionName
                    : defaults.SessionName;
                config.Port = defaults.Port != 0 ? defaults.Port : config.Port;
                config.MaxPlayers = defaults.MaxPlayers > 0 ? defaults.MaxPlayers : config.MaxPlayers;
            }

            config.AppId = Env("TN_APPID", config.AppId);
            config.SessionName = Env("TN_SESSION", config.SessionName);
            config.Port = ParsePort(Env("TN_PORT", config.Port.ToString()), config.Port);

            config.BlastRadius = ParseFloat(Env("TN_BLASTRADIUS", null), config.BlastRadius);
            config.MineTrigger = ParseFloat(Env("TN_MINETRIGGER", null), config.MineTrigger);
            config.BombFuse = ParseFloat(Env("TN_BOMBFUSE", null), config.BombFuse);
            config.MineLifetime = ParseFloat(Env("TN_MINELIFETIME", null), config.MineLifetime);
            config.CaptchaInterval =
                ParseFloat(Env("TN_CAPTCHAINTERVAL", null), config.CaptchaInterval);
            config.BatteryFraction =
                ParseFloat(Env("TN_BATTERYFRACTION", null), config.BatteryFraction);
            config.DamageBoost = ParseFloat(Env("TN_DAMAGEBOOST", null), config.DamageBoost);
            config.ExtraDrain = ParseFloat(Env("TN_EXTRADRAIN", null), config.ExtraDrain);
            config.AutoCollectRange =
                ParseFloat(Env("TN_AUTOCOLLECTRANGE", null), config.AutoCollectRange);
            config.SentryDamage =
                (int)ParseFloat(Env("TN_SENTRYDAMAGE", null), config.SentryDamage);
            config.SentryRange = ParseFloat(Env("TN_SENTRYRANGE", null), config.SentryRange);
            config.StationDamage =
                (int)ParseFloat(Env("TN_STATIONDAMAGE", null), config.StationDamage);
            config.BeaconCaptureRate =
                ParseFloat(Env("TN_BEACONCAPTURERATE", null), config.BeaconCaptureRate);
            config.BeaconPointRate =
                ParseFloat(Env("TN_BEACONPOINTRATE", null), config.BeaconPointRate);
            config.EventCreditsPerPoint =
                (int)ParseFloat(Env("TN_EVENTCREDITS", null), config.EventCreditsPerPoint);
            config.EventsEnabled =
                ParseFloat(Env("TN_EVENTS", null), config.EventsEnabled ? 1f : 0f) > 0f;
            config.StartEvent = Env("TN_STARTEVENT", config.StartEvent);
            config.AllowList = Env("TN_ALLOWLIST", config.AllowList);

            string[] args;
            try
            {
                args = Environment.GetCommandLineArgs();
            }
            catch
            {
                args = Array.Empty<string>();
            }

            for (int i = 0; i < args.Length - 1; i++)
            {
                string key = args[i].ToLowerInvariant();
                string value = args[i + 1];

                switch (key)
                {
                    case "-appid":
                        config.AppId = value;
                        break;
                    case "-session":
                        config.SessionName = value;
                        break;
                    case "-port":
                        config.Port = ParsePort(value, config.Port);
                        break;
                    case "-scene":
                        if (int.TryParse(value, out int scene))
                        {
                            config.SceneIndex = scene;
                        }
                        break;
                    case "-maxplayers":
                        if (int.TryParse(value, out int max) && max > 0)
                        {
                            config.MaxPlayers = max;
                        }
                        break;
                    case "-blastradius":
                        config.BlastRadius = ParseFloat(value, config.BlastRadius);
                        break;
                    case "-minetrigger":
                        config.MineTrigger = ParseFloat(value, config.MineTrigger);
                        break;
                    case "-bombfuse":
                        config.BombFuse = ParseFloat(value, config.BombFuse);
                        break;
                    case "-minelifetime":
                        config.MineLifetime = ParseFloat(value, config.MineLifetime);
                        break;
                    case "-captchainterval":
                        config.CaptchaInterval = ParseFloat(value, config.CaptchaInterval);
                        break;
                    case "-batteryfraction":
                        config.BatteryFraction = ParseFloat(value, config.BatteryFraction);
                        break;
                    case "-damageboost":
                        config.DamageBoost = ParseFloat(value, config.DamageBoost);
                        break;
                    case "-extradrain":
                        config.ExtraDrain = ParseFloat(value, config.ExtraDrain);
                        break;
                    case "-autocollectrange":
                        config.AutoCollectRange = ParseFloat(value, config.AutoCollectRange);
                        break;
                    case "-sentrydamage":
                        config.SentryDamage = (int)ParseFloat(value, config.SentryDamage);
                        break;
                    case "-sentryrange":
                        config.SentryRange = ParseFloat(value, config.SentryRange);
                        break;
                    case "-stationdamage":
                        config.StationDamage = (int)ParseFloat(value, config.StationDamage);
                        break;
                    case "-beaconcapturerate":
                        config.BeaconCaptureRate =
                            ParseFloat(value, config.BeaconCaptureRate);
                        break;
                    case "-beaconpointrate":
                        config.BeaconPointRate = ParseFloat(value, config.BeaconPointRate);
                        break;
                    case "-eventcredits":
                        config.EventCreditsPerPoint =
                            (int)ParseFloat(value, config.EventCreditsPerPoint);
                        break;
                    case "-events":
                        config.EventsEnabled =
                            ParseFloat(value, config.EventsEnabled ? 1f : 0f) > 0f;
                        break;
                    case "-startevent":
                        config.StartEvent = value ?? string.Empty;
                        break;
                    case "-allowlist":
                        config.AllowList = value ?? string.Empty;
                        break;
                }
            }

            if (config.SceneIndex < 0)
            {
                config.SceneIndex = ProjectBindings.WorldSceneIndex;
            }

            return config;
        }

        private static string Env(string name, string fallback)
        {
            try
            {
                string value = Environment.GetEnvironmentVariable(name);
                return string.IsNullOrWhiteSpace(value) ? fallback : value;
            }
            catch
            {
                return fallback;
            }
        }

        private static ushort ParsePort(string value, ushort fallback) =>
            ushort.TryParse(value, out ushort port) && port != 0 ? port : fallback;

        private static float ParseFloat(string value, float fallback) =>
            !string.IsNullOrWhiteSpace(value) &&
            float.TryParse(value, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out float parsed) &&
            parsed > 0f
                ? parsed
                : fallback;

        public override string ToString() =>
            $"session '{SessionName}', port {Port}, scene {SceneIndex}, " +
            $"max {MaxPlayers}, appId {(string.IsNullOrEmpty(AppId) ? "(shipped)" : Short(AppId))}";

        private static string Short(string value) =>
            value.Length <= 8 ? value : value.Substring(0, 8) + "...";
    }
}
