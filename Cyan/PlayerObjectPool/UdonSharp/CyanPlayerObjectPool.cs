using System;
using JetBrains.Annotations;
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using VRC.Udon.Common.Enums;

namespace Cyan.PlayerObjectPool
{
    /// <summary>
    /// An object pool system that will assign one object for each player that joins the instance. 
    /// </summary>
    [AddComponentMenu("")] // Do not show this component in the AddComponent menu since it is for Udon only.
    [DefaultExecutionOrder(1000)] // Have high execution order to ensure that other components update before this one.
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)] // This system requires manual sync. 
    public class CyanPlayerObjectPool : UdonSharpBehaviour
    {
        #region Constants

        /// <summary>
        /// Event name that will be sent to the pool event listener when any changes happen to the object assignments.
        /// </summary>
        [PublicAPI]
        public const string OnAssignmentChangedEvent = "_OnAssignmentChanged";
        /// <summary>
        /// Event name that will be sent to the pool even listener when the local player is assigned an object.
        /// </summary>
        [PublicAPI]
        public const string OnLocalPlayerAssignedEvent = "_OnLocalPlayerAssigned";

        
        // If player capacity is increased, then this should be changed. Real max is 82
        private const int MaxPlayers = 100;
        // Generic tag value for verifying if something is valid. Used for verifying object owners and if player object
        // index has been set.
        private const string TagValid = "True";
        // The tag used to store per object, the player that owns it. This is used in verifying if every assigned object
        // still has its player in the room as well as if every player has properly assigned objects. This is a caching
        // system to improve runtime.
        private const string PlayerPoolOwnerTagPrefix = "_player_pool_owner_id_";
        // The tag prefix used to store each player's object index. The player id will be appended to the end and the
        // player's assigned object will stored with this tag. This is a caching system used to bring lookups to
        // constant time.
        private const string PlayerObjectIndexTagPrefix = "_player_object_index_";
        // Check for this value to ensure that player object index tags are valid.
        // If this is ever unset, then another system cleared tags and needs to be recalculated. 
        private const string PlayerObjectIndexSet = "_player_object_index_valid";

        #endregion

        #region Public Settings
        
        /// <summary>
        /// When assigning objects to players, should the assigned player also take ownership over this object? If your
        /// pool objects do not use Synced variables, then this option can be considered for less network overhead.
        /// Default value is True. 
        /// </summary>
        public bool setNetworkOwnershipForPoolObjects = true;

        /// <summary>
        /// Setting this to true will print debug logging information about the status of the pool.
        /// Warnings and errors will still be printed even if this is set to false.
        /// </summary>
        public bool printDebugLogs = false;

        // UdonBehaviour that will listen for different events from the Object pool system. 
        // Current events:
        // - _OnAssignmentChanged
        // - _OnLocalPlayerAssigned
        public UdonBehaviour poolEventListener;

        #endregion

        // The assignment of objects to players. Each index represents an object in the pool. The value stores the
        // player that owns this object, or -1 if no one has been assigned this object.
        [UdonSynced]
        private int[] _assignment = new int[0];
        private int[] _prevAssignment = new int[0];

        // Array of UdonBehaviours that are in the pool. Type is Component so that it can be properly casted to either
        // UdonBehaviour or custom UdonSharpBehaviour while still having valid type checking.
        [HideInInspector]
        public Component[] pooledUdon;

        // The list of GameObjects in the pool. 
        private GameObject[] _poolObjects;
        
        // Cached is master value. This is used to determine if the local player becomes the new master and thus should
        // verify all pooled objects.
        private bool _isMaster = false;
        // Cache the local player variable. Mainly used for player tag based caching.
        private VRCPlayerApi _localPlayer;

        // Used to check if we have requested to check for updates on assignment. Only one request can happen at a time.
        private bool _delayUpdateAssignment;

        // Temporary data used for verifying each player's object.
        private VRCPlayerApi[] _allPlayersTemp;
        private int[] _playerIdsWithObjects;


        #region Public API

        /// <summary>
        /// Given a player, get the GameObject that has been assigned to this player.
        /// </summary>
        /// <param name="player"></param>
        /// <returns>
        /// If the player is valid and has been assigned an object, that GameObject will be returned.
        /// Otherwise null will be returned.
        /// </returns>
        [PublicAPI]
        public GameObject _GetPlayerPooledObject(VRCPlayerApi player)
        {
            return Utilities.IsValid(player) ? _GetPlayerPooledObjectById(player.playerId) : null;
        }
        
        /// <summary>
        /// Given a player id, get the GameObject that has been assigned to this player.
        /// </summary>
        /// <param name="playerId"></param>
        /// <returns>
        /// If the player id has been assigned an object, that GameObject will be returned.
        /// Otherwise null will be returned.
        /// </returns>
        [PublicAPI]
        public GameObject _GetPlayerPooledObjectById(int playerId)
        {
            int index = _GetPlayerPooledIndexById(playerId);

            if (index == -1)
            {
                _LogWarning("Could not find object for player: {playerId}");
                return null;
            }

            return _poolObjects[index];
        }

        /// <summary>
        /// Given a player, get the UdonBehaviour that has been assigned to this player.
        /// </summary>
        /// <param name="player"></param>
        /// <returns>
        /// If the player is valid and has been assigned an object, the UdonBehaviour on the assigned GameObject will be
        /// returned as a Component. Component is used so that you may validly cast it to either UdonBehaviour or a
        /// custom UdonSharpBehaviour and have proper type checking.
        /// If no object is assigned to the given player id, null will be returned.
        /// </returns>
        [PublicAPI]
        public Component _GetPlayerPooledUdon(VRCPlayerApi player)
        {
            return Utilities.IsValid(player) ? _GetPlayerPooledUdonById(player.playerId) : null;
        }

        /// <summary>
        /// Given a player id, get the UdonBehaviour that has been assigned to this player.
        /// </summary>
        /// <param name="playerId"></param>
        /// <returns>
        /// If the player id has been assigned an object, the UdonBehaviour on the assigned GameObject will be returned
        /// as a Component. Component is used so that you may validly cast it to either UdonBehaviour or a custom
        /// UdonSharpBehaviour and have proper type checking.
        /// If no object is assigned to the given player id, null will be returned.
        /// </returns>
        [PublicAPI]
        public Component _GetPlayerPooledUdonById(int playerId)
        {
            int index = _GetPlayerPooledIndexById(playerId);

            if (index == -1)
            {
                _LogWarning($"Could not find object for player: {playerId}");
                return null;
            }

            return pooledUdon[index];
        }
        
        
        /// <summary>
        /// These methods and variables are used so that Graph and CyanTrigger programs can call the public api methods
        /// with parameters and read the output data.
        /// </summary>
        #region Public API for Graph and CyanTrigger programs

        [HideInInspector, PublicAPI] 
        public VRCPlayerApi playerInput;
        
        [HideInInspector, PublicAPI] 
        public int playerIdInput;
        
        [HideInInspector, PublicAPI]
        public GameObject poolObjectOutput;
        
        [HideInInspector, PublicAPI] 
        public UdonBehaviour poolUdonOutput;
        
        [PublicAPI]
        [Obsolete("This method is intended only for non UdonSharp programs. Use _GetPlayerPoolObject instead.")]
        public void _GetPlayerPoolObjectEvent()
        {
            poolObjectOutput = _GetPlayerPooledObject(playerInput);
        }
        
        [PublicAPI]
        [Obsolete("This method is intended only for non UdonSharp programs. Use _GetPlayerPooledObjectById instead.")]
        public void _GetPlayerPooledObjectByIdEvent()
        {
            poolObjectOutput = _GetPlayerPooledObjectById(playerIdInput);
        }

        [PublicAPI]
        [Obsolete("This method is intended only for non UdonSharp programs. Use _GetPlayerPooledUdon instead.")]
        public void _GetPlayerPooledUdonEvent()
        {
            poolUdonOutput = (UdonBehaviour)_GetPlayerPooledUdon(playerInput);
        }

        [PublicAPI]
        [Obsolete("This method is intended only for non UdonSharp programs. Use _GetPlayerPooledUdonById instead.")]
        public void _GetPlayerPooledUdonByIdEvent()
        {
            poolUdonOutput = (UdonBehaviour)_GetPlayerPooledUdonById(playerIdInput);
        }

        #endregion
        
        #endregion
        
        private void Start()
        {
            _localPlayer = Networking.LocalPlayer;

            // Initialize the pool arrays
            int size = transform.childCount;
            _poolObjects = new GameObject[size];
            pooledUdon = new Component[size];
            _prevAssignment = new int[size];
            
            _Log($"Initializing pool with {size} objects. Please make sure there are enough objects " +
                 $"to cover two times the world player cap.");

            // Go through and get the pool objects. 
            for (int i = 0; i < size; ++i)
            {
                Transform child = transform.GetChild(i);
                GameObject poolObj = child.gameObject;
                _poolObjects[i] = poolObj;
                pooledUdon[i] = poolObj.GetComponent(typeof(UdonBehaviour));
                poolObj.SetActive(false);

                _prevAssignment[i] = -1;
            }
            
            // Initialize the assignment on master. Nothing should be assigned at this point.
            if (Networking.IsMaster)
            {
                _assignment = new int[size];
                for (int i = 0; i < size; ++i)
                {
                    _assignment[i] = -1;
                }
                _isMaster = true;
                
                RequestSerialization();
                _DelayUpdateAssignment();
            }

            // Initialize temp arrays to not need to recreate them every time.
            _allPlayersTemp = new VRCPlayerApi[MaxPlayers];
            _playerIdsWithObjects = new int[MaxPlayers];
            
            // Initialize caching system to say that player object index values have been set.
            // If everything is valid, checking for assignments will automatically update the cache system.
            _localPlayer.SetPlayerTag(PlayerObjectIndexSet, TagValid);
        }

        #region VRChat callbacks

        // If any player tries to request ownership, reject the request since this should only be owned by the master.
        // This code makes multiple assumptions that rely on players only gaining ownership when the previous owner left
        // and that it will never gain ownership more than once.
        public override bool OnOwnershipRequest(VRCPlayerApi requestingPlayer, VRCPlayerApi requestedOwner)
        {
            return false;
        }

        // On player join, assign that player an object in the pool.
        // Only master handles this request.
        // O(n) See _AssignObject for more details
        public override void OnPlayerJoined(VRCPlayerApi player)
        {
            if (!Networking.IsMaster)
            {
                return;
            }
            
            _AssignObject(player);
        }

        // On player left, assign find the object assigned to that player and return it back to the pool.
        // Only master handles this request.
        // If the local player was not master before, but is now master due to previous master leaving, verify all
        // player/object assignments.
        // O(1) best case. See _ReturnPlayerObject for more details
        public override void OnPlayerLeft(VRCPlayerApi player)
        {
            // Have everyone clean up the object locally to ensure owner is properly set for the object before it is
            // eventually disabled.
            _EarlyObjectCleanup(player);
            
            if (!Networking.IsMaster)
            {
                return;
            }

            _ReturnPlayerObject(player);

            // Master left and local player is the new master.
            if (!_isMaster && Networking.IsMaster)
            {
                _isMaster = true;
                
                // verify all players have objects, but wait one frame to ensure that all players have left.
                SendCustomEventDelayedFrames(nameof(_VerifyAllPlayersHaveObjects), 1, EventTiming.LateUpdate);
            }
        }

        // Synced data has changed, check for changes in the player/object assignments.
        public override void OnDeserialization()
        {
            // Calling delay update to ensure that the update itself does not happen on the same frame as
            // OnDeserialization. Some vrchat api's do not work appropriately, such as VRCPlayerApi.Teleport
            _DelayUpdateAssignment();
        }

        #endregion

        #region Player Tag Caching System
        
        // Given a player id, get the object assigned to the player. 
        // Player ids and object indices are cached using player tags.
        // O(1) best case, O(n) if tags were not set. 
        private int _GetPlayerPooledIndexById(int playerId)
        {
            // Tags have not been set up. This should only happen if another prefab called ClearPlayerTags.
            // O(n)
            if (_localPlayer.GetPlayerTag(PlayerObjectIndexSet) != TagValid)
            {
                _SetPlayerIndexTags();
            }
            
            string playerTag = _GetPlayerObjectIndexTag(playerId);
            string tagValue = _localPlayer.GetPlayerTag(playerTag);
            
            // UdonSharp does not support inline variable declarations.
            int results;
            if (!int.TryParse(tagValue, out results))
            {
                return -1;
            }

            return results;
        }

        // Go through each object assignment and cache the player id with the assigned object index. 
        // O(n)
        private void _SetPlayerIndexTags()
        {
            _LogWarning("Caching all player object index values. Please verify nothing calls " +
                        "VRCPlayerApi.ClearPlayerTags!");
            int size = _assignment.Length;
            for (int index = 0; index < size; ++index)
            {
                int ownerId = _assignment[index];
                if (ownerId == -1)
                {
                    continue;
                }

                _SetPlayerObjectIndexTag(ownerId, index);
            }

            _localPlayer.SetPlayerTag(PlayerObjectIndexSet, TagValid);
        }

        // Given the player id, return the proper tag to store the player's object index.
        private string _GetPlayerObjectIndexTag(int playerId)
        {
            return PlayerObjectIndexTagPrefix + playerId;
        }

        // Given a player id and object index, cache the object index using tag system.
        private void _SetPlayerObjectIndexTag(int playerId, int index)
        {
            string playerTag = _GetPlayerObjectIndexTag(playerId);
            _localPlayer.SetPlayerTag(playerTag, index == -1 ? "" : index.ToString());
        }

        #endregion

        #region Player/Object Assigment
        
        // Go through all objects, find the first one that is unclaimed and return the index.
        // If all objects are taken, -1 is returned.
        // O(n)
        // TODO optimize to use a queue of free objects to bring runtime to O(1).
        // TODO Recreate the queue on master change to ensure new master does not have bad data.
        private int _GetFirstUnclaimedObjectIndex()
        {
            int size = _assignment.Length;
            for (int i = 0; i < size; ++i)
            {
                if (_assignment[i] == -1)
                {
                    return i;
                }
            }

            return -1;
        }
        
        // Given a player, assign them an object from the pool.
        // O(n) - Need to loop through all assigned objects to find one free object.
        private void _AssignObject(VRCPlayerApi player)
        {
            int id = player.playerId;
            int index = _GetFirstUnclaimedObjectIndex();
            
            // Pool is empty and could not find an object to assign to the new player.
            // This should be fatal as the owner didn't create enough pool objects.
            if (index == -1)
            {
                _LogError($"Not enough objects to assign to new player! player: {id}");
                return;
            }
            
            // This case shouldn't happen based on how getting an index works, but still logging just in case.
            if (_assignment[index] != -1)
            {
                _LogWarning("Assigning player to an object but other player was already assigned the object!" +
                                 $" prev: {_assignment[index]}, new: {id}");
            }
            
            GameObject obj = _poolObjects[index];
            if (obj.activeSelf)
            {
                _LogWarning($"Assigning player to an active object! player: {id}, obj: {obj.name}");
            }
            
            _assignment[index] = id;
            _Log($"Assigning player {id} to index {index}");
            
            RequestSerialization();
            _DelayUpdateAssignment();
        }

        // Given a player, find their assigned object and return it back to the pool.
        // O(1) best case, O(n) assuming another prefab cleared tags.
        private void _ReturnPlayerObject(VRCPlayerApi player)
        {
            if (!Utilities.IsValid(player))
            {
                _LogWarning("Cannot return player object as player is invalid!");
                return;
            }
            _ReturnPlayerObjectByPlayerId(player.playerId);
        }
        
        // Given a player, find their assigned object and return it back to the pool.
        // O(1) best case, O(n) assuming another prefab cleared tags.
        private void _ReturnPlayerObjectByPlayerId(int playerId)
        {
            int index = _GetPlayerPooledIndexById(playerId);

            if (index == -1)
            {
                _LogWarning($"Could not return object for player: {playerId}");
                return;
            }
            
            if (_assignment[index] != playerId)
            {
                _LogWarning("Removing assignment to object that wasn't to specified player!" +
                                 $" assignment: {_assignment[index]}, player: {playerId}");
            }
            _assignment[index] = -1;
            
            RequestSerialization();
            _DelayUpdateAssignment();
        }
        
        // Check assignment changes in the next frame. This method is used to ensure that only one assignment update
        // check happens per frame. This should be the main method called to verify changes to assignments.
        private void _DelayUpdateAssignment()
        {
            // Update assignment check already requested, Ignore new request.
            if (_delayUpdateAssignment)
            {
                return;
            }

            _delayUpdateAssignment = true;
            SendCustomEventDelayedFrames(nameof(_OnDelayUpdateAssignment), 1);
        }

        // Check assignment changes if requested.
        // O(n) See _OnAssignmentChanged for more details.
        // public needed for SendCustomEventDelayedFrames, but should not be called externally!
        public void _OnDelayUpdateAssignment()
        {
            // Update assignment check not requested, Ignore assignment check.
            if (!_delayUpdateAssignment)
            {
                return;
            }
            
            _delayUpdateAssignment = false;
            _OnAssignmentChanged();
        }
        
        private void _OnAssignmentChanged()
        {
            _AssignObjectsToPlayers();

            if (printDebugLogs)
            {
                _PrintAssignment();
            }
        }

        // Go through the assigment array and verify changes since last assignment. 
        // O(n)
        private void _AssignObjectsToPlayers()
        {
            bool assignmentHasChanged = false;
            bool requireVerification = false;
            int size = _assignment.Length;
            for (int index = 0; index < size; ++index)
            {
                int newAssign = _assignment[index];
                int prevAssign = _prevAssignment[index];
                
                // No difference between new and previous assignment. Skip this index.
                if (newAssign == prevAssign)
                {
                    continue;
                }
                
                // Update the previous assignment to match new assignment.
                _prevAssignment[index] = newAssign;

                assignmentHasChanged = true;
                
                // New assignment has no owner. Disable the object and removed cached player owner. 
                if (newAssign == -1)
                {
                    _CleanupPlayerObject(index, prevAssign);
                }
                // New assignment is an owner. Assign the object to that player.
                else
                {
                    if (!_SetupPlayerObject(index, newAssign))
                    {
                        requireVerification = true;
                    }
                }
            }

            // Notify event listener that there was a change in the player/object assignments
            if (assignmentHasChanged && Utilities.IsValid(poolEventListener))
            {
                poolEventListener.SendCustomEvent(OnAssignmentChangedEvent);
            }

            if (requireVerification && Networking.IsMaster)
            {
                _VerifyAllPlayersHaveObjects();
            }
        }

        // Set up an object after it has been assigned to a player. This sets the "Owner" variable on the object and
        // sends the _OnOwnerSet event to allow the object to initialize itself.
        private bool _SetupPlayerObject(int index, int playerId)
        {
            VRCPlayerApi player = VRCPlayerApi.GetPlayerById(playerId);
            GameObject poolObj = _poolObjects[index];
            
            // Ensure this gets set even if player is invalid. The cleanup case should verify and remove the player later.
            _SetPlayerObjectIndexTag(playerId, index);
            
            if (!Utilities.IsValid(player))
            {
                _LogWarning($"Trying to assign invalid player to object! player: {playerId}, obj: {poolObj.name}");
                return false;
            }
            
            _Log($"Assigning {poolObj.name} to player {playerId}");

            if (player.isLocal && setNetworkOwnershipForPoolObjects)
            {
                Networking.SetOwner(player, poolObj);
            }

            UdonBehaviour poolUdon = (UdonBehaviour)pooledUdon[index];
            poolUdon.SetProgramVariable("Owner", player);
            poolUdon.SendCustomEvent("_OnOwnerSet");
            poolObj.SetActive(true);
                    
            // Notify the event listener that the local player has been assigned an object.
            if (player.isLocal && Utilities.IsValid(poolEventListener))
            {
                poolEventListener.SendCustomEvent(OnLocalPlayerAssignedEvent);
            }

            return true;
        }
        
        // Once an object has been unassigned, clean up the object by removing the cached index and disable the object.
        private void _CleanupPlayerObject(int index, int playerId)
        {
            GameObject poolObj = _poolObjects[index];
            UdonBehaviour poolUdon = (UdonBehaviour)pooledUdon[index];
            _SetPlayerObjectIndexTag(playerId, -1);
                    
            _Log($"Cleaning up obj {poolObj.name}");
                    
            poolUdon.SetProgramVariable("Owner", null);
            poolObj.SetActive(false);
        }

        // Called by everyone when any player leaves the instance. This is to clear out the Owner variable and give an
        // early callback that pool objects can implement. The object itself will be disabled later once assignments
        // have been updated.
        private void _EarlyObjectCleanup(VRCPlayerApi player)
        {
            int playerId = player.playerId;
            int index = _GetPlayerPooledIndexById(playerId);
            if (index == -1)
            {
                _LogWarning($"Could not find object for leaving player: {playerId}");
                return;
            }
            
            UdonBehaviour poolUdon = (UdonBehaviour)pooledUdon[index];
            poolUdon.SendCustomEvent("_OnCleanup");
            poolUdon.SetProgramVariable("Owner", null);
        }

        // Debug method used to print the current player/object assignments
        private void _PrintAssignment()
        {
            string assignment = "Player Assignments: ";
            int size = _assignment.Length;
            for (int i = 0; i < size; ++i)
            {
                if (_assignment[i] != -1)
                {
                    assignment += "[Player: " + _assignment[i] + ", object: " + i + "], ";
                }
            }
            
            _Log(assignment);
        }
        
        
        // Go through all assignments and all players and ensure that each player still has
        // an object and each object has a player.
        // O(n)
        // public needed for SendCustomEventDelayedFrames, but should not be called externally!
        public void _VerifyAllPlayersHaveObjects()
        {
            if (!Networking.IsMaster)
            {
                return;
            }
            
            _Log("Verifying all players have an object and all objects have a player.");

            // Go through all assignments in the pool and find their assigned owner.
            // Put owner ids into a separate array as the assignment array will be modified.
            int count = 0;
            int size = _assignment.Length;
            for (int index = 0; index < size; ++index)
            {
                int ownerId = _assignment[index];
                if (ownerId == -1)
                {
                    continue;
                }
                
                _playerIdsWithObjects[count] = ownerId;
                ++count;
                
                // Set tags for used object owners
                // This will give us a constant look up if a user has an assigned object.
                _localPlayer.SetPlayerTag(PlayerPoolOwnerTagPrefix + ownerId, TagValid);
            }


            // Go through each player and find if that player has an object, otherwise assign them one.
            VRCPlayerApi.GetPlayers(_allPlayersTemp);
            for (int index = 0; index < MaxPlayers; ++index)
            {
                VRCPlayerApi player = _allPlayersTemp[index];
                if (!Utilities.IsValid(player))
                {
                    break;
                }
                
                int id = player.playerId;
                string tagName = PlayerPoolOwnerTagPrefix + id;
                string tagValue = _localPlayer.GetPlayerTag(tagName);
                
                // Remove the tag to check later if tag exists from assignments to know player is not in the room.
                _localPlayer.SetPlayerTag(tagName, "");
                
                // Tag is valid. No need to do anything for this player.
                if (tagValue == TagValid)
                {
                    continue;
                }
                
                // Player did not have tag, meaning they did not have an object. Assign them one.
                _LogWarning($"Player did not have an object during verification! Player: {id}");
                _AssignObject(player);
            }
            
            // Go through each object again and verify if the assigned player is still in the instance.
            // Clear used tags to prevent issues for next iteration of this verification method.
            // We cannot directly clear all tags due to other caching mechanics used.
            for (int index = 0; index < count; ++index)
            {
                int ownerId = _playerIdsWithObjects[index];
                if (ownerId == int.MaxValue)
                {
                    break;
                }

                string tagName = PlayerPoolOwnerTagPrefix + ownerId;
                string tagValue = _localPlayer.GetPlayerTag(tagName);
                _localPlayer.SetPlayerTag(tagName, "");
                
                // If tag exists, then this player is no longer in the instance and needs to be removed from the
                // assignment array.
                if (tagValue == TagValid)
                {
                    _LogWarning($"Missing player still owned an object during verification! Player: {ownerId}");
                    _ReturnPlayerObjectByPlayerId(ownerId);
                }
            }
        }
        
        #endregion

        #region Logging

        private const string LogPrefix = "[Cyan][PlayerPool]";
        private void _Log(string message)
        {
            if (printDebugLogs)
            {
                Debug.Log($"{LogPrefix} {message}");
            }
        }

        private void _LogWarning(string message)
        {
            Debug.LogWarning($"{LogPrefix} {message}");
        }
        
        private void _LogError(string message)
        {
            Debug.LogError($"{LogPrefix} {message}");
        }

        #endregion
    }
}

