
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
    }
}