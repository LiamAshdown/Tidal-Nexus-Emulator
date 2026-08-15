using System;
using System.Collections.Generic;
using Fusion;
using TidalNexus.StandaloneServer.Services;
using UnityEngine;

namespace TidalNexus.StandaloneServer
{

    public sealed class WorldDirector : MonoBehaviour
    {

        public static WorldDirector Ensure()
        {

            foreach (WorldDirector stale in
                UnityEngine.Object.FindObjectsByType<WorldDirector>(FindObjectsSortMode.None))
            {
                if (stale != null)
                {
                    UnityEngine.Object.Destroy(stale.gameObject);
                }
            }

            _active = null;

            var host = new GameObject("WorldDirector (runtime)");
            UnityEngine.Object.DontDestroyOnLoad(host);
            WorldDirector director = host.AddComponent<WorldDirector>();
            ServerLog.Info($"world director object created (active={host.activeInHierarchy}, "
                + $"enabled={director.enabled})");
            return director;
        }

        private const float NpcAttackInterval = 1.5f;

        private const float MaxAggroRadius = 70f;

        private const float FallbackAggroRadius = 40f;

        private const float HomeLeash = 130f;

        private const float ChaseSpeedMultiplier = 1.25f;

        private const float LeashMultiplier = 2f;

        private const float RescanInterval = 3f;

        private readonly Dictionary<NPCBehaviour, NpcState> _states =
            new Dictionary<NPCBehaviour, NpcState>();

        private float _nextRescan;
        private readonly List<Player> _players = new List<Player>();

        public int TrackedNpcs => _states.Count;

        private static WorldDirector _active;

        private void Awake()
        {

            if (_active != null && _active != this)
            {
                enabled = false;
                return;
            }

            _active = this;
        }

        private bool _ticked;

        private void FixedUpdate()
        {

            if (!ServerHub.Ready || ServerHub.Runner == null)
            {
                return;
            }

            if (!_ticked)
            {
                _ticked = true;
                ServerLog.Info("world director simulating");
            }

            float delta = Time.fixedDeltaTime;

            _players.Clear();
            foreach (Player p in UnityEngine.Object.FindObjectsByType<Player>(FindObjectsSortMode.None))
            {
                if (p != null && WorldLookup.IsAlive(p.health))
                {
                    _players.Add(p);
                }
            }

            if (Time.time >= _nextRescan)
            {
                _nextRescan = Time.time + RescanInterval;
                Rescan();
            }

            foreach (KeyValuePair<NPCBehaviour, NpcState> pair in _states)
            {
                NpcState state = pair.Value;

                if (state.Npc == null)
                {
                    continue;
                }

                NpcDamageLedger.TickLedger(state.Npc, delta);

                if (state.Npc.health != null && WorldLookup.HullOf(state.Npc.health) <= 0)
                {

                    state.Target = null;
                    PublishTarget(state);

                    ServerHub.Npcs?.Died(state.Npc);
                    _finished.Add(pair.Key);
                    continue;
                }

                Acquire(state);
                PublishTarget(state);
                Move(state, delta);
                Attack(state);
            }

            if (_finished.Count > 0)
            {
                foreach (NPCBehaviour done in _finished)
                {
                    _states.Remove(done);
                }

                _finished.Clear();
            }

            ApplyPlayerDamage(delta);
        }

        private Player TopAttacker(NpcState state)
        {
            List<NPCBehaviour.Attackers> attackers = state.Npc.attackers;
            if (attackers == null || attackers.Count == 0)
            {
                return null;
            }

            float reach = ClampedAggroRadius(state) * Mathf.Max(1f, LeashMultiplier);

            Player best = null;
            int bestDamage = 0;

            foreach (NPCBehaviour.Attackers entry in attackers)
            {
                if (entry == null || entry.player == null || entry.damage <= bestDamage)
                {
                    continue;
                }

                if (entry.player.health == null || WorldLookup.HullOf(entry.player.health) <= 0)
                {
                    continue;
                }

                if (Enums.Distance2D(entry.player.transform.position, state.Position) > reach)
                {
                    continue;
                }

                best = entry.player;
                bestDamage = entry.damage;
            }

            return best;
        }

        private static void PublishTarget(NpcState state)
        {
            NPCAttackRadius radius = state.Npc.attackRadius;
            if (radius == null)
            {
                return;
            }

            try
            {
                if (state.Target != null && state.Target.Object != null)
                {
                    radius.TargetPlayerRef = state.Target.Object.Id;
                    radius.TargetPlayer = state.Target;
                }
                else if (radius.TargetPlayerRef.IsValid)
                {
                    radius.TargetPlayerRef = default;
                    radius.TargetPlayer = null;
                }
            }
            catch (Exception)
            {

            }
        }

