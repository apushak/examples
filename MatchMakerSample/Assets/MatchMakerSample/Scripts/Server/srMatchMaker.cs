using System;
using System.Collections.Generic;
using System.Collections;
using System.Threading;
using System.Threading.Tasks;
using KS.Reactor.Server;
using KS.Reactor;

namespace MatchMakerSample.Server
{
    // The match maker assigns players to matches, starts rooms for matches and sends players to those rooms. Players
    // can request to join a public match, start a private match and get a room code, or join a private match using a
    // room code.
    // 
    // There is always one public match accepting players that any players who request a public match are put into. A
    // timer starts when the public match has the minimum number of players. A game room is launched for the match when
    // the timer reaches a set time or the match has the maximum number of players, and players for the match are sent
    // to the launched room once it is ready.
    // 
    // The player who creates a private match is the owner of the match, and the match will start when they send an
    // RPC to start the match if it has enough players. It will start automatically if it has the maximum number of
    // players. If the match owner disconnects, the first player who joined after the owner will become the new owner.
    // 
    // Players send their username as an authentation parameter when they connect to the matchmaker. The matchmaker
    // sends an authentication token and the username for each player to the game rooms it launches and waits for the
    // game room to acknowledge it received the data. When the matchmaker receives the acknowledgment, it sends each
    // player their authentication token and the room info they will use to connect to the game room.
    // 
    // If a room is launched for a match but the match is cancelled because players disconnected before the room is
    // ready to accept players, that room will become a stand-by room and will be used for the next match that starts.
    // There is never more than one stand-by room at a time, and if a second room's match is cancelled before it
    // starts, the room is shutdown instead. The match-maker sends periodic keep alive RPCs to the stand-by room to
    // prevent it from shutting itself down.
    public class srMatchMaker : ksServerRoomScript
    {
        // Minimum number of players in a match.
        [ksEditable]
        public int MinPlayers = 2;

        // Maximum number of players in a match.
        [ksEditable]
        public int MaxPlayers = 10;

        // Maximum amount of time in seconds to wait before starting a public match that has the minimum number of
        // players.
        [ksEditable]
        public float MaxWaitTime = 30f;

        // Time in seconds to wait after launching a room before cancelling the match if we don't get a response that
        // the room is ready to accept players.
        [ksEditable]
        public float StartMatchTimeout = 10f;

        // How often in seconds to send a KEEP_ALIVE RPC to the stand-by room. Must be larger than
        // <see cref="srGame.ShutdownTime"/>.
        [ksEditable]
        public float StandbyKeepAliveInterval = 30f;

        // Number of times to reattempt to launch a room for a match if an error occurs.
        [ksEditable]
        public int NumLaunchRetries = 1;

        // The name of the scene to launch for game rooms.
        [ksEditable]
        public string Scene = "Game";

        // The name of the game object with a ksRoomType component in the scene to launch.
        [ksEditable]
        public string RoomType = "GameRoom";

        private Match m_nextPublicMatch;
        private Dictionary<string, Match> m_privateMatches = new Dictionary<string, Match>();
        private List<Match> m_startingMatches = new List<Match>();
        private ksRoomInfo m_standbyRoom;
        private float m_nextMatchTimer;
        private float m_keepAliveTimer;
        private long m_timeoutTicks;

        // Called after all other scripts on all entities are attached.
        public override void Initialize()
        {
            // Convert StartMatchTimeout from seconds to ticks.
            m_timeoutTicks = (long)(StartMatchTimeout * TimeSpan.TicksPerSecond);

            MinPlayers = ksMath.Max(1, MinPlayers);
            MaxPlayers = ksMath.Max(MinPlayers, MaxPlayers);
            Properties[Prop.MIN_PLAYERS] = MinPlayers;
            
            m_nextPublicMatch = new Match(Room, NumLaunchRetries);
            Room.OnAuthenticate += Authenticate;
            Room.OnPlayerJoin += PlayerJoined;
            Room.OnPlayerLeave += PlayerLeft;
            Room.OnUpdate[0] += Update;
        }

        // Called when the script is detached.
        public override void Detached()
        {
            Room.OnPlayerJoin -= PlayerJoined;
            Room.OnPlayerLeave -= PlayerLeft;
            Room.OnUpdate[0] -= Update;
        }

