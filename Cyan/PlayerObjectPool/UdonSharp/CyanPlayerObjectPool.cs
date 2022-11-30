using System;
using JetBrains.Annotations;
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon.Common;

namespace Cyan.PlayerObjectPool
{
    /// <summary>
    /// An object pool system that will assign an index for each player that joins the instance. 
    /// </summary>
    [AddComponentMenu("")] // Do not show this component in the AddComponent menu since it is for Udon only.
    [DefaultExecutionOrder(-1000)] // Have low execution order to ensure that this script updates before other behaviours
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)] // This system requires manual sync. 
    public class CyanPlayerObjectPool : UdonSharpBehaviour
    {
        #region Constants

        // Current pool version that will be printed at start. 
        private const string Version = "v1.1.0";
        
        // These constants affect the behaviour of the pool
        #region Constants

        // Any time comparisons should use this epsilon to verify differences, otherwise they may not fire when expected.
        private const float TimeCheckEpsilon = 0.1f;
        // The minimum duration between serialization requests to reduce overall network load when multiple people join
        // at the same time. 
        private const float DelaySerializationDuration = 1f;
        // How many times will the pool try to delay serialization while the network is clogged? After reaching this
        // number, the assignment will be forced to try serializing.
        private const int MaxCloggedDelayAttempts = 4;
        // The duration to wait to verify all pool indices after a player has become the new master. This delay is
        // needed to ensure that all players have joined/left since the master swap. It is possible that the previous
        // master assigned pool indices to players that have not yet joined on the new master's client. In this case, 
        // the new master should wait to assign indices to prevent reassignment for those players.
        private const float DelayNewMasterVerificationDuration = 1f;
        
        #endregion
        
        // Tag based constants. These values should be unique to not cause overlap with other systems and prefabs.
        #region Tag constants

        // This tag is used to set the path of this pool system. This allows objects to find this pool system without
        // having a direct reference. There should only be one pool system in the world.
        public const string PoolPathTag = "_cyan_object_pool_system_path";
        // Prefix for all tags to prevent player tag overlaps with other systems and prefabs.
        private const string PoolIdTagPrefix = "_cyan_object_pool_";
        // Generic tag value for verifying if something is valid. Used for verifying index owners and if player index
        // index has been set.
        private const string TagValid = "True";
        // The tag used to store per index, the player that owns it. This is used in verifying if every assigned index
        // still has its player in the room as well as if every player has properly assigned indices. This is a caching
        // system to improve runtime.
        private const string PlayerPoolOwnerTagPrefix = "_player_pool_owner_id_";
        // The tag prefix used to store each player's index. The player id will be appended to the end and the
        // player's assigned index will stored with this tag. This is a caching system used to bring lookups to
        // constant time.
        private const string PlayerIndexTagPrefix = "_player_index_";
        // Check for this value to ensure that player index tags are valid.
        // If this is ever unset, then another system cleared tags and needs to be recalculated. 
        private const string PlayerIndexSet = "_player_object_index_valid";

        #endregion
        
        #endregion

        #region Public Settings

        /// <summary>
        /// How large is the object pool? This number should be equal to (world player cap * 2 + 2). All object 
        /// assigners should also have this many pool objects to ensure each player gets an object.
        /// </summary>
        [Tooltip("How large is the object pool? This number should be equal to (world player cap * 2 + 2). All object assigners should also have this many pool objects to ensure each player gets an object.")]
        public int poolSize = 82;
        
        /// <summary>
        /// Setting this to true will print debug logging information about the status of the pool.
        /// Warnings and errors will still be printed even if this is set to false.
        /// </summary>
        [Tooltip("Setting this to true will print debug logging information about the status of the pool. Warnings and errors will still be printed even if this is set to false.")]
        public bool printDebugLogs = false;

        #endregion

        // The assignment of indices to players. The value stores the player that owns this index, or -1 if no one has
        // been assigned this index.
        [UdonSynced]
        private int[] _assignment = new int[0];
        private int[] _prevAssignment = new int[0];

        // The assignment array, storing the player apis directly. This is used to send player api data to even listener
        // as calling GetPlayerById returns null for players leaving the instance.
        private VRCPlayerApi[] _assignedPlayers = new VRCPlayerApi[0];
        
        // Cached is master value. This is used to determine if the local player becomes the new master and thus should
        // verify all pooled indices.
        private bool _isMaster = false;
        // Cache the local player variable. Mainly used for player tag based caching.
        private VRCPlayerApi _localPlayer;

        // Used to check if we have requested to check for updates on assignment. Only one request can happen at a time.
        private bool _delayUpdateAssignment;
        
        // The time of the last request to serialize the index pool assignments. This is used to ensure that only one
        // serialization request happens within a given duration. This is to help reduce overall networking load
        // when multiple people join at the same time.
        private float _lastSerializationRequestTime;
        // The count of how many attempts the pool has tried to serialize the assignments, but was prevented due to the
        // network being clogged. After so many attempts, serialization will happen anyway.
        private int _networkedCloggedSerializationAttempts = 0;

        // Temporary data used for verifying each player's index.
        private VRCPlayerApi[] _allPlayersTemp = new VRCPlayerApi[0];
        private int[] _playerIdsWithIndices = new int[0];
        private int[] _playerIndexIds = new int[0];

        // In rare cases, synced data can arrive before all player join messages have happened. In this case, an
        // assignment may be considered invalid and needs to be verified again when the player joins.
        private bool _verifyAssignmentsOnPlayerJoin = false;

        // Queue of indices indicating which pool indices have not been assigned. This system allows constant time
        // assigning indices to players instead of O(n) to find the first unassigned index.
        private int[] _unclaimedQueue;
        // The start index into the queue where items should be pulled from.
        // This may be larger than the queue length if many items have been cycled. 
        private int _unclaimedQueueStart = 0;
        // The end index into the queue where new items are added into the queue.
        // This may be larger than the queue length if many items have been cycled. 
        private int _unclaimedQueueEnd = 0;

        // Array to hold all assignment listeners which will update object assignments. This list will grow depending on
        // how many object assigners are in the world. 
        private CyanPlayerObjectAssigner[] _assignmentListeners;
        // Current number of object assigners to forward events to. 
        private int _listenersCount = 0;
        
        // SerializationResults cannot be created at runtime in Udon. Create at compile time to allow calling
        // OnPostSerialization during runtime.
        // This is only used in Editor, or when there is only one player in the instance.
        private readonly SerializationResult _trueSerializationResults = new SerializationResult(true, 0);


        #region Public API

        /// <summary>
        /// Given a player, get the pool index for the given player. The pool index will be a value between 0 and the
        /// total number of objects in the pool. This is useful since Player Ids will continue to increase with no cap
        /// as the instance is alive.
        /// </summary>
        /// <param name="player"></param>
        /// <returns>
        /// Returns the pool index for the current player.
        /// </returns>
        [PublicAPI]
        public int _GetPlayerPoolIndex(VRCPlayerApi player)
        {
            return VRC.SDKBase.Utilities.IsValid(player) ? _GetPlayerPooledIndexById(player.playerId) : -1;
        }

        /// <summary>
        /// Given a player id, get the pool index for the given player. The pool index will be a value between 0 and the
        /// total number of objects in the pool. This is useful since Player Ids will continue to increase with no cap
        /// as the instance is alive.
        /// </summary>
        /// <param name="playerId"></param>
        /// <returns>
        /// Returns the pool index for the current player.
        /// </returns>
        [PublicAPI]
        public int _GetPlayerPoolIndexById(int playerId)
        {
            return _GetPlayerPooledIndexById(playerId);
        }

        /// <summary>
        /// Get an ordered list of players based on the pool's assignment. This list will be the same order for all
        /// clients and is useful for randomization.
        /// O(n) to iterate over all players
        /// </summary>
        /// <returns>
        /// Returns an ordered array of players that have currently been assigned an index.
        /// </returns>
        [PublicAPI]
        public VRCPlayerApi[] _GetOrderedPlayers()
        {
            // Go through assignment array and find all valid players and store them into a temporary location.
            int count = 0;
            int size = _assignment.Length;
            for (int i = 0; i < size; ++i)
            {
                // Get player id for this index.
                int id = _assignment[i];
                
                // ID is invalid, skip this index.
                if (id == -1)
                {
                    continue;
                }
                
                // Get the player for the given id.
                VRCPlayerApi player = VRCPlayerApi.GetPlayerById(id);
                
                // Player is invalid, skip this player.
                if (!VRC.SDKBase.Utilities.IsValid(player))
                {
                    continue;
                }
                
                // Store player in temporary location to be moved over later.
                _allPlayersTemp[count] = player;
                ++count;
            }

            // Copy over players from temp array into the array to return.
            // This is to ensure correct size in the array.
            VRCPlayerApi[] players = new VRCPlayerApi[count];
            for (int i = 0; i < count; ++i)
            {
                players[i] = _allPlayersTemp[i];
            }

            return players;
        }
        
        /// <summary>
        /// Get an ordered list of players based on the pool's assignment. This list will be the same order for all
        /// clients and is useful for randomization.
        /// O(n) to iterate over all assignments
        /// </summary>
        /// <param name="players">
        /// A valid player array to store the list of players who have been assigned an index. This array should be
        /// large enough to store all players. It is recommended to use the same size as the total number of player
        /// objects since this also represents the max player count.
        /// </param>
        /// <returns>
        /// Returns the number of valid players added into the input array.
        /// </returns>
        [PublicAPI]
        public int _GetOrderedPlayersNoAlloc(VRCPlayerApi[] players)
        {
            // The input array is null. No players can be stored in it. Return early.
            if (players == null)
            {
                _LogError("_GetOrderedPlayersNoAlloc provided with a null array!");
                return 0;
            }
            
            // Go through the assignment array and find all valid players.
            int count = 0;
            int size = _assignment.Length;
            int maxCount = players.Length;
            for (int i = 0; i < size; ++i)
            {
                // Get player id for this index.
                int id = _assignment[i];
                
                // ID is invalid, skip this index.
                if (id == -1)
                {
                    continue;
                }
                
                // Get the player for the given id.
                VRCPlayerApi player = VRCPlayerApi.GetPlayerById(id);
                
                // Player is invalid, skip this player.
                if (!VRC.SDKBase.Utilities.IsValid(player))
                {
                    continue;
                }

                // VRChat's GetPlayers will throw an error if you call it with an array to small. 
                if (count >= maxCount)
                {
                    Debug.LogError("_GetOrderedPlayersNoAlloc called with an array too small to fit all players!");
                    return count;
                }

                // Assign the player in the array and increment our total count
                players[count] = player;
                ++count;
            }

            return count;
        }

        /// <summary>
        /// These methods and variables are used so that Graph and CyanTrigger programs can call the public api methods
        /// with parameters and read the output data.
        /// </summary>
        #region Public API for Graph and CyanTrigger programs

        [NonSerialized, PublicAPI] 
        public VRCPlayerApi playerInput;
        
        [NonSerialized, PublicAPI] 
        public int playerIdInput;

        [NonSerialized, PublicAPI] 
        public VRCPlayerApi[] playerArrayInput;

        [NonSerialized, PublicAPI] 
        public VRCPlayerApi[] playerArrayOutput;
        
        [NonSerialized, PublicAPI] 
        public int playerCountOutput;

        [NonSerialized, PublicAPI] 
        public int playerIndexOutput;
        

        [PublicAPI]
        [Obsolete("This method is intended only for non UdonSharp programs. Use _GetOrderedPlayers instead.")]
        public void _GetOrderedPlayersEvent()
        {
            playerArrayOutput = _GetOrderedPlayers();
        }
        
        [PublicAPI]
        [Obsolete("This method is intended only for non UdonSharp programs. Use _GetOrderedPlayersNoAlloc instead.")]
        public void _GetOrderedPlayersNoAllocEvent()
        {
            playerCountOutput = _GetOrderedPlayersNoAlloc(playerArrayInput);
        }

        [PublicAPI]
        [Obsolete("This method is intended only for non UdonSharp programs. Use _GetPlayerPoolIndex instead.")]
        public void _GetPlayerPoolIndexEvent(VRCPlayerApi player)
        {
            playerIndexOutput = _GetPlayerPoolIndex(playerInput);
        }

        [PublicAPI]
        [Obsolete("This method is intended only for non UdonSharp programs. Use _GetPlayerPoolIndexById instead.")]
        public void _GetPlayerPoolIndexByIdEvent()
        {
            playerIndexOutput = _GetPlayerPoolIndexById(playerIdInput);
        }
        
        #endregion
        
        #endregion
        
        private void Start()
        {
            _localPlayer = Networking.LocalPlayer;

            // Initialize the path so that pool object assigners can easily find this pool system.
            // If multiple exist, then delete this instance.
            if (!_InitializePoolPath())
            {
                DestroyImmediate(this);
                return;
            }
            
            // Initialize tag based caching system.
            _InitializeTag();

            // Initialize the pool arrays
            _assignment = new int[poolSize];
            _prevAssignment = new int[poolSize];
            _assignedPlayers = new VRCPlayerApi[poolSize];
            
            // Initialize listeners array.
            _assignmentListeners = new CyanPlayerObjectAssigner[4];
            
            // Initialize temp arrays to not need to recreate them every time.
            int maxSize = poolSize + 4;
            _allPlayersTemp = new VRCPlayerApi[maxSize];
            _playerIdsWithIndices = new int[maxSize];
            _playerIndexIds = new int[maxSize];
            
            _LogInfo($"[{Version}]");
            _LogDebug($"Initializing pool with {poolSize} indices. Please make sure there are enough indices " +
                 "to cover two times the world player cap.");

            // Initialize assignment and prev assignment arrays.
            for (int i = 0; i < poolSize; ++i)
            {
                _assignment[i] = -1;
                _prevAssignment[i] = -1;
            }

            // Initialize items for master. Nothing should be assigned at this point.
            if (Networking.IsMaster)
            {
                _isMaster = true;
                _FillUnclaimedIndexQueue();
                
                _DelayRequestSerialization();
            }
        }

        // Register an Object Assigner so that multiple prefabs can reuse the same index assignment from this pool.
        public void _RegisterObjectAssigner(CyanPlayerObjectAssigner objectAssigner)
        {
            // Verify the list can hold the new listener, otherwise increase the array size.
            int curSize = _assignmentListeners.Length;
            if (_listenersCount >= curSize)
            {
                // Create a new array with double the capacity.
                CyanPlayerObjectAssigner[] tempArray = new CyanPlayerObjectAssigner[curSize * 2];
                // Copy over the old elements.
                for (int cur = 0; cur < curSize; ++cur)
                {
                    tempArray[cur] = _assignmentListeners[cur];
                }
                // Set the array to the new version.
                _assignmentListeners = tempArray;
            }

            // Add the listener to the list of listeners.
            _assignmentListeners[_listenersCount] = objectAssigner;
            ++_listenersCount;
        }

        #region VRChat callbacks

        // If any player tries to request ownership, reject the request since this should only be owned by the master.
        // This code makes multiple assumptions that rely on players only gaining ownership when the previous owner left
        // and that it will never gain ownership more than once.
        public override bool OnOwnershipRequest(VRCPlayerApi requestingPlayer, VRCPlayerApi requestedOwner)
        {
            return false;
        }

        // On player join, assign that player an index in the pool.
        // Only master handles this request.
        // O(1) + O(n) next frame - See _AssignIndex for more details
        public override void OnPlayerJoined(VRCPlayerApi player)
        {
            // There is currently a bug in VRChat where OnPlayerLeft player will return -1 if the player id is never
            // checked before they left. This line is here only to ensure the id is checked for non master clients.
            // https://vrchat.canny.io/vrchat-udon-closed-alpha-bugs/p/vrcplayerapiplayerid-may-returns-1-in-onplayerleft
            int temp = player.playerId;
            
            // In rare cases, synced data can arrive before all player join messages have happened. In this case, an
            // assignment may be considered invalid and needs to be verified again when the player joins. If this value
            // is true, then an invalid player assignment has been found and all assignments should be verified in case
            // the player is now valid. This should only happen on non master clients. This assumes that the invalid
            // player will eventually have a join message.
            if (_verifyAssignmentsOnPlayerJoin)
            {
                _DelayUpdateAssignment();
            }
            
            if (!Networking.IsMaster)
            {
                return;
            }

            _CheckForMasterSwap();
            
            _AssignIndex(player);
        }

        // On player left, assign find the index assigned to that player and return it back to the pool.
        // Only master handles this request.
        // If the local player was not master before, but is now master due to previous master leaving, verify all
        // player/index assignments.
        // O(1) best case. See _ReturnPlayerIndex for more details
        public override void OnPlayerLeft(VRCPlayerApi player)
        {
            // If the local player leaves, the player here will be null. Return early to prevent error in logs.
            if (player == null)
            {
                return;
            }

            int playerId = player.playerId;
            int index = _GetPlayerPooledIndexById(playerId);
            
            // Have everyone clean up the index locally to ensure owner is properly set for the index before it is
            // eventually disabled.
            _CleanupPlayerIndex(index, playerId);
            
            if (!Networking.IsMaster)
            {
                return;
            }
            
            _CheckForMasterSwap();

            _ReturnPlayerIndex(index, playerId);
        }

        // Synced data has changed, check for changes in the player/index assignments.
        public override void OnDeserialization()
        {
            // Calling delay update to ensure that the update itself does not happen on the same frame as
            // OnDeserialization. Some vrchat api's do not work appropriately, such as VRCPlayerApi.Teleport
            _DelayUpdateAssignment();
        }

        // OnPostSerialization is called after serialization of the assignment array has finished.
        public override void OnPostSerialization(SerializationResult result)
        {
            // Check the results to know if there were any errors and retry.
            if (!result.success)
            {
                _LogError("Failed to serialize data! Requesting again after delay.");
                _DelayRequestSerialization();
                return;
            }
            
            // Results were successful, update player assignments.
            _DelayUpdateAssignment();
        }

        #endregion

        #region Player Tag Caching System

        private bool _InitializePoolPath()
        {
            string path = name;
            Transform parent = transform.parent;
            while (parent != null)
            {
                path = parent.name + "/" + path;
                parent = parent.parent;
            }

            string currentPath = _localPlayer.GetPlayerTag(PoolPathTag);
            if (path != currentPath && !string.IsNullOrEmpty(currentPath))
            {
                _LogError("Multiple Pools exist in the scene! CyanPlayerObjectPool only supports one pool object per scene!\nPath: " + currentPath+"\nThisPath: " +path);
                return false;
            }
            
            _localPlayer.SetPlayerTag(PoolPathTag, path);
            return true;
        }
        
        private void _InitializeTag()
        {
            // Verify the tag has not been set before setting it valid. 
            if (!string.IsNullOrEmpty(_GetTag(PlayerIndexSet)))
            {
                _LogError("Object pool index set tag has already been set!");
                return;
            }
            
            // Initialize caching system to say that player index values have been set.
            // If everything is valid, checking for assignments will automatically update the cache system.
            _SetTag(PlayerIndexSet, TagValid);
        }
        
        // Cache a value using the player tag system, allowing for constant time lookup.
        private void _SetTag(string tagName, string value)
        {
            _localPlayer.SetPlayerTag(PoolIdTagPrefix + tagName, value);
        }
        
        // Get the value for a tag using the player tag caching system.
        private string _GetTag(string tagName)
        {
            return _localPlayer.GetPlayerTag(PoolIdTagPrefix + tagName);
        }
        
        // Given a player id, get the index assigned to the player. 
        // Player ids and indices are cached using player tags.
        // O(1) best case, O(n) if tags were not set. 
        private int _GetPlayerPooledIndexById(int playerId)
        {
            // Tags have not been set up. This should only happen if another prefab called ClearPlayerTags.
            // O(n)
            if (_GetTag(PlayerIndexSet) != TagValid)
            {
                _LogWarning("Caching all player index values. Please verify nothing calls " +
                         "VRCPlayerApi.ClearPlayerTags!");
                
                _InitializeTag();
                _SetPlayerIndexTags(true);
            }
            
            string playerTag = _GetPlayerIndexTag(playerId);
            string tagValue = _GetTag(playerTag);
            
            // UdonSharp does not support inline variable declarations.
            int results;
            if (!int.TryParse(tagValue, out results))
            {
                return -1;
            }

            return results;
        }

        // Go through each index assignment and cache the player id with the assigned index. 
        // O(n)
        private void _SetPlayerIndexTags(bool force)
        {
            int size = _assignment.Length;
            for (int index = 0; index < size; ++index)
            {
                int ownerId = _assignment[index];
                if (ownerId == -1)
                {
                    continue;
                }

                // This is called from _GetPlayerPooledIndexById when tags are missing. To prevent double looping,
                // ignore previous tag value when force setting tags.
                if (!force)
                {
                    // Ensure to not overwrite current index set for the given player in case multiple exist due to errors.
                    int expectedIndex = _GetPlayerPooledIndexById(ownerId);
                    if (expectedIndex != -1)
                    {
                        continue;
                    } 
                }

                _SetPlayerIndexTag(ownerId, index);
            }
        }

        // Given the player id, return the proper tag to store the player's index.
        private string _GetPlayerIndexTag(int playerId)
        {
            return PlayerIndexTagPrefix + playerId;
        }

        // Given a player id and index, cache the index using tag system.
        private void _SetPlayerIndexTag(int playerId, int index)
        {
            string playerTag = _GetPlayerIndexTag(playerId);
            _SetTag(playerTag, index == -1 ? "" : index.ToString());
        }

        // Given a player id and an expected index, clear the cached tag if the current value equals the
        // expected index value.
        private void _ClearPlayerIndexTagIfExpected(int playerId, int expectedIndex)
        {
            int index = _GetPlayerPooledIndexById(playerId);
            if (expectedIndex == index)
            {
                // Cleanup tags to ensure you can't get this player's index anymore.
                _SetPlayerIndexTag(playerId, -1);
            }
        }

        #endregion

        #region Player/Index Assigment

        // These methods are for a queue system to hold unassigned indices. The queue is a first in, first out queue.
        // This system is needed to ensure that assigning indices to players is constant time instead of O(n).
        #region Unassigned Index Queue

        // Construct the queue given the current state of the assigned indices. 
        private void _FillUnclaimedIndexQueue()
        {
            int size = _assignment.Length;
            _unclaimedQueue = new int[size];
            
            for (int i = 0; i < size; ++i)
            {
                // Ensure queue is initialized with -1.
                _unclaimedQueue[i] = -1;
                
                // If the current index does not have a player assigned, add it to the queue.
                if (_assignment[i] == -1)
                {
                    _EnqueueItemToUnclaimedQueue(i);
                }
            }
        }

        // Get the current size of the queue.
        private int _UnclaimedQueueCount()
        {
            return _unclaimedQueueEnd - _unclaimedQueueStart;
        }

        // Get the first unclaimed index from the queue. Returns -1 if no index is in the queue.
        // O(1)
        private int _DequeueItemFromUnclaimedQueue()
        {
            // If the queue is empty, return invalid.
            if (_UnclaimedQueueCount() <= 0)
            {
                return -1;
            }

            // Get the index for the start of the queue and increment the value.
            int index = _unclaimedQueueStart % _unclaimedQueue.Length;
            ++_unclaimedQueueStart;
            
            // Get the first element in the queue
            int element = _unclaimedQueue[index];
            // Clear the value at this index to ensure old elements are never reused.
            _unclaimedQueue[index] = -1;
            
            return element;
        }
        
        // Add the given unclaimed index into the queue
        // O(1)
        private void _EnqueueItemToUnclaimedQueue(int value)
        {
            if (_UnclaimedQueueCount() >= _unclaimedQueue.Length)
            {
                _LogError("Trying to queue an item when the queue is full!");
                return;
            }
            
            // Get the index for the end of the queue and increment the value.
            int index = _unclaimedQueueEnd % _unclaimedQueue.Length;
            ++_unclaimedQueueEnd;
            
            _unclaimedQueue[index] = value;
        }

        #endregion


        // Given a player, assign them an index from the pool. This should only be called by Master.
        // O(1) + O(n) next frame for updating assignments
        // Thanks to the UnclaimedQueue, getting a free index is constant time. 
        // Updating the current indices based on the assignment takes O(n). See _AssignIndicesToPlayers for more details
        private void _AssignIndex(VRCPlayerApi player)
        {
            int id = player.playerId;

            int index = _GetPlayerPooledIndexById(id);
            if (index != -1)
            {
                _LogWarning($"Attempting to assign player {id} an index when they already have one. {index}");
                return;
            }
            
            index = _DequeueItemFromUnclaimedQueue();
            
            // Pool is empty and could not find an index to assign to the new player.
            // This should be fatal as the owner didn't set the pool size high enough.
            if (index == -1)
            {
                _LogError($"Not enough indices to assign to new player! player: {id}. Increase the pool size to " +
                          "match double the world capacity!");
                return;
            }
            
            // This case shouldn't happen based on how getting an index works, but still logging just in case.
            if (_assignment[index] != -1)
            {
                _LogWarning("Assigning player to an index but other player was already assigned the index!" +
                                 $" prev: {_assignment[index]}, new: {id}");
            }

            _assignment[index] = id;
            _LogDebug($"Assigning player {id} to index {index}");
            
            // Set the tag early for caching to know that player has been assigned an index.
            _SetPlayerIndexTag(id, index);
            
            _DelayRequestSerialization();
        }

        // Clear the assignment for a given index and player. This should only be called by Master.
        // O(1) + O(n) next frame for updating assignments
        private void _ReturnPlayerIndex(int index, int playerId)
        {
            if (index == -1)
            {
                _LogError($"Cannot return player index if index is invalid! Player: {playerId}");
                return;
            }
            
            if (_assignment[index] != playerId)
            {
                _LogWarning("Removing assignment to index that wasn't to specified player!" +
                                 $" assignment: {_assignment[index]}, player: {playerId}");
            }
            
            // Set the assignment for this index to invalid.
            _assignment[index] = -1;
            // Return the index into the unclaimed queue.
            _EnqueueItemToUnclaimedQueue(index);
            _LogDebug($"Unassigning index {index} from player {playerId}");
            
            // Clear the player tag only if the current tag is equal to the index we are cleaning up.
            // If players had multiple indices, this will not revert unexpected index tags.
            _ClearPlayerIndexTagIfExpected(playerId, index);

            _DelayRequestSerialization();
        }

        // Delay requesting serialization. On each call, it will ensure that serialization does not happen until at
        // least some duration after the last request. This delay is used to reduce overall network load when multiple
        // people join at the same time. 
        private void _DelayRequestSerialization()
        {
            // Set the last request time to now.
            _lastSerializationRequestTime = Time.timeSinceLevelLoad;
            // Delay call to handle serialization.
            SendCustomEventDelayedSeconds(nameof(_OnDelayRequestSerialization), DelaySerializationDuration);
        }
        
        // Handle delayed request serializations. On each call, it will ensure that serialization does not happen until 
        // at least some duration after the last request. This delay is used to reduce overall network load when 
        // multiple people join at the same time. 
        public void _OnDelayRequestSerialization()
        {
            // If the last request time was less than the expected duration, return early since this means another
            // request has happened since this one was requested.
            if (Time.timeSinceLevelLoad - _lastSerializationRequestTime < DelaySerializationDuration - TimeCheckEpsilon) {
                return;
            }

            // Check if the network is clogged and delay serialization until the network has calmed down.
            if (Networking.IsClogged)
            {
                ++_networkedCloggedSerializationAttempts;
                if (_networkedCloggedSerializationAttempts <= MaxCloggedDelayAttempts)
                {
                    _LogWarning($"Network is clogged, delaying serialization. Attempt: {_networkedCloggedSerializationAttempts}");
                    _DelayRequestSerialization();
                    return;
                }
                _LogWarning($"Network is still clogged, but attempting serialization. Attempt: {_networkedCloggedSerializationAttempts}");
            }

            _networkedCloggedSerializationAttempts = 0;
            // Request serialization of the object pool assignments so that everyone else can see the updated
            // assignments. 
            RequestSerialization();

            // Serialization will only happen when multiple players are in the world.
            // If there is only one player, update assignment directly. 
            bool shouldManuallyUpdateAssignment = VRCPlayerApi.GetPlayerCount() == 1;

            // CyanEmu does not handle calling OnPostSerialization ever, so it always needs to be called manually in editor.
#if UNITY_EDITOR
            shouldManuallyUpdateAssignment = true;
#endif
            
            if (shouldManuallyUpdateAssignment)
            {
                OnPostSerialization(_trueSerializationResults);
            }
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
            _AssignIndicesToPlayers();

            if (printDebugLogs)
            {
                _PrintAssignment();
            }
        }

        // Go through the assigment array and verify changes since last assignment. 
        // O(n)
        private void _AssignIndicesToPlayers()
        {
            _verifyAssignmentsOnPlayerJoin = false;
            
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
                
                // Index was previously assigned to a player and needs to be cleaned up.
                if (prevAssign != -1)
                {
                    _CleanupPlayerIndex(index, prevAssign);
                }
                
                // New assignment is an owner. Assign the index to that player.
                if (newAssign != -1)
                {
                    if (!_SetupPlayerIndex(index, newAssign))
                    {
                        requireVerification = true;
                        
                        // Continue the loop early and prevent setting the _prevAssignment array to allow retrying this
                        // index.
                        continue;
                    }
                }
                
                // Update the previous assignment to match new assignment.
                _prevAssignment[index] = newAssign;
            }

            if (requireVerification)
            {
                if (Networking.IsMaster)
                {
                    // If require verification while master, verify all player assignments
                    _VerifyAllPlayersHaveIndices();
                }
                else
                {
                    // Player was not master, but an invalid player was found. Ignore for now and wait for the next
                    // player join to attempt assigning indices again.
                    _verifyAssignmentsOnPlayerJoin = true;
                }
            }
        }

        // Set up an index after it has been assigned to a player. This will notify all object assigners to properly
        // assign the corresponding object for the index.
        private bool _SetupPlayerIndex(int index, int playerId)
        {
            VRCPlayerApi player = VRCPlayerApi.GetPlayerById(playerId);
            
            // Verify the player at the given id does not already have an index. 
            int curIndex = _GetPlayerPooledIndexById(playerId);
            if (curIndex != -1 && curIndex != index)
            {
                _LogWarning($"Attempting to assign player {playerId} an index when they already have one. TryAssign: {index}, Cur: {curIndex}");
                return false;
            }
            
            // Ensure this gets set even if player is invalid. The cleanup case should verify and remove the player later.
            _SetPlayerIndexTag(playerId, index);
            
            if (!VRC.SDKBase.Utilities.IsValid(player))
            {
                _LogError($"Trying to assign invalid player to index! player: {playerId}, index: {index}");
                return false;
            }
            
            // Save the player api assigned to this index.
            _assignedPlayers[index] = player;

            _LogDebug($"Player {player.playerId} has been assigned index {index}");

            // Go through all assigment listeners and notify them of the new player/index assignment.
            for (int curListener = 0; curListener < _listenersCount; ++curListener)
            {
                _assignmentListeners[curListener]._OnPlayerAssigned(player, index);
            }

            return true;
        }
        
        // Once an index has been unassigned, clean up the index by removing the cached index and notify listeners.
        // Called by everyone when any player leaves the instance and when the assignment array removes an assignment.
        private void _CleanupPlayerIndex(int index, int playerId)
        {
            if (playerId == _localPlayer.playerId)
            {
                _LogError($"Cleaning up local player's index while still in the instance! player: {playerId}, index: {index}");
            }
            
            if (index == -1)
            {
                _LogWarning($"Could not find index for leaving player: {playerId}");
                return;
            }

            // Clear the player tag only if the current tag is equal to the index we are cleaning up.
            // If players had multiple indices, this will not revert unexpected index tags.
            _ClearPlayerIndexTagIfExpected(playerId, index);
            
            VRCPlayerApi player = _assignedPlayers[index];
            _assignedPlayers[index] = null;

            // Index was already cleaned up. No need to send event again.
            if (player == null)
            {
                return;
            }
            
            _LogDebug($"Index {index} has been unassigned from player {player.playerId}");

            // Go through all assigment listeners and notify them of the new player/index assignment.
            for (int curListener = 0; curListener < _listenersCount; ++curListener)
            {
                _assignmentListeners[curListener]._OnPlayerUnassigned(player, index);
            }
        }

        // Debug method used to print the current player/index assignments
        private void _PrintAssignment()
        {
            string assignment = "Player Assignments: ";
            int size = _assignment.Length;
            for (int i = 0; i < size; ++i)
            {
                if (_assignment[i] != -1)
                {
                    assignment += "[Player: " + _assignment[i] + ", index: " + i + "], ";
                }
            }
            
            _LogDebug(assignment);
        }
        
        // When the local player becomes the new master, cache some values and delay verification of all player's having
        // an index.
        private void _CheckForMasterSwap()
        {
            // Master left and local player is the new master.
            if (!_isMaster && Networking.IsMaster)
            {
                _LogDebug("Local player is now master!");
                _isMaster = true;
                _FillUnclaimedIndexQueue();
                
                // It's possible that previous master has sent assignment data for players that have not yet "joined"
                // for this client. Force add them to the tag system to prevent double assignment for the player.
                _SetPlayerIndexTags(false);

                // verify all players have indices, but wait a duration to ensure that all players have joined/left.
                SendCustomEventDelayedSeconds(nameof(_VerifyAllPlayersHaveIndices), DelayNewMasterVerificationDuration);
            }
        }
        
        // Go through all assignments and all players and ensure that each player still has
        // an index and each index has a player.
        // O(n)
        // public needed for SendCustomEventDelayedSeconds, but should not be called externally!
        public void _VerifyAllPlayersHaveIndices()
        {
            if (!Networking.IsMaster)
            {
                return;
            }
            
            _LogDebug("Verifying all players have an index and all indices have a player.");

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
                
                string tagName = PlayerPoolOwnerTagPrefix + ownerId;
                
                // Check if this player already had an index assigned. If so, remove the duplicate assignment.
                // Duplicate is found if the tag has already been set, or if this player has an index assigned that
                // isn't this index.
                string tagValue = _GetTag(tagName);
                int expectedIndex = _GetPlayerPooledIndexById(ownerId);
                if ((expectedIndex != index && expectedIndex != -1) || tagValue == TagValid)
                {
                    _LogWarning($"Player had multiple indices during verification! Player: {ownerId}, Index: {index}");
                    
                    // Cleanup the player index first and then remove the assignment.
                    _CleanupPlayerIndex(index, ownerId);
                    _ReturnPlayerIndex(index, ownerId);
                    
                    continue;
                }
                
                _playerIdsWithIndices[count] = ownerId;
                _playerIndexIds[count] = index;
                ++count;
                
                // Set tags for used index owners
                // This will give us a constant look up if a user has an assigned index.
                _SetTag(tagName, TagValid);
            }


            // Go through each player and find if that player has an index, otherwise assign them one.
            VRCPlayerApi.GetPlayers(_allPlayersTemp);
            int maxSize = _allPlayersTemp.Length;
            for (int index = 0; index < maxSize; ++index)
            {
                VRCPlayerApi player = _allPlayersTemp[index];
                if (!VRC.SDKBase.Utilities.IsValid(player))
                {
                    break;
                }
                
                int id = player.playerId;
                string tagName = PlayerPoolOwnerTagPrefix + id;
                string tagValue = _GetTag(tagName);
                
                // Remove the tag to check later if tag exists from assignments to know player is not in the room.
                _SetTag(tagName, "");
                
                // Tag is valid. No need to do anything for this player.
                if (tagValue == TagValid)
                {
                    continue;
                }
                
                // Player did not have tag, meaning they did not have an index. Assign them one.
                _LogWarning($"Player did not have an index during verification! Player: {id}");
                _AssignIndex(player);
            }
            
            // Go through each index again and verify if the assigned player is still in the instance.
            // Clear used tags to prevent issues for next iteration of this verification method.
            // We cannot directly clear all tags due to other caching mechanics used.
            for (int index = 0; index < count; ++index)
            {
                int ownerId = _playerIdsWithIndices[index];

                string tagName = PlayerPoolOwnerTagPrefix + ownerId;
                string tagValue = _GetTag(tagName);
                _SetTag(tagName, "");
                
                // If tag exists, then this player is no longer in the instance and needs to be removed from the
                // assignment array.
                if (tagValue == TagValid)
                {
                    int objIndex = _playerIndexIds[index];
                    _LogWarning($"Missing player still owned an index during verification! Player: {ownerId}, index: {objIndex}");
                    
                    // Cleanup the player index first and then remove the assignment.
                    _CleanupPlayerIndex(objIndex, ownerId);
                    _ReturnPlayerIndex(objIndex, ownerId);
                }
            }
        }
        
        #endregion

        #region Logging

        private const string LogPrefix = "[Cyan][PlayerPool]";

        private void _LogInfo(string message)
        {
            Debug.Log($"{LogPrefix} {message}");
        }
        
        private void _LogDebug(string message)
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

