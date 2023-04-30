using JetBrains.Annotations;
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDKBase;
using Random = UnityEngine.Random;

namespace Cyan.PlayerObjectPool
{
    [AddComponentMenu("")]
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class DemoPooledObject : CyanPlayerObjectPoolObject
    {
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
        public override void _OnOwnerSet()
        {
            // Initialize the object here
            if (Owner.isLocal)
            {
                _SetValue(Random.Range(0, 100));
            }
            
            // After assigning the owner, have everyone act as if the value has changed, as the owner has.
            // This is needed as an edge case when the synced value of this pool object is changed BEFORE
            // the owner has been assigned. See OnDeserialization as well.
            _UpdateDebugDisplay();
        }

        [PublicAPI]
        public override void _OnCleanup()
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
            // OnDeserialization happens when synced variable data has been changed. Note that this method may happen
            // before the owner has been assigned to this object. Be sure to check for that case and handle initial
            // variable updates both in OnDeserialization and in _OnOwnerSet.
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
            string ownerName = Utilities.IsValid(Owner) ? $"{Owner.displayName} {Owner.playerId}" : "Invalid";
            text.text = $"{ownerName}\n{value}";
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
        }

        public void _IncreaseValue()
        {
            if (Owner.isLocal)
            {
                _SetValue((value + 1) % 100);
            }
        }
    }
}