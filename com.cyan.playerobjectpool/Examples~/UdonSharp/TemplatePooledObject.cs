using Cyan.PlayerObjectPool;
using JetBrains.Annotations;
using UnityEngine;

[AddComponentMenu("")]
public class TemplatePooledObject : CyanPlayerObjectPoolObject
{
    // This method will be called on all clients when the object is enabled and the Owner has been assigned.
    [PublicAPI]
    public override void _OnOwnerSet()
    {
        // Initialize the object here
    }

    // This method will be called on all clients when the original owner has left and the object is about to be disabled.
    [PublicAPI]
    public override void _OnCleanup()
    {
        // Cleanup the object here
    }
}
