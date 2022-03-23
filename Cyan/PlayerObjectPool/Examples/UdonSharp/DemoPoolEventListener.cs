﻿
using System;
using JetBrains.Annotations;
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

namespace Cyan.PlayerObjectPool
{
    public class DemoPoolEventListener : UdonSharpBehaviour
    {
        public CyanPlayerObjectAssigner objectPool;
        private DemoPooledObject _localPoolObject;
        
        void Start()
        {
            DisableInteractive = true;
        }

        public override void Interact()
        {
            // Prevent interacting before local pool object has been assigned
            if (_localPoolObject == null)
            {
                return;
            }
            
            _localPoolObject._IncreaseValue();
        }

        [PublicAPI]
        public void _OnLocalPlayerAssigned()
        {
            Debug.Log("The local player has been assigned an object from the pool!");
            
            // Get the local player's pool object so we can later perform operations on it.
            _localPoolObject = (DemoPooledObject)objectPool._GetPlayerPooledUdon(Networking.LocalPlayer);
            
            // Allow the user to interact with this object.
            DisableInteractive = false;
        }
        
        [PublicAPI, NonSerialized] 
        public VRCPlayerApi playerAssignedPlayer;
        [PublicAPI, NonSerialized] 
        public int playerAssignedIndex;
        [PublicAPI, NonSerialized]
        public UdonBehaviour playerAssignedPoolObject;
        [PublicAPI]
        public void _OnPlayerAssigned()
        {
            Debug.Log($"Object {playerAssignedIndex} assigned to player {playerAssignedPlayer.displayName} {playerAssignedPlayer.playerId}");
        }
        
        [PublicAPI, NonSerialized]
        public VRCPlayerApi playerUnassignedPlayer;
        [PublicAPI, NonSerialized] 
        public int playerUnassignedIndex;
        [PublicAPI, NonSerialized]
        public UdonBehaviour playerUnassignedPoolObject;
        [PublicAPI]
        public void _OnPlayerUnassigned()
        {
            Debug.Log($"Object {playerUnassignedIndex} unassigned from player {playerUnassignedPlayer.displayName} {playerUnassignedPlayer.playerId}");
        }
    }
}