        private void Rescan()
        {
            NPCBehaviour[] found;
            try
            {
                found = UnityEngine.Object.FindObjectsByType<NPCBehaviour>(FindObjectsSortMode.None);
            }
            catch
            {
                return;
            }

            foreach (NPCBehaviour npc in found)
            {
                if (npc != null && !_states.ContainsKey(npc))
                {
                    _states[npc] = new NpcState(npc);
                }
            }

            var stale = new List<NPCBehaviour>();
            foreach (KeyValuePair<NPCBehaviour, NpcState> pair in _states)
            {
                if (pair.Key == null)
                {
                    stale.Add(pair.Key);
                }
            }

            foreach (NPCBehaviour key in stale)
            {
                _states.Remove(key);
            }
        }

        private void Acquire(NpcState state)
        {
            NPCData data = state.Npc.data;
            if (data == null)
            {
                return;
            }

            if (state.Target != null)
            {
                bool alive = WorldLookup.IsAlive(state.Target.health);
                float leash = ClampedAggroRadius(state) * Mathf.Max(1f, LeashMultiplier);
                bool nearHome = Vector3.Distance(state.Position, state.Home) <= HomeLeash;

                if (alive && nearHome &&
                    Vector3.Distance(state.Target.transform.position, state.Position) <= leash)
                {
                    return;
                }

                state.Target = null;
            }

            Player retaliation = TopAttacker(state);
            if (retaliation != null)
            {
                state.Target = retaliation;
                return;
            }

            if (!data.isAggressive)
            {
                return;
            }

            float radius = ClampedAggroRadius(state);
            Player best = null;
            float bestDistance = float.MaxValue;

            foreach (Player player in _players)
            {
                float distance = Vector3.Distance(state.Position, player.transform.position);
                if (distance <= radius && distance < bestDistance)
                {
                    best = player;
                    bestDistance = distance;
                }
            }

            state.Target = best;
        }

        private float ClampedAggroRadius(NpcState state)
        {
            return Mathf.Min(AggroRadius(state), MaxAggroRadius);
        }

        private float AggroRadius(NpcState state)
        {
            try
            {
                if (state.Npc.attackRadius != null)
                {
                    var collider = state.Npc.attackRadius.GetComponent<SphereCollider>();
                    if (collider != null)
                    {
                        return collider.radius * Mathf.Max(
                            state.Npc.attackRadius.transform.lossyScale.x, 0.01f);
                    }
                }
            }
            catch
            {

            }

            return FallbackAggroRadius;
        }

        private void Move(NpcState state, float delta)
        {
            NPCBehaviour npc = state.Npc;
            NPCData data = npc.data;
            if (data == null || data.movementSpeed <= 0f)
            {
                return;
            }

            if (npc.idleNPC)
            {
                return;
            }

            if (state.Target != null)
            {
                Vector3 from = npc.syncedPosition != Vector3.zero
                    ? npc.syncedPosition
                    : npc.transform.position;

                Vector3 to = state.Target.transform.position;

                float speed = data.movementSpeed * ChaseSpeedMultiplier;
                Vector3 next = Vector3.MoveTowards(from, to, speed * delta);

                float step = Vector3.Distance(npc.transform.position, next);
                if (step > speed * delta * 4f + 0.5f)
                {
                    ServerLog.Info(
                        $"[JUMP] {(data != null ? data.name : "npc")} moved {step:F1} in one tick: " +
                        $"{npc.transform.position} -> {next} " +
                        $"(synced was {npc.syncedPosition}, speed {speed:F1})");
                }

                npc.syncedPosition = next;
                npc.transform.position = next;
                Face(npc, to);
                return;
            }

            Vector3 patrol = PatrolPosition(state, data);

            state.PositionClock -= delta;
            if (state.PositionClock > 0f)
            {
                return;
            }

            state.PositionClock = IdlePositionInterval;

            Face(npc, patrol);
            npc.syncedPosition = patrol;
            npc.transform.position = patrol;
        }

        private const float IdlePositionInterval = 0.1f;

