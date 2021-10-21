
using UnityEngine;
using VRC.Udon;

#if UNITY_EDITOR
using UdonSharpEditor;
using UnityEditor;
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
        private UdonBehaviour _poolUdon;
        
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
            UpdatePoolSize();
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
            if (!PrefabUtility.IsPartOfNonAssetPrefabInstance(transform.root))
            {
                return false;
            }
#endif
            
            return true;
        }

        public UdonBehaviour GetPoolUdon()
        {
            if (!ShouldInitialize())
            {
                return null;
            }
            
            if (_poolUdon != null)
            {
                return _poolUdon;
            }

            foreach (var obj in gameObject.scene.GetRootGameObjects())
            {
                foreach (var udon in obj.GetComponentsInChildren<UdonBehaviour>(true))
                {
                    if (UdonSharpEditorUtility.GetUdonSharpBehaviourType(udon) == typeof(CyanPlayerObjectPool))
                    {
                        _poolUdon = udon;
                        break;
                    }
                }

                if (_poolUdon)
                {
                    break;
                }
            }

            if (_poolUdon == null)
            {
                GameObject poolPrefab = PrefabUtility.InstantiatePrefab(cyanPlayerObjectPoolPrefab) as GameObject;
                Undo.RegisterCreatedObjectUndo(poolPrefab, "Create Object Pool Prefab");

                foreach (var udon in poolPrefab.GetComponentsInChildren<UdonBehaviour>(true))
                {
                    if (UdonSharpEditorUtility.GetUdonSharpBehaviourType(udon) == typeof(CyanPlayerObjectPool))
                    {
                        _poolUdon = udon;
                        break;
                    }
                }
            }
            
            return _poolUdon;
        }

        public int GetPoolSize()
        {
            GetPoolUdon().publicVariables.TryGetVariableValue(nameof(CyanPlayerObjectPool.poolSize), out int value);
            return value;
        }

        public void UpdatePoolSize()
        {
            UpdatePoolSize(GetPoolSize());
        }

        public void ClearChildren()
        {
            while (transform.childCount > 0)
            {
                GameObject poolObject = transform.GetChild(transform.childCount - 1).gameObject;
                Undo.DestroyObjectImmediate(poolObject);
            }
        }
        
        public void UpdatePoolSize(int size)
        {
            if (poolObjectPrefab == null)
            {
                ClearChildren();
                return;
            }
            
            while (transform.childCount > size)
            {
                GameObject poolObject = transform.GetChild(transform.childCount - 1).gameObject;
                Undo.DestroyObjectImmediate(poolObject);
            }

            while (transform.childCount < size)
            {
                GameObject poolObject = null;
                if (PrefabUtility.IsPartOfPrefabAsset(poolObjectPrefab))
                {
                    poolObject = (GameObject)PrefabUtility.InstantiatePrefab(poolObjectPrefab, transform);
                }
                else
                {
                    poolObject = Instantiate(poolObjectPrefab, transform);
                    poolObject.name = poolObjectPrefab.name;
                }
                Undo.RegisterCreatedObjectUndo(poolObject, "Create Pool Object");
                GameObjectUtility.EnsureUniqueNameForSibling(poolObject);
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