        // Called during the update cycle
        private void Update()
        {
            // Iterate the starting matches and check for/handle launch room task completion/state changes.
            long now = DateTime.Now.Ticks;
            for (int i = m_startingMatches.Count - 1; i >= 0; i--)
            {
                try
                {
                    UpdateMatch(m_startingMatches[i], now);
                }
                catch (Exception e)
                {
                    ksLog.Error(this, "Uncaught exception in UpdateMatch for match " + m_startingMatches[i].Id + ".", e);
                    CancelMatch(m_startingMatches[i], CancelReason.SERVER_ERROR);
                    m_startingMatches.RemoveAt(i);
                }
            }

            try
            {
                UpdateStandbyRoom();
            }
            catch (Exception e)
            {
                ksLog.Error(this, "Uncaught exception in UpdateStandbyRoom.", e);
                m_standbyRoom = null;
            }

            if (m_nextPublicMatch.Players.Count < MinPlayers)
            {
                if (m_nextMatchTimer != 0f)
                {
                    // Rest the next match timer because there aren't enough players to start a match, and tell the
                    // remaining players to clear their countdown timer.
                    m_nextMatchTimer = 0f;
                    Room.CallRPC(m_nextPublicMatch.Players, RPC.FINDING_PLAYERS);
                }
                return;
            }

            if (m_nextMatchTimer == 0f)
            {
                // Tell the players to start a countdown timer for the match to start.
                Room.CallRPC(m_nextPublicMatch.Players, RPC.COUNTDOWN, MaxWaitTime);
            }

            // Start the match if enough time has passed since we've had the minimum number of players.
            m_nextMatchTimer += Time.Delta;
            if (m_nextMatchTimer >= MaxWaitTime)
            {
                StartMatch(m_nextPublicMatch);

                // Create a new public match for any new players who request a public match.
                m_nextPublicMatch = new Match(Room, NumLaunchRetries);
                m_nextMatchTimer = 0f;
            }
        }

        // Authenticates a player. Return a non-zero result to fail authentication.
        private Task<ksAuthenticationResult> Authenticate(ksIServerPlayer player, ksMultiType[] args,
            CancellationToken cancellationToken)
        {
            // If the number of arguments is wrong, return a non-zero result to fail authentication.
            if (args.Length != 1)
            {
                return Task.FromResult(new ksAuthenticationResult(1));
            }

            // The argument is the player's username. You can perform you own username validation here.
            string name = args[0].String.Trim();
            // If the the name is empty, name them `Player #`.
            if (string.IsNullOrEmpty(name))
            {
                name = "Player " + player.Id;
            }

            // Set the username on the player script.
            player.Scripts.Get<spPlayerMatchData>().Name = args[0];
            return Task.FromResult(new ksAuthenticationResult(0));
        }

        // Called when players connect.
        private void PlayerJoined(ksIServerPlayer player)
        {
            // Set player visibility to false because we don't want to sync information about players to everyone and
            // instead want to only sync information about players to other players in the same match.
            player.IsVisible = false;

            // Tell players their name when they join. This is needed in case the authenticator changed the player's
            // name during validation or because the user did not provide a name. Because we off player visibility, we
            // need to use RPCs for player names instead of player properties.
            Room.CallRPC(player, RPC.NAME, player.Scripts.Get<spPlayerMatchData>().Name);
        }

        // Called when players disconnect.
        private void PlayerLeft(ksIServerPlayer player)
        {
            // Remove the player from their match.
            Match match = player.Scripts.Get<spPlayerMatchData>().Match;
            if (match != null)
            {
                match.Remove(player);

                // If the match was private and has no players, remove it from the private matches dictionary.
                if (!match.IsPublic && match.Players.Count == 0)
                {
                    m_privateMatches.Remove(match.RoomCode);
                }
            }
        }

