
using JetBrains.Annotations;
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDKBase;

namespace Cyan.PlayerObjectPool
{
    public class DemoPooledObject : UdonSharpBehaviour
    {
        // Who is the current owner of this object. Null if object is not currently in use. 
        [PublicAPI, HideInInspector]
        public VRCPlayerApi Owner;
        
        public Text text;
        
        [UdonSynced]
        public int value = -1;
        private int _prevValue = -1;
        
        private VRCPlayerApi _localPlayer;

        private void Start()
        {
            _localPlayer = Networking.LocalPlayer;
        }

        [PublicAPI]
        public void _OnOwnerSet()
        {
            // Initialize the object here
            if (Owner.isLocal)
            {
                _SetValue(Random.Range(0, 100));
            }
        }

        [PublicAPI]
        public void _OnCleanup()
        {
            // Cleanup the object here
            if (Networking.IsMaster) 
            {
                _SetValue(-1);
            }
            transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        }

        public override void OnDeserialization()
        {
            _OnValueChanged();
        }
        
        [PublicAPI]
        public void _SetValue(int newValue)
        {
            value = newValue;
            RequestSerialization();
            _OnValueChanged();
        }
        
        private void _OnValueChanged()
        {
            if (_prevValue == value)
            {
                return;
            }

            _prevValue = value;

            _UpdateDebugDisplay();
        }

        private void _UpdateDebugDisplay()
        {
            string ownerName = Utilities.IsValid(Owner) ? Owner.displayName + " " + Owner.playerId : "Invalid";
            text.text = ownerName + "\n" + value;
        }

        private void Update()
        {
            if (!Utilities.IsValid(Owner) || value == -1)
            {
                return;
            }

            Vector3 pos = Owner.GetPosition();
            transform.position = pos + Vector3.up * 2;

            pos = _localPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.Head).position;
            transform.LookAt(pos);

            if (Owner.isLocal && Input.GetKeyDown(KeyCode.Alpha0))
            {
                _SetValue((value + 1) % 100);
            }
        }
    }
}