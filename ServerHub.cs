using System;
using System.Collections.Generic;
using Fusion;
using TidalNexus.StandaloneServer.Data;
using TidalNexus.StandaloneServer.Services;
using UnityEngine;

namespace TidalNexus.StandaloneServer
{

    public static class ServerHub
    {

        private static readonly Dictionary<string, PlayerSession> Live =
            new Dictionary<string, PlayerSession>(StringComparer.OrdinalIgnoreCase);

        private static readonly Dictionary<PlayerRef, PlayerSession> ByRef =
            new Dictionary<PlayerRef, PlayerSession>();

        public static bool Ready { get; private set; }

        public static NetworkRunner Runner { get; private set; }

        public static AccountService Accounts { get; private set; }
        public static CombatService Combat { get; private set; }
        public static MissionService Missions { get; private set; }
        public static NpcService Npcs { get; private set; }
        public static EconomyService Economy { get; private set; }
        public static ProgressionService Progression { get; private set; }
        public static SocialService Social { get; private set; }
        public static PvpService Pvp { get; private set; }
        public static LeaderboardService Leaderboards { get; private set; }
        public static AdminService Admin { get; private set; }
        public static OrdnanceService Ordnance { get; private set; }
        public static AntiBotService AntiBot { get; private set; }
        public static SkillService Skills { get; private set; }
        public static ExtrasService Extras { get; private set; }
        public static AchievementService Achievements { get; private set; }
        public static SentryService Sentry { get; private set; }
        public static StationDefenceService StationDefence { get; private set; }
        public static CommandService Commands { get; private set; }
        public static EventService Events { get; private set; }

        public static ServerConfig Config { get; set; }

        public static void Boot(NetworkRunner runner)
        {
            if (runner == null || !runner.IsServer)
            {
                return;
            }

            Runner = runner;
            AccountStore.LoadAll();

            Accounts = new AccountService();
            Progression = new ProgressionService();
            Economy = new EconomyService();
            Combat = new CombatService();
            Npcs = new NpcService();
            Missions = new MissionService();
            Social = new SocialService();
            Pvp = new PvpService();
            Leaderboards = new LeaderboardService();
            Admin = new AdminService();
            Ordnance = new OrdnanceService();
            AntiBot = new AntiBotService();
            Skills = new SkillService();
            Extras = new ExtrasService();
            Achievements = new AchievementService();
            Sentry = new SentryService();
            StationDefence = new StationDefenceService();
            Commands = new CommandService();
            Events = new EventService();

            Steps.Clear();
            Step("npcs", Npcs.Tick);
            Step("combat", Combat.Tick);
            Step("pvp", Pvp.Tick);
            Step("social", Social.Tick);
            Step("ordnance", Ordnance.Tick);
            Step("antibot", AntiBot.Tick);
            Step("skills", Skills.Tick);
            Step("extras", Extras.Tick);
            Step("leaderboards", Leaderboards.Tick);
            Step("sentry", Sentry.Tick);
            Step("stationdefence", StationDefence.Tick);
            Step("events", Events.Tick);

            Step("persistence", AccountStore.Pump);

            Ready = true;

            try
            {
                ServerLog.Info(
                    "input words = " +
                    Fusion.NetworkProjectConfig.Global.Simulation.InputDataWordCount);
            }
            catch (System.Exception e)
            {
                ServerLog.Warn($"could not read InputDataWordCount: {e.Message}");
            }

            ServerLog.Info("server hub ready");
        }

        public static void Farewell()
        {
            if (!Ready)
            {
                return;
            }

            foreach (PlayerSession session in Live.Values)
            {
                Wire.SendDisconnect(session.Player);
            }

            ServerLog.Info($"said goodbye to {Live.Count} player(s)");
        }

        private struct PendingDrop
        {
            public PlayerRef Player;
            public float Remaining;
            public string Reason;
        }

        private static readonly List<PendingDrop> Drops = new List<PendingDrop>();

        private const float DropDelaySeconds = 3f;

        public static void DisconnectAfterFlush(PlayerRef player, string reason)
        {
            if (Runner == null || player == PlayerRef.None)
            {
                return;
            }

            foreach (PendingDrop queued in Drops)
            {
                if (queued.Player == player)
                {
                    return;
                }
            }

            Drops.Add(new PendingDrop
            {
                Player = player,
                Remaining = DropDelaySeconds,
                Reason = reason,
            });
        }

        private static void PumpDrops(float deltaTime)
        {
            for (int i = Drops.Count - 1; i >= 0; i--)
            {
                PendingDrop drop = Drops[i];
                drop.Remaining -= deltaTime;

                if (drop.Remaining > 0f)
                {
                    Drops[i] = drop;
                    continue;
                }

                Drops.RemoveAt(i);

                try
                {

                    if (Runner != null && Runner.IsRunning)
                    {
                        Runner.Disconnect(drop.Player);
                        ServerLog.Info($"dropped player {drop.Player.PlayerId}: {drop.Reason}");
                    }
                }
                catch (System.Exception ex)
                {
                    ServerLog.Warn(
                        $"could not drop player {drop.Player.PlayerId}: {ex.Message}");
                }
            }
        }

        public static void Shutdown()
        {
            if (!Ready)
            {
                return;
            }

            EndAllSessions();

            AccountStore.SaveAll();

            Drops.Clear();
            Steps.Clear();

            Ready = false;
            ServerLog.Info("server hub shut down, accounts saved");
        }

