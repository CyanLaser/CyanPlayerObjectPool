CyanPlayerObjectPool
Created by CyanLaser#4695

Assigns a unique object to every player in the world.

- Can be used with all Udon Compilers! - Includes examples for UdonGraph, UdonSharp, CyanTrigger
- Network Race Condition Safe - Every player will get an object over time, even when multiple join at once.
- Master Crash Verification - On master change, all players and all objects are verified to ensure that each pair is still valid. Old objects will be cleaned up and new players will be assigned an object.
- Supports multiple object types - Prefabs created for this system can be used together without conflicting. 


--- General Use ---


Dependencies

UdonSharp - https://github.com/Merlin-san/UdonSharp
While UdonSharp is required to run, you may use UdonGraph or CyanTrigger to create your pooled object programs.


Setup

1. Drag the PlayerObjectPool and PlayerObjectAssigner prefabs into your scene.
2. In the PlayerObjectPool prefab, set the pool size to double the world capacity.
3. Create an Udon program to be used for each pooled object, implementing the required items (see template UdonGraph, CyanTrigger, or UdonSharp script)
4. Create a new GameObject with this Udon program, make it a prefab, and drag the prefab into the "Pool Object Prefab" field in the PlayerObjectAssigner prefab. This will automatically create enough instances of the pool object for the current pool size.

See the example scenes for more details on proper setup.


Pooled Object Requirements

When creating an Udon program to be used as a pooled object, it needs three things:
1. A public VRCPlayerApi variable named "Owner". This variable will store the current assigned owner for the object, or null if no owner has been assigned.
2. A public event named "_OnOwnerSet". This event will be called by everyone when the object is assigned a new owner.
3. A public event named "_OnCleanup". This event will be called by everyone when the object owner is leaving the world and the object is about to be unassigned. 


--- Advanced Use ---


Setup for Prefab Authors

1. Right click on the PlayerObjectAssigner prefab and create a new Prefab Variant.
2. Create your Pool Object prefab and set it in the Variant's "Pool Object Prefab" field.
3. (Optional) If your system depends on objects outside of the Pool Object (Pool Event Listeners), create a new prefab for your system, have the prefab variant as a child. Add other systems dependent on your pool system as children.
4. Create a Readme file for your prefab and tell users to drag your system prefab into the scene. When the prefab is added to the scene, it will auto link with the Object Pool system, or create a new one if one did not already exist. 


Pool Event Listener

The PlayerObjectAssigner can send optional events to a listener UdonBehaviour so that you can handle different callbacks. 
- _OnLocalPlayerAssigned - This event is called whenever the local player has been assigned their object. Use this event to enable external features that require the local player to have an assigned object. 
- _OnPlayerAssigned - This event is called when any player is assigned a pool object. When using this event, if the program has a public VRCPlayerAPI variable named "playerAssignedPlayer", a public int variable named "playerAssignedIndex", or a public UdonBehaviour variable named "playerAssignedPoolObject", it will be set before the event is called.
- _OnPlayerUnassigned - This event is called when any player's object has been unassigned. When using this event, if the program has a public VRCPlayerAPI variable named "playerUnassignedPlayer", a public int variable named "playerUnassignedIndex", or a public UdonBehaviour variable named "playerUnassignedPoolObject", it will be set before the event is called.


UdonGraph and CyanTrigger Helper Methods

The PlayerObjectAssigner script contains a few helper methods that can be used with UdonGraph and CyanTrigger. 
For these methods, you will need to use SetProgramVariable on the pool to set the required input and GetProgramVariable to get the output. See the example scenes for more details

_GetPlayerPooledObjectEvent
  Input: VRCPlayerApi "playerInput"
  Output: GameObject "poolObjectOutput"
  Description: Given a player, get the GameObject that has been assigned to this player.
  
_GetPlayerPooledObjectByIdEvent
  Input: int "playerIdInput"
  Output: GameObject "poolObjectOutput"
  Description: Given a player id, get the GameObject that has been assigned to this player.
  
_GetPlayerPooledUdonEvent
  Input: VRCPlayerApi "playerInput"
  Output: UdonBehaviour "poolUdonOutput"
  Description: Given a player, get the UdonBehaviour that has been assigned to this player.
  
_GetPlayerPooledUdonByIdEvent
  Input: int "playerIdInput"
  Output: UdonBehaviour "poolUdonOutput"
  Description: Given a player id, get the UdonBehaviour that has been assigned to this player.

_GetOrderedPlayersEvent
  Input: Nothing
  Output: VRCPlayer[] "playerArrayOutput"
  Description: Get an ordered list of players based on the pool's assignment. This list will be the same order for all clients and is useful for randomization.
  
_GetOrderedPlayersNoAllocEvent
  Input: VRCPlayer[] "playerArrayInput"
  Output: int "playerCountOutput"
  Description: Fill the input array in order with players based on the pool's assignment. The number of players will be stored in the output variable. This list will be the same order for all clients and is useful for randomization.
  
_GetActivePoolObjectsEvent
  Input: Nothing
  Output: Component[] "poolObjectArrayOutput"
  Description: Get an array of active pool objects based on the current assignments. This list will be the same order for all clients and is useful for randomization.
  
_GetActivePoolObjectsNoAllocEvent
  Input: Component[] "poolObjectArrayInput"
  Output: int "poolObjectCountOutput"
  Description: Fill the input array in order with active pool objects based on the current assignments. The number of pooled objects will be stored in the output variable. This list will be the same order for all clients and is useful for randomization.
  
_GetPlayerPoolIndexEvent
  Input: VRCPlayerApi "playerInput"
  Output: int "playerIndexOutput"
  Description: Given a player, get the pool index for the given player. The pool index will be a value between 0 and the total number of objects in the pool. This is useful since Player Ids will continue to increase with no cap as the instance is alive.
  
_GetPlayerPoolIndexByIdEvent
  Input: int "playerIdInput"
  Output: int "playerIndexOutput"
  Description: Given a player id, get the pool index for the given player. The pool index will be a value between 0 and the total number of objects in the pool. This is useful since Player Ids will continue to increase with no cap as the instance is alive.


UdonSharp Helper Methods

The PlayerObjectAssigner script contains a few helper methods that can be used with UdonSharp. 

GameObject _GetPlayerPooledObject(VRCPlayerApi player)
GameObject _GetPlayerPooledObjectById(int playerId)
Component _GetPlayerPooledUdon(VRCPlayerApi player)
Component _GetPlayerPooledUdonById(int playerId)
VRCPlayerApi[] _GetOrderedPlayers()
int _GetOrderedPlayersNoAlloc(VRCPlayerApi[] players)
Component[] _GetActivePoolObjects()
int _GetActivePoolObjectsNoAlloc(Component[] pooledObjects)
int _GetPlayerPoolIndex(VRCPlayerApi player)
int _GetPlayerPoolIndexById(int playerId)

Implementation Details

This pool system works by having master do all the work. On player join, master will assign this player a free object and on player leave, master will remove assignment of this object. Assignment is handled through a synced int array. Whenever this array changes, all clients will read through the array to find out the changes and set the object state accordingly. The script also implements various error handling in case master crashes before assigning or removing assignments for various players. 