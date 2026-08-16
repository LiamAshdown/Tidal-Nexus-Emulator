using System;
using System.Collections.Generic;
using Fusion;
using TidalNexus.StandaloneServer.Core;
using TidalNexus.StandaloneServer.Data;
using UnityEngine;

namespace TidalNexus.StandaloneServer.Services
{

    public sealed class EventService
    {

        private static readonly int[,] Schedule =
        {

            {  0, 0, 1, 0, 0, 0, 4, 0, 0, 0, 2, 6, 0, 0, 1, 0, 0, 0, 4, 0, 0, 0, 2, 0 },
            {  0, 0, 4, 0, 0, 0, 2, 0, 0, 0, 1, 5, 0, 0, 4, 0, 0, 0, 2, 0, 0, 0, 1, 0 },
            {  0, 0, 2, 0, 0, 0, 1, 0, 0, 0, 4, 5, 0, 0, 2, 0, 0, 0, 1, 0, 0, 0, 4, 0 },
            {  0, 0, 1, 0, 0, 0, 4, 0, 0, 0, 2, 5, 0, 0, 1, 0, 0, 0, 4, 0, 0, 0, 2, 0 },
            {  0, 0, 4, 0, 0, 0, 2, 0, 0, 0, 1, 5, 0, 0, 4, 0, 0, 0, 2, 0, 0, 0, 1, 0 },
            {  0, 0, 2, 0, 0, 0, 1, 0, 0, 0, 4, 5, 0, 0, 2, 0, 0, 0, 1, 0, 0, 0, 4, 0 },
            {  0, 0, 1, 0, 0, 0, 4, 0, 0, 0, 2, 5, 0, 0, 1, 0, 0, 0, 4, 0, 0, 0, 2, 0 },
        };

        private const int ScheduleOffsetHours = 3;

        private const float LeadInSeconds = 300f;

        private const int DamagePerPoint = 1000;

        public enum Kind { None = 0, Beacon = 1, Kraken = 2, Royale = 4 }

        private NetworkObject _active;

        private EventMode _mode;

        private float _timer;
        private bool _started;
        private bool _ending;

        private int _lastSlot = -1;

        private float _scheduleClock;
        private bool _bootHandled;

        private EventMode _pending;
        private float _pendingWaited;

        private int _spawnToken;

        private readonly Dictionary<string, Contribution> _scores =
            new Dictionary<string, Contribution>();

        private Kind _scoresKind = Kind.None;

        private Core.BeaconFaction _beaconWinner = Core.BeaconFaction.None;

        internal void NoteBeaconWinner(Core.BeaconFaction winner) => _beaconWinner = winner;

        public Core.BeaconFaction BeaconWinner =>
            _scoresKind == Kind.Beacon ? _beaconWinner : Core.BeaconFaction.None;

        public sealed class Contribution
        {
            public string Name = string.Empty;
            public string Clan = string.Empty;
            public int Faction;
            public int Captures;
            public int Kills;
            public int Deaths;
            public int Damage;
            public int Points;
        }

        public static bool WorldIsLive { get; set; }

        public bool IsRunning => _active != null;
        public Kind Running => _active != null ? _mode.Kind : Kind.None;

        private static readonly EventEffect[] BuffedEvent =
        {
            EventEffect.BZ_Fame_2x,
            EventEffect.World_Exp_2x,
            EventEffect.BZ_No_FriendlyFire,
        };

        private static readonly EventEffect[] NoEffects = new EventEffect[0];

        public static EventEffect[] EffectsOf(Kind kind)
        {
            switch (kind)
            {
                case Kind.Beacon:
                case Kind.Kraken:
                    return BuffedEvent;
                default:
                    return NoEffects;
            }
        }

        public bool HasEffect(EventEffect effect)
        {
            return IsRunning && System.Array.IndexOf(EffectsOf(Running), effect) >= 0;
        }

        public void Tick(float deltaTime)
        {
            if (ServerHub.Runner == null || !WorldIsLive)
            {
                return;
            }

            if (!_bootHandled)
            {
                _bootHandled = true;
                Kind wanted = KindNamed(ServerHub.Config?.StartEvent);
                if (wanted != Kind.None)
                {
                    Start(wanted);
                }
            }

            _scheduleClock += deltaTime;
            if ((ServerHub.Config?.EventsEnabled ?? true) && _scheduleClock >= 10f)
            {
                _scheduleClock = 0f;
                CheckSchedule();
            }

            if (_pending != null)
            {
                SpawnPending(deltaTime);
                return;
            }

            if (_active == null)
            {
                return;
            }

            if (!_active.IsValid)
            {
                try
                {
                    _mode.Ending();
                }
                catch (Exception e)
                {
                    ServerLog.Warn($"{_mode.Kind} event teardown failed: {e.Message}");
                }

                Clear();
                return;
            }

            _timer -= deltaTime;

            if (!_started)
            {
                PublishTimer();
                if (_timer <= 0f)
                {
                    Begin();
                }

                return;
            }

            _mode.Advance(deltaTime, _timer);
            PublishTimer();

            if (_timer <= 0f)
            {
                Stop("ended");
            }
        }

