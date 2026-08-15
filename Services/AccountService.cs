using System;
using System.Collections.Generic;
using Fusion;
using TidalNexus.StandaloneServer.Core;
using TidalNexus.StandaloneServer.Data;
using UnityEngine;

namespace TidalNexus.StandaloneServer.Services
{

    public sealed class AccountService
    {

        public const int FactionNautilus = (int)Enums.Faction.Nautilus;
        public const int FactionSerran = (int)Enums.Faction.Serran;
        public const int FactionAzularis = (int)Enums.Faction.Azularis;
        public const int FactionNone = (int)Enums.Faction.None;

        public Account Login(PlayerRef player, string presentedId, string presentedNickname)
        {
            Account account = AccountStore.GetOrCreate(presentedId, presentedNickname);

            if (account.banned)
            {
                ServerLog.Info($"rejected banned account {account.id} ({account.nickname})");
                return null;
            }

            PlayerSession existing = ServerHub.SessionOf(account);
            PlayerRef occupied = existing != null ? existing.Player : PlayerRef.None;
            if (occupied != PlayerRef.None && occupied != player)
            {
                ServerLog.Warn(
                    $"{account.nickname} [{account.id}] is already online as player " +
                    $"{occupied.PlayerId} - displacing that session");

                Wire.SendDisconnect(occupied);

                PlayerDirector.Leave(ServerHub.Runner, occupied);

                ServerHub.DisconnectAfterFlush(occupied, "displaced by a second login");
            }

            ServerHub.BeginSession(player, account);
            ServerLog.Info(
                $"login {account.nickname} [{account.id}] " +
                $"lvl {account.level} faction {account.faction}");

            return account;
        }

        public void Logout(PlayerRef player)
        {
            Account account = ServerHub.AccountFor(player);
            if (account == null)
            {
                return;
            }

            NetworkObject obj = WorldLookup.ObjectOf(player);
            if (obj != null)
            {
                Vector3 p = obj.transform.position;
                account.x = p.x;
                account.y = p.y;
                account.z = p.z;
            }

            ServerHub.EndSession(player);
        }

