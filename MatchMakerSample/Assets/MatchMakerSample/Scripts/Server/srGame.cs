using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using KS.Reactor.Server;
using KS.Reactor;

namespace MatchMakerSample.Server
{
    // The game script receives authentication tokens for each player and their usernames from the srMatchMaker. When
    // players connect they authenticate using their unique authentication token. If the room was started without the
    // "authenticated" tag, it will allow players to pass authentication without an authentication token, which can be
    // useful for testing locally without having to first connect to a match maker.
    //
    // A timer to start the game will start after the first player connects. If all players connect, the start game
    // timer is reduced to StartTime if it is larger.
    //
    // The game ends after a set time or if there is only one player remaining, and a random winner is chosen. Players
    // are informed of who won and the room shuts itself down.
    //
    // The room shutsdown if no users connect after a set time, or if all players disconnect after
    // starting the game. The srMatchMaker script can send a KEEP_ALIVE RPC to reset the shutdown timer when no players
    // are connected.
    public class srGame : ksServerRoomScript
    {
        private enum States
        {
            STARTING = 0,
            PLAYING = 1,
            FINISHED = 2
        }

        // Time to wait in seconds before starting the game after all players are connected.
        [ksEditable]
        public float StartTime = 3f;

        // Maximum time in seconds to wait before starting the game after the first player connects.
        [ksEditable]
        public float MaxStartTime = 10f;

        // Duration of the game in seconds.
        [ksEditable]
        public float GameTime = 10f;

        // Shutdown if no players are connected for this many seconds.
        [ksEditable]
        public float ShutdownTime = 60f;

        // Maps player authentication tokens to player names for players who haven't authenticated yet.
        private Dictionary<ulong, string> m_playerAuthData = new Dictionary<ulong, string>();
        private bool m_hasAuthData = false;
        private States m_state = States.STARTING;
        private float m_shutdownTimer;
        private float m_timer;
        private ksRandom m_rand = new ksRandom();

        // Called after all other scripts on all entities are attached.
        public override void Initialize()
        {
            Room.OnAuthenticate += Authenticate;
            Room.OnUpdate[0] += Update;
            m_timer = MaxStartTime;
            m_shutdownTimer = ShutdownTime;
        }
        
        // Called when the script is detached.
        public override void Detached()
        {
            Room.OnAuthenticate -= Authenticate;
            Room.OnUpdate[0] -= Update;
        }
        
        // Authenticates a player. Return a non-zero result to fail authentication.
        private async Task<ksAuthenticationResult> Authenticate(ksIServerPlayer player, ksMultiType[] args,
            CancellationToken cancellationToken)
        {
            // If the room does not have the authenticated tag, we allow all players to pass authentication.
            if (!Room.Info.PublicTags.Contains(Tags.AUTHENTICATED))
            {
                player.Properties[Prop.NAME] = "Player " + player.Id;
                return new ksAuthenticationResult(0);
            }

            // Fail authentication if the number of arguments is wrong, or if we haven't gotten the authentication
            // data from the match maker.
            if (args.Length != 1 || !m_hasAuthData)
            {
                return new ksAuthenticationResult(1);
            }

            string name;
            // Authentication can happen in parallel if multiple players connect at the same time, so we need to lock
            // access to the dictionary to ensure two authentication threads don't try to modify it at the same time.
            lock (m_playerAuthData)
            {
                // Fail authentication if the player connected with an invalid token. Remove player tokens from the
                // dictionary when they authenticate.
                if (!m_playerAuthData.Remove(args[0], out name))
                {
                    return new ksAuthenticationResult(1);
                }
            }

            // Set the player's name property to the name their token was mapped to.
            player.Properties[Prop.NAME] = name;
            return await Task.FromResult(new ksAuthenticationResult(0));
        }
        
        // Called during the update cycle
        private void Update()
        {
            if (Room.ConnectedPlayerCount == 0)
            {
                if (m_state != States.STARTING)
                {
                    ksLog.Info(this, "Shutting down because all players disconnected.");
                    Room.ShutDown();
                }
                else
                {
                    m_shutdownTimer -= Time.UnscaledDelta;
                    if (m_shutdownTimer <= 0)
                    {
                        ksLog.Info(this, "Shutting down because no players were connected for " + ShutdownTime +
                            " seconds.");
                        Room.ShutDown();
                    }
                }
                return;
            }

            switch (m_state)
            {
                case States.STARTING: StartingUpdate(); break;
                case States.PLAYING: PlayingUpdate(); break;
            }
        }

        // Called every frame when the game is in the STARTING state.
        private void StartingUpdate()
        {
            m_timer -= Time.Delta;
            if (m_timer <= 0)
            {
                StartGame();
            }
            // If we aren't waiting for more players to connect (m_playerAuthData is empty), set the timer for starting
            // the game to StartTime if it is larger.
            else if (m_playerAuthData.Count == 0 && m_timer > StartTime)
            {
                m_timer = StartTime;
            }
        }

        // Starts the game
        private void StartGame()
        {
            m_state = States.PLAYING;
            ksLog.Info(this, "Game Start!");

            // Add your game start logic here...
        }

        // Called every frame when the game is in the PLAYING state.
        private void PlayingUpdate()
        {
            // End the game when the timer reaches zero or there is only 1 player connected.
            m_timer -= Time.Delta;
            if (m_timer <= 0 || Room.Players.Count == 1)
            {
                EndGame();
            }
        }

        // Ends the game
        private void EndGame()
        {
            m_state = States.FINISHED;

            // Choose a random player to be the winner.
            ksIServerPlayer winner = Room.Players[m_rand.Next(Room.Players.Count)];
            ksLog.Info(this, winner.Properties[Prop.NAME] + " Won!");

            // Tell players who won and shutdown the game room.
            Room.CallRPC(RPC.GAME_OVER, winner.Id);
            Room.ShutDown();
        }

        // Called when we receive player authentication data from the match maker.
        [ksClusterRPC(RPC.AUTH_DATA)]
        private void SetAuthData(uint roomId, ksMultiType[] args)
        {
            if (m_hasAuthData)
            {
                ksLog.Warning(this, "Ignoring duplicate auth data.");
                return;
            }
            // There are 2 args for each player; an authentication token followed by a username.
            for (int i = 0; i + 1 < args.Length; i += 2)
            {
                ulong token = args[i];
                string name = args[i + 1];
                m_playerAuthData[token] = name;
            }

            // Tell the match maker we received the authentication data and are ready to accept connections.
            Cluster.CallRoomRPC(new uint[] { roomId }, RPC.AUTH_DATA);

            // Because the Authenticate function is called off the main-thread, we don't set this flag until after we
            // are done modifying the player auth data dictionary so the Authenticate function won't try to read the
            // dictionary while we are modifying it.
            m_hasAuthData = true;
        }

        // Called when the match maker sends a KEEP_ALIVE RPC to prevent this room from shuttind down if no users
        // connect. Resets the shutdown timer and clears authentication data if we have any. The match maker does this
        // if the match was cancelled and it wants to keep this room alive for a different match.
        [ksClusterRPC(RPC.KEEP_ALIVE)]
        private void KeepAlive()
        {
            if (Room.ConnectedPlayerCount > 0)
            {
                ksLog.Warning(this, "Ignoring keep alive RPC because players are connected.");
                return;
            }
            ksLog.Debug(this, "Got keep alive RPC");
            if (m_hasAuthData)
            {
                m_hasAuthData = false;
                m_playerAuthData.Clear();
            }
            m_shutdownTimer = ShutdownTime;
        }
    }
}