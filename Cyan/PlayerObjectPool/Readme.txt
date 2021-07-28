CyanPlayerObjectPool
Created by CyanLaser#4695
2021-07-06

Assigns a unique object to every player in the world.


Dependencies
UdonSharp - https://github.com/Merlin-san/UdonSharp
While required to run, you may use UdonGraph or CyanTriggers to create your pooled object programs.


Setup

1. Drag the PlayerObjectPool prefab into your scene.
2. Create an udon program to be used for each pooled object, implementing the required items (see template graph or udon sharp script)
3. Create a new GameObject with this udon program, child it under the PlayerObjectPool prefab. Duplicate it so that there is enough for each players. It is recommended to have 2x the world cap.

See the example scenes for more details on proper setup.


Pooled Object Requirements

When creating an udon program to be used as a pooled object, it needs three things:
1. A public VRCPlayerApi variable named "Owner". This variable will store the current assigned owner for the object, or null if no owner has been assigned.
2. A public event named "_OnOwnerSet". This event will be called by everyone when the object is assigned a new owner.
3. A public event named "_OnCleanup". This event will be called by everyone when the object owner is leaving the world and the object is about to be unassigned. 


Pool Event Listener

The PlayerObjectPool can send optional events to a listener UdonBehaviour so that you can handle different callbacks. 
- _OnAssignmentChanged - This event is called whenever an object has been assigned to an owner or when an object has been unassigned. 
- _OnLocalPlayerAssigned - This event is called whenever the local player has been assigned their object. Use this event to enable external features that require the local player to have an assigned object. 


UdonSharp Helper Methods

The PlayerObjectPool script contains a few helper methods that can be used with UdonSharp (sorry graph users). 
These methods allow you to easily get the object or UdonBehaviour for a given player.

GameObject _GetPlayerPooledObject(VRCPlayerApi player)
GameObject _GetPlayerPooledObjectById(int playerId)
Component _GetPlayerPooledUdon(VRCPlayerApi player)
Component _GetPlayerPooledUdonById(int playerId)


Implementation Details

This pool system works by having master do all the work. On player join, master will assign this player a free object and on player leave, master will remove assignment of this object. Assignment is handled through a synced int array. Whenever this array changes, all clients will read through the array to find out the changes and set the object state accordingly. 
The script also implements various error handling in case master crashes before assigning or removing assignments for various players. 