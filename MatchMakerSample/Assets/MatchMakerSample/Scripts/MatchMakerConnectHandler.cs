using UnityEngine;
using UnityEngine.UI;
using TMPro;
using KS.Reactor.Client.Unity;
using KS.Reactor.Client;

namespace MatchMakerSample.Client
{
    // Script for connecting to the Match Maker room using a ksConnect component when the user clicks a button to find
    // a public match, or create or join a private match.
    [RequireComponent(typeof(ksConnect))]
    public class MatchMakerConnectHandler : MonoBehaviour
    {
        private enum MatchOptions
        {
            FIND_PUBLIC,
            START_PRIVATE,
            JOIN_PRIVATE
        }

        public RectTransform ConnectUI; // UI object to enable when you aren't connected to the match maker.
        public TMP_InputField NameInput; // Input field for the player's username.
        public TMP_InputField RoomCodeInput; // Input field for the room code for joining a private match.
        public Button FindMatchButton; // Button to find a public match.
        public Button StartMatchButton; // Button to start a private match.
        public Button JoinMatchButton; // Button to join a private match with a room code.
        public TMP_Text Message;
        private ksConnect m_connect;
        private MatchOptions m_option;

        private static string m_username;

        private void Start()
        {
            ConnectUI.gameObject.SetActive(true);

            FindMatchButton.onClick.AddListener(FindMatch);
            StartMatchButton.onClick.AddListener(StartPrivateMatch);
            JoinMatchButton.onClick.AddListener(JoinPrivateMatch);

            // Register an input validator for the room code input to only allow letters and force them to be upper
            // case.
            RoomCodeInput.onValidateInput += ValidateRoomCodeInput;

            // Set the username field to the static user name field. This will be null the first time we load the
            // scene, but we will set the static user name field to the name we connected with before loading the game
            // scene, so the next time we load this scene it will remember the player's user name.
            NameInput.text = m_username;

            m_connect = GetComponent<ksConnect>();
            m_connect.OnGetRooms.AddListener(HandleGetRooms);
            m_connect.OnConnect.AddListener(HandleConnect);
            m_connect.OnDisconnect.AddListener(HandleDisconnect);
        }

        // Connects to the match maker room and requests to find a public match.
        private void FindMatch()
        {
            Connect(MatchOptions.FIND_PUBLIC);
        }

        // Connects to the match maker room and requests to start a private match.
        private void StartPrivateMatch()
        {
            Connect(MatchOptions.START_PRIVATE);
        }

        // Connects to the match maker room and requests to join a private match with a room code.
        private void JoinPrivateMatch()
        {
            if (RoomCodeInput.text == null || RoomCodeInput.text.Length != Consts.ROOM_CODE_LENGTH)
            {
                SetMessage("You must enter a " + Consts.ROOM_CODE_LENGTH + "-character room code.", true);
            }
            else
            {
                Connect(MatchOptions.JOIN_PRIVATE);
            }
        }

        // Connects to the match maker with one of the options for finding/starting/joining a match.
        private void Connect(MatchOptions option)
        {
            // Store the option we chose so we can choose what to do with it after we connect.
            m_option = option;
            SetUIEnabled(false);

            // Begin the connection process the ksConnect script is configured for.
            m_connect.BeginConnect();
            SetMessage("Connecting...");
        }

        // Called when we get the list of running rooms. Connects to the first room in the list, or displays a message
        // if no rooms were found.
        private void HandleGetRooms(ksConnect.GetRoomsEvent ev)
        {
            if (!string.IsNullOrEmpty(ev.Error))
            {
                SetMessage("Error getting rooms: " + ev.Error, true);
                SetUIEnabled(true);
            }
            else if (ev.Rooms.Count == 0)
            {
                SetMessage("No servers found.", true);
                SetUIEnabled(true);
            }
            else
            {
                // Store the username in a static field so we remember the username for the next time we load this
                // scene.
                m_username = NameInput.text;
                m_connect.Connect(ev.Rooms[0], m_username);
            }
        }

        // Called when a connect attempt completes.
        private void HandleConnect(ksConnect.ConnectEvent ev)
        {
            if (ev.Status != ksBaseRoom.ConnectStatus.SUCCESS)
            {
                SetMessage("Connect error: " + ev.Status, true);
                SetUIEnabled(true);
            }
            else
            {
                // Clear the UI message and hide the connect UI.
                SetMessage(null);
                ConnectUI.gameObject.SetActive(false);

                // Send an RPC to tell the server what we want to do.
                switch (m_option)
                {
                    case MatchOptions.FIND_PUBLIC: ev.Room.CallRPC(RPC.FIND_PUBLIC_MATCH); break;
                    case MatchOptions.START_PRIVATE: ev.Room.CallRPC(RPC.START_PRIVATE_MATCH); break;
                    case MatchOptions.JOIN_PRIVATE: ev.Room.CallRPC(RPC.JOIN_PRIVATE_MATCH, RoomCodeInput.text); break;
                }
            }
        }

        // Called when we disconnect from the server.
        private void HandleDisconnect(ksConnect.DisconnectEvent ev)
        {
            if (GameConnectHandler.RoomInfo != null)
            {
                return;
            }
            // Enable and show the connect UI.
            SetUIEnabled(true);
            ConnectUI.gameObject.SetActive(true);

            // The connect status is ABORTED for user-initiated disconnects. For any other disconnect reason, we display
            // a disconnect message in the UI.
            if (ev.Status != ksBaseRoom.ConnectStatus.ABORTED)
            {
                SetMessage("Disconnect: " + ev.Status);
            }
        }

        // Enables or disables UI interaction.
        private void SetUIEnabled(bool enabled)
        {
            NameInput.interactable = enabled;
            RoomCodeInput.interactable = enabled;
            FindMatchButton.interactable = enabled;
            StartMatchButton.interactable = enabled;
            JoinMatchButton.interactable= enabled;
        }

        // Sets a message to display in the UI.
        public void SetMessage(string message, bool error = false)
        {
            Message.text = message;
            Message.color = error ? Color.red : Color.white;
        }

        // Called to validate each characer the user times in the room code input. Verifies the letter is a character
        // and changes lower case letters to upper case.
        private char ValidateRoomCodeInput(string text, int index, char addedChar)
        {
            return char.IsLetter(addedChar) ? char.ToUpperInvariant(addedChar) : '\0';
        }
    }
}