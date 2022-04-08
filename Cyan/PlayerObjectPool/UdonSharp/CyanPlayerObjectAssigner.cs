
using System;
using JetBrains.Annotations;
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

namespace Cyan.PlayerObjectPool
{
    /// <summary>
    /// Object pool assigner will take the indices assigned to players from the Object Pool system and assign them the
    /// corresponding objects. You may have multiple Object Assigner systems in a world.
    /// </summary>
    [AddComponentMenu("")] // Do not show this component in the AddComponent menu since it is for Udon only.
    [DefaultExecutionOrder(1000)] // Have high execution order to ensure that the pool system and children scripts are initialized.
    [UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
    public class CyanPlayerObjectAssigner : UdonSharpBehaviour
    {
        #region Constants

        // Get reference to the pool path constant.
        public const string PoolPathTag = CyanPlayerObjectPool.PoolPathTag;

        
        #region Event Constants

        /// <summary>
        /// Event name that will be sent to the pool event listener when the local player is assigned an object.
        /// </summary>
        [PublicAPI]
        public const string OnLocalPlayerAssignedEvent = "_OnLocalPlayerAssigned";
        
        /// <summary>
        /// Variable name that will be set before the OnPlayerAssignedEvent is sent to the pool event listener.
        /// This variable will store the player api data of the last player whose object was assigned.
        /// </summary>
        [PublicAPI]
        public const string PlayerAssignedPlayerVariableName = "playerAssignedPlayer";
        /// <summary>
        /// Variable name that will be set before the OnPlayerAssignedEvent is sent to the pool event listener.
        /// This variable will store the object index of the last player whose object was assigned.
        /// </summary>
        [PublicAPI]
        public const string PlayerAssignedIndexVariableName = "playerAssignedIndex";
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
        /// This variable will store the player api data of the last player whose object was unassigned.
        /// </summary>
        [PublicAPI]
        public const string PlayerUnassignedPlayerVariableName = "playerUnassignedPlayer";
        /// <summary>
        /// Variable name that will be set before the OnPlayerUnassignedEvent is sent to the pool event listener.
        /// This variable will store the object index of the last player whose object was unassigned.
        /// </summary>
        [PublicAPI]
        public const string PlayerUnassignedIndexVariableName = "playerUnassignedIndex";
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
        
        #endregion
        
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
        /// When true, on assignment, the GameObject will be enabled and on unassignment, the GameObject will be
        /// disabled. When disabled, you will need to handle the object's current active state manually.
        /// </summary>
        [Tooltip("When true, on assignment, the pool object's GameObject will be enabled and on unassignment, the pool object's GameObject will be disabled. When false, you will need to handle the object's current active state manually.")]
        public bool disableUnassignedObjects = true;
        
        /// <summary>
        /// UdonBehaviour that will listen for different events from the Object pool system. 
        /// Current events:
        /// - _OnLocalPlayerAssigned
        /// - _OnPlayerAssigned
        /// - _OnPlayerUnassigned
        /// </summary>
        [Tooltip("UdonBehaviour that will listen for different events from the Object pool system. Current events: _OnLocalPlayerAssigned, _OnPlayerAssigned, _OnPlayerUnassigned")]
        public UdonBehaviour poolEventListener;

        /// <summary>
        /// The transform used to store the pool objects. If empty, this transform will be used. This can be used to prevent issues with execution order.
        /// https://feedback.vrchat.com/vrchat-udon-closed-alpha-bugs/p/1123-udon-objects-with-udon-children-initialize-late-despite-execution-order-ove
        /// </summary>
        [Tooltip("The transform used to store the pool objects. If empty, this transform will be used. This can be used to prevent issues with execution order.")]
        public Transform poolObjectsParent;
        
        #endregion
        
        
        
        // Array of UdonBehaviours that are in the pool. Type is Component so that it can be properly casted to either
        // UdonBehaviour or custom UdonSharpBehaviour while still having valid type checking.
        [NonSerialized, PublicAPI]
        public Component[] pooledUdon = new Component[0];

        // The list of GameObjects in the pool. 
        private GameObject[] _poolObjects = new GameObject[0];
        
        // Reference to the object pool system.
        private CyanPlayerObjectPool _objectPool;

        // Local version of the assignment array.
        private int[] _localAssignment = new int[0];
        
        // Temporary array to hold the pooled objects.
        private Component[] _poolObjectsTemp = new Component[0];

        // If this pool has been initialized and contains objects to distribute. If there are no objects, then this
        // system will only forward events to the pool event listener.
        private bool _enabledAndInitialized = false;

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
            int index = _objectPool._GetPlayerPoolIndexById(playerId);

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
            // Pool is not enabled, so always return null.
            if (!_enabledAndInitialized)
            {
                return null;
            }
            
            int index = _objectPool._GetPlayerPoolIndexById(playerId);

            if (index == -1)
            {
                _LogWarning($"Could not find object for player: {playerId}");
                return null;
            }

            return pooledUdon[index];
        }

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
            return _objectPool._GetPlayerPoolIndex(player);
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
            return _objectPool._GetPlayerPoolIndexById(playerId);
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
            return _objectPool._GetOrderedPlayers();
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
            return _objectPool._GetOrderedPlayersNoAlloc(players);
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
            // Pool is not enabled, so always return empty list.
            if (!_enabledAndInitialized)
            {
                return new Component[0];
            }
            
            // Go through assignment array and find all valid players and store the respective pooled object into a
            // temporary location.
            int count = 0;
            int size = _localAssignment.Length;
            for (int i = 0; i < size; ++i)
            {
                // Get player id for this object's index.
                int id = _localAssignment[i];
                
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
            // Pool is not enabled, so always return empty list.
            if (!_enabledAndInitialized)
            {
                return 0;
            }
            
            // The input array is null. No pool objects can be stored in it. Return early.
            if (pooledObjects == null)
            {
                _LogError("_GetActivePoolObjectsNoAlloc provided with a null array!");
                return 0;
            }
            
            // Go through the assignment array and find all valid players.
            int count = 0;
            int size = _localAssignment.Length;
            int maxCount = pooledObjects.Length;
            for (int i = 0; i < size; ++i)
            {
                // Get player id for this object's index.
                int id = _localAssignment[i];
                
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

        [NonSerialized, PublicAPI] 
        public VRCPlayerApi playerInput;
        
        [NonSerialized, PublicAPI] 
        public int playerIdInput;

        [NonSerialized, PublicAPI] 
        public VRCPlayerApi[] playerArrayInput;

        [NonSerialized, PublicAPI] 
        public Component[] poolObjectArrayInput;
        
        [NonSerialized, PublicAPI]
        public GameObject poolObjectOutput;
        
        [NonSerialized, PublicAPI] 
        public UdonBehaviour poolUdonOutput;
        
        [NonSerialized, PublicAPI] 
        public VRCPlayerApi[] playerArrayOutput;
        
        [NonSerialized, PublicAPI] 
        public int playerCountOutput;

        [NonSerialized, PublicAPI] 
        public Component[] poolObjectArrayOutput;
        
        [NonSerialized, PublicAPI] 
        public int poolObjectCountOutput;
        
        [NonSerialized, PublicAPI] 
        public int playerIndexOutput;
        
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
        
        
        void Start()
        {
            string poolPath = Networking.LocalPlayer.GetPlayerTag(PoolPathTag);
            if (string.IsNullOrEmpty(poolPath))
            {
                _LogError("Could not find path to CyanPlayerObjectPool system! Are you sure it is in the scene and enabled?");
                return;
            }
            
            _objectPool = GameObject.Find(poolPath).GetComponent<CyanPlayerObjectPool>();

            if (poolObjectsParent == null)
            {
                poolObjectsParent = transform;
            }
            
            int assignerSize = poolObjectsParent.childCount;
            int poolSize = _objectPool.poolSize;
            
            // Pool has invalid size, return early without initializing system.
            if (assignerSize > 0 && assignerSize < poolSize)
            {
                _LogError($"Pool Assigner does not have enough objects for the pool size! Pool size: {poolSize}, Assigner size: {assignerSize}");
                return;
            }

            if (assignerSize > poolSize)
            {
                _LogWarning($"Pool Assigner has more objects than needed for the pool size! Pool size: {poolSize}, Assigner size: {assignerSize}");
            }
            
            // Initialize local assignments to be invalid.
            _localAssignment = new int[poolSize];
            for (int i = 0; i < poolSize; ++i)
            {
                _localAssignment[i] = -1;
            }
            
            // Register this object assigner so that it will get events for object assigned and unassigned.
            _objectPool._RegisterObjectAssigner(this);
            
            // This pool has no children, disable self, but only pass assignment events to the Pool Event Listener
            if (assignerSize == 0)
            {
                _LogDebug("Pool Assigner has no objects.");
                return;
            }
            
            _LogDebug($"Initializing object pool assigner with {poolSize} objects.");

            _poolObjects = new GameObject[poolSize];
            pooledUdon = new Component[poolSize];
            
            // Initialize temp arrays to not need to recreate them every time.
            _poolObjectsTemp = new Component[poolSize];
            
            // Go through and get the pool objects. 
            for (int i = 0; i < poolSize; ++i)
            {
                Transform child = poolObjectsParent.GetChild(i);
                GameObject poolObj = child.gameObject;
                _poolObjects[i] = poolObj;
                pooledUdon[i] = poolObj.GetComponent(typeof(UdonBehaviour));
                if (disableUnassignedObjects)
                {
                    poolObj.SetActive(false);
                }
            }
            
            // Delete extra objects since they are not needed
            for (int i = poolSize; i < assignerSize; ++i)
            {
                Transform child = poolObjectsParent.GetChild(i);
                GameObject poolObj = child.gameObject;
                Destroy(poolObj);
            }
            
            _enabledAndInitialized = true;
        }

        #region Object Pool Listener Methods
        
        /// <summary>
        /// Method that is called by the Object Pool System when a player has been assigned an index.
        /// </summary>
        /// <param name="player">
        /// The player that has been assigned an index.
        /// </param>
        /// <param name="index">
        /// The index which the player was assigned.
        /// </param>
        public void _OnPlayerAssigned(VRCPlayerApi player, int index)
        {
            _localAssignment[index] = player.playerId;

            UdonBehaviour poolUdon = null;
            bool isLocal = player.isLocal;
            
            // Only update objects if the pool has been enabled and initialized.
            // Otherwise, only forward events to pool listeners. 
            if (_enabledAndInitialized)
            {
                GameObject poolObj = _poolObjects[index];

                if (setNetworkOwnershipForPoolObjects && isLocal)
                {
                    Networking.SetOwner(player, poolObj);
                }
            
#if UNITY_EDITOR
                // If in editor, auto set the owner
                if (setNetworkOwnershipForPoolObjects)
                {
                    Networking.SetOwner(player, poolObj);
                }
#endif

                poolUdon = (UdonBehaviour)pooledUdon[index];
                poolUdon.SetProgramVariable("Owner", player);
                poolUdon.SendCustomEvent("_OnOwnerSet");
                if (disableUnassignedObjects)
                {
                    poolObj.SetActive(true);
                }
            }
            
            // Notify pool listener that a player has joined and the object has been assigned.
            if (VRC.SDKBase.Utilities.IsValid(poolEventListener))
            {
                poolEventListener.SetProgramVariable(PlayerAssignedPlayerVariableName, player);
                poolEventListener.SetProgramVariable(PlayerAssignedIndexVariableName, index);
                poolEventListener.SetProgramVariable(PlayerAssignedUdonVariableName, poolUdon);
                poolEventListener.SendCustomEvent(OnPlayerAssignedEvent);
            }
            
            if (isLocal && VRC.SDKBase.Utilities.IsValid(poolEventListener))
            {
                poolEventListener.SendCustomEvent(OnLocalPlayerAssignedEvent);
            }
        }

        /// <summary>
        /// Method that is called by the Object Pool System when a player has been unassigned an index.
        /// </summary>
        /// <param name="player">
        /// The player that has been unassigned the index.
        /// </param>
        /// <param name="index">
        /// The index which the player was originally assigned.
        /// </param>
        public void _OnPlayerUnassigned(VRCPlayerApi player, int index)
        {
            _localAssignment[index] = -1;

            UdonBehaviour poolUdon = null;
            
            // Only update objects if the pool has been enabled and initialized.
            // Otherwise, only forward events to pool listeners. 
            if (_enabledAndInitialized)
            {
                GameObject poolObj = _poolObjects[index];

                // Pool object has already been cleaned up, return early.
                if (!poolObj.activeSelf)
                {
                    return;
                }

                poolUdon = (UdonBehaviour) pooledUdon[index];
                poolUdon.SendCustomEvent("_OnCleanup");
                poolUdon.SetProgramVariable("Owner", null);
                if (disableUnassignedObjects)
                {
                    poolObj.SetActive(false);
                }
            }

            // Notify pool listener that a player has left and the object has been unassigned.
            if (VRC.SDKBase.Utilities.IsValid(poolEventListener))
            {
                poolEventListener.SetProgramVariable(PlayerUnassignedPlayerVariableName, player);
                poolEventListener.SetProgramVariable(PlayerUnassignedIndexVariableName, index);
                poolEventListener.SetProgramVariable(PlayerUnassignedUdonVariableName, poolUdon);
                poolEventListener.SendCustomEvent(OnPlayerUnassignedEvent);
            }
        }

        #endregion


        #region Logging

        private string _GetLogPrefix()
        {
            return $"[Cyan][PoolAssigner][{name}]";
        }
        
        private void _LogInfo(string message)
        {
            Debug.Log($"{_GetLogPrefix()} {message}");
        }
        
        private void _LogDebug(string message)
        {
            if (VRC.SDKBase.Utilities.IsValid(_objectPool) && _objectPool.printDebugLogs)
            {
                Debug.Log($"{_GetLogPrefix()} {message}");
            }
        }

        private void _LogWarning(string message)
        {
            Debug.LogWarning($"{_GetLogPrefix()} {message}");
        }
        
        private void _LogError(string message)
        {
            Debug.LogError($"{_GetLogPrefix()} {message}");
        }

        #endregion
    }
}