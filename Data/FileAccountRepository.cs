using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace TidalNexus.StandaloneServer.Data
{

    public sealed class FileAccountRepository : IAccountRepository
    {
        private readonly Dictionary<string, Account> _accounts =
            new Dictionary<string, Account>(StringComparer.OrdinalIgnoreCase);

        private readonly Dictionary<string, Clan> _clans =
            new Dictionary<string, Clan>(StringComparer.OrdinalIgnoreCase);

        private bool _loaded;

        private bool _loadFailed;

        private bool _directoriesReady;

        private string AccountDir => Path.Combine(ServerPaths.DataRoot, "accounts");

        private string ClanDir => Path.Combine(ServerPaths.DataRoot, "clans");

        private string ServerStatePath => Path.Combine(ServerPaths.DataRoot, "server.json");

        public string Description => ServerPaths.DataRoot;

        public void Open()
        {
            if (_loaded)
            {
                return;
            }

            int accounts = 0, clans = 0;

            try
            {

                string accountDir = AccountDir;
                string clanDir = ClanDir;

                EnsureDirectories();

                RecoverOrphanedTemps<Account>(accountDir);
                RecoverOrphanedTemps<Clan>(clanDir);

                foreach (string file in Directory.GetFiles(accountDir, "*.json"))
                {
                    Account a = ReadJson<Account>(file);
                    if (a != null && !string.IsNullOrEmpty(a.id))
                    {
                        _accounts[a.id] = a;
                        accounts++;
                    }
                }

                foreach (string file in Directory.GetFiles(clanDir, "*.json"))
                {
                    Clan c = ReadJson<Clan>(file);
                    if (c != null && !string.IsNullOrEmpty(c.tag))
                    {
                        _clans[c.tag] = c;
                        clans++;
                    }
                }
            }
            catch (Exception e)
            {
                _loadFailed = true;

                ServerLog.Error(
                    $"account store: LOAD FAILED from {ServerPaths.DataRoot} after " +
                    $"{accounts} accounts and {clans} clans - " +
                    $"{e.GetType().Name}: {e.Message}");
                ServerLog.Error(
                    "account store: writes onto existing files are blocked until a load " +
                    "succeeds, and players connecting now will look new; fix the data " +
                    "directory - the next connection retries the load");
                return;
            }

            _loadFailed = false;
            _loaded = true;

            ServerLog.Info(
                $"account store: {accounts} accounts, {clans} clans from {ServerPaths.DataRoot}");
        }

        private static void RecoverOrphanedTemps<T>(string dir) where T : class
        {
            foreach (string temp in Directory.GetFiles(dir, "*.json.tmp"))
            {
                string path = temp.Substring(0, temp.Length - ".tmp".Length);

                if (File.Exists(path))
                {

                    ServerLog.Warn(
                        $"{Path.GetFileName(temp)} sits beside a live record; keeping the " +
                        "record and leaving the temp alone - compare them by hand");
                    continue;
                }

                if (ReadJson<T>(temp) == null)
                {
                    ServerLog.Error(
                        $"orphaned {Path.GetFileName(temp)} does not parse; leaving it in " +
                        "place rather than promoting a record with no content");
                    continue;
                }

                try
                {
                    File.Move(temp, path);
                    ServerLog.Warn(
                        $"recovered {Path.GetFileName(path)} from an interrupted save");
                }
                catch (Exception e)
                {
                    ServerLog.Error($"could not recover {Path.GetFileName(temp)}: {e.Message}");
                }
            }
        }

        public Account Find(string id)
        {
            Open();
            return id != null && _accounts.TryGetValue(id, out Account a) ? a : null;
        }

        public Account FindByNickname(string nickname)
        {
            Open();
            foreach (Account a in _accounts.Values)
            {
                if (string.Equals(a.nickname, nickname, StringComparison.OrdinalIgnoreCase))
                {
                    return a;
                }
            }

            return null;
        }

        public IEnumerable<Account> Accounts
        {
            get
            {
                Open();
                return _accounts.Values;
            }
        }

        public void Write(Account account)
        {
            if (account == null || string.IsNullOrEmpty(account.id))
            {
                return;
            }

            EnsureDirectories();

            if (!_accounts.ContainsKey(account.id))
            {
                _accounts[account.id] = account;
            }

            WriteJson(Path.Combine(AccountDir, Sanitise(account.id) + ".json"), account);
        }

        public Clan FindClan(string tag)
        {
            Open();
            return tag != null && _clans.TryGetValue(tag, out Clan c) ? c : null;
        }

        public IEnumerable<Clan> Clans
        {
            get
            {
                Open();
                return _clans.Values;
            }
        }

        public void WriteClan(Clan clan)
        {
            if (clan == null || string.IsNullOrEmpty(clan.tag))
            {
                return;
            }

            EnsureDirectories();

            if (!_clans.ContainsKey(clan.tag))
            {
                _clans[clan.tag] = clan;
            }

            WriteJson(Path.Combine(ClanDir, Sanitise(clan.tag) + ".json"), clan);
        }

        public void DeleteClan(string tag)
        {
            Open();

            if (tag == null || !_clans.Remove(tag))
            {
                return;
            }

            string path = Path.Combine(ClanDir, Sanitise(tag) + ".json");
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        public ServerState ReadServerState()
        {
            try
            {
                return File.Exists(ServerStatePath)
                    ? JsonUtility.FromJson<ServerState>(File.ReadAllText(ServerStatePath))
                    : null;
            }
            catch (Exception e)
            {
                ServerLog.Warn($"could not read server state: {e.Message}");
                return null;
            }
        }

        public void WriteServerState(ServerState state)
        {
            if (state == null)
            {
                return;
            }

            WriteJson(ServerStatePath, state);
        }

        private void EnsureDirectories()
        {
            if (_directoriesReady)
            {
                return;
            }

            try
            {
                Directory.CreateDirectory(AccountDir);
                Directory.CreateDirectory(ClanDir);
                _directoriesReady = true;
            }
            catch (Exception e)
            {
                ServerLog.Error($"could not create the record directories: {e.Message}");
            }
        }

        private static T ReadJson<T>(string path) where T : class
        {
            try
            {
                return JsonUtility.FromJson<T>(File.ReadAllText(path));
            }
            catch (Exception e)
            {
                ServerLog.Warn($"could not read {Path.GetFileName(path)}: {e.Message}");
                return null;
            }
        }

        private void WriteJson(string path, object value)
        {

            if (_loadFailed && File.Exists(path))
            {
                ServerLog.Error(
                    $"refusing to overwrite {Path.GetFileName(path)}: the account store did " +
                    "not load, so the record in memory is not the player's real one");
                return;
            }

            string temp = path + ".tmp";

            try
            {
                File.WriteAllText(temp, JsonUtility.ToJson(value, true));

                if (File.Exists(path))
                {

                    File.Replace(temp, path, null);
                }
                else
                {
                    File.Move(temp, path);
                }
            }
            catch (Exception e)
            {

                ServerLog.Error($"could not write {Path.GetFileName(path)}: {e.Message}");
            }
        }

        private static string Sanitise(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
            {
                name = name.Replace(c, '_');
            }

            return name;
        }
    }
}
