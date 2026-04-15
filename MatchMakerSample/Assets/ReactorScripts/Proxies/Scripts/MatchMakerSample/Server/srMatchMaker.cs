/* This file was auto-generated. DO NOT MODIFY THIS FILE. */
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;
using KS.Reactor.Client.Unity;
using KS.Reactor;
using KS.Unity;

namespace KSProxies.Scripts.MatchMakerSample.Server
{
    
    public class srMatchMaker : ksProxyRoomScript
    {
#if UNITY_EDITOR
        public Int32 MinPlayers;
        public Int32 MaxPlayers;
        public Single MaxWaitTime;
        public Single StartMatchTimeout;
        public Single StandbyKeepAliveInterval;
        public Int32 NumLaunchRetries;
        public String Scene;
        public String RoomType;
        public srMatchMaker() : base() 
        {
            MinPlayers = 2;
            MaxPlayers = 10;
            MaxWaitTime = 30f;
            StartMatchTimeout = 10f;
            StandbyKeepAliveInterval = 30f;
            NumLaunchRetries = 1;
            Scene = "Game";
            RoomType = "GameRoom";
        }
#endif
    }
}