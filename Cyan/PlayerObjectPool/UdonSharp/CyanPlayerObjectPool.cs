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
        /// Event name that will be sent to the pool event listener when the local player is assigned an object.
        /// </summary>
        [PublicAPI]
        public const string OnLocalPlayerAssignedEvent = "_OnLocalPlayerAssigned";
        
        /// <summary>
        /// Variable name that will be set before the OnPlayerAssignedEvent is sent to the pool event listener.
        /// This variable will store the player id of the last player whose object was assigned.
        /// </summary>
        [PublicAPI]
        public const string PlayerAssignedIdVariableName = "playerAssignedId";
        /// <summary>
        /// Variable name that will be set before the OnPlayerAssignedEvent is sent to the pool event listener.
        /// This variable will store the player api data of the last player whose object was assigned.
        /// </summary>
        [PublicAPI]
        public const string PlayerAssignedPlayerVariableName = "playerAssignedPlayer";
        /// <summary>
        /// Variable name that will be set before the OnPlayerAssignedEvent is sent to the pool event listener.
        /// This variable will store the Udon pool object of the last player whose object was assigned.
        /// </summary>
        [PublicAPI]
        public const string PlayerAssignedUdonVariableName = "playerAssignedPoolObject";
        /// <summary>
        /// Event name that will be sent to the pool event listener when any player has joined and is assigned an object.
        /// </summary>
        [PublicAPI]
        public const string OnPlayerAssignedEvent = "_OnPlayerAssigned";
        
        /// <summary>
        /// Variable name that will be set before the OnPlayerUnassignedEvent is sent to the pool event listener.
        /// This variable will store the player id of the last player whose object was unassigned.
        /// </summary>
        [PublicAPI]
        public const string PlayerUnassignedIdVariableName = "playerUnassignedId";
        /// <summary>
        /// Variable name that will be set before the OnPlayerUnassignedEvent is sent to the pool event listener.
        /// This variable will store the player api data of the last player whose object was unassigned.
        /// </summary>
        [PublicAPI]
        public const string PlayerUnassignedPlayerVariableName = "playerUnassignedPlayer";
        /// <summary>
        /// Variable name that will be set before the OnPlayerUnassignedEvent is sent to the pool event listener.
        /// This variable will store the Udon pool object of the last player whose object was unassigned.
        /// </summary>
        [PublicAPI]
        public const string PlayerUnassignedUdonVariableName = "playerUnassignedPoolObject";
        /// <summary>
        /// Event name that will be sent to the pool event listener when any player has left and their object has been
        /// unassigned. Note that 
        /// </summary>
        [PublicAPI]
        public const string OnPlayerUnassignedEvent = "_OnPlayerUnassigned";
        
        
        // If player capacity is increased, then this should be changed. Real max is 82
        private const int MaxPlayers = 100;
        // Prefix for all tags that will be combined with the pool id to ensure that each pool has unique tags even when
        // multiple object pool systems exist in the same scene. The id value is based on the object's instance id.
        private const string PoolIdTagPrefix = "_cyan_object_pool_id_";
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
        // The minimum duration between serialization requests to reduce overall network load when multiple people join
        // at the same time. 
        private const float DelaySerializationDuration = 2f;
        // The duration to wait to verify all pool objects after a player has become the new master. This delay is
        // needed to ensure that all players have joined/left since the master swap. It is possible that the previous
        // master assigned pool objects to players that have not yet joined on the new master's client. In this case,
        // the pool objects would be forced removed from those players and assigned another object.
        private const float DelayNewMasterVerificationDuration = 1f;
        
        #endregion

        #region Public Settings
        
        /// <summary>
        /// When assigning objects to players, should the assigned player also take ownership over this object? If your
        /// pool objects do not use Synced variables, then this option can be considered for less network overhead.
        /// Default value is True. 
        /// </summary>
        [Tooltip("When assigning objects to players, should the assigned player also take ownership over this object? If your pool objects do not use Synced variables, then this option can be considered for less network overhead. Default value is True.")]
        public bool setNetworkOwnershipForPoolObjects = true;

        /// <summary>
        /// Setting this to true will print debug logging information about the status of the pool.
        /// Warnings and errors will still be printed even if this is set to false.
        /// </summary>
        [Tooltip("Setting this to true will print debug logging information about the status of the pool. Warnings and errors will still be printed even if this is set to false.")]
        public bool printDebugLogs = false;

        // UdonBehaviour that will listen for different events from the Object pool system. 
        // Current events:
        // - _OnAssignmentChanged
        // - _OnLocalPlayerAssigned
        // - _OnPlayerAssigned
        // - _OnPlayerUnassigned
        [Tooltip("Optional UdonBehaviour that will listen for different events from the Object pool system. Currently supported events: \"_OnAssignmentChanged\", \"_OnLocalPlayerAssigned\", \"_OnPlayerAssigned\", \"_OnPlayerUnassigned\"")]
        public UdonBehaviour poolEventListener;

        #endregion

        // The assignment of objects to players. Each index represents an object in the pool. The value stores the
        // player that owns this object, or -1 if no one has been assigned this object.
        [UdonSynced]
        private int[] _assignment = new int[0];
        private int[] _prevAssignment = new int[0];

        // The assignment array, storing the player apis directly. This is used to send player api data to even listener
        // as calling GetPlayerById returns null for players leaving the instance.
        private VRCPlayerApi[] _assignedPlayers;
        
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
        // Id set for this pool. Used to ensure that multiple instances of this pool will all have unique tags.
        private int _poolTagId;

        // Used to check if we have requested to check for updates on assignment. Only one request can happen at a time.
        private bool _delayUpdateAssignment;
        
        // The time of the last request to serialize the object pool assignments. This is used to ensure that only one
        // serialization request happens within a given duration. This is to help reduce overall networking load
        // when multiple people join at the same time.
        private float _lastSerializationRequestTime;

        // Temporary data used for verifying each player's object.
        private Component[] _poolObjectsTemp;
        private VRCPlayerApi[] _allPlayersTemp;
        private int[] _playerIdsWithObjects;
        private int[] _playerObjectIds;

        // In rare cases, synced data can arrive before all player join messages have happened. In this case, an
        // assignment may be considered invalid and needs to be verified again when the player joins.
        private bool _verifyAssignmentsOnPlayerJoin = false;

        // Queue of indices indicating which pool objects have not been assigned. This system allows constant time
        // assigning objects to players instead of O(n) to find the first unassigned object.
        private int[] _unclaimedQueue;
        // The start index into the queue where items should be pulled from.
        // This may be larger than the queue length if many items have been cycled. 
        private int _unclaimedQueueStart = 0;
        // The end index into the queue where new items are added into the queue.
        // This may be larger than the queue length if many items have been cycled. 
        private int _unclaimedQueueEnd = 0;

        
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
            return VRC.SDKBase.Utilities.IsValid(player) ? _GetPlayerPooledObjectById(player.playerId) : null;
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
                _LogWarning($"Could not find object for player: {playerId}");
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
            return VRC.SDKBase.Utilities.IsValid(player) ? _GetPlayerPooledUdonById(player.playerId) : null;
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
        /// Get an ordered list of players based on the pool's assignment. This list will be the same order for all
        /// clients and is useful for randomization.
        /// O(n) to iterate over all players
        /// </summary>
        /// <returns>
        /// Returns an ordered array of players that have currently been assigned an object.
        /// </returns>
        [PublicAPI]
        public VRCPlayerApi[] _GetOrderedPlayers()
        {
            // Go through assignment array and find all valid players and store them into a temporary location.
            int count = 0;
            int size = _assignment.Length;
            for (int i = 0; i < size; ++i)
            {
                // Get player id for this object's index.
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
        /// A valid player array to store the list of players who have been assigned an object. This array should be
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
                // Get player id for this object's index.
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
        /// Get an array of active pool objects based on the current assignments. This list will be the same order for
        /// all clients and is useful for randomization.
        /// O(n) to iterate over all assignments
        /// </summary>
        /// <returns>
        /// Returns an array of Udon components which is each pool object. Note that this is a component array, making
        /// it easy to cast to UdonSharpBehaviours or UdonBehaviours.
        /// </returns>
        [PublicAPI]
        public Component[] _GetActivePoolObjects()
        {
            // Go through assignment array and find all valid players and store the respective pooled object into a
            // temporary location.
            int count = 0;
            int size = _assignment.Length;
            for (int i = 0; i < size; ++i)
            {
                // Get player id for this object's index.
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
                
                // Store this player's pool object in temporary location to be moved over later.
                _poolObjectsTemp[count] = pooledUdon[i];
                ++count;
            }

            // Copy over the pool object from temp array into the array to return.
            // This is to ensure correct size in the array.
            Component[] pooledObjects = new Component[count];
            for (int i = 0; i < count; ++i)
            {
                pooledObjects[i] = _poolObjectsTemp[i];
            }

            return pooledObjects;
        }
        
        /// <summary>
        /// Get an array of active pool objects based on the current assignments. This list will be the same order for
        /// all clients and is useful for randomization.
        /// O(n) to iterate over all assignments
        /// </summary>
        /// <param name="pooledObjects">
        /// A valid component array to store the list of pooled objects that have been assigned to a valid player. This 
        /// array should be large enough to store all pooled objects. 
        /// </param>
        /// <returns>
        /// Returns the number of valid pooled objects added into the input array.
        /// </returns>
        [PublicAPI]
        public int _GetActivePoolObjectsNoAlloc(Component[] pooledObjects)
        {
            // The input array is null. No pool objects can be stored in it. Return early.
            if (pooledObjects == null)
            {
                _LogError("_GetActivePoolObjectsNoAlloc provided with a null array!");
                return 0;
            }
            
            // Go through the assignment array and find all valid players.
            int count = 0;
            int size = _assignment.Length;
            int maxCount = pooledObjects.Length;
            for (int i = 0; i < size; ++i)
            {
                // Get player id for this object's index.
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
                    Debug.LogError("_GetActivePoolObjectsNoAlloc called with an array too small to fit all udon components!");
                    return count;
                }

                // Store this player's pool object in the array and increment our total count
                pooledObjects[count] = pooledUdon[i];
                ++count;
            }

            return count;
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
        public VRCPlayerApi[] playerArrayInput;

        [HideInInspector, PublicAPI] 
        public Component[] poolObjectArrayInput;
        
        [HideInInspector, PublicAPI]
        public GameObject poolObjectOutput;
        
        [HideInInspector, PublicAPI] 
        public UdonBehaviour poolUdonOutput;
        
        [HideInInspector, PublicAPI] 
        public VRCPlayerApi[] playerArrayOutput;
        
        [HideInInspector, PublicAPI] 
        public int playerCountOutput;

        [HideInInspector, PublicAPI] 
        public Component[] poolObjectArrayOutput;
        
        [HideInInspector, PublicAPI] 
        public int poolObjectCountOutput;
        
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
        [Obsolete("This method is intended only for non UdonSharp programs. Use _GetActivePoolObjects instead.")]
        public void _GetActivePoolObjectsEvent()
        {
            poolObjectArrayOutput = _GetActivePoolObjects();
        }
        
        [PublicAPI]
        [Obsolete("This method is intended only for non UdonSharp programs. Use _GetActivePoolObjectsNoAlloc instead.")]
        public void _GetActivePoolObjectsNoAllocEvent()
        {
            poolObjectCountOutput = _GetActivePoolObjectsNoAlloc(poolObjectArrayInput);
        }
        
        #endregion
        
        #endregion
        
        private void Start()
        {
            _localPlayer = Networking.LocalPlayer;
            
            // Get pool tag id based on instance id.
            _poolTagId = GetInstanceID();
            
            // Initialize tag based caching system.
            _InitializeTag();

            // Initialize the pool arrays
            int size = transform.childCount;
            _poolObjects = new GameObject[size];
            pooledUdon = new Component[size];
            _prevAssignment = new int[size];
            _assignedPlayers = new VRCPlayerApi[size];
            
            // Initialize temp arrays to not need to recreate them every time.
            _poolObjectsTemp = new Component[size];
            _allPlayersTemp = new VRCPlayerApi[MaxPlayers];
            _playerIdsWithObjects = new int[MaxPlayers];
            _playerObjectIds = new int[MaxPlayers];
            
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

            // Force initialization of the assignment array, even if the local user isn't master. 
            if (_assignment == null || _assignment.Length != size)
            {
                _assignment = new int[size];
                for (int i = 0; i < size; ++i)
                {
                    _assignment[i] = -1;
                }
            }
            
            // Initialize the assignment on master. Nothing should be assigned at this point.
            if (Networking.IsMaster)
            {
                _isMaster = true;
                _FillUnclaimedObjectQueue();
                
                _DelayRequestSerialization();
                _DelayUpdateAssignment();
            }
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
        // O(1) + O(n) next frame - See _AssignObject for more details
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
            
            _AssignObject(player);
        }

        // On player left, assign find the object assigned to that player and return it back to the pool.
        // Only master handles this request.
        // If the local player was not master before, but is now master due to previous master leaving, verify all
        // player/object assignments.
        // O(1) best case. See _ReturnPlayerObject for more details
        public override void OnPlayerLeft(VRCPlayerApi player)
        {
            // If the local player leaves, the player here will be null. Return early to prevent error in logs.
            if (player == null)
            {
                return;
            }

            int playerId = player.playerId;
            int index = _GetPlayerPooledIndexById(playerId);
            
            // Have everyone clean up the object locally to ensure owner is properly set for the object before it is
            // eventually disabled.
            _CleanupPlayerObject(index, playerId);
            
            if (!Networking.IsMaster)
            {
                return;
            }
            
            _CheckForMasterSwap();

            _ReturnPlayerObject(index, playerId);
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
        
        private void _InitializeTag()
        {
            // Verify the tag has not been set before setting it valid. 
            if (!string.IsNullOrEmpty(_GetTag(PlayerObjectIndexSet)))
            {
                _LogError("Object pool index set tag was set before initialization!");
            }
            
            // Initialize caching system to say that player object index values have been set.
            // If everything is valid, checking for assignments will automatically update the cache system.
            _SetTag(PlayerObjectIndexSet, TagValid);
        }
        
        // Get the tag prefix for this pool, which will be unique for this instance.
        private string _GetPoolTagIdPrefix()
        {
            return PoolIdTagPrefix + _poolTagId + "_";
        }

        // Cache a value using the player tag system, allowing for constant time lookup.
        private void _SetTag(string tagName, string value)
        {
            _localPlayer.SetPlayerTag(_GetPoolTagIdPrefix() + tagName, value);
        }
        
        // Get the value for a tag using the player tag caching system.
        private string _GetTag(string tagName)
        {
            return _localPlayer.GetPlayerTag(_GetPoolTagIdPrefix() + tagName);
        }
        
        // Given a player id, get the object assigned to the player. 
        // Player ids and object indices are cached using player tags.
        // O(1) best case, O(n) if tags were not set. 
        private int _GetPlayerPooledIndexById(int playerId)
        {
            // Tags have not been set up. This should only happen if another prefab called ClearPlayerTags.
            // O(n)
            if (_GetTag(PlayerObjectIndexSet) != TagValid)
            {
                _LogWarning("Caching all player object index values. Please verify nothing calls " +
                         "VRCPlayerApi.ClearPlayerTags!");
                
                _InitializeTag();
                _SetPlayerIndexTags();
            }
            
            string playerTag = _GetPlayerObjectIndexTag(playerId);
            string tagValue = _GetTag(playerTag);
            
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
            int size = _assignment.Length;
            for (int index = 0; index < size; ++index)
            {
                int ownerId = _assignment[index];
                if (ownerId == -1)
                {
                    continue;
                }

                // Ensure to not overwrite current index set for the given player in case multiple exist due to errors.
                int expectedIndex = _GetPlayerPooledIndexById(ownerId);
                if (expectedIndex != -1)
                {
                    continue;
                }
                
                _SetPlayerObjectIndexTag(ownerId, index);
            }
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
            _SetTag(playerTag, index == -1 ? "" : index.ToString());
        }

        #endregion

        #region Player/Object Assigment

        // These methods are for a queue system to hold unassigned objects. The queue is a first in, first out queue.
        // This system is needed to ensure that assigning objects to players is constant time instead of O(n).
        #region Unassigned Object Queue

        // Construct the queue given the current state of the assigned objects. 
        private void _FillUnclaimedObjectQueue()
        {
            int size = _assignment.Length;
            _unclaimedQueue = new int[size];
            
            for (int i = 0; i < size; ++i)
            {
                // Ensure queue is initialized with -1.
                _unclaimedQueue[i] = -1;
                
                // If the current object does not have a player assigned, add it to the queue.
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

        // Get the first unclaimed object from the queue. Returns -1 if no object is in the queue.
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
        
        // Add the given unclaimed object into the queue
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


        // Given a player, assign them an object from the pool. This should only be called by Master.
        // O(1) + O(n) next frame for updating assignments
        // Thanks to the UnclaimedQueue, getting a free object is constant time. 
        // Updating the current objects based on the assignment takes O(n). See _AssignObjectsToPlayers for more details
        private void _AssignObject(VRCPlayerApi player)
        {
            int id = player.playerId;

            int index = _GetPlayerPooledIndexById(id);
            if (index != -1)
            {
                _LogWarning($"Attempting to assign player {id} an object when they already have one. {index}");
                return;
            }
            
            index = _DequeueItemFromUnclaimedQueue();
            
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
            
            // Set the tag early for caching to know that player has been assigned an object.
            _SetPlayerObjectIndexTag(id, index);
            
            _DelayRequestSerialization();
            _DelayUpdateAssignment();
        }

        // Clear the assignment for a given index and player. This should only be called by Master.
        // O(1) + O(n) next frame for updating assignments
        private void _ReturnPlayerObject(int index, int playerId)
        {
            if (index == -1)
            {
                _LogError($"Cannot return player object if index is invalid! Player: {playerId}");
                return;
            }
            
            if (_assignment[index] != playerId)
            {
                _LogWarning("Removing assignment to object that wasn't to specified player!" +
                                 $" assignment: {_assignment[index]}, player: {playerId}");
            }
            
            // Set the assignment for this object to invalid.
            _assignment[index] = -1;
            // Return the object into the unclaimed queue.
            _EnqueueItemToUnclaimedQueue(index);
            
            _SetPlayerObjectIndexTag(playerId, -1);
            
            _DelayRequestSerialization();
            _DelayUpdateAssignment();
        }

        // Delay requesting serialization. On each call, it will ensure that serialization does not happen until at
        // least some duration after the last request. This delay is used to reduce overall network load when multiple
        // people join at the same time. 
        private void _DelayRequestSerialization()
        {
            // Set the last request time to now.
            _lastSerializationRequestTime = Time.time;
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
            if (Time.time - _lastSerializationRequestTime < DelaySerializationDuration) {
                return;
            }
            
            // Request serialization of the object pool assignments so that everyone else can see the updated
            // assignments. 
            RequestSerialization();
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
            _verifyAssignmentsOnPlayerJoin = false;
            
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
                
                // Object was previously assigned to a player and needs to be cleaned up.
                if (prevAssign != -1)
                {
                    _CleanupPlayerObject(index, prevAssign);
                }
                
                // New assignment is an owner. Assign the object to that player.
                if (newAssign != -1)
                {
                    if (!_SetupPlayerObject(index, newAssign))
                    {
                        requireVerification = true;
                        
                        // Continue the loop early and prevent setting the _prevAssignment array to allow retrying this
                        // index.
                        continue;
                    }
                }
                
                // Update the previous assignment to match new assignment.
                _prevAssignment[index] = newAssign;

                assignmentHasChanged = true;
            }

            // Notify event listener that there was a change in the player/object assignments
            if (assignmentHasChanged && VRC.SDKBase.Utilities.IsValid(poolEventListener))
            {
                poolEventListener.SendCustomEvent(OnAssignmentChangedEvent);
            }

            if (requireVerification)
            {
                if (Networking.IsMaster)
                {
                    // If require verification while master, verify all player assignments
                    _VerifyAllPlayersHaveObjects();
                }
                else
                {
                    // Player was not master, but an invalid player was found. Ignore for now and wait for the next
                    // player join to attempt assigning objects again.
                    _verifyAssignmentsOnPlayerJoin = true;
                }
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
            
            if (!VRC.SDKBase.Utilities.IsValid(player))
            {
                _LogError($"Trying to assign invalid player to object! player: {playerId}, obj: {poolObj.name}");
                return false;
            }
            
            _Log($"Assigning {poolObj.name} to player {playerId}");

            if (player.isLocal && setNetworkOwnershipForPoolObjects)
            {
                Networking.SetOwner(player, poolObj);
            }
            
            // Save the player api assigned to this object.
            _assignedPlayers[index] = player;

            UdonBehaviour poolUdon = (UdonBehaviour)pooledUdon[index];
            poolUdon.SetProgramVariable("Owner", player);
            poolUdon.SendCustomEvent("_OnOwnerSet");
            poolObj.SetActive(true);
                
            // Notify pool listener that a player has joined and the object has been assigned.
            if (VRC.SDKBase.Utilities.IsValid(poolEventListener))
            {
                poolEventListener.SetProgramVariable(PlayerAssignedIdVariableName, playerId);
                poolEventListener.SetProgramVariable(PlayerAssignedPlayerVariableName, player);
                poolEventListener.SetProgramVariable(PlayerAssignedUdonVariableName, poolUdon);
                poolEventListener.SendCustomEvent(OnPlayerAssignedEvent);
            }
            
            // Notify the event listener that the local player has been assigned an object.
            if (player.isLocal && VRC.SDKBase.Utilities.IsValid(poolEventListener))
            {
                poolEventListener.SendCustomEvent(OnLocalPlayerAssignedEvent);
            }

            return true;
        }
        
        // Once an object has been unassigned, clean up the object by removing the cached index and disable the object.
        // Called by everyone when any player leaves the instance. This is to clear out the Owner variable and give an
        // early callback that pool objects can implement. 
        private void _CleanupPlayerObject(int index, int playerId)
        {
            if (playerId == _localPlayer.playerId)
            {
                _LogError($"Cleaning up local player's object while still in the instance! player: {playerId}, obj: {index}");
            }
            
            if (index == -1)
            {
                _LogWarning($"Could not find object for leaving player: {playerId}");
                return;
            }
            
            // Cleanup tags to ensure you can't get this object anymore.
            _SetPlayerObjectIndexTag(playerId, -1);
            
            GameObject poolObj = _poolObjects[index];
            
            // Pool object has already been cleaned up, return early.
            if (!poolObj.activeSelf)
            {
                return;
            }
            
            _Log($"Cleaning up obj {index}: {poolObj.name}, Player: " + playerId);
            
            VRCPlayerApi player = _assignedPlayers[index];
            _assignedPlayers[index] = null;

            UdonBehaviour poolUdon = (UdonBehaviour)pooledUdon[index];
            poolUdon.SendCustomEvent("_OnCleanup");
            poolUdon.SetProgramVariable("Owner", null);
            poolObj.SetActive(false);
            
            // Notify pool listener that a player has left and the object has been unassigned.
            if (VRC.SDKBase.Utilities.IsValid(poolEventListener))
            {
                poolEventListener.SetProgramVariable(PlayerUnassignedIdVariableName, playerId);
                poolEventListener.SetProgramVariable(PlayerUnassignedPlayerVariableName, player);
                poolEventListener.SetProgramVariable(PlayerUnassignedUdonVariableName, poolUdon);
                poolEventListener.SendCustomEvent(OnPlayerUnassignedEvent);
            }
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
        
        // When the local player becomes the new master, cache some values and delay verification of all player's having
        // an object.
        private void _CheckForMasterSwap()
        {
            // Master left and local player is the new master.
            if (!_isMaster && Networking.IsMaster)
            {
                _Log("Local player is now master!");
                _isMaster = true;
                _FillUnclaimedObjectQueue();
                
                // It's possible that previous master has sent assignment data for players that have not yet "joined"
                // for this client. Force add them to the tag system to prevent double assignment for the player.
                _SetPlayerIndexTags();

                // verify all players have objects, but wait a duration to ensure that all players have joined/left.
                SendCustomEventDelayedSeconds(nameof(_VerifyAllPlayersHaveObjects), DelayNewMasterVerificationDuration);
            }
        }
        
        // Go through all assignments and all players and ensure that each player still has
        // an object and each object has a player.
        // O(n)
        // public needed for SendCustomEventDelayedSeconds, but should not be called externally!
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
                _playerObjectIds[count] = index;
                ++count;
                
                // Set tags for used object owners
                // This will give us a constant look up if a user has an assigned object.
                _SetTag(PlayerPoolOwnerTagPrefix + ownerId, TagValid);
            }


            // Go through each player and find if that player has an object, otherwise assign them one.
            VRCPlayerApi.GetPlayers(_allPlayersTemp);
            for (int index = 0; index < MaxPlayers; ++index)
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

                string tagName = PlayerPoolOwnerTagPrefix + ownerId;
                string tagValue = _GetTag(tagName);
                _SetTag(tagName, "");
                
                // If tag exists, then this player is no longer in the instance and needs to be removed from the
                // assignment array.
                if (tagValue == TagValid)
                {
                    int objIndex = _playerObjectIds[index];
                    _LogWarning($"Missing player still owned an object during verification! Player: {ownerId}, Obj: {objIndex}");
                    
                    // Cleanup the player object first and then remove the assignment.
                    _CleanupPlayerObject(objIndex, ownerId);
                    _ReturnPlayerObject(objIndex, ownerId);
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
                Debug.Log($"{LogPrefix}[{_poolTagId}] {message}");
            }
        }

        private void _LogWarning(string message)
        {
            Debug.LogWarning($"{LogPrefix}[{_poolTagId}] {message}");
        }
        
        private void _LogError(string message)
        {
            Debug.LogError($"{LogPrefix}[{_poolTagId}] {message}");
        }

        #endregion
    }
}

