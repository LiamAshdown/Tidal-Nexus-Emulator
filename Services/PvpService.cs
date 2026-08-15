using System;
using System.Collections.Generic;
using Fusion;
using TidalNexus.StandaloneServer.Data;
using UnityEngine;

namespace TidalNexus.StandaloneServer.Services
{

    public sealed class PvpService
    {
        private const long UnflagDelay = 30;
        private const int ArenaSize = 4;

        private const long Indefinite = 4102444800L;

        private const float ArenaMatchLimit = 300f;

        public sealed class Flag
        {
            public bool Pushed;
        }

        public sealed class Zone
        {
            public bool Pushed;
        }

        private readonly List<string> _arenaQueue = new List<string>();

        private sealed class Match
        {
            public readonly List<string> Roster = new List<string>();
            public readonly List<string> Standing = new List<string>();
            public readonly List<string> Fallen = new List<string>();
            public float EndsAt;
        }

        private readonly List<Match> _matches = new List<Match>();

        private readonly List<PlayerSession> _players = new List<PlayerSession>();

        private float _clock;

        private float _nextZoneSweep;

        private const float ZoneSweepInterval = 0.25f;

        public void Tick(float deltaTime)
        {
            _clock += deltaTime;

            AdvanceMatches();

            ServerHub.SnapshotSessions(_players);
            long now = NowUnix();

            bool sweepZone = _clock >= _nextZoneSweep;
            if (sweepZone)
            {
                _nextZoneSweep = _clock + ZoneSweepInterval;
            }

            foreach (PlayerSession session in _players)
            {
                ReconcileFlag(session, now);

                if (sweepZone)
                {
                    ReconcileZone(session);
                }
            }
        }

        private static void ReconcileFlag(PlayerSession session, long now)
        {
            bool flagged = session.Account.pvpFlaggedUntilUnix > now;

            Flag flag = session.Peek<Flag>();
            if (flag == null)
            {
                if (!flagged)
                {
                    return;
                }

                flag = session.State<Flag>();
            }

            if (flag.Pushed == flagged)
            {
                return;
            }

            if (PushFlag(session.Player, flagged))
            {
                flag.Pushed = flagged;
            }
        }

        private void ReconcileZone(PlayerSession session)
        {
            bool inside = InZone(WorldLookup.ObjectOf(session.Player));

            Zone zone = session.Peek<Zone>();
            if (zone == null)
            {
                if (!inside)
                {
                    return;
                }

                zone = session.State<Zone>();
            }

            if (zone.Pushed == inside)
            {
                return;
            }

            if (PushZone(session.Player, inside))
            {
                zone.Pushed = inside;
            }
        }

        private static long NowUnix() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        public bool IsFlagged(Account account) =>
            account != null && account.pvpFlaggedUntilUnix > NowUnix();

        public void FlagOn(Account account)
        {
            if (account == null)
            {
                return;
            }

            account.pvpFlaggedUntilUnix = Indefinite;
        }

        public void FlagOff(Account account)
        {
            if (account == null || !IsFlagged(account))
            {
                return;
            }

            account.pvpFlaggedUntilUnix = NowUnix() + UnflagDelay;
        }

        private static bool PushFlag(PlayerRef player, bool flagged)
        {
            PlayerNetworkValues values = ValuesOf(WorldLookup.ObjectOf(player));
            if (values == null)
            {
                return false;
            }

            values.isFlagged = flagged;
            return true;
        }

        private static bool PushZone(PlayerRef player, bool inside)
        {
            PlayerNetworkValues values = ValuesOf(WorldLookup.ObjectOf(player));
            if (values == null)
            {
                return false;
            }

            values.isPvPZone = inside;
            return true;
        }

        private static PlayerNetworkValues ValuesOf(NetworkObject obj)
        {
            return obj == null ? null : obj.GetComponentInChildren<PlayerNetworkValues>();
        }

        public bool MayFight(Account attacker, Account defender)
        {
            if (attacker == null || defender == null)
            {
                return false;
            }

            if (InBattleZone(attacker) && InBattleZone(defender))
            {
                return true;
            }

            return IsFlagged(attacker) && IsFlagged(defender);
        }

        public bool InBattleZone(Account account)
        {
            return InZone(WorldLookup.ObjectOf(account));
        }

        private bool InZone(NetworkObject obj)
        {
            Area zone = BattleZone();

            return obj != null && zone != null && zone.IsInsideBoundry(obj.gameObject);
        }

        private Area _battleZone;
        private float _nextZoneLookup;
        private bool _zoneMissingLogged;

