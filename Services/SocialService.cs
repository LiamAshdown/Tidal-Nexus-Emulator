using System;
using System.Collections.Generic;
using Fusion;
using TidalNexus.StandaloneServer.Data;

namespace TidalNexus.StandaloneServer.Services
{

    public sealed class SocialService
    {
        private const int PartyLimit = 5;
        private const int ClanTagMin = 2;
        private const int ClanTagMax = 5;

        private sealed class Party
        {
            public string LeaderId;
            public readonly List<string> Members = new List<string>();
        }

        private readonly List<Party> _parties = new List<Party>();

        private readonly Dictionary<string, string> _partyInvites =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        private const float PartyPositionInterval = 1f;

        private float _positionClock;

        public void Tick(float deltaTime)
        {
            _positionClock += deltaTime;
            if (_positionClock < PartyPositionInterval)
            {
                return;
            }

            _positionClock = 0f;

            if (_parties.Count == 0)
            {
                return;
            }

            foreach (Party party in _parties)
            {
                List<Account> members = AccountsIn(party);

                foreach (Account member in members)
                {
                    PlayerRef reference = ServerHub.RefFor(member);
                    if (reference != PlayerRef.None)
                    {
                        Wire.SendPartyPositions(reference, members);
                    }
                }
            }
        }

        public bool TryCreateClan(Account founder, string tag, string name,
            out bool badTag, out bool badName)
        {
            badTag = false;
            badName = false;

            tag = (tag ?? string.Empty).Trim();
            name = (name ?? string.Empty).Trim();

            if (tag.Length < ClanTagMin || tag.Length > ClanTagMax ||
                AccountStore.FindClan(tag) != null)
            {
                badTag = true;
                return false;
            }

            if (name.Length < 3 || name.Length > 24)
            {
                badName = true;
                return false;
            }

            if (!string.IsNullOrEmpty(founder.clanTag))
            {
                return false;
            }

            const long Cost = 100000;
            if (founder.credits < Cost)
            {
                return false;
            }

            founder.credits -= Cost;

            var clan = new Clan
            {
                tag = tag,
                name = name,
                founderId = founder.id,
                faction = founder.faction,
                createdUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            };

            clan.members.Add(new ClanMember
            {
                accountId = founder.id,
                nickname = founder.nickname,
                rank = 2,
                joinedUnix = clan.createdUnix,
            });

            AccountStore.CreateClan(clan);
            founder.clanTag = tag;
            AccountStore.MarkDirty(founder);

            ServerLog.Info($"{founder.nickname} founded clan [{tag}] {name}");
            return true;
        }

        public void LeaveClan(Account account)
        {
            if (account == null || string.IsNullOrEmpty(account.clanTag))
            {
                return;
            }

            Clan clan = AccountStore.FindClan(account.clanTag);
            account.clanTag = string.Empty;
            AccountStore.MarkDirty(account);

            if (clan == null)
            {
                return;
            }

            clan.members.RemoveAll(m =>
                string.Equals(m.accountId, account.id, StringComparison.OrdinalIgnoreCase));

            if (clan.members.Count == 0)
            {
                AccountStore.DeleteClan(clan.tag);
                ServerLog.Info($"clan [{clan.tag}] disbanded - last member left");
                return;
            }

            bool hasLeader = clan.members.Exists(m => m.rank == 2);
            if (!hasLeader)
            {
                ClanMember heir = clan.members[0];
                foreach (ClanMember m in clan.members)
                {
                    if (m.rank > heir.rank ||
                        (m.rank == heir.rank && m.joinedUnix < heir.joinedUnix))
                    {
                        heir = m;
                    }
                }

                heir.rank = 2;
            }

            AccountStore.SaveClan(clan);
        }

        public bool TryInviteToClan(Account inviter, string nickname)
        {
            Clan clan = AccountStore.FindClan(inviter?.clanTag);
            if (clan == null || !HasRank(clan, inviter.id, 1))
            {
                return false;
            }

            Account target = AccountStore.FindByNickname(nickname);
            if (target == null || !string.IsNullOrEmpty(target.clanTag))
            {
                return false;
            }

            if (target.faction != clan.faction)
            {
                return false;
            }

            if (!clan.invited.Contains(target.id))
            {
                clan.invited.Add(target.id);
                AccountStore.SaveClan(clan);
            }

            PlayerRef p = ServerHub.RefFor(target);
            if (p != PlayerRef.None)
            {
                ServerHub.RpcFor(p)?.RPC_ClanInviteRelay(inviter.nickname, clan.tag);
            }

            return true;
        }

        public bool DeclineClanInvite(Account account)
        {
            if (account == null)
            {
                return false;
            }

            bool removed = false;

            foreach (Clan clan in AccountStore.AllClans)
            {
                if (clan?.invited != null && clan.invited.Remove(account.id))
                {
                    AccountStore.SaveClan(clan);
                    removed = true;
                    ServerLog.Info($"{account.nickname} declined the invite from [{clan.tag}]");
                }
            }

            return removed;
        }

        public bool TryAcceptClanInvite(Account account)
        {
            if (account == null || !string.IsNullOrEmpty(account.clanTag))
            {
                return false;
            }

            foreach (Clan clan in AccountStore.AllClans)
            {
                if (!clan.invited.Contains(account.id))
                {
                    continue;
                }

                clan.invited.Remove(account.id);
                clan.members.Add(new ClanMember
                {
                    accountId = account.id,
                    nickname = account.nickname,
                    rank = 0,
                    joinedUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                });

                AccountStore.SaveClan(clan);
                account.clanTag = clan.tag;
                AccountStore.MarkDirty(account);
                return true;
            }

            return false;
        }

        public bool TryKickFromClan(Account actor, string targetId)
        {
            Clan clan = AccountStore.FindClan(actor?.clanTag);
            if (clan == null || !HasRank(clan, actor.id, 1))
            {
                return false;
            }

            ClanMember target = clan.members.Find(m =>
                string.Equals(m.accountId, targetId, StringComparison.OrdinalIgnoreCase));

            if (target == null || target.rank >= RankOf(clan, actor.id))
            {
                return false;
            }

            clan.members.Remove(target);
            AccountStore.SaveClan(clan);

            Account targetAccount = AccountStore.Find(targetId);
            if (targetAccount != null)
            {
                targetAccount.clanTag = string.Empty;
                AccountStore.MarkDirty(targetAccount);
            }

            return true;
        }

        public bool TryPromote(Account actor, string targetId, int rank)
        {
            Clan clan = AccountStore.FindClan(actor?.clanTag);
            if (clan == null || RankOf(clan, actor.id) != 2)
            {
                return false;
            }

            ClanMember target = clan.members.Find(m =>
                string.Equals(m.accountId, targetId, StringComparison.OrdinalIgnoreCase));

            if (target == null)
            {
                return false;
            }

            if (string.Equals(target.accountId, actor.id, StringComparison.OrdinalIgnoreCase))
            {
                ServerLog.Info($"{actor.nickname} tried to change their own clan rank");
                return false;
            }

            int wanted = InternalRank(rank);
            if (wanted < 0)
            {
                ServerLog.Info($"clan promote: unrecognised rank {rank}");
                return false;
            }

            target.rank = wanted;

            if (target.rank == 2)
            {
                ClanMember self = clan.members.Find(m =>
                    string.Equals(m.accountId, actor.id, StringComparison.OrdinalIgnoreCase));
                if (self != null)
                {
                    self.rank = 1;
                }
            }

            AccountStore.SaveClan(clan);
            return true;
        }

        public void SetClanDescription(Account actor, string text)
        {
            Clan clan = AccountStore.FindClan(actor?.clanTag);
            if (clan == null || !HasRank(clan, actor.id, 1))
            {
                return;
            }

            clan.description = (text ?? string.Empty).Substring(
                0, Math.Min(280, (text ?? string.Empty).Length));
            AccountStore.SaveClan(clan);
        }

        private static bool BannerFits(string[] parts, out string refusal)
        {
            refusal = null;

            DataManager data = GameData.Data;
            if (data == null)
            {
                refusal = "no item catalogue to check against";
                return false;
            }

            var slot = new int[parts.Length];

            for (int i = 0; i < parts.Length; i++)
            {
                if (!int.TryParse(parts[i].Trim(), out slot[i]) || slot[i] < 0)
                {
                    refusal = $"slot {i} is \"{parts[i]}\"";
                    return false;
                }
            }

            int[] sizes =
            {
                data.clanBannerStyles?.Count ?? 0,
                data.clanMainColors?.Count ?? 0,
                data.clanPatternStyles?.Count ?? 0,
                data.clanPatternColors?.Count ?? 0,
                data.clanEmblemStyles?.Count ?? 0,
                data.clanEmblemColors?.Count ?? 0,
                int.MaxValue,
                data.clanOrnamentColors?.Count ?? 0,
            };

            for (int i = 0; i < sizes.Length; i++)
            {
                if (i != OrnamentSlot && slot[i] >= sizes[i])
                {
                    refusal = $"slot {i} is {slot[i]} of {sizes[i]}";
                    return false;
                }
            }

            ClanBannerStyleData style = data.clanBannerStyles[slot[0]];
            int ornaments = style?.ornaments?.Count ?? 0;

            if (slot[OrnamentSlot] >= ornaments)
            {
                refusal = $"ornament {slot[OrnamentSlot]} of {ornaments} for style {slot[0]}";
                return false;
            }

            return true;
        }

        private const int OrnamentSlot = 6;

        public void SetClanBanner(Account actor, string design)
        {
            Clan clan = AccountStore.FindClan(actor?.clanTag);
            if (clan == null || !HasRank(clan, actor.id, 1))
            {
                return;
            }

            string[] parts = (design ?? string.Empty).Split(',');
            if (parts.Length != 8)
            {
                return;
            }

            if (!BannerFits(parts, out string refusal))
            {
                ServerLog.Warn($"{actor.nickname} sent an unusable clan banner: {refusal}");
                return;
            }

            clan.banner = design;
            AccountStore.SaveClan(clan);
        }

        public bool DeclareWar(Account actor, string targetTag)
        {
            Clan clan = AccountStore.FindClan(actor?.clanTag);
            Clan target = AccountStore.FindClan(targetTag);

            if (clan == null || target == null || clan == target ||
                !HasRank(clan, actor.id, 2))
            {
                return false;
            }

            if (!clan.wars.Contains(target.tag))
            {
                clan.wars.Add(target.tag);
                AccountStore.SaveClan(clan);
            }

            if (!target.wars.Contains(clan.tag))
            {
                target.wars.Add(clan.tag);
                AccountStore.SaveClan(target);
            }

            return true;
        }

        public IEnumerable<Clan> SearchClans(string search)
        {
            var matches = new List<Clan>();

            foreach (Clan clan in AccountStore.AllClans)
            {
                if (clan == null)
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(search) &&
                    clan.name.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0 &&
                    clan.tag.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                matches.Add(clan);
            }

            matches.Sort((a, b) => b.lifetimeFame.CompareTo(a.lifetimeFame));
            return matches;
        }

        public bool TryStartFactionVote(Account actor)
        {
            Clan clan = AccountStore.FindClan(actor.clanTag);
            if (clan == null || clan.factionChangeVote || !HasRank(clan, actor.id, 2))
            {
                return false;
            }

            clan.factionChangeVote = true;
            clan.factionVoters.Clear();
            AccountStore.SaveClan(clan);

            ServerLog.Info($"{actor.nickname} started a faction vote in [{clan.tag}]");
            return true;
        }

        public bool CastFactionVote(Account voter, bool inFavour)
        {
            Clan clan = AccountStore.FindClan(voter.clanTag);
            if (clan == null || !clan.factionChangeVote || RankOf(clan, voter.id) < 0)
            {
                return false;
            }

            if (!inFavour)
            {
                clan.factionChangeVote = false;
                clan.factionVoters.Clear();
                AccountStore.SaveClan(clan);
                ServerLog.Info($"{voter.nickname} voted against - faction vote in "
                    + $"[{clan.tag}] is over");
                return true;
            }

            if (clan.factionVoters.Contains(voter.id))
            {
                return false;
            }

            clan.factionVoters.Add(voter.id);
            AccountStore.SaveClan(clan);

            ServerLog.Info($"{voter.nickname} voted for the faction change in "
                + $"[{clan.tag}] ({clan.factionVoters.Count}/{clan.members.Count})");
            return true;
        }

        public void PushClanToMembers(Clan clan)
        {
            if (clan == null)
            {
                return;
            }

            foreach (ClanMember member in clan.members)
            {
                Account a = AccountStore.Find(member.accountId);
                if (a == null)
                {
                    continue;
                }

                PlayerRef p = ServerHub.RefFor(a);
                if (p != PlayerRef.None)
                {
                    ServerHub.Accounts?.PushState(p, a);
                    Wire.SendClanData(p, a);
                }
            }
        }

        private static int InternalRank(int clientRank)
        {
            switch (clientRank)
            {
                case (int)Enums.ClanRank.Leader: return 2;
                case (int)Enums.ClanRank.Officer: return 1;
                case (int)Enums.ClanRank.Member: return 0;
                default: return -1;
            }
        }

        private static int RankOf(Clan clan, string accountId)
        {
            ClanMember m = clan.members.Find(x =>
                string.Equals(x.accountId, accountId, StringComparison.OrdinalIgnoreCase));
            return m?.rank ?? -1;
        }

        private static bool HasRank(Clan clan, string accountId, int minimum) =>
            RankOf(clan, accountId) >= minimum;

        private Party PartyOf(string accountId)
        {
            return _parties.Find(p => p.Members.Contains(accountId));
        }

        private sealed class PartyMembership : IDisposable
        {
            private SocialService _social;
            private Account _account;

            public void Bind(SocialService social, Account account)
            {
                _social = social;
                _account = account;
            }

            public void Dispose()
            {
                _social?.LeaveParty(_account);
            }
        }

        private bool BindToSession(Account account)
        {
            PlayerSession session = ServerHub.SessionOf(account);
            if (session == null)
            {
                return false;
            }

            session.State<PartyMembership>().Bind(this, account);
            return true;
        }

        private static List<Account> AccountsIn(Party party)
        {
            var members = new List<Account>();
            if (party == null)
            {
                return members;
            }

            foreach (string id in party.Members)
            {
                Account member = AccountStore.Find(id);
                if (member != null)
                {
                    members.Add(member);
                }
            }

            return members;
        }

        private static void PushParty(Party party)
        {
            List<Account> members = AccountsIn(party);
            if (members.Count == 0)
            {
                return;
            }

            Account leader = LeaderIn(party, members);
            if (leader == null)
            {
                ServerLog.Warn($"party led by {party.LeaderId} has no leader in its "
                    + "roster - not sending it");
                return;
            }

            foreach (Account member in members)
            {
                PlayerRef reference = ServerHub.RefFor(member);
                if (reference != PlayerRef.None)
                {
                    Wire.SendParty(reference, members, leader);
                }
            }
        }

        public void PushParty(Account account)
        {
            Party party = PartyOf(account?.id);
            if (party == null)
            {
                return;
            }

            PlayerRef reference = ServerHub.RefFor(account);
            if (reference == PlayerRef.None)
            {
                return;
            }

            List<Account> members = AccountsIn(party);
            Account leader = LeaderIn(party, members);
            if (leader == null)
            {
                ServerLog.Warn($"party led by {party.LeaderId} has no leader in its "
                    + $"roster - not sending it to {account.nickname}");
                return;
            }

            Wire.SendParty(reference, members, leader);
        }

        private static Account LeaderIn(Party party, List<Account> members)
        {
            return members.Find(a =>
                string.Equals(a.id, party.LeaderId, StringComparison.OrdinalIgnoreCase));
        }

        private static void PushNoParty(Account account)
        {
            PlayerRef reference = ServerHub.RefFor(account);
            if (reference != PlayerRef.None)
            {
                Wire.SendPartyCleared(reference);
            }
        }

        private static void PushNoParty(IEnumerable<Account> accounts)
        {
            foreach (Account account in accounts)
            {
                PushNoParty(account);
            }
        }

        public bool TryInviteToParty(Account inviter, string nickname)
        {
            Account target = AccountStore.FindByNickname(nickname);
            if (inviter == null || target == null || target.id == inviter.id)
            {
                return false;
            }

            if (PartyOf(target.id) != null)
            {
                PlayerRef existing = ServerHub.RefFor(inviter);
                ServerHub.RpcFor(existing)?.RPC_PlayerAlreadyInPartyNotification();
                return false;
            }

            Party party = PartyOf(inviter.id);
            if (party != null && party.Members.Count >= PartyLimit)
            {
                return false;
            }

            _partyInvites[target.id] = inviter.id;

            PlayerRef p = ServerHub.RefFor(target);
            if (p != PlayerRef.None)
            {
                ServerHub.RpcFor(p)?.RPC_PartyInviteRelay(inviter.nickname);
            }

            return true;
        }

        public bool DeclinePartyInvite(Account account)
        {
            if (account == null || !_partyInvites.Remove(account.id))
            {
                return false;
            }

            ServerLog.Info($"{account.nickname} declined a party invite");
            return true;
        }

        public bool TryAcceptPartyInvite(Account account)
        {
            if (account == null || !_partyInvites.TryGetValue(account.id, out string inviterId))
            {
                return false;
            }

            _partyInvites.Remove(account.id);

            if (PartyOf(account.id) != null)
            {

                PlayerRef already = ServerHub.RefFor(account);
                ServerHub.RpcFor(already)?.RPC_PlayerAlreadyInPartyNotification();

                ServerLog.Info($"{account.nickname} accepted a party invite "
                    + "while already in a party");
                return false;
            }

            Account inviter = AccountStore.Find(inviterId);
            if (inviter == null)
            {
                return false;
            }

            if (!BindToSession(account))
            {
                return false;
            }

            Party party = PartyOf(inviterId);
            if (party == null)
            {

                if (!BindToSession(inviter))
                {
                    ServerLog.Info($"{account.nickname} accepted an invite from "
                        + $"{inviter.nickname}, who has since disconnected");
                    return false;
                }

                party = new Party { LeaderId = inviterId };
                party.Members.Add(inviterId);
                _parties.Add(party);
            }

            if (party.Members.Count >= PartyLimit || party.Members.Contains(account.id))
            {
                return false;
            }

            party.Members.Add(account.id);

            PushParty(party);
            return true;
        }

        public void LeaveParty(Account account)
        {
            Party party = PartyOf(account?.id);
            if (party == null)
            {
                return;
            }

            List<Account> before = AccountsIn(party);

            party.Members.Remove(account.id);

            if (party.Members.Count <= 1 ||
                string.Equals(party.LeaderId, account.id, StringComparison.OrdinalIgnoreCase))
            {

                _parties.Remove(party);
                PushNoParty(before);
                return;
            }

            PushParty(party);
            PushNoParty(account);
        }

        public bool TryKickFromParty(Account actor, string nickname)
        {
            Party party = PartyOf(actor?.id);
            Account target = AccountStore.FindByNickname(nickname);

            if (party == null || target == null ||
                !string.Equals(party.LeaderId, actor.id, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (string.Equals(target.id, actor.id, StringComparison.OrdinalIgnoreCase))
            {
                ServerLog.Info($"{actor.nickname} tried to kick themselves out of their party");
                return false;
            }

            List<Account> before = AccountsIn(party);

            if (!party.Members.Remove(target.id))
            {
                return false;
            }

            if (party.Members.Count <= 1)
            {
                _parties.Remove(party);
                PushNoParty(before);
                return true;
            }

            PushParty(party);
            PushNoParty(target);
            return true;
        }

        public bool TryPromoteParty(Account actor, string nickname)
        {
            Party party = PartyOf(actor?.id);
            Account target = AccountStore.FindByNickname(nickname);

            if (party == null || target == null ||
                !string.Equals(party.LeaderId, actor.id, StringComparison.OrdinalIgnoreCase) ||
                !party.Members.Contains(target.id))
            {
                return false;
            }

            party.LeaderId = target.id;

            PushParty(party);
            return true;
        }

        public void DisbandParty(Account actor)
        {
            Party party = PartyOf(actor?.id);
            if (party == null ||
                !string.Equals(party.LeaderId, actor.id, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            List<Account> members = AccountsIn(party);
            _parties.Remove(party);
            PushNoParty(members);
        }

        public IEnumerable<Account> PartyMembers(Account account) =>
            AccountsIn(PartyOf(account?.id));

        public void Say(Account sender, int channel, string message, string target = "",
            string languageCode = "")
        {
            if (sender == null || string.IsNullOrWhiteSpace(message) || sender.IsMuted)
            {
                return;
            }

            if (message.Length > 200)
            {
                message = message.Substring(0, 200);
            }

            if (!MaySpeakOn(sender, channel))
            {
                ServerLog.Info($"{sender.nickname} tried to speak on the "
                    + $"{(ChatChannel)channel} channel ({channel})");
                return;
            }

            foreach (KeyValuePair<PlayerRef, Account> kv in ServerHub.Online)
            {
                if (!Hears(sender, kv.Value, channel, target))
                {
                    continue;
                }

                ServerHub.RpcFor(kv.Key)?.RPC_ReceiveChatMessage(
                    (Enums.Faction)sender.faction,
                    sender.clanTag ?? string.Empty,
                    sender.nickname,
                    message,
                    (ChatChannel)channel,
                    target ?? string.Empty,
                    languageCode ?? string.Empty,
                    string.Empty,
                    string.Empty,
                    false,
                    sender.admin,
                    Enums.NetworkLayerType.WorldMap);
            }
        }

        private static bool MaySpeakOn(Account sender, int channel)
        {
            switch ((ChatChannel)channel)
            {
                case ChatChannel.Global:
                case ChatChannel.Language:
                case ChatChannel.Faction:
                case ChatChannel.Clan:
                case ChatChannel.Party:
                case ChatChannel.Say:
                case ChatChannel.Whisper:
                    return true;

                case ChatChannel.ModeratorMessage:
                    return sender.admin;

                default:
                    return false;
            }
        }

        private bool Hears(Account sender, Account listener, int channel, string target = "")
        {
            return (ChatChannel)channel switch
            {
                ChatChannel.Faction => sender.faction == listener.faction,

                ChatChannel.Clan => !string.IsNullOrEmpty(sender.clanTag) &&
                     string.Equals(sender.clanTag, listener.clanTag,
                         StringComparison.OrdinalIgnoreCase),

                ChatChannel.Party => IsPartyMate(sender, listener),

                ChatChannel.Whisper =>
                    string.Equals(listener.nickname, target, StringComparison.OrdinalIgnoreCase) ||
                    ReferenceEquals(listener, sender),

                ChatChannel.Say => WithinEarshot(sender, listener),

                _ => true,
            };
        }

        private const float SayRange = 60f;

        private bool WithinEarshot(Account sender, Account listener)
        {
            if (ReferenceEquals(sender, listener))
            {
                return true;
            }

            var from = new UnityEngine.Vector3(sender.x, sender.y, sender.z);
            var to = new UnityEngine.Vector3(listener.x, listener.y, listener.z);
            return UnityEngine.Vector3.Distance(from, to) <= SayRange;
        }

        private bool IsPartyMate(Account a, Account b)
        {
            Party party = PartyOf(a.id);
            return party != null && party.Members.Contains(b.id);
        }
    }
}
