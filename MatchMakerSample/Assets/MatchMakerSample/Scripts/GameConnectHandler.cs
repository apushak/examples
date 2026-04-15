using UnityEngine;
using UnityEngine.UI;
using TMPro;
using KS.Reactor;
using KS.Reactor.Client.Unity;
using KS.Reactor.Client;
using UnityEngine.SceneManagement;

namespace MatchMakerSample.Client
{
    // Script for connecting to the game room using a ksConnect component and optionally a room info and token,
    // and loading the ReturnScene when the game is over and a button is clicked.
    [RequireComponent(typeof(ksConnect))]
    public class GameConnectHandler : MonoBehaviour
    {
        // Info for the room to connect to. If null, connects without authentication using the process the ksConnect
        // script is configured for.
        public static ksRoomInfo RoomInfo;

        // Authentication token to connect with. Only used if RoomInfo is set.
        public static ulong Token;

        // The name of the scene to load when the user clicks the finish button after the game is over.
        public static string ReturnScene;

        public RectTransform GameOverUI; // The object to show when the game is over.
        public TMP_Text Message;
        public Button FinishButton;

        // Start is called once before the first execution of Update after the MonoBehaviour is created
        void Start()
        {
            // Hide the game over UI.
            GameOverUI.gameObject.SetActive(false);

            FinishButton.onClick.AddListener(LoadReturnScene);

            ksConnect connect = GetComponent<ksConnect>();
            connect.OnConnect.AddListener(HandleConnect);
            connect.OnDisconnect.AddListener(HandleDisconnect);

            // If a room info is set, connect to it using the authentication token, then clear both. Otherwise begin the
            // connect process the ksConnect script is configured for.
            if (RoomInfo != null)
            {
                connect.Connect(RoomInfo, Token);
                RoomInfo = null;
                Token = 0;
            }
            else
            {
                connect.BeginConnect();
            }
        }

        // Called when a connect attempt completes.
        private void HandleConnect(ksConnect.ConnectEvent ev)
        {
            if (ev.Status != ksBaseRoom.ConnectStatus.SUCCESS)
            {
                ShowGameOverUI("Connect error: " + ev.Status, true);
            }
        }

        // Called after disconnecting.
        private void HandleDisconnect(ksConnect.DisconnectEvent ev)
        {
            // If we aren't already showing the game over UI, then this disconnect is unexpected and we show the game
            // over UI with a disconnect error message.
            if (!GameOverUI.gameObject.activeSelf)
            {
                ShowGameOverUI("Disconnect: " + ev.Status, true);
            }
        }

        // Shows the game over UI with a message.
        public void ShowGameOverUI(string message, bool error = false)
        {
            Message.text = message;
            Message.color = error ? Color.red : Color.white;
            GameOverUI.gameObject.SetActive(true);
        }

        // Loads the return scene.
        private void LoadReturnScene()
        {
            if (!string.IsNullOrEmpty(ReturnScene))
            {
                SceneManager.LoadScene(ReturnScene);
            }
            else
            {
                ksLog.Info(this, "No ReturnScene to return to.");
            }
        }
    }
}