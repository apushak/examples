using System;
using System.Collections.Generic;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;
using KS.Reactor.Client.Unity;
using KS.Reactor.Client;
using KS.Reactor;

namespace MatchMakerSample.Client
{
    // Client match maker script that handles RPCs from the match maker room for connecting to a game room and
    // updating the UI.
    public class crMatchMaker : ksRoomScript
    {
        private enum States
        {
            FINDING_PLAYERS = 0,
            COUNTDOWN = 1,
            STARTING = 2
        }

        public RectTransform LobbyUI;// UI object to enable when connected.
        public PlayerListUI PlayerList;
        public Button StartButton;
        public Button CancelButton;
        public TMP_Text Message;// Message the depends on the state
        public TMP_Text RoomCodeText;

        private string m_localUsername;
        private States m_state;
        private float m_countdown;
        private int m_minPlayers;

        protected override void Awake()
        {
            // Disable the lobby UI from Awake.
            LobbyUI.gameObject.SetActive(false);

            // Reactor scripts that override Awake should always call the base Awake.
            base.Awake();
        }

        // Called when we connect to the server.
        public override void Initialize()
        {
            // Show the lobby UI when we connect.
            LobbyUI.gameObject.SetActive(true);

            // Disable and hide the startbutton. It's only visible for the owner of private matches (usually the creator
            // of the match unless they disconnect), and it's only interactable if the match has the minimum number of
            // players.
            StartButton.gameObject.SetActive(false);
            StartButton.interactable = false;

            StartButton.onClick.AddListener(StartGame);
            CancelButton.onClick.AddListener(Cancel);

            m_minPlayers = Properties[Prop.MIN_PLAYERS];
            RoomCodeText.text = "";
            m_state = States.FINDING_PLAYERS;
            UpdateMessage();
        }

        // Called when we disconnect or when the script is removed.
        public override void Detached()
        {
            // Hide the lobby UI when we disconnect.
            LobbyUI.gameObject.SetActive(false);
            StartButton.gameObject.SetActive(false);
            StartButton.onClick.RemoveListener(StartGame);
            CancelButton.onClick.RemoveListener(Cancel);
            PlayerList.Clear();
            RoomCodeText.text = "";
        }

        private void Update()
        {
            // If we're in the COUNTDOWN state, update the countdown.
            if (m_state == States.COUNTDOWN && m_countdown > 1f)
            {
                m_countdown -= Time.Delta;
                // Don't let the countdown go below 1. The server will send an RPC to change the message to say the game
                // is starting when it reaches zero.
                m_countdown = Mathf.Max(1f, m_countdown);
                UpdateMessage();
            }
        }

        // Send an RPC to the server to start the game for a private match. Only the match owner (usually the match
        // creator unless they disconnected) can start the game.
        private void StartGame()
        {
            Room.CallRPC(RPC.START_GAME);
        }

        // Disconnects from the match maker server.
        private void Cancel()
        {
            Room.Disconnect();
        }

        // Updates the displayed message based on the state.
        private void UpdateMessage()
        {
            switch (m_state)
            {
                case States.FINDING_PLAYERS: Message.text = "Finding Players"; break;
                case States.STARTING: Message.text = "Starting Game"; break;
                case States.COUNTDOWN: Message.text = "Starting Game in " + Mathf.CeilToInt(m_countdown); break;
            }
        }

        // Called by the server to tell us our username.
        [ksRPC(RPC.NAME)]
        private void SetUsername(string name)
        {
            m_localUsername = name;

            // Add the local player to the player list. The local player is always the first player in the list.
            PlayerList.Add(Room.LocalPlayerId, name);
        }

        // Called by the server to make us the owner of the private match who is in charge of starting the match once
        // enough players have joined.
        [ksRPC(RPC.OWNER)]
        private void MakeOwner()
        {
            StartButton.gameObject.SetActive(true);
        }

        // Called by the server to set the displayed room code other users can use to join the private match.
        [ksRPC(RPC.ROOM_CODE)]
        public void SetRoomCode(string roomCode)
        {
            RoomCodeText.text = "Room Code: " + roomCode;
        }

