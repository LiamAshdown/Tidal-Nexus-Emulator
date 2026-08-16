using System.Collections.Generic;
using Fusion;
using TidalNexus.StandaloneServer.Data;
using UnityEngine;

namespace TidalNexus.StandaloneServer.Services
{

    public sealed class OrdnanceService
    {

        private static float BlastRadius => ServerHub.Config?.BlastRadius ?? 8f;

        private static float MineTrigger => ServerHub.Config?.MineTrigger ?? 5f;

        private static float BombFuse => ServerHub.Config?.BombFuse ?? 3f;

        private static float MineLifetime => ServerHub.Config?.MineLifetime ?? 300f;

        private readonly List<Placed> _placed = new List<Placed>();

        private sealed class Placed
        {
            public NetworkObject Object;
            public NetworkObject Owner;
            public int Damage;
            public float Timer;
            public bool IsMine;
        }

        private static float TorpedoRange =>
            (GameData.Settings != null && GameData.Settings.torpedoDistance > 0f
                ? GameData.Settings.torpedoDistance
                : 10f) * 2f;

        private static bool WithinTorpedoRange(PlayerRef player, NetworkObject target, string who)
        {
            NetworkObject shooter = WorldLookup.ObjectOf(player);
            if (shooter == null)
            {
                return false;
            }

            Vector3 from = shooter.transform.position;
            Vector3 to = target.transform.position;

            if (Core.WeaponRange.InRange(from.x, from.y, from.z, to.x, to.y, to.z, TorpedoRange))
            {
                return true;
            }

            ServerLog.Warn(
                $"{who} torpedoed a target {Vector3.Distance(from, to):F1} away; " +
                $"the reach is {TorpedoRange:F0}");
            return false;
        }

        public bool DropTorpedo(PlayerRPC self, Account account, PlayerRef player, NetworkId targetId)
        {
            if (self == null || account == null || ServerHub.Runner == null)
            {
                return false;
            }

            NetworkObject target = ServerHub.Runner.FindObject(targetId);
            if (target == null)
            {
                ServerLog.Warn($"torpedo at unknown target {targetId}");
                return false;
            }

            if (ServerHub.Combat == null || !ServerHub.Combat.MayAttack(player, target))
            {
                ServerLog.Warn($"{account.nickname} torpedoed a target they may not attack");
                return false;
            }

            if (!WithinTorpedoRange(player, target, account.nickname))
            {
                return false;
            }

            int index = account.torpedoIndex;
            if (!TakeTorpedo(account, index))
            {
                ServerLog.Info($"{account.nickname} fired torpedo {index} with none in stock");
                return false;
            }

            self.RPC_DropTorpedoRelay(index, false, targetId, 1f, false, NextOrdnanceId());

            Save(account, player);
            return true;
        }

        public bool DropDecoy(PlayerRPC self, Account account, PlayerRef player, NetworkId id)
        {
            if (self == null || account == null)
            {
                return false;
            }

            int index = account.decoyIndex;
            if (!TakeDecoy(account, index))
            {
                ServerLog.Info($"{account.nickname} dropped decoy {index} with none in stock");
                return false;
            }

            self.RPC_DropDecoyRelay(index, id);

            Save(account, player);
            return true;
        }

        public bool DropBomb(Account account, PlayerRef player)
        {
            int index = account?.bombIndex ?? 0;
            BombAmmoData data = Bomb(index);

            if (data == null || !TakeBomb(account, index))
            {
                ServerLog.Info($"{account?.nickname} dropped bomb {index} with none in stock");
                return false;
            }

            return Place(account, player, data.GetPrefab((Enums.Faction)account.faction),
                data.damage, BombFuse, isMine: false);
        }

        public bool DropMine(Account account, PlayerRef player)
        {
            int index = account?.mineIndex ?? 0;
            MineAmmoData data = Mine(index);

            if (data == null || !TakeMine(account, index))
            {
                ServerLog.Info($"{account?.nickname} dropped mine {index} with none in stock");
                return false;
            }

            return Place(account, player, data.GetPrefab((Enums.Faction)account.faction),
                data.damage, MineLifetime, isMine: true);
        }

        private bool Place(Account account, PlayerRef player, NetworkPrefabRef prefab,
            int damage, float timer, bool isMine)
        {
            if (ServerHub.Runner == null || !prefab.IsValid)
            {
                ServerLog.Warn($"no prefab for {(isMine ? "mine" : "bomb")}");
                return false;
            }

            NetworkObject owner = ServerHub.Runner.GetPlayerObject(player);
            if (owner == null)
            {
                return false;
            }

            NetworkObject placed = ServerHub.Runner.Spawn(
                prefab, owner.transform.position, Quaternion.identity, null,
                (runner, obj) =>
                {
                    var mine = obj.GetComponent<Mine>();
                    if (mine != null)
                    {

                        mine.ownerId = owner.Id;
                        mine.damage = damage;
                    }

                    var bomb = obj.GetComponent<Bomb>();
                    if (bomb != null)
                    {
                        bomb.damage = damage;
                    }
                });

            if (placed == null)
            {
                return false;
            }

            _placed.Add(new Placed
            {
                Object = placed,
                Owner = owner,
                Damage = damage,
                Timer = timer,
                IsMine = isMine,
            });

            Save(account, player);
            ServerLog.Info($"{account.nickname} dropped a {(isMine ? "mine" : "bomb")} "
                + $"for {damage} at {owner.transform.position}");
            return true;
        }

