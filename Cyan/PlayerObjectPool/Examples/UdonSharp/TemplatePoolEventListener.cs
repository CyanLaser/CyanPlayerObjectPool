
using JetBrains.Annotations;
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

[AddComponentMenu("")]
public class TemplatePoolEventListener : UdonSharpBehaviour
{
    // This event is called when any object is assigned to a player or unassigned from a player.
    [PublicAPI]
    public void _OnAssignmentChanged()
    {
        
    }

    // This event is called when the local player's pool object has been assigned.
    [PublicAPI]
    public void _OnLocalPlayerAssigned()
    {
        
    }
    
    // This event is called when any player is assigned a pool object.
    // The variables will be set before the event is called.
    [PublicAPI, HideInInspector]
    public int playerAssignedId;
    [PublicAPI, HideInInspector]
    public VRCPlayerApi playerAssignedPlayer;
    [PublicAPI, HideInInspector]
    public UdonBehaviour playerAssignedPoolObject;
    [PublicAPI]
    public void _OnPlayerAssigned()
    {
        
    }
    
    // This event is called when any player's object has been unassigned.
    // The variables will be set before the event is called.
    [PublicAPI, HideInInspector]
    public int playerUnassignedId;
    [PublicAPI, HideInInspector]
    public VRCPlayerApi playerUnassignedPlayer;
    [PublicAPI, HideInInspector]
    public UdonBehaviour playerUnassignedPoolObject;
    [PublicAPI]
    public void _OnPlayerUnassigned()
    {
        
    }
}