        private static int RankIn(Clan clan, Account account)
        {
            if (clan?.members == null)
            {
                return -1;
            }

            foreach (ClanMember member in clan.members)
            {
                if (string.Equals(member.accountId, account.id,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return member.rank;
                }
            }

            return -1;
        }

        private static ExtraData.ExtraType CoreModuleOf(Account account)
        {
            List<ExtraData> catalogue = GameData.Data?.extraDatas;
            if (catalogue == null)
            {
                return ExtraData.ExtraType.None;
            }

            foreach (int index in account.equippedExtras)
            {
                if (index >= 0 && index < catalogue.Count &&
                    catalogue[index] is CoreModuleData module)
                {
                    return module.type;
                }
            }

            return ExtraData.ExtraType.None;
        }

        public void PushWallet(PlayerRef player, Account account)
        {
            if (account == null)
            {
                return;
            }

            NetworkObject obj = WorldLookup.ObjectOf(player);
            if (obj == null)
            {
                return;
            }

            try
            {
                var local = obj.GetComponentInChildren<PlayerLocalValues>();
                if (local == null)
                {
                    return;
                }

                local.credits = (int)Math.Min(account.credits, int.MaxValue);
                local.borax = account.borax;
                local.battlePoint = account.battlePoints;
            }
            catch (Exception ex)
            {
                ServerLog.Warn($"could not push wallet: {ex.Message}");
            }
        }

        public void PushState(PlayerRef player, Account account)
        {
            if (account == null)
            {
                return;
            }

            NetworkObject obj = WorldLookup.ObjectOf(player);
            if (obj == null)
            {
                return;
            }

            var values = obj.GetComponentInChildren<PlayerNetworkValues>();
            if (values == null)
            {
                ServerLog.Warn($"no PlayerNetworkValues for {account.nickname}");
                return;
            }

            try
            {
                int[] name = UsernameCodec3Int.Encode(Sanitise(account.nickname));
                values.nickname1 = name[0];
                values.nickname2 = name[1];
                values.nickname3 = name[2];

                int[] tag = UsernameCodec3Int.Encode(
                    string.IsNullOrEmpty(account.clanTag) ? "null" : Sanitise(account.clanTag));
                values.clanTag1 = tag[0];
                values.clanTag2 = tag[1];
                values.clanTag3 = tag[2];

                values.speed = TurbineSpeed(account);

                values.weaponSlots = account.weaponSlots;

                values.experience = (int)Math.Min(account.experience, int.MaxValue);
                values.prestige = account.prestige;
                values.selectedAmmo = account.ammoIndex;
                values.isVip = account.IsVip;
                values.playerRef = player;

                values.permission = (Enums.Permission)account.Role;

                values.title = account.title;
                values.design = account.design;
                values.skin = Math.Max(0, account.skin);
                values.sentryDesign = Math.Max(0, account.sentryDesign);
                values.hpxDesign = Math.Max(0, account.hpxDesign);
                values.spxDesign = Math.Max(0, account.spxDesign);

                values.coreModule = CoreModuleOf(account);

                Clan clan = AccountStore.FindClan(account.clanTag);
                values.clanBanner = clan != null ? clan.banner : "0,0,0,0,0,0,0,0";

                values.clanRank = (Enums.ClanRank)Wire.ClientRank(RankIn(clan, account));

                var local = obj.GetComponentInChildren<PlayerLocalValues>();
                if (local != null)
                {
                    local.hullSlots = account.hullSlots;
                    local.shieldSlots = account.shieldSlots;
                    local.turbineSlots = account.turbineSlots;

                    local.lastArenaPos = account.lastArenaPos > 0
                        ? account.lastArenaPos
                        : Account.NeverPlacedArena;

                    local.credits = (int)System.Math.Min(account.credits, int.MaxValue);
                    local.borax = account.borax;
                    local.battlePoint = account.battlePoints;

                    local.energy = account.energy;

                    local.ammo1 = account.ammo1;
                    local.ammo2 = account.ammo2;
                    local.ammo3 = account.ammo3;
                    local.ammo4 = account.ammo4;
                    local.ammo5 = account.ammo5;
                    local.photonAmmo1 = account.photonAmmo1;
                    local.photonAmmo2 = account.photonAmmo2;
                    local.photonAmmo3 = account.photonAmmo3;
                    local.photonAmmo4 = account.photonAmmo4;

                    local.torpedo1 = account.torpedo1;
                    local.torpedo2 = account.torpedo2;
                    local.torpedo3 = account.torpedo3;
                    local.torpedo4 = account.torpedo4;
                    local.torpedoice = account.torpedoice;
                    local.photonTorpedo1 = account.photonTorpedo1;
                    local.photonTorpedo2 = account.photonTorpedo2;
                    local.photonTorpedo3 = account.photonTorpedo3;
                    local.photonTorpedo4 = account.photonTorpedo4;
                    local.bomb1 = account.bomb1;
                    local.bomb2 = account.bomb2;
                    local.bomb3 = account.bomb3;
                    local.photonBomb1 = account.photonBomb1;
                    local.photonBomb2 = account.photonBomb2;
                    local.photonBomb3 = account.photonBomb3;
                    local.mine1 = account.mine1;
                    local.mine2 = account.mine2;
                    local.mine3 = account.mine3;
                    local.photonMine1 = account.photonMine1;
                    local.photonMine2 = account.photonMine2;
                    local.photonMine3 = account.photonMine3;
                    local.decoy1 = account.decoy1;
                    local.decoy2 = account.decoy2;
                    local.decoy3 = account.decoy3;
                }
                else
                {
                    ServerLog.Warn($"no PlayerLocalValues for {account.nickname} - "
                        + "hull, shield and turbine slots stay unset");
                }
            }
            catch (Exception e)
            {
                ServerLog.Warn($"pushing state for {account.nickname} failed: {e.Message}");
            }

            RefreshDerivedStats(obj, account);
        }

        private static void RefreshDerivedStats(NetworkObject obj, Account account)
        {
            Player player = obj.GetComponentInChildren<Player>();
            if (player == null)
            {
                return;
            }

            try
            {
                player.RefreshStats();
            }
            catch (Exception ex)
            {

                ServerLog.Info("RefreshStats ran; its trailing client-only "
                    + $"SetModel threw as expected ({ex.Message})");
            }

            int level = NetworkedLevel(account);
            WarnIfSlotStringsAreShort(account, level);
            DeriveMissingMaxima(player, account, level);

            try
            {
                if (player.health != null)
                {
                    if (player.health.maxHull > 0 && player.health.hull <= 0)
                    {
                        player.health.hull = player.health.maxHull;
                    }

                    if (player.health.maxShield > 0 && player.health.shield <= 0)
                    {
                        player.health.shield = player.health.maxShield;
                    }

                    ServerLog.Info($"{account.nickname} stats: hull "
                        + $"{player.health.hull}/{player.health.maxHull} shield "
                        + $"{player.health.shield}/{player.health.maxShield}");
                }
            }
            catch (Exception ex)
            {
                ServerLog.Warn($"could not fill health from derived maxima: {ex.Message}");
            }
        }

        private static void DeriveMissingMaxima(Player player, Account account, int level)
        {
            try
            {
                if (player.health == null ||
                    (player.health.maxHull > 0 && player.health.maxShield > 0))
                {
                    return;
                }

                List<HullData> hulls = GameData.Data != null ? GameData.Data.hullDatas : null;
                if (hulls == null || hulls.Count == 0)
                {
                    ServerLog.Warn($"no hull data - {account.nickname} has no derived maxima");
                    return;
                }

                var points = new int[hulls.Count];
                for (int i = 0; i < hulls.Count; i++)
                {
                    points[i] = hulls[i] != null ? hulls[i].hull : 0;
                }

                if (player.health.maxHull <= 0)
                {
                    player.health.maxHull = ShipStats.MaxHull(account.hullSlots, level, points);
                }

                if (player.health.maxShield <= 0)
                {
                    player.health.maxShield =
                        ShipStats.MaxShield(account.shieldSlots, level, points);
                }

                ServerLog.Warn($"RefreshStats left {account.nickname} with no maxima; "
                    + $"derived {player.health.maxHull}/{player.health.maxShield} from the "
                    + $"fitted plates at level {level}");
            }
            catch (Exception ex)
            {
                ServerLog.Warn($"could not derive maxima for {account.nickname}: {ex.Message}");
            }
        }

        private static void WarnIfSlotStringsAreShort(Account account, int level)
        {
            int needed = ShipStats.SlotCount(level);

            Check(account.weaponSlots, "weaponSlots");
            Check(account.hullSlots, "hullSlots");
            Check(account.shieldSlots, "shieldSlots");
            Check(account.turbineSlots, "turbineSlots");

            void Check(long slots, string name)
            {
                if (!EquipmentSlots.CanClientRead(slots, needed))
                {
                    ServerLog.Warn($"{account.nickname} has {name}={slots}, which is too "
                        + $"short for the {needed} slots level {level} unlocks - the client "
                        + "will throw inside RefreshStats");
                }
            }
        }

        public static string Sanitise(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return "Deckhand";
            }

            var sb = new System.Text.StringBuilder(16);
            foreach (char c in name)
            {
                if (sb.Length == 16)
                {
                    break;
                }

                bool ok = c == '_' ||
                          (c >= 'a' && c <= 'z') ||
                          (c >= 'A' && c <= 'Z') ||
                          (c >= '0' && c <= '9');
                if (ok)
                {
                    sb.Append(c);
                }
            }

            return sb.Length == 0 ? "Deckhand" : sb.ToString();
        }

