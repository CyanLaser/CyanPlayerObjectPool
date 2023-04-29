
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

namespace Cyan.PlayerObjectPool
{
    public class DemoPoolEventListener : CyanPlayerObjectPoolEventListener
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

        public override void _OnLocalPlayerAssigned()
        {
            Debug.Log("The local player has been assigned an object from the pool!");
            
            // Get the local player's pool object so we can later perform operations on it.
            _localPoolObject = (DemoPooledObject)objectPool._GetPlayerPooledUdon(Networking.LocalPlayer);
            
            // Allow the user to interact with this object.
            DisableInteractive = false;
        }

        public override void _OnPlayerAssigned(VRCPlayerApi player, int poolIndex, UdonBehaviour poolObject)
        {
            Debug.Log($"Object {poolIndex} assigned to player {player.displayName} {player.playerId}");
        }

        public override void _OnPlayerUnassigned(VRCPlayerApi player, int poolIndex, UdonBehaviour poolObject)
        {
            Debug.Log($"Object {poolIndex} unassigned from player {player.displayName} {player.playerId}");
        }
    }
}