        public void Tick(float deltaTime)
        {
            if (_placed.Count == 0 || ServerHub.Runner == null)
            {
                return;
            }

            for (int i = _placed.Count - 1; i >= 0; i--)
            {
                Placed p = _placed[i];

                if (p.Object == null || !p.Object.IsValid)
                {
                    _placed.RemoveAt(i);
                    continue;
                }

                p.Timer -= deltaTime;

                bool detonate = p.Timer <= 0f;

                if (!detonate && p.IsMine)
                {
                    detonate = Triggered(p);
                }

                if (!detonate)
                {
                    continue;
                }

                Detonate(p);
                _placed.RemoveAt(i);
            }
        }

        private bool Triggered(Placed p)
        {
            Vector3 at = p.Object.transform.position;
            PlayerRef owner = CombatService.FindOwner(p.Owner);

            foreach (NetworkObject candidate in Nearby(at, MineTrigger))
            {
                if (!IsOwn(owner, candidate) && ServerHub.Combat.MayAttack(owner, candidate))
                {
                    return true;
                }
            }

            return false;
        }

        private void Detonate(Placed p)
        {
            Vector3 at = p.Object.transform.position;
            PlayerRef owner = CombatService.FindOwner(p.Owner);
            int hits = 0;

            foreach (NetworkObject victim in Nearby(at, BlastRadius))
            {
                if (IsOwn(owner, victim) ||
                    !ServerHub.Combat.MayAttack(owner, victim))
                {
                    continue;
                }

                ServerHub.Combat.ApplyDamage(p.Owner, victim, p.Damage);
                hits++;
            }

            try
            {

                ServerHub.Runner.Despawn(p.Object);
            }
            catch (System.Exception e)
            {
                ServerLog.Warn($"despawn of spent ordnance failed: {e.Message}");
            }

            ServerLog.Info($"{(p.IsMine ? "mine" : "bomb")} detonated at {at}, {hits} hit");
        }

        private static bool IsOwn(PlayerRef owner, NetworkObject candidate)
        {
            if (owner == PlayerRef.None)
            {
                return false;
            }

            return CombatService.FindOwner(candidate) == owner;
        }

        private static IEnumerable<NetworkObject> Nearby(Vector3 at, float radius)
        {
            float sqr = radius * radius;

            foreach (Health health in Object.FindObjectsByType<Health>(
                         FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (health == null || health.hull <= 0)
                {
                    continue;
                }

                if ((health.transform.position - at).sqrMagnitude > sqr)
                {
                    continue;
                }

                NetworkObject obj = health.GetComponentInParent<NetworkObject>();
                if (obj != null)
                {
                    yield return obj;
                }
            }
        }

        private bool TakeTorpedo(Account a, int index)
        {
            switch (index)
            {
                case 1: return Take(ref a.torpedo1);
                case 2: return Take(ref a.torpedo2);
                case 3: return Take(ref a.torpedo3);
                case 4: return Take(ref a.torpedo4);
                case 5: return Take(ref a.torpedoice);
                case 6: return Take(ref a.photonTorpedo1);
                case 7: return Take(ref a.photonTorpedo2);
                case 8: return Take(ref a.photonTorpedo3);
                case 9: return Take(ref a.photonTorpedo4);
                default: return false;
            }
        }

        private bool TakeBomb(Account a, int index)
        {
            switch (index)
            {
                case 1: return Take(ref a.bomb1);
                case 2: return Take(ref a.bomb2);
                case 3: return Take(ref a.bomb3);
                case 4: return Take(ref a.photonBomb1);
                case 5: return Take(ref a.photonBomb2);
                case 6: return Take(ref a.photonBomb3);
                default: return false;
            }
        }

        private bool TakeMine(Account a, int index)
        {
            switch (index)
            {
                case 1: return Take(ref a.mine1);
                case 2: return Take(ref a.mine2);
                case 3: return Take(ref a.mine3);
                case 4: return Take(ref a.photonMine1);
                case 5: return Take(ref a.photonMine2);
                case 6: return Take(ref a.photonMine3);
                default: return false;
            }
        }

        private bool TakeDecoy(Account a, int index)
        {
            switch (index)
            {
                case 1: return Take(ref a.decoy1);
                case 2: return Take(ref a.decoy2);
                case 3: return Take(ref a.decoy3);
                default: return false;
            }
        }

        private static bool Take(ref int counter)
        {
            if (counter <= 0)
            {
                return false;
            }

            counter--;
            return true;
        }

        private static BombAmmoData Bomb(int index)
        {
            List<BombAmmoData> list = GameData.Data?.bombAmmoDatas;
            return list != null && index >= 1 && index <= list.Count ? list[index - 1] : null;
        }

        private static MineAmmoData Mine(int index)
        {
            List<MineAmmoData> list = GameData.Data?.mineAmmoDatas;
            return list != null && index >= 1 && index <= list.Count ? list[index - 1] : null;
        }

        private static void Save(Account account, PlayerRef player)
        {
            AccountStore.MarkDirty(account);
            ServerHub.Accounts?.PushState(player, account);
        }

        private static int _ordnanceId;

        private static int NextOrdnanceId() => ++_ordnanceId;
    }
}
