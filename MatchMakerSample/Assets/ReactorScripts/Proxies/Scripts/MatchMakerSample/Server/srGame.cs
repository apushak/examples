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
    
    public class srGame : ksProxyRoomScript
    {
#if UNITY_EDITOR
        public Single StartTime;
        public Single MaxStartTime;
        public Single GameTime;
        public Single ShutdownTime;
        public srGame() : base() 
        {
            StartTime = 3f;
            MaxStartTime = 10f;
            GameTime = 10f;
            ShutdownTime = 60f;
        }
#endif
    }
}