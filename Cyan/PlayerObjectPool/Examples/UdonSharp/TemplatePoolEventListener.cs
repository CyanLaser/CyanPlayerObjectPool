
using System;
using JetBrains.Annotations;
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

[AddComponentMenu("")]
public class TemplatePoolEventListener : UdonSharpBehaviour
{
    // This event is called when the local player's pool object has been assigned.
    [PublicAPI]
    public void _OnLocalPlayerAssigned()
    {
        
    }
    
    // This event is called when any player is assigned a pool object.
    // The variables will be set before the event is called.
    [PublicAPI, NonSerialized]
    public VRCPlayerApi playerAssignedPlayer;
    [PublicAPI, NonSerialized] 
    public int playerAssignedIndex;
    [PublicAPI, NonSerialized]
    public UdonBehaviour playerAssignedPoolObject;
    [PublicAPI]
    public void _OnPlayerAssigned()
    {
        
    }
    
    // This event is called when any player's object has been unassigned.
    // The variables will be set before the event is called.
    [PublicAPI, NonSerialized]
    public VRCPlayerApi playerUnassignedPlayer;
    [PublicAPI, NonSerialized] 
    public int playerUnassignedIndex;
    [PublicAPI, NonSerialized]
    public UdonBehaviour playerUnassignedPoolObject;
    [PublicAPI]
    public void _OnPlayerUnassigned()
    {
        
    }
}