        private static Vector3 PatrolPosition(NpcState state, NPCData data)
        {
            NPCBehaviour npc = state.Npc;

            if (npc.idleNPC ||
                npc.assignedPatrolPositions == null ||
                npc.assignedPatrolPositions.Count < 2 ||
                GlobalTimer.Instance == null)
            {
                return npc.transform.position;
            }

            EnsureLoop(state);

            if (state.LoopLength <= 0f)
            {
                return npc.transform.position;
            }

            float period = state.LoopLength / Mathf.Max(0.01f, data.movementSpeed);
            var phase = (float)(GlobalTimer.Instance.Timer / (double)period % 1.0);
            if (phase < 0f)
            {
                phase += 1f;
            }

            return PositionOnLoop(state, phase * state.LoopLength);
        }

        private static void EnsureLoop(NpcState state)
        {
            var points = state.Npc.assignedPatrolPositions;

            if (state.LoopSegments != null && state.LoopSegments.Length == points.Count)
            {
                return;
            }

            var lengths = new float[points.Count];
            float total = 0f;

            for (int i = 0; i < points.Count; i++)
            {

                Vector3 a = points[i];
                Vector3 b = points[(i + 1) % points.Count];
                lengths[i] = Vector3.Distance(a, b);
                total += lengths[i];
            }

            state.LoopSegments = lengths;
            state.LoopLength = total;
        }

        private static Vector3 PositionOnLoop(NpcState state, float travelled)
        {
            var points = state.Npc.assignedPatrolPositions;
            float covered = 0f;

            for (int i = 0; i < state.LoopSegments.Length; i++)
            {
                float segment = state.LoopSegments[i];
                if (segment > 0f && travelled < covered + segment)
                {
                    return Vector3.Lerp(
                        points[i],
                        points[(i + 1) % points.Count],
                        (travelled - covered) / segment);
                }

                covered += segment;
            }

            return points[0];
        }

        private static void Face(NPCBehaviour npc, Vector3 at)
        {
            Vector3 facing = at - npc.transform.position;
            if (facing.sqrMagnitude > 0.01f)
            {
                npc.transform.rotation = Quaternion.LookRotation(facing);
            }
        }

        private void Attack(NpcState state)
        {
            if (state.Target == null || state.Npc.data == null)
            {
                return;
            }

            if (Time.time < state.NextAttackAt)
            {
                return;
            }

            float distance = Enums.Distance2D(state.Position, state.Target.transform.position);
            if (distance > ClampedAggroRadius(state))
            {
                return;
            }

            state.NextAttackAt = Time.time + NpcAttackInterval;

            Player victim = state.Target;
            bool aliveBefore = WorldLookup.IsAlive(victim.health);

            NpcDamageLedger.DamagePlayer(victim, state.Npc.data.damage);

            if (aliveBefore && victim.health != null && WorldLookup.HullOf(victim.health) <= 0)
            {
                state.Target = null;
                PublishTarget(state);
                ReportPlayerDeath(victim);
            }
        }

        private static void ReportPlayerDeath(Player victim)
        {
            if (ServerHub.Runner == null || victim == null || victim.Object == null)
            {
                return;
            }

            try
            {
                ServerHub.Combat?.PlayerDied(victim.Object.InputAuthority);
            }
            catch (Exception e)
            {
                ServerLog.Warn($"could not book player death: {e.Message}");
            }
        }

        private void ApplyPlayerDamage(float delta)
        {
            foreach (Player player in _players)
            {
                if (player.networkValues == null)
                {
                    continue;
                }

                NpcState target = FindTargetOf(player);

                bool firing = target != null &&
                              target.Alive &&
                              CombatService.InCannonRange(
                                  player.transform.position, target.Position);

                try
                {
                    if (player.networkValues.inShootingRange != firing)
                    {
                        player.networkValues.inShootingRange = firing;

                        ServerLog.Info(
                            $"[COMBAT] inShootingRange={firing} target=" +
                            $"{(target?.Npc != null && target.Npc.data != null ? target.Npc.data.name : "none")}");
                    }
                }
                catch (Exception)
                {

                }
            }
        }

        private NpcState FindTargetOf(Player player)
        {
            NetworkId attacking;
            try
            {
                attacking = player.networkValues.currentAttackingTarget;
            }
            catch
            {
                return null;
            }

            if (!attacking.IsValid)
            {
                return null;
            }

            foreach (KeyValuePair<NPCBehaviour, NpcState> pair in _states)
            {
                NpcState state = pair.Value;
                if (state.Alive && state.Npc.Object != null &&
                    state.Npc.Object.Id.Equals(attacking))
                {
                    return state;
                }
            }

            return null;
        }

        private readonly List<NPCBehaviour> _finished = new List<NPCBehaviour>();
    }
}
