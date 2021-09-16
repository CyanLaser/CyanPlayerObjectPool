using JetBrains.Annotations;
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

[AddComponentMenu("")]
public class TemplatePooledObject : UdonSharpBehaviour
{
    // Who is the current owner of this object. Null if object is not currently in use. 
    [PublicAPI, HideInInspector]
    public VRCPlayerApi Owner;
    
    // This method will be called on all clients when the object is enabled and the Owner has been assigned.
    [PublicAPI]
    public void _OnOwnerSet()
    {
        // Initialize the object here
    }

    // This method will be called on all clients when the original owner has left and the object is about to be disabled.
    [PublicAPI]
    public void _OnCleanup()
    {
        // Cleanup the object here
    }
}