        private Area BattleZone()
        {
            if (_battleZone != null)
            {
                return _battleZone;
            }

            if (_clock < _nextZoneLookup)
            {
                return null;
            }

            _nextZoneLookup = _clock + ZoneSweepInterval;

            foreach (Area area in UnityEngine.Object.FindObjectsByType<Area>(
                FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (area == null ||
                    area.areaIndex != PlayerDirector.BattleZoneArea ||
                    area.layer != Enums.NetworkLayerType.WorldMap)
                {
                    continue;
                }

                if (!HasBoundaries(area))
                {
                    Missing("the battle zone area has no usable boundary box");
                    return null;
                }

                _battleZone = area;
                _zoneMissingLogged = false;
                return _battleZone;
            }

            Missing($"no world-map area with index {PlayerDirector.BattleZoneArea} in the scene");
            return null;
        }

        private void Missing(string reason)
        {
            if (_zoneMissingLogged)
            {
                return;
            }

            _zoneMissingLogged = true;
            ServerLog.Warn($"{reason} - PvP in the battle zone stays flag-only");
        }

        private static bool HasBoundaries(Area area)
        {
            if (area.boundaries == null || area.boundaries.Count == 0)
            {
                return false;
            }

            foreach (BoxCollider box in area.boundaries)
            {
                if (box == null)
                {
                    return false;
                }
            }

            return true;
        }

        public void QueueForArena(Account account)
        {
            if (account == null || _arenaQueue.Contains(account.id))
            {
                return;
            }

            if (MatchOf(account.id) != null)
            {
                return;
            }

            _arenaQueue.Add(account.id);
            SetQueueFlag(account, true);

            PlayerRef p = ServerHub.RefFor(account);
            ServerHub.RpcFor(p)?.RPC_ArenaQueueCheck();

            TryStartMatch();
        }

        public void CancelArenaQueue(Account account)
        {
            if (account == null || !_arenaQueue.Remove(account.id))
            {
                return;
            }

            SetQueueFlag(account, false);
            PlayerRef p = ServerHub.RefFor(account);
            ServerHub.RpcFor(p)?.RPC_ArenaCancelInfo();
        }

        private void PruneQueue()
        {
            for (int i = _arenaQueue.Count - 1; i >= 0; i--)
            {
                if (ServerHub.SessionOf(AccountStore.Find(_arenaQueue[i])) == null)
                {
                    _arenaQueue.RemoveAt(i);
                }
            }
        }

        private void TryStartMatch()
        {

            PruneQueue();

            if (_arenaQueue.Count < ArenaSize)
            {
                return;
            }

            var roster = new List<string>();
            for (int i = 0; i < ArenaSize && _arenaQueue.Count > 0; i++)
            {
                roster.Add(_arenaQueue[0]);
                _arenaQueue.RemoveAt(0);
            }

            var match = new Match { EndsAt = _clock + ArenaMatchLimit };
            match.Roster.AddRange(roster);
            match.Standing.AddRange(roster);
            _matches.Add(match);

            foreach (string id in roster)
            {
                Account a = AccountStore.Find(id);
                if (a == null)
                {
                    continue;
                }

                SetQueueFlag(a, false);

                PlayerRef p = ServerHub.RefFor(a);
                ServerHub.RpcFor(p)?.RPC_ArenaStart();
            }

            ServerLog.Info($"arena match started with {roster.Count} players");
        }

        private Match MatchOf(string accountId)
        {
            return _matches.Find(m => m.Roster.Contains(accountId));
        }

        private void AdvanceMatches()
        {
            if (_matches.Count == 0)
            {
                return;
            }

            List<Match> finished = null;

            foreach (Match match in _matches)
            {

                for (int i = 0; i < match.Standing.Count;)
                {
                    if (StillFighting(match.Standing[i]))
                    {
                        i++;
                        continue;
                    }

                    match.Fallen.Add(match.Standing[i]);
                    match.Standing.RemoveAt(i);
                }

                if (match.Standing.Count > 1 && _clock < match.EndsAt)
                {
                    continue;
                }

                (finished ??= new List<Match>()).Add(match);
            }

            if (finished == null)
            {
                return;
            }

            foreach (Match match in finished)
            {
                _matches.Remove(match);
                Finish(match);
            }
        }

        private static bool StillFighting(string accountId)
        {
            Account account = AccountStore.Find(accountId);
            PlayerRef p = account == null ? PlayerRef.None : ServerHub.RefFor(account);
            if (p == PlayerRef.None)
            {
                return false;
            }

            Health health = WorldLookup.HealthOf(WorldLookup.ObjectOf(p));
            return health == null || WorldLookup.IsAlive(health);
        }

        private void Finish(Match match)
        {

            var places = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            int place = 1;

            if (match.Standing.Count == 1)
            {
                places[match.Standing[0]] = place++;
            }
            else
            {
                place++;
            }

            for (int i = match.Fallen.Count - 1; i >= 0; i--)
            {
                places[match.Fallen[i]] = place++;
            }

            string winnerId = match.Standing.Count == 1 ? match.Standing[0] : null;

            Account winner = null;
            var roster = new List<Account>();

            foreach (string id in match.Roster)
            {
                Account a = AccountStore.Find(id);
                if (a == null)
                {
                    continue;
                }

                a.lastArenaPos = places.TryGetValue(id, out int p) ? p : match.Roster.Count;

                roster.Add(a);

                if (winnerId != null &&
                    string.Equals(id, winnerId, StringComparison.OrdinalIgnoreCase))
                {
                    winner = a;
                }
            }

            ServerLog.Info($"arena match ended, winner {winner?.nickname ?? "nobody"}");
            EndArena(winner, roster);
        }

        public void EndArena(Account winner, IEnumerable<Account> participants)
        {
            if (participants == null)
            {
                return;
            }

            foreach (Account a in participants)
            {
                if (a == null)
                {
                    continue;
                }

                bool won = ReferenceEquals(a, winner);
                if (won)
                {
                    a.lifetimeArena++;
                    a.weeklyArena++;
                    a.credits += 20000;
                    ServerHub.Progression?.AwardExperience(a, 3000);
                }

                AccountStore.MarkDirty(a);

                PlayerRef p = ServerHub.RefFor(a);

                ServerHub.RpcFor(p)?.RPC_ArenaEnd(
                    won, won ? 1 : 0, (int)a.weeklyArena);
            }
        }

        private static void SetQueueFlag(Account account, bool queued)
        {
            PlayerNetworkValues values = ValuesOf(WorldLookup.ObjectOf(account));
            if (values != null)
            {
                values.isArenaQueue = queued;
            }
        }

    }
}