        //
        // Checks the match for and handles room launch task completion and state changes. Called on matches in the
        // starting matches list.
        private void UpdateMatch(Match match, long nowTicks)
        {
            // Handle completion of the launch room task.
            if (match.Task != null && match.Task.IsCompleted)
            {
                RoomStartCompleted(match);
                match.Task = null;
            }

            // Cleanup the match if the match is not starting and isn't waiting for a room launch task.
            if (match.State != Match.States.STARTING)
            {
                if (match.Task == null)
                {
                    if (match.State == Match.States.CANCELLED && match.RoomInfo != null)
                    {
                        // Set the match's room to stand-by, or stop it if there is already a stand-by room.
                        SetStandbyOrStopRoom(match.RoomInfo);
                    }
                    m_startingMatches.Remove(match);
                    if (!match.IsPublic && match.State == Match.States.CANCELLED)
                    {
                        m_privateMatches.Remove(match.RoomCode);
                    }
                }
            }
            // If the match has less than the minimum number of players, cancel the match if it was public and put the
            // players into the next public match, or put the match back in the finding players state if it was private.
            else if (match.Players.Count < MinPlayers)
            {
                if (match.IsPublic)
                {
                    ksLog.Info(this, "Cancelling match " + match.Id + " because players disconnected.");
                    CancelMatch(match, CancelReason.REMOTE_DISCONNECT);
                    foreach (ksIServerPlayer player in match.Players)
                    {
                        FindPublicMatch(player);
                    }
                }
                else
                {
                    ksLog.Info(this, "Private match " + match.Id + " not starting because players disconnected.");
                    match.State = Match.States.FINDING_PLAYERS;

                    // Tell players the match needs more players.
                    Room.CallRPC(match.Players, RPC.FINDING_PLAYERS);
                }
            }
            // If the room failed to start before the timeout, try again or cancel if we ran out of retries.
            else if (nowTicks >= match.TimeoutTime)
            {
                ksLog.Warning(this, "Start match " + match.Id + " timedout.");
                RetryOrCancelMatch(match, CancelReason.TIMEOUT);
            }
        }

        // Does nothing if there is no stand-by room. If there are no connected players, sets stand-by room to null.
        // Sends a KEEP_ALIVE RPC to the stand-by room if enough time has passed since the last one.
        private void UpdateStandbyRoom()
        {
            if (m_standbyRoom == null)
            {
                return;
            }
            if (Cluster.IsConnected)
            {
                // Stop the stand-by room if there are no players.
                if (Room.ConnectedPlayerCount == 0)
                {
                    ksLog.Info(this, "Stopping standby room " + m_standbyRoom.Id);
                    Cluster.StopRoom(m_standbyRoom.Id).OnComplete = RoomStopCompleted;
                    m_standbyRoom = null;
                }
                else
                {
                    // Increment the keep alive timer and send a KEEP_ALIVE RPC to the stand-by room if enough time
                    // has passed since the last RPC.
                    m_keepAliveTimer += Time.Delta;
                    if (m_keepAliveTimer >= StandbyKeepAliveInterval)
                    {
                        Cluster.CallRoomRPC(new uint[] { m_standbyRoom.Id }, RPC.KEEP_ALIVE);
                        m_keepAliveTimer = 0f;
                    }
                }
            }
            else
            {
                ksLog.Warning(this, "Lost cluster connection. Setting stand-by room to null.");
                m_standbyRoom = null;
            }
        }

        // Puts the player in the next public match, and starts the match if it is full. You could change the logic to
        // try match players based on their skill ranking.
        [ksRPC(RPC.FIND_PUBLIC_MATCH)]
        private void FindPublicMatch(ksIServerPlayer player)
        {
            m_nextPublicMatch.Add(player);
            if (m_nextPublicMatch.Players.Count == MaxPlayers)
            {
                StartMatch(m_nextPublicMatch);

                // Create a new public match for any new players who request a public match.
                m_nextPublicMatch = new Match(Room, NumLaunchRetries);
                m_nextMatchTimer = 0f;
            }
            else if (m_nextMatchTimer > 0f)
            {
                // Tell the player how much time remains in the countdown to start the match.
                Room.CallRPC(player, RPC.COUNTDOWN, MaxWaitTime - m_nextMatchTimer);
            }
        }

        // Creates a private match and sends the player who create the match a room code that other players can use to
        // join the match.
        [ksRPC(RPC.START_PRIVATE_MATCH)]
        private void CreatePrivateMatch(ksIServerPlayer player)
        {
            Match match = new Match(Room, NumLaunchRetries);
            do
            {
                match.GenerateRoomCode();
            } while (m_privateMatches.ContainsKey(match.RoomCode));
            m_privateMatches[match.RoomCode] = match;
            match.Add(player);
        }

        // Adds the player to the private match with the given room code, or sends the player an invalid-room-code RPC
        // if a private match with that room code could not be found, or they could not be added to the match.
        [ksRPC(RPC.JOIN_PRIVATE_MATCH)]
        private void JoinPrivateMatch(ksIServerPlayer player, string roomCode)
        {
            Match match;
            if (!m_privateMatches.TryGetValue(roomCode, out match) || match.State != Match.States.FINDING_PLAYERS)
            {
                Room.CallRPC(player, RPC.INVALID_ROOM_CODE);
            }
            else
            {
                match.Add(player);

                // Start the match if it has the maximum number of players.
                if (match.Players.Count == MaxPlayers)
                {
                    StartMatch(match);
                }
            }
        }

        // Starts the private match the player is in if it can be started and the player is the match owner.
        [ksRPC(RPC.START_GAME)]
        private void StartPrivateMatch(ksIServerPlayer player)
        {
            Match match = player.Scripts.Get<spPlayerMatchData>().Match;
            if (match != null &&
                !match.IsPublic &&
                match.State == Match.States.FINDING_PLAYERS &&
                match.Players.Count >= MinPlayers &&
                player == match.Players[0])// The first player is the owner.
            {
                StartMatch(match);
            }
        }

        // Begins the process of starting a match by launching a room for the match. If there is a stand-by room, uses
        // it instead and skips to the next step of sending player-authentication data to the room. The match can only
        // be started from the FINDING_PLAYRERS state, or from the STARTING state if allowRetry is true.
        private void StartMatch(Match match, bool allowRetry = false)
        {
            try
            {
                if (match.State != Match.States.FINDING_PLAYERS &&
                    (match.State != Match.States.STARTING || !allowRetry))
                {
                    ksLog.Warning(this, "Cannot start " + match.State + " match " + match.Id + ".");
                    return;
                }
                if (!Cluster.IsConnected)
                {
                    ksLog.Error(this, "Cannot start match " + match.Id + " because there is no cluster connection.");
                    CancelMatch(match, CancelReason.SERVER_ERROR);
                    return;
                }

                // Set the timeout tim in ticks that the room must be ready by.
                match.TimeoutTime = DateTime.Now.Ticks + m_timeoutTicks;

                if (match.State != Match.States.STARTING)
                {
                    match.State = Match.States.STARTING;
                    Room.CallRPC(match.Players, RPC.STARTING_GAME);
                    m_startingMatches.Add(match);
                }

                // Use the stand-by room if there is one.
                if (m_standbyRoom != null)
                {
                    ksLog.Info(this, "Starting match " + match.Id + " in stand-by room " + m_standbyRoom.Id + " (" +
                        m_standbyRoom.Name + ").");
                    SendMatchAuthDataToRoom(match, m_standbyRoom);
                    m_standbyRoom = null;
                    return;
                }

                // Launch a new game room for this match.
                string roomName = "Game " + match.Id;
                ksLog.Info(this, "Starting " + roomName);

                // Start the room with the authenticated tag. The srGame script won't let players connect without
                // authenticating with a token when this tag is set.
                match.Task = Cluster.StartRoom(roomName, Scene, RoomType, null, Tags.AUTHENTICATED);
            }
            catch (Exception e)
            {
                ksLog.Error(this, "Uncaught exception in StartMatch for match " + match.Id, e);
                CancelMatch(match, CancelReason.SERVER_ERROR);
            }
        }

        // Reattempts to launch a room for a match, or cancels the match if there are no retries left.
        private void RetryOrCancelMatch(Match match, uint reason)
        {
            if (match.State != Match.States.STARTING)
            {
                return;
            }
            if (match.Retries > 0)
            {
                match.Retries--;
                ksLog.Info(this, "Retrying to start match " + match.Id + ".");
                StartMatch(match, true);
            }
            else
            {
                CancelMatch(match, reason);
            }
        }

        // Cancels a match and informs the players the match was cancelled.
        private void CancelMatch(Match match, uint reason)
        {
            if (match.State == Match.States.CANCELLED || match.State == Match.States.STARTED)
            {
                ksLog.Info(this, "Cannot cancel " + match.State + " match " + match.Id + ".");
                return;
            }
            ksLog.Info(this, "Cancelling match " + match.Id + ", reason = " + reason);
            match.State = Match.States.CANCELLED;
            Room.CallRPC(match.Players, RPC.CANCEL_MATCH, reason);
            for (int i = 0; i < match.Players.Count; i++)
            {
                match.Players[i].Scripts.Get<spPlayerMatchData>().Match = null;
            }
        }

        // Called when a room-launch task completes.
        private void RoomStartCompleted(Match match)
        {
            if (!string.IsNullOrEmpty(match.Task.Error))
            {
                ksLog.Error(this, "Error starting room: " + match.Task.Error);

                // Reattempt to launch the room, or cancel if there are no retries left.
                RetryOrCancelMatch(match, CancelReason.SERVER_ERROR);
            }
            else if (match.State == Match.States.STARTING && match.Players.Count >= MinPlayers)
            {
                // Send player-authenticatin data to the launched room.
                SendMatchAuthDataToRoom(match, match.Task.Result);
            }
            else
            {
                // There aren't enough players for the match because players disconnected. Set the room to stand-by, or
                // stop it if there is already a stand-by room.
                SetStandbyOrStopRoom(match.Task.Result);
            }
        }

        // Called when a room-stop cluster call completes. This is called off the main-thread.
        private void RoomStopCompleted(ksAsyncResult result)
        {
            if (!string.IsNullOrEmpty(result.Error))
            {
                ksLog.Error(this, "Error stopping room: " + result.Error);
            }
        }

        // Generates a unique token for each player in a match, then sends the player tokens and their usernames to the
        // given room. The players will use these tokens to connect to the room.
        private void SendMatchAuthDataToRoom(Match match, ksRoomInfo roomInfo)
        {
            ksLog.Info(this, "Sending match " + match.Id + " auth data for " + match.Players.Count +
                " players to room " + roomInfo.Id);
            match.RoomInfo = roomInfo;
            match.GenerateTokens();
            ksMultiType[] args = new ksMultiType[match.Players.Count * 2];
            int index = 0;
            for (int i = 0; i < match.Players.Count; i++)
            {
                spPlayerMatchData playerData = match.Players[i].Scripts.Get<spPlayerMatchData>();
                args[index++] = playerData.Token;
                args[index++] = playerData.Name;
            }
            Cluster.CallRoomRPC(new uint[] { match.RoomInfo.Id }, RPC.AUTH_DATA, args);
        }

        // Sets a room to be the stand-by room to use for the next match that starts, or stops the room if there is
        // already a stand-by room.
        private void SetStandbyOrStopRoom(ksRoomInfo roomInfo)
        {
            if (!Cluster.IsConnected)
            {
                ksLog.Warning(this, "Cannot set stand-by room; no cluster connection.");
                return;
            }
            if (m_standbyRoom != null)
            {
                ksLog.Info(this, "Stopping room " + roomInfo.Id);
                Cluster.StopRoom(roomInfo.Id).OnComplete = RoomStopCompleted;
                return;
            }
            ksLog.Info(this, "Room " + roomInfo.Id + " is on standby.");
            Cluster.CallRoomRPC(new uint[] { roomInfo.Id }, RPC.KEEP_ALIVE);
            m_standbyRoom = roomInfo;
            m_keepAliveTimer = 0f;
        }

        // Called when a game room acknowledges the authentication data we sent it. Sends each player in the room's
        // match the room info and authentication token so they can connect to the room, if the match still has at
        // least the minimum number of players.
        [ksClusterRPC(RPC.AUTH_DATA)]
        private void AcknowledgeAuthData(uint roomId)
        {
            for (int i = 0; i < m_startingMatches.Count; i++)
            {
                Match match = m_startingMatches[i];
                if (match.RoomInfo.Id == roomId)
                {
                    if (match.Players.Count >= MinPlayers)
                    {
                        ksLog.Info(this, "Sending " + match.Players.Count + " players to room " + roomId +
                            " for match " + match.Id + ".");
                        match.State = Match.States.STARTED;
                        foreach (ksIServerPlayer player in match.Players)
                        {
                            spPlayerMatchData playerData = player.Scripts.Get<spPlayerMatchData>();
                            Room.CallRPC(player, RPC.MOVE_TO_ROOM, match.RoomInfo, playerData.Token);
                        }
                    }
                    // If there's less than the required number of players, we don't need to do anything as the match
                    // will be cancelled in the next update.
                    break;
                }
            }
        }
    }
}