        private void CheckSchedule()
        {
            EventSchedule.CellAt(
                DateTime.UtcNow, ScheduleOffsetHours, out int day, out int hour);

            int slot = EventSchedule.SlotIndex(day, hour);

            if (slot == _lastSlot)
            {
                return;
            }

            _lastSlot = slot;

            int entry = Schedule[day, hour];
            if (entry != (int)Kind.Beacon && entry != (int)Kind.Kraken
                && entry != (int)Kind.Royale)
            {
                return;
            }

            if (_active != null || _pending != null)
            {
                Kind busy = _active != null ? _mode.Kind : _pending.Kind;
                ServerLog.Info($"scheduled {(Kind)entry} skipped - {busy} still running");
                return;
            }

            Start((Kind)entry, scheduled: true);
        }

        public bool Start(Kind kind, bool scheduled = false)
        {
            if (_active != null || _pending != null || kind == Kind.None
                || ServerHub.Runner == null)
            {
                return false;
            }

            EventMode mode = EventMode.Create(this, kind);
            if (mode == null)
            {
                ServerLog.Warn($"no behaviour registered for the {kind} event");
                return false;
            }

            ServerPrefabs prefabs = ServerPrefabs.Load();
            GameObject prefab = prefabs != null ? mode.Prefab(prefabs) : null;
            if (prefab == null)
            {
                ServerLog.Warn($"no prefab bound for the {kind} event");
                return false;
            }

            _pending = mode;
            _pendingWaited = 0f;

            Vector3 where = mode.Place();
            int token = ++_spawnToken;

            try
            {
                ServerHub.Runner.SpawnAsync(prefab, where, Quaternion.identity,
                    onCompleted: result => OnSpawned(token, mode, scheduled, where, result.Object));
            }
            catch (Exception e)
            {
                ServerLog.Warn($"could not spawn the {kind} event: {e.Message}");
                _pending = null;

                _spawnToken++;
                return false;
            }

            ServerLog.Info($"{kind} event prefab loading");
            return true;
        }

        private void SpawnPending(float deltaTime)
        {
            _pendingWaited += deltaTime;

            if (_pendingWaited > 30f)
            {
                ServerLog.Warn($"the {_pending.Kind} event prefab never loaded");
                _pending = null;

                _spawnToken++;
            }
        }

        private static GameObject _statsPlaceholder;

        private static GameObject StatsPlaceholder()
        {
            if (_statsPlaceholder == null)
            {
                _statsPlaceholder = new GameObject("event stats (server placeholder)");
                UnityEngine.Object.DontDestroyOnLoad(_statsPlaceholder);
            }

            return _statsPlaceholder;
        }

        private static void GiveClientOnlyStartSomethingToHold(NetworkObject spawned)
        {
            if (spawned == null)
            {
                return;
            }

            var kraken = spawned.GetComponentInChildren<EventKraken>();
            if (kraken != null && kraken.stats == null)
            {
                kraken.stats = StatsPlaceholder();
            }

            var beacon = spawned.GetComponentInChildren<EventBZBeacon>();
            if (beacon != null && beacon.stats == null)
            {
                beacon.stats = StatsPlaceholder();
            }
        }

        private void OnSpawned(int token, EventMode mode, bool scheduled, Vector3 where,
            NetworkObject spawned)
        {
            GiveClientOnlyStartSomethingToHold(spawned);

            if (token != _spawnToken)
            {
                ServerLog.Warn($"a late {mode.Kind} event spawn arrived and was discarded");

                try
                {
                    if (spawned != null && spawned.IsValid)
                    {
                        ServerHub.Runner?.Despawn(spawned);
                    }
                }
                catch (Exception e)
                {
                    ServerLog.Warn($"could not despawn the late {mode.Kind} event: {e.Message}");
                }

                return;
            }

            _pending = null;

            if (spawned == null)
            {
                ServerLog.Warn($"the {mode.Kind} event did not spawn");
                return;
            }

            _active = spawned;
            _mode = mode;
            _started = false;
            _ending = false;
            _timer = LeadInSeconds;
            _scores.Clear();
            _scoresKind = mode.Kind;
            _beaconWinner = Core.BeaconFaction.None;

            mode.Bind(spawned);
            PublishTimer();

            ServerLog.Info($"{mode.Kind} event spawned at {(int)where.x},{(int)where.z}"
                + (scheduled ? " (scheduled)" : " (triggered)"));
            AdminService.Broadcast($"{mode.Label} starts in 5 minutes.");
        }

