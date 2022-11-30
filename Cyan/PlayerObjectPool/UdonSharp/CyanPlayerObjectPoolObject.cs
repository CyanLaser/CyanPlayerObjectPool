using System;
using UdonSharp;
using VRC.SDKBase;

namespace Cyan.PlayerObjectPool
{
    public abstract class CyanPlayerObjectPoolObject : UdonSharpBehaviour
    {
        // Who is the current owner of this object. Null if object is not currently in use. 
        [NonSerialized]
        public VRCPlayerApi Owner;
    
        // This method will be called on all clients when the object is enabled and the Owner has been assigned.
        public abstract void _OnOwnerSet();

        // This method will be called on all clients when the original owner has left and the object is about to be disabled.
        public abstract void _OnCleanup();
    }
}