using System;
using System.Collections.Generic;
using Fusion;
using TidalNexus.StandaloneServer.Data;

namespace TidalNexus.StandaloneServer
{

    public sealed class PlayerSession : IDisposable
    {

        private readonly Dictionary<Type, object> _state = new Dictionary<Type, object>();

        public PlayerSession(Account account, PlayerRef player)
        {
            Account = account ?? throw new ArgumentNullException(nameof(account));
            Player = player;
        }

        public Account Account { get; }

        public string Id => Account.id;

        public PlayerRef Player { get; private set; }

        public bool Ended { get; private set; }

        internal void MoveTo(PlayerRef player)
        {
            Player = player;
        }

        public T State<T>() where T : class, new()
        {

            if (Ended)
            {
                ServerLog.Warn(
                    $"{typeof(T).Name} asked for state on {Account.nickname}'s ended session");
            }

            if (_state.TryGetValue(typeof(T), out object existing))
            {
                return (T)existing;
            }

            var created = new T();
            _state[typeof(T)] = created;
            return created;
        }

        public T Peek<T>() where T : class
        {
            return _state.TryGetValue(typeof(T), out object existing) ? (T)existing : null;
        }

        public void Dispose()
        {
            Ended = true;

            foreach (object bag in _state.Values)
            {
                if (!(bag is IDisposable disposable))
                {
                    continue;
                }

                try
                {
                    disposable.Dispose();
                }
                catch (Exception ex)
                {
                    ServerLog.Warn(
                        $"session state {bag.GetType().Name} failed to dispose for " +
                        $"{Account.nickname}: {ex.Message}");
                }
            }

            _state.Clear();
        }
    }
}
