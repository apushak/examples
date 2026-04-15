using KS.Reactor.Server;

namespace MatchMakerSample.Server
{
    // Data associated with a player connected to the match maker room.
    public class spPlayerMatchData : ksServerPlayerScript
    {
        public Match Match; // The match the player belongs to.
        public string Name; // Username
        public ulong Token; // Authentication token the player will use to connect to the game room.
    }
}