
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

namespace Cyan.PlayerObjectPool
{
    [AddComponentMenu("")]
    [ExecuteInEditMode]
    public class CyanPoolSetupHelper : MonoBehaviour
    {
        public GameObject cyanPlayerObjectPoolPrefab;

        public GameObject poolObjectPrefab;

        [HideInInspector, SerializeField]
        private CyanPlayerObjectPool pool;
        [SerializeField] 
        private CyanPlayerObjectAssigner assigner;
        
#if UNITY_EDITOR
        // On dragging this as a prefab into the scene, create the necessary number of pool objects based on the
        // Object Pool's settings. If no Object Pool exists in the scene, add one. 
        public void Awake()
        {
            if (!ShouldInitialize())
            {
                return;
            }

            hideFlags = HideFlags.DontSaveInBuild;

            GetPoolUdon();
            VerifyPoolSize();
        }

        // Check if this object is in a valid scene and not in a prefab editor.
        public bool ShouldInitialize()
        {
            if (Application.isPlaying)
            {
                return false;
            }
            
            if (!gameObject.scene.isLoaded)
            {
                return false;
            }

#if UNITY_EDITOR
            if (EditorSceneManager.IsPreviewSceneObject(this))
            {
                return false;
            }
#endif
            
            return true;
        }

        public CyanPlayerObjectPool GetPoolUdon()
        {
            // If this object shouldn't be initialized, always return null to prevent the scene from being edited.
            if (!ShouldInitialize())
            {
                return null;
            }
            
            // If the udon behaviour has been cached, return it.
            if (pool != null)
            {
                return pool;
            }

            // Given an object, return the first UdonBehaviour that is of type CyanPlayerObjectPool contained in this
            // object's hierarchy.
            CyanPlayerObjectPool GetPoolUdon(GameObject obj)
            {
                CyanPlayerObjectPool[] pools = obj.GetComponentsInChildren<CyanPlayerObjectPool>(true);

                if (pools.Length == 0)
                {
                    return null;
                }

                return pools[0];
            }
            
            // Go through every object to find the Object pool
            foreach (var obj in gameObject.scene.GetRootGameObjects())
            {
                pool = GetPoolUdon(obj);

                if (pool != null)
                {
                    break;
                }
            }

            // Object pool does not currently exist in the scene. Spawn a new one.
            if (pool == null && cyanPlayerObjectPoolPrefab != null)
            {
                GameObject poolPrefab = PrefabUtility.InstantiatePrefab(cyanPlayerObjectPoolPrefab) as GameObject;
                Undo.RegisterCreatedObjectUndo(poolPrefab, "Create Object Pool Prefab");

                pool = GetPoolUdon(poolPrefab);
            }
            
            return pool;
        }

        // Get the current size of the Object Pool.
        public int GetPoolSize()
        {
            return GetPoolUdon().poolSize;
        }
        
        public int GetObjectCount()
        {
            return GetPoolParentTransform().childCount;
        }

        public Transform GetPoolParentTransform()
        {
            if (assigner.poolObjectsParent)
            {
                return assigner.poolObjectsParent;
            }
            return assigner.transform;
        }

        // Update the number of pool objects for this assigner based on the current size of the Object pool.
        public void UpdatePoolSize()
        {
            UpdatePoolSize(GetPoolSize());
        }

        // Delete all children under this Object Assigner.
        public void ClearChildren()
        {
            Transform poolTransform = GetPoolParentTransform();
            while (GetObjectCount() > 0)
            {
                GameObject poolObject = poolTransform.GetChild(0).gameObject;
                Undo.DestroyObjectImmediate(poolObject);
            }
        }

        public void RespawnAllPoolObjects()
        {
            if (!ShouldInitialize())
            {
                return;
            }
            
            // No pool object prefab to update size.
            if (poolObjectPrefab == null)
            {
                return;
            }

            ClearChildren();
            UpdatePoolSize();
        }

        // Verify that the pool's current size matches the Object Pool's size.
        public void VerifyPoolSize()
        {
            if (!ShouldInitialize())
            {
                return;
            }
            
            int size = GetPoolSize();
            if (GetObjectCount() != size)
            {
                UpdatePoolSize(size);
            }
        }
        
        // Given a size, spawn new pooled objects or delete old objects until this object assigner has the appropriate size.
        public void UpdatePoolSize(int size)
        {
            // No pool object prefab to update size.
            if (poolObjectPrefab == null)
            {
                return;
            }
            
            Transform poolTransform = GetPoolParentTransform();
            // Too many children, delete the last items until size is met.
            while (GetObjectCount() > size)
            {
                GameObject poolObject = poolTransform.GetChild(GetObjectCount() - 1).gameObject;
                Undo.DestroyObjectImmediate(poolObject);
            }

            // Too few children, spawn new items until size is met.
            while (GetObjectCount() < size)
            {
                GameObject poolObject = null;
                // If pool object is a prefab, spawn as a prefab instance
                if (PrefabUtility.IsPartOfPrefabAsset(poolObjectPrefab))
                {
                    poolObject = (GameObject)PrefabUtility.InstantiatePrefab(poolObjectPrefab, poolTransform);
                }
                // If pool object is not a prefab, instantiate as normal gameobject.
                else
                {
                    poolObject = Instantiate(poolObjectPrefab, poolTransform);
                    poolObject.name = poolObjectPrefab.name;
                }
                
                // Ensure that no siblings have the same name. This adds the (#) to the end of the object. 
                GameObjectUtility.EnsureUniqueNameForSibling(poolObject);
                
                // Register that the object has been created to ensure that undo deletes them properly.
                Undo.RegisterCreatedObjectUndo(poolObject, "Create Pool Object");
            }
        }
#endif
    }
    
#if UNITY_EDITOR
    [CustomEditor(typeof(CyanPoolSetupHelper))]
    public class CyanPoolSetupHelperEditor : Editor
    {
        // Render nothing so that people use the CyanPlayerObjectAssigner inspector
        public override void OnInspectorGUI() { }
    }
#endif
}

