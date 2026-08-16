using System.Collections.Generic;
using Fusion;

namespace TidalNexus.StandaloneServer
{

    public static class LootOwnership
    {

        private const int PruneAbove = 256;

        private static readonly Dictionary<NetworkId, string> _owner =
            new Dictionary<NetworkId, string>();

        private static readonly List<NetworkId> _stale = new List<NetworkId>();

        public static void Claim(NetworkId id, string accountId)
        {
            if (string.IsNullOrEmpty(accountId))
            {
                return;
            }

            if (_owner.Count > PruneAbove)
            {
                Prune();
            }

            _owner[id] = accountId;
        }

        public static bool IsOwned(NetworkId id)
        {
            return _owner.ContainsKey(id);
        }

        public static bool OwnedBy(NetworkId id, string accountId)
        {
            return _owner.TryGetValue(id, out string held)
                && !string.IsNullOrEmpty(accountId)
                && held == accountId;
        }

        public static bool OwnedByOther(NetworkId id, string accountId)
        {
            return _owner.TryGetValue(id, out string held) && held != accountId;
        }

        public static bool TryGetOwner(NetworkId id, out string accountId)
        {
            return _owner.TryGetValue(id, out accountId);
        }

        public static void Release(NetworkId id)
        {
            _owner.Remove(id);
        }

        public static void Prune()
        {
            if (ServerHub.Runner == null)
            {
                _owner.Clear();
                return;
            }

            _stale.Clear();

            foreach (KeyValuePair<NetworkId, string> entry in _owner)
            {
                if (ServerHub.Runner.FindObject(entry.Key) == null)
                {
                    _stale.Add(entry.Key);
                }
            }

            foreach (NetworkId id in _stale)
            {
                _owner.Remove(id);
            }

            _stale.Clear();
        }
    }
}
