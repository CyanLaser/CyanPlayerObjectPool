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
            }
        }

        public override void OnInspectorGUI()
        {
            serializedObject.UpdateIfRequiredOrScript();
            
            CyanPlayerObjectPoolEditorHelpers.RenderHeader("CyanPlayerObjectAssigner");
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

                // Only show Pool options when in a valid scene and not a prefab editor.
                if (shouldInitialize)
                {
                    if (GUILayout.Button("Ping Object Pool"))
                    {
                        EditorGUIUtility.PingObject(_setupHelper.GetPoolUdon().gameObject);
                    }
                
                    EditorGUI.BeginDisabledGroup(true);

                    EditorGUILayout.IntField("Pool Size", _setupHelper.GetPoolSize());
                
                    EditorGUI.EndDisabledGroup();
                }

                EditorGUI.BeginChangeCheck();

                EditorGUILayout.PropertyField(_poolObjectProp);

                bool changes = EditorGUI.EndChangeCheck();

                _setupHelperSerializedObject.ApplyModifiedProperties();
                
                // Only apply changes when the scene is valid and not in a prefab editor.
                if (changes && shouldInitialize)
                {
                    _setupHelper.ClearChildren();
                    _setupHelper.UpdatePoolSize();
                }
            
                CyanPlayerObjectPoolEditorHelpers.RemoveIndent();
            }
            
            EditorGUILayout.EndVertical();
        }
    }
}