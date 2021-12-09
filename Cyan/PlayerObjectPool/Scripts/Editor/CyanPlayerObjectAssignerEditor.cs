using UdonSharpEditor;
using UnityEditor;
using UnityEngine;

namespace Cyan.PlayerObjectPool
{
    [CustomEditor(typeof(CyanPlayerObjectAssigner))]
    public class CyanPlayerObjectAssignerEditor : Editor
    {
        private readonly GUIContent _runtimeSettingsFoldoutGuiContent = new GUIContent("Pool Settings", "");
        private readonly GUIContent _editorFoldoutGuiContent = new GUIContent("Editor Settings", "");
        
        private SerializedProperty _ownershipProp;
        private SerializedProperty _eventListenerProp;
        
        // CyanPoolSetupHelper properties
        private CyanPoolSetupHelper _setupHelper;
        private SerializedObject _setupHelperSerializedObject;
        private SerializedProperty _poolObjectProp;
        
        private bool _showSettings = true;
        private bool _showEditorSettings = true;
        
        private void Awake()
        {
            _ownershipProp = serializedObject.FindProperty(nameof(CyanPlayerObjectAssigner.setNetworkOwnershipForPoolObjects));
            _eventListenerProp = serializedObject.FindProperty(nameof(CyanPlayerObjectAssigner.poolEventListener));
            
            _setupHelper = (target as CyanPlayerObjectAssigner).GetComponent<CyanPoolSetupHelper>();
            if (_setupHelper != null)
            {
                _setupHelperSerializedObject = new SerializedObject(_setupHelper);
                _poolObjectProp = _setupHelperSerializedObject.FindProperty(nameof(CyanPoolSetupHelper.poolObjectPrefab));

                _setupHelper.VerifyPoolSize();
            }
        }

        public override void OnInspectorGUI()
        {
            if (UdonSharpGUI.DrawDefaultUdonSharpBehaviourHeader(target)) return;
            if (!UdonSharpEditorUtility.IsProxyBehaviour((CyanPlayerObjectAssigner)target)) return;
            
            serializedObject.UpdateIfRequiredOrScript();
            
            CyanPlayerObjectPoolEditorHelpers.RenderHeader("Cyan Player Object Assigner");
            RenderSettings();
            
            RenderHelperSettings();

            serializedObject.ApplyModifiedProperties();
        }
        
        private void RenderSettings()
        {
            GUILayout.Space(5);
            
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            
            _showSettings = CyanPlayerObjectPoolEditorHelpers.RenderFoldout(_showSettings, _runtimeSettingsFoldoutGuiContent);

            if (_showSettings)
            {
                CyanPlayerObjectPoolEditorHelpers.AddIndent();
                
                EditorGUILayout.PropertyField(_ownershipProp, new GUIContent("Assign Network Owner", _ownershipProp.tooltip));
                EditorGUILayout.PropertyField(_eventListenerProp);
                
                CyanPlayerObjectPoolEditorHelpers.RemoveIndent();
            }
            
            EditorGUILayout.EndVertical();
        }

        private void RenderHelperSettings()
        {
            if (_setupHelper == null)
            {
                return;
            }

            bool shouldInitialize = _setupHelper.ShouldInitialize();
            
            _setupHelperSerializedObject.UpdateIfRequiredOrScript();
            
            GUILayout.Space(5);
            
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            
            _showEditorSettings = CyanPlayerObjectPoolEditorHelpers.RenderFoldout(_showEditorSettings, _editorFoldoutGuiContent);

            if (_showEditorSettings)
            {
                CyanPlayerObjectPoolEditorHelpers.AddIndent();

                int assignerSize = _setupHelper.GetObjectCount();
                
                // Only show Pool options when in a valid scene and not a prefab editor.
                if (shouldInitialize)
                {
                    int poolSize = _setupHelper.GetPoolSize();
                    
                    EditorGUILayout.BeginHorizontal();

                    if (GUILayout.Button("Ping Object Pool"))
                    {
                        EditorGUIUtility.PingObject(_setupHelper.GetPoolUdon().gameObject);
                    }
                    
                    GUILayout.Space(5);
                    
                    if (GUILayout.Button("Respawn All Pool Objects"))
                    {
                        _setupHelper.RespawnAllPoolObjects();
                    }
                    
                    EditorGUILayout.EndHorizontal();
                
                    EditorGUI.BeginDisabledGroup(true);

                    EditorGUILayout.IntField("Pool Size", poolSize);

                    EditorGUI.EndDisabledGroup();
                    
                    if (poolSize != assignerSize && assignerSize > 0)
                    {
                        EditorGUILayout.HelpBox($"Pool Object count does not match Pool Size! This assigner has {assignerSize} objects when the pool is expecting {poolSize} objects.", MessageType.Error);
                    }
                }

                EditorGUI.BeginChangeCheck();

                EditorGUILayout.PropertyField(_poolObjectProp);

                bool changes = EditorGUI.EndChangeCheck();

                _setupHelperSerializedObject.ApplyModifiedProperties();

                if (_poolObjectProp.objectReferenceValue == null && assignerSize == 0)
                {
                    GUILayout.Space(5);
                    EditorGUILayout.HelpBox("Pool Object has not been assigned. Assignment and Unassignment events will still be sent to the Pool Event Listener but no object will be assigned.", MessageType.Warning);
                }
                
                // Only apply changes when the scene is valid and not in a prefab editor.
                if (changes && shouldInitialize)
                {
                    _setupHelper.RespawnAllPoolObjects();
                }
            
                CyanPlayerObjectPoolEditorHelpers.RemoveIndent();
            }
            
            EditorGUILayout.EndVertical();
        }
    }
}