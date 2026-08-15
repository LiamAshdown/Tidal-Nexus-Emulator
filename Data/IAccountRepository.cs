using System.Collections.Generic;

namespace TidalNexus.StandaloneServer.Data
{

    public interface IAccountRepository
    {

        string Description { get; }

        void Open();

        Account Find(string id);

        Account FindByNickname(string nickname);

        IEnumerable<Account> Accounts { get; }

        void Write(Account account);

        Clan FindClan(string tag);

        IEnumerable<Clan> Clans { get; }

        void WriteClan(Clan clan);

        void DeleteClan(string tag);

        ServerState ReadServerState();

        void WriteServerState(ServerState state);
    }
}