        public static float TurbineSpeed(Account account)
        {
            List<TurbineData> turbines =
                GameData.Data != null ? GameData.Data.turbineDatas : null;

            if (turbines == null || turbines.Count == 0)
            {

                ServerLog.Warn("no turbine data - falling back to the hull's own speed");
                return ShipStats.BaseSpeed;
            }

            var speeds = new int[turbines.Count];
            for (int i = 0; i < turbines.Count; i++)
            {
                speeds[i] = turbines[i] != null ? turbines[i].speed : 0;
            }

            return ShipStats.Speed(account.turbineSlots, NetworkedLevel(account), speeds);
        }

        private static int NetworkedLevel(Account account)
        {
            return GameData.LevelForExperience(account.experience);
        }

        public bool TryChangeNickname(Account account, string requested, out string reason)
        {
            string clean = Sanitise(requested);

            if (clean.Length < 3)
            {
                reason = "too short";
                return false;
            }

            if (AccountStore.NicknameTaken(clean, account.id))
            {
                reason = "taken";
                return false;
            }

            account.nickname = clean;
            AccountStore.MarkDirty(account);
            reason = null;
            return true;
        }

        public static PlayerFaction CoreFaction(int faction)
        {
            switch (faction)
            {
                case FactionNautilus: return PlayerFaction.Nautilus;
                case FactionSerran: return PlayerFaction.Serran;
                case FactionAzularis: return PlayerFaction.Azularis;
                default: return PlayerFaction.None;
            }
        }

        public FactionCensus Census()
        {
            int nautilus = 0;
            int serran = 0;
            int azularis = 0;

            foreach (Account a in AccountStore.All)
            {
                if (a == null)
                {
                    continue;
                }

                switch (CoreFaction(a.faction))
                {
                    case PlayerFaction.Nautilus: nautilus++; break;
                    case PlayerFaction.Serran: serran++; break;
                    case PlayerFaction.Azularis: azularis++; break;
                }
            }

            return new FactionCensus(nautilus, serran, azularis);
        }

        public bool CanJoinFaction(int faction)
        {
            return FactionBalance.CanJoin(Census(), CoreFaction(faction));
        }

        public bool TryChangeFaction(Account account, int faction)
        {
            if (account == null || !CanJoinFaction(faction))
            {
                return false;
            }

            if (!string.IsNullOrEmpty(account.clanTag))
            {
                Clan clan = AccountStore.FindClan(account.clanTag);
                if (clan != null && clan.faction != faction)
                {
                    ServerHub.Social?.LeaveClan(account);
                }
            }

            account.faction = faction;
            AccountStore.MarkDirty(account);
            ServerLog.Info($"{account.nickname} joined faction {faction}");
            return true;
        }
    }
}
