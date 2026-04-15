using System;
using System.Collections.Generic;
using System.Collections;
using UnityEngine;
using KS.Reactor.Client.Unity;
using KS.Reactor.Client;
using KS.Reactor;

namespace MatchMakerSample.Client
{
    // The client game script handles the GAME_OVER RPC by displying the winner and disconnecting.
    [RequireComponent(typeof(GameConnectHandler))]
    public class crGame : ksRoomScript
    {
        // Called when the game is over.
        [ksRPC(RPC.GAME_OVER)]
        private void GameOver(uint winningPlayerId)
        {
            // Display the gme over UI with a message saying who won.
            string message;
            if (winningPlayerId == Room.LocalPlayerId)
            {
                message = "You Won!";
            }
            else
            {
                // Get the winner's name from their name property.
                ksPlayer player = Room.GetPlayer(winningPlayerId);
                message = (player == null ? "Player " + winningPlayerId : player.Properties[Prop.NAME].String) + " Won!";
            }
            GetComponent<GameConnectHandler>().ShowGameOverUI(message);
            Room.Disconnect();
        }
    }
}