        private void Begin()
        {
            _started = true;
            _timer = _mode.Duration;

            _mode.Begin();
            PublishTimer();

            EventEffect[] effects = EffectsOf(_mode.Kind);
            string buffs = effects.Length == 0
                ? "none"
                : string.Join(", ", System.Array.ConvertAll(effects, e => e.ToString()));

            ServerLog.Info(
                $"{_mode.Kind} event started for {(int)_timer}s, effects: {buffs}");
            AdminService.Broadcast($"{_mode.Label} has begun.");
        }

        public bool Stop(string why = "stopped")
        {
            if (_active == null || _ending)
            {
                return false;
            }

            _ending = true;
            EventMode mode = _mode;

            mode.Ending();

            try
            {
                mode.Report(Contributions());
                mode.Award(_scores);
            }
            catch (Exception e)
            {
                ServerLog.Warn($"{mode.Kind} event payout failed: {e.Message}");
            }

            try
            {
                if (_active.IsValid)
                {
                    ServerHub.Runner?.Despawn(_active);
                }
            }
            catch (Exception e)
            {
                ServerLog.Warn($"could not despawn the {mode.Kind} event: {e.Message}");
            }

            ServerLog.Info($"{mode.Kind} event {why}");
            AdminService.Broadcast($"{mode.Label} has ended.");

            Clear();
            return true;
        }

        private void Clear()
        {
            _active = null;
            _mode = null;
            _started = false;
            _ending = false;
            _timer = 0f;
        }

        private void PublishTimer() => _mode?.PublishTimer(Mathf.Max(0f, _timer));

        internal void Credit(Account account, bool capture = false, int kills = 0,
            int deaths = 0, int damage = 0, int points = 0)
        {
            if (account == null)
            {
                return;
            }

            if (!_scores.TryGetValue(account.id, out Contribution c))
            {
                c = new Contribution
                {
                    Name = account.nickname,
                    Clan = account.clanTag ?? string.Empty,
                    Faction = account.faction,
                };

                _scores[account.id] = c;
            }

            if (capture)
            {
                c.Captures++;
                c.Points += 10;
            }

            c.Kills += kills;
            c.Deaths += deaths;
            c.Points += points;

            if (damage > 0)
            {

                int earned = c.Damage / DamagePerPoint;
                c.Damage += damage;
                c.Points += c.Damage / DamagePerPoint - earned;
            }
        }

        public bool JoinRoyale(PlayerRef player, Account account)
        {
            if (_active == null || account == null || _started)
            {
                return false;
            }

            return _mode.Join(player, account);
        }

        public void NoteKill(Account killer, Account victim)
        {
            if (_active == null || !_started)
            {
                return;
            }

            _mode.NoteKill(killer, victim);
        }

        public void NoteDamage(NetworkObject attacker, NPCBehaviour target, int amount)
        {
            if (_active == null || !_started || target == null || amount <= 0)
            {
                return;
            }

            Account account = ServerHub.AccountFor(CombatService.FindOwner(attacker));
            if (account == null)
            {
                return;
            }

            _mode.NoteDamage(account, target, amount);
        }

        public void NoteNpcKill(Account killer, NPCBehaviour target)
        {
            if (_active == null || !_started || killer == null || target == null)
            {
                return;
            }

            _mode.NoteNpcKill(killer, target);
        }

        private List<Contribution> Contributions()
        {
            var list = new List<Contribution>();
            foreach (KeyValuePair<string, Contribution> kv in _scores)
            {
                list.Add(kv.Value);
            }

            return list;
        }

        public List<Contribution> ReportFor(Kind kind) =>
            kind != Kind.None && kind == _scoresKind
                ? Contributions()
                : new List<Contribution>();

        internal static Vector3 AreaCentre(int areaIndex)
        {
            AreaStation[] stations = UnityEngine.Object.FindObjectsByType<AreaStation>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);

            Vector3 sum = Vector3.zero;
            int found = 0;

            foreach (AreaStation station in stations)
            {
                if (station?.data != null && station.data.areaIndex == areaIndex)
                {
                    sum += station.transform.position;
                    found++;
                }
            }

            if (found > 0)
            {
                return sum / found;
            }

            Area[] areas = UnityEngine.Object.FindObjectsByType<Area>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);

            foreach (Area area in areas)
            {
                if (area != null && area.areaIndex == areaIndex)
                {
                    return area.transform.position;
                }
            }

            ServerLog.Warn($"no sector {areaIndex} to place an event in");
            return Vector3.zero;
        }

        public static Kind KindNamed(string name) => EventMode.KindNamed(name);
    }
}
