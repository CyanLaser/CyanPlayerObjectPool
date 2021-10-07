
using JetBrains.Annotations;
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

namespace Cyan.PlayerObjectPool
{
    public class DemoPoolEventListener : UdonSharpBehaviour
    {
        public CyanPlayerObjectPool objectPool;
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
        public void _OnAssignmentChanged()
        {
            Debug.Log("Object assignments have changed. Either a player joined or a player left.");

            VRCPlayerApi[] players = objectPool._GetOrderedPlayers();
            Debug.Log("Printing players after assignment change:");
            foreach (var player in players)
            {
                Debug.Log(player.displayName);
            }
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
        
        [PublicAPI, HideInInspector]
        public int playerAssignedId;
        [PublicAPI, HideInInspector]
        public VRCPlayerApi playerAssignedPlayer;
        [PublicAPI, HideInInspector]
        public UdonBehaviour playerAssignedPoolObject;
        [PublicAPI]
        public void _OnPlayerAssigned()
        {
            Debug.Log("Object assigned to player " + playerAssignedPlayer.displayName +" " + playerAssignedId);
        }
        
        [PublicAPI, HideInInspector]
        public int playerUnassignedId;
        [PublicAPI, HideInInspector]
        public VRCPlayerApi playerUnassignedPlayer;
        [PublicAPI, HideInInspector]
        public UdonBehaviour playerUnassignedPoolObject;
        [PublicAPI]
        public void _OnPlayerUnassigned()
        {
            Debug.Log("Object unassigned from player " + playerUnassignedPlayer.displayName +" " + playerUnassignedId);
        }
    }
}