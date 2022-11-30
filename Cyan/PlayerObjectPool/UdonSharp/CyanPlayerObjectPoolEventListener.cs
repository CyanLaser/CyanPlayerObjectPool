using UdonSharp;
using VRC.SDKBase;
using VRC.Udon;

namespace Cyan.PlayerObjectPool
{
    public abstract class CyanPlayerObjectPoolEventListener : UdonSharpBehaviour
    {
        // This event is called when the local player's pool object has been assigned.
        public abstract void _OnLocalPlayerAssigned();
    
        // This event is called when any player is assigned a pool object.
        public abstract void _OnPlayerAssigned(VRCPlayerApi player, int poolIndex, UdonBehaviour poolObject);
    
        // This event is called when any player's object has been unassigned.
        public abstract void _OnPlayerUnassigned(VRCPlayerApi player, int poolIndex, UdonBehaviour poolObject);
    }
}