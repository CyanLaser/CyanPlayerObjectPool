
using Cyan.PlayerObjectPool;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

[AddComponentMenu("")]
public class TemplatePoolEventListener : CyanPlayerObjectPoolEventListener
{
    // This event is called when the local player's pool object has been assigned.
    public override void _OnLocalPlayerAssigned()
    {
        
    }

    // This event is called when any player is assigned a pool object.
    public override void _OnPlayerAssigned(VRCPlayerApi player, int poolIndex, UdonBehaviour poolObject)
    {
        
    }

    // This event is called when any player's object has been unassigned.
    public override void _OnPlayerUnassigned(VRCPlayerApi player, int poolIndex, UdonBehaviour poolObject)
    {
        
    }
}