        public static void Tick(float deltaTime)
        {
            if (!Ready)
            {
                return;
            }

            PumpDrops(deltaTime);

            for (int i = 0; i < Steps.Count; i++)
            {
                TickStep step = Steps[i];

                try
                {
                    step.Run(deltaTime);
                    Recovered(step);
                }
                catch (Exception ex)
                {
                    Faulted(step, ex);
                }
            }
        }

        private sealed class TickStep
        {
            public string Name;
            public Action<float> Run;
            public int Failures;
            public float NextReport;

            public int ReportedFailures;

            public bool Failing;

            public bool Announced;
        }

        private static readonly List<TickStep> Steps = new List<TickStep>();

        private static void Step(string name, Action<float> run)
        {
            Steps.Add(new TickStep { Name = name, Run = run });
        }

        private const float FaultReportInterval = 30f;

        private static void Faulted(TickStep step, Exception ex)
        {
            step.Failures++;
            step.Failing = true;

            float now = Time.realtimeSinceStartup;
            if (now < step.NextReport)
            {
                return;
            }

            int since = step.Failures - step.ReportedFailures;

            step.ReportedFailures = step.Failures;
            step.NextReport = now + FaultReportInterval;
            step.Announced = true;

            ServerLog.Error(
                $"{step.Name} tick threw ({since} since the last report, " +
                $"{step.Failures} since boot): {ex}");
        }

        private static void Recovered(TickStep step)
        {
            if (!step.Failing)
            {
                return;
            }

            step.Failing = false;

            if (!step.Announced)
            {
                return;
            }

            step.Announced = false;
            ServerLog.Info(
                $"{step.Name} tick recovered after {step.Failures} failures");
        }

        public static Account AccountFor(PlayerRef player)
        {
            return ByRef.TryGetValue(player, out PlayerSession session) ? session.Account : null;
        }

        public static Account AccountFor(RpcInfo info)
        {
            return AccountFor(info.Source);
        }

        public static PlayerSession BeginSession(PlayerRef player, Account account)
        {
            if (account == null || string.IsNullOrEmpty(account.id))
            {
                return null;
            }

            if (ByRef.TryGetValue(player, out PlayerSession stale) &&
                !string.Equals(stale.Id, account.id, StringComparison.OrdinalIgnoreCase))
            {
                EndSession(player);
            }

            if (Live.TryGetValue(account.id, out PlayerSession session) &&
                ReferenceEquals(session.Account, account))
            {
                ByRef.Remove(session.Player);
                session.MoveTo(player);
            }
            else
            {

                if (session != null)
                {
                    EndSession(session.Player);
                }

                session = new PlayerSession(account, player);
                Live[account.id] = session;
            }

            ByRef[player] = session;
            return session;
        }

        public static void EndSession(PlayerRef player)
        {
            if (!ByRef.TryGetValue(player, out PlayerSession session))
            {
                return;
            }

            ByRef.Remove(player);

            if (Live.TryGetValue(session.Id, out PlayerSession held) &&
                ReferenceEquals(held, session))
            {
                Live.Remove(session.Id);
            }

            try
            {
                AccountStore.SaveNow(session.Account);
            }
            catch (Exception ex)
            {
                ServerLog.Warn($"could not save {session.Account.nickname} at logout: {ex.Message}");
            }
            finally
            {

                session.Dispose();
            }
        }

        public static void EndAllSessions()
        {
            if (Live.Count == 0)
            {
                ByRef.Clear();
                return;
            }

            var leaving = new List<PlayerSession>(Live.Values);
            foreach (PlayerSession session in leaving)
            {
                EndSession(session.Player);
            }

            foreach (PlayerSession session in leaving)
            {
                session.Dispose();
            }

            Live.Clear();
            ByRef.Clear();
        }

        public static PlayerSession SessionFor(PlayerRef player)
        {
            return ByRef.TryGetValue(player, out PlayerSession session) ? session : null;
        }

        public static PlayerSession SessionOf(Account account)
        {
            if (account == null || string.IsNullOrEmpty(account.id))
            {
                return null;
            }

            return Live.TryGetValue(account.id, out PlayerSession session) ? session : null;
        }

        public static IEnumerable<PlayerSession> Sessions => Live.Values;

        public static void SnapshotSessions(List<PlayerSession> into)
        {
            if (into == null)
            {
                return;
            }

            into.Clear();
            foreach (PlayerSession session in Live.Values)
            {
                into.Add(session);
            }
        }

        public static IEnumerable<KeyValuePair<PlayerRef, Account>> Online
        {
            get
            {
                foreach (PlayerSession session in Live.Values)
                {
                    yield return new KeyValuePair<PlayerRef, Account>(
                        session.Player, session.Account);
                }
            }
        }

        public static PlayerRPC RpcFor(PlayerRef player)
        {

            NetworkObject obj = WorldLookup.ObjectOf(player);
            return obj == null ? null : obj.GetComponent<PlayerRPC>();
        }

        public static PlayerRef RefFor(Account account)
        {
            PlayerSession session = SessionOf(account);
            if (session == null)
            {
                return PlayerRef.None;
            }

            return ByRef.ContainsKey(session.Player) ? session.Player : PlayerRef.None;
        }
    }
}
