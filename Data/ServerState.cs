using System;

namespace TidalNexus.StandaloneServer.Data
{

    [Serializable]
    public sealed class ServerState
    {

        public long lastWeeklyResetUnix;

        private static ServerState _current;

        public static ServerState Current
        {
            get
            {
                if (_current != null)
                {
                    return _current;
                }

                _current = AccountStore.Repository.ReadServerState();

                if (_current == null)
                {
                    _current = new ServerState
                    {

                        lastWeeklyResetUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                    };

                    Save();
                }

                return _current;
            }
        }

        public static void Save()
        {
            if (_current == null)
            {
                return;
            }

            AccountStore.Repository.WriteServerState(_current);
        }
    }
}
