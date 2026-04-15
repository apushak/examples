using System.Collections.Generic;
using KS.Reactor.Server;
using KS.Reactor;

namespace MatchMakerSample.Server
{
    // State tracking for a match.
    public class Match
    {
        public enum States
        {
            // The match is accepting players.
            FINDING_PLAYERS = 0,
            // The room for the match is starting.
            STARTING = 1,
            // The room for the match was started and players were send the data needed to connect to the room.
            STARTED = 2,
            // The match was cancelled.
            CANCELLED = 3
        }

        public uint Id;
        public States State = States.FINDING_PLAYERS;
        public ksIServerRoom Room;
        public string RoomCode;// Room code for private match. Null or empty if the match is public.
        public ksAsyncResult<ksRoomInfo> Task;// Task to start the room for the match.
        public ksRoomInfo RoomInfo;// Info used to connect to the room for this match. Null if the room hasn't started.
        public long TimeoutTime;// Reattempt to launch the room or cancel if the room hasn't started by this time.
        public int Retries;// Number of retries left to launch the room.

        // Is the match public?
        public bool IsPublic
        {
            get { return string.IsNullOrEmpty(RoomCode); }
        }

        // The players in the room. Do not add or remove players from this list directly. Instead use the
        // Match.Add/Remove methods.
        public List<ksIServerPlayer> Players
        {
            get { return m_players; }
        }

        private HashSet<ulong> m_tokens = new HashSet<ulong>();
        private List<ksIServerPlayer> m_players = new List<ksIServerPlayer>();

        private static ksRandom m_rand = new ksRandom();
        private static uint m_nextId = 1;

        public Match(ksIServerRoom room, int retries)
        {
            Id = m_nextId++;
            Room = room;
            Retries = retries;
        }

        // Adds a player to the match.
        public void Add(ksIServerPlayer player)
        {
            if (State != States.FINDING_PLAYERS)
            {
                ksLog.Warning(this, "Cannot add player to " + State + " match " + Id + ".");
                return;
            }
            spPlayerMatchData playerData = player.Scripts.Get<spPlayerMatchData>();
            if (playerData.Match != null)
            {
                ksLog.Warning(this, "Cannot add player to match" + Id + " because they already have a match.");
                return;
            }
            playerData.Match = this;

            if (m_players.Count > 0)
            {
                // Tell the new player about the existing players.
                ksMultiType[] args = new ksMultiType[m_players.Count * 2];
                int index = 0;
                for (int i = 0; i < m_players.Count; i++)
                {
                    spPlayerMatchData otherPlayerData = m_players[i].Scripts.Get<spPlayerMatchData>();
                    args[index++] = otherPlayerData.Player.Id;
                    args[index++] = otherPlayerData.Name;
                }
                Room.CallRPC(player, RPC.ADD_PLAYERS, args);

                // Tell the existing players about the new player.
                Room.CallRPC(m_players, RPC.ADD_PLAYERS, player.Id, playerData.Name);
            }

            // If the match is private, send the player the room code.
            if (!IsPublic)
            {
                Room.CallRPC(player, RPC.ROOM_CODE, RoomCode);

                // If this is the first player in a private match, tell the player they are the owner of the match. Only
                // the owner of a private match can start the match.
                if (m_players.Count == 0)
                {
                    Room.CallRPC(player, RPC.OWNER);
                }
            }

            m_players.Add(player);
        }

        // Removes a player from a match.
        public void Remove(ksIServerPlayer player)
        {
            // If the match is private and the owner (first player) left, tell the next player they are the new owner.
            if (!IsPublic && m_players.Count > 1 && m_players[0] == player)
            {
                Room.CallRPC(m_players[1], RPC.OWNER);
            }

            if (m_players.Remove(player))
            {
                spPlayerMatchData playerData = player.Scripts.Get<spPlayerMatchData>();
                m_tokens.Remove(playerData.Token);
                playerData.Token = 0;
                playerData.Match = null;
                Room.CallRPC(m_players, RPC.REMOVE_PLAYER, player.Id);
                if (player.IsConnected)
                {
                    Room.CallRPC(player, RPC.LEAVE_MATCH, player.Id);
                }
            }
        }

        // Generates a unique token for each player in the match that they will use to authenticate with the game room.
        public void GenerateTokens()
        {
            for (int i = 0; i < m_players.Count; i++)
            {
                spPlayerMatchData playerData = m_players[i].Scripts.Get<spPlayerMatchData>();
                if (playerData.Token == 0)
                {
                    playerData.Token = GenerateToken();
                }
            }
        }

        // Generates a room code with characters from 'A'-'Z'.
        public void GenerateRoomCode()
        {
            RoomCode = "";
            for (int i = 0; i < Consts.ROOM_CODE_LENGTH; i++)
            {
                RoomCode += (char)m_rand.Next('A', 'Z' + 1);
            }
        }

        // Generate a random non-zero 64-bit token.
        private ulong GenerateToken()
        {
            unchecked
            {
                ulong token;
                do
                {
                    token = (uint)m_rand.Next();
                    token <<= 32;
                    token += (uint)m_rand.Next();
                } while (token == 0 && !m_tokens.Add(token));
                return token;
            }
        }
    }
}