        // Called by the server when the room code we used to join a private match was invalid.
        [ksRPC(RPC.INVALID_ROOM_CODE)]
        private void HandleInvalidRoomCode()
        {
            GetComponent<MatchMakerConnectHandler>().SetMessage("Invalid Room Code", true);
            Room.Disconnect();
        }

        // Called by the server to set a countdown for when the match will start.
        [ksRPC(RPC.COUNTDOWN)]
        private void SetCountdown(float countdown)
        {
            m_countdown = countdown;
            m_state = States.COUNTDOWN;
            UpdateMessage();
        }

        // Called by the server to change to the FINDING_PLAYERS state.
        [ksRPC(RPC.FINDING_PLAYERS)]
        private void FindingPlayers()
        {
            m_state = States.FINDING_PLAYERS;
            UpdateMessage();
        }

        // Called by the server when the room for the game is starting.
        [ksRPC(RPC.STARTING_GAME)]
        private void StartingGame()
        {
            m_state = States.STARTING;
            UpdateMessage();
            StartButton.interactable = false;
        }

        // Called by the server to tell us to connect to a different room using an authentication token.
        [ksRPC(RPC.MOVE_TO_ROOM)]
        private void OnMoveToRoom(ksRoomInfo roomInfo, ulong token)
        {
            // Disconnect from the current room
            Room.Disconnect();

            // Load the scene for the new room. We use the overload that takes LoadSceneParameters because it returns a
            // scene we can use to check if the scene is valid.
            Scene scene = SceneManager.LoadScene(roomInfo.Scene, new LoadSceneParameters());
            if (!scene.IsValid())
            {
                GetComponent<MatchMakerConnectHandler>().SetMessage("Unable to load scene " + roomInfo.Scene, true);
            }
            else
            {
                // Set the room info and token to connect with, and the scene to return to when the game is over.
                GameConnectHandler.RoomInfo = roomInfo;
                GameConnectHandler.Token = token;
                GameConnectHandler.ReturnScene = gameObject.scene.name;
            }
        }

        // Adds one or more players to the player list for this match.
        [ksRPC(RPC.ADD_PLAYERS)]
        private void AddPlayers(ksMultiType[] args)
        {
            // There are two args for each player; a player id and a username.
            for (int i = 0; i + 1 < args.Length; i += 2)
            {
                uint playerId = args[i];
                string name = args[i + 1];
                PlayerList.Add(playerId, name);
            }

            // If we have enough players, make the start button clickable.
            StartButton.interactable = PlayerList.Count >= m_minPlayers;
        }

        // Removes a player from the player list for this match.
        [ksRPC(RPC.REMOVE_PLAYER)]
        private void RemovePlayer(uint playerId)
        {
            PlayerList.Remove(playerId);

            // If we don't have enough players, make the start button non-interactable.
            StartButton.interactable = PlayerList.Count >= m_minPlayers;
        }

        // Called by the server to tell us we were removed from the match.
        [ksRPC(RPC.LEAVE_MATCH)]
        private void LeaveMatch()
        {
            // Clear the player list and re-add the local player.
            PlayerList.Clear();
            PlayerList.Add(Room.LocalPlayerId, m_localUsername);

            StartButton.gameObject.SetActive(false);
            m_state = States.FINDING_PLAYERS;
            UpdateMessage();
        }

        // Called by the server to tell us the match was cancelled.
        [ksRPC(RPC.CANCEL_MATCH)]
        private void CancelMatch(uint reason)
        {
            ksLog.Debug(this, "Match cancelled. Reason: " + reason);

            // If it was cancelled because of a remote-user disconnect, we leave the match, but stay connected and look
            // for another match. Otherwise we disconnect and display an error message.
            if (reason == CancelReason.REMOTE_DISCONNECT)
            {
                LeaveMatch();
            }
            else
            {
                GetComponent<MatchMakerConnectHandler>().SetMessage("Unable to start game. Error code: " + reason, true);
                Room.Disconnect();
            }
        }
    }
}