using System;
using System.Collections.Generic;
using UnityEngine;

namespace TidalNexus.StandaloneServer.Data
{

    public static class AccountStore
    {

        private static readonly Dictionary<string, Account> Dirty =
            new Dictionary<string, Account>(StringComparer.OrdinalIgnoreCase);

        private static readonly List<Account> Pending = new List<Account>();

        private const float DefaultFlushSeconds = 5f;

        private static float _flushSeconds = -1f;

        private static float _dueAt;

        private static IAccountRepository _repository;

        public static IAccountRepository Repository => _repository ??= CreateDefault();

        public static void Use(IAccountRepository repository)
        {
            if (repository == null || ReferenceEquals(repository, _repository))
            {
                return;
            }

            if (_repository != null)
            {
                Flush();
            }

            _repository = repository;
            ServerLog.Info($"account store: using {repository.Description}");
        }

        private static IAccountRepository CreateDefault()
        {
            string mongo = Environment.GetEnvironmentVariable("TN_MONGO");
            if (!string.IsNullOrWhiteSpace(mongo))
            {
                ServerLog.Error(
                    "TN_MONGO is set but this build has no database repository - falling " +
                    "back to files, which are NOT safe to share between channels");
            }

            return new FileAccountRepository();
        }

        public static IEnumerable<Account> All => Repository.Accounts;

        public static IEnumerable<Clan> AllClans => Repository.Clans;

        public static void LoadAll()
        {
            Repository.Open();
        }

        public static Account GetOrCreate(string id, string suggestedNickname = null)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                id = Guid.NewGuid().ToString("N");
            }

            Account existing = Repository.Find(id);
            if (existing != null)
            {
                return existing;
            }

            var account = new Account
            {
                id = id,
                nickname = string.IsNullOrWhiteSpace(suggestedNickname)
                    ? "Deckhand" + UnityEngine.Random.Range(1000, 9999)
                    : suggestedNickname,
                lastSeenUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            };

            SaveNow(account);
            ServerLog.Info($"created account {id} ({account.nickname})");
            return account;
        }

        public static Account Find(string id)
        {
            return Repository.Find(id);
        }

        public static Account FindByNickname(string nickname)
        {
            return Repository.FindByNickname(nickname);
        }

        public static bool NicknameTaken(string nickname, string exceptId = null)
        {
            Account a = FindByNickname(nickname);
            return a != null && !string.Equals(a.id, exceptId, StringComparison.OrdinalIgnoreCase);
        }

        public static void MarkDirty(Account account)
        {
            if (account == null || string.IsNullOrEmpty(account.id))
            {
                return;
            }

            if (Dirty.Count == 0)
            {
                _dueAt = Time.realtimeSinceStartup + FlushSeconds;
            }

            Dirty[account.id] = account;
            FlushIfDue();
        }

        public static void SaveNow(Account account)
        {
            if (account == null || string.IsNullOrEmpty(account.id))
            {
                return;
            }

            Dirty.Remove(account.id);
            Persist(account);
        }

        public static void SaveAll()
        {
            Dirty.Clear();

            var accounts = new List<Account>(Repository.Accounts);
            foreach (Account a in accounts)
            {
                Persist(a);
            }

            var clans = new List<Clan>(Repository.Clans);
            foreach (Clan c in clans)
            {
                Repository.WriteClan(c);
            }
        }

        public static void Flush()
        {
            if (Dirty.Count == 0)
            {
                return;
            }

            Pending.Clear();
            Pending.AddRange(Dirty.Values);
            Dirty.Clear();

            for (int i = 0; i < Pending.Count; i++)
            {
                Persist(Pending[i]);
            }

            Pending.Clear();
        }

        public static void Pump(float deltaTime)
        {
            FlushIfDue();
        }

        private static void FlushIfDue()
        {
            if (Dirty.Count == 0 || Time.realtimeSinceStartup < _dueAt)
            {
                return;
            }

            Flush();
        }

        private static void Persist(Account account)
        {
            if (account == null || string.IsNullOrEmpty(account.id))
            {
                return;
            }

            account.lastSeenUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            Repository.Write(account);
        }

        private static float FlushSeconds
        {
            get
            {
                if (_flushSeconds >= 0f)
                {
                    return _flushSeconds;
                }

                _flushSeconds = DefaultFlushSeconds;

                string configured = Environment.GetEnvironmentVariable("TN_FLUSHSECONDS");
                if (!string.IsNullOrWhiteSpace(configured) &&
                    float.TryParse(configured, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out float seconds) &&
                    seconds >= 0f)
                {
                    _flushSeconds = seconds;
                }

                return _flushSeconds;
            }
        }

        public static Clan FindClan(string tag)
        {
            return Repository.FindClan(tag);
        }

        public static Clan CreateClan(Clan clan)
        {
            Repository.WriteClan(clan);
            return clan;
        }

        public static void DeleteClan(string tag)
        {
            Repository.DeleteClan(tag);
        }

        public static void SaveClan(Clan clan)
        {
            Repository.WriteClan(clan);
        }
    }
}
