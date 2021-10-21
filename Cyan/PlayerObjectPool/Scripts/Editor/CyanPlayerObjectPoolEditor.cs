
using System.Collections.Generic;
using UdonSharpEditor;
using UnityEditor;
using UnityEngine;
using VRC.Udon;

namespace Cyan.PlayerObjectPool
{
    [CustomEditor(typeof(CyanPlayerObjectPool))]
    public class CyanPlayerObjectPoolEditor : Editor
    {
        private readonly GUIContent _settingsFoldoutGuiContent = new GUIContent("Pool Settings", "");
        private readonly GUIContent _poolAssignersFoldoutGuiContent = new GUIContent("Pool Types", "");
        
        
        private Texture2D _typesBackgroundTexture;

        private CyanPlayerObjectPool _instance;
        private UdonBehaviour _udon;

        private int _poolSize;
        
        private SerializedProperty _sizeProp;
        private SerializedProperty _debugProp;

        private bool _showSettings = true;
        private bool _showPoolAssigners = true;


        private void Awake()
        {
            _instance = (CyanPlayerObjectPool) target;
            _udon = UdonSharpEditorUtility.GetBackingUdonBehaviour(_instance);
            _poolSize = _instance.poolSize;
            
            _sizeProp = serializedObject.FindProperty(nameof(CyanPlayerObjectPool.poolSize));
            _debugProp = serializedObject.FindProperty(nameof(CyanPlayerObjectPool.printDebugLogs));
            
            _typesBackgroundTexture = CyanPlayerObjectPoolEditorHelpers.CreateTexture(3, 3, 
                (x, y) => x == 1 && y == 1 
                    ? CyanPlayerObjectPoolEditorHelpers.BackgroundColor 
                    : CyanPlayerObjectPoolEditorHelpers.LineColorDark);
        }

        public override void OnInspectorGUI()
        {
            serializedObject.UpdateIfRequiredOrScript();

            CyanPlayerObjectPoolEditorHelpers.RenderHeader("CyanPlayerObjectPool");
            RenderSettings();
            
            serializedObject.ApplyModifiedProperties();
            
            // Prevent modifying the scene if this is part of a prefab editor.
            if (!_udon.gameObject.scene.isLoaded || !PrefabUtility.IsPartOfNonAssetPrefabInstance(_instance.transform.root))
            {
                return;
            }
            
            // TODO check for multiple pools in the scene and display warning
            
            List<CyanPoolSetupHelper> helpers = new List<CyanPoolSetupHelper>(FindObjectsOfType<CyanPoolSetupHelper>());
            // Sort based on name
            helpers.Sort((h1, h2) => h1.name.CompareTo(h2.name));
            
            UpdateHelpers(helpers);
            RenderObjectAssigners(helpers);
        }

        private void RenderSettings()
        {
            GUILayout.Space(5);
            
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            
            _showSettings = CyanPlayerObjectPoolEditorHelpers.RenderFoldout(_showSettings, _settingsFoldoutGuiContent);

            if (_showSettings)
            {
                CyanPlayerObjectPoolEditorHelpers.AddIndent();
                
                EditorGUILayout.PropertyField(_sizeProp);
                EditorGUILayout.PropertyField(_debugProp);
                
                CyanPlayerObjectPoolEditorHelpers.RemoveIndent();
            }
            
            EditorGUILayout.EndVertical();
        }

        private void UpdateHelpers(List<CyanPoolSetupHelper> helpers)
        {
            // If the size has changed, notify all helpers to update the spawned number of pool objects.
            int newSize = _sizeProp.intValue;
            if (_poolSize != newSize)
            {
                _poolSize = newSize;
                foreach (var helper in helpers)
                {
                    helper.UpdatePoolSize(_poolSize);
                }
            }
        }

        private void RenderObjectAssigners(List<CyanPoolSetupHelper> helpers)
        {
            GUILayout.Space(5);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            _showPoolAssigners = CyanPlayerObjectPoolEditorHelpers.RenderFoldout(_showPoolAssigners, _poolAssignersFoldoutGuiContent);

            if (_showPoolAssigners)
            {
                GUILayout.Space(5);

                var boxStyle = new GUIStyle
                {
                    border = new RectOffset(1, 1, 1, 1), 
                    normal =
                    {
                        background = _typesBackgroundTexture
                    }
                };

                Rect rect = EditorGUILayout.BeginVertical(boxStyle);
                float width = rect.width;
                float height = rect.height - 1;
                float xStart = rect.x;
                float sectionHeight = 24;
                float buttonHeight = 18;
                float labelHeight = EditorGUIUtility.singleLineHeight;
                
                float between = 9;
                float betweenHalf = Mathf.Floor(between / 2f); 
                float indexLabelWidth = 20;
                float pingWidth = 40;
                
                // Create vertical bars separating the sections
                GUI.Box(new Rect(rect.x + indexLabelWidth + between, rect.y + 1, 1, height), GUIContent.none, boxStyle);
                GUI.Box(new Rect(rect.x + indexLabelWidth + pingWidth + between * 2, rect.y + 1, 1, height), GUIContent.none, boxStyle);
                
                for (int cur = 0; cur < helpers.Count; ++cur)
                {
                    var helper = helpers[cur];

                    rect = EditorGUILayout.BeginHorizontal(GUILayout.Height(sectionHeight));

                    if (cur + 1 < helpers.Count)
                    {
                        // Create horizontal bar separating the sections
                        GUI.Box(new Rect(rect.x, rect.yMax, width, 1), GUIContent.none, boxStyle);
                    }

                    float textY = rect.y + Mathf.Ceil((rect.height - labelHeight) / 2f);
                    float buttonY = rect.y + Mathf.Ceil((rect.height - buttonHeight) / 2f);
                    
                    float indexLabelStartX = xStart + betweenHalf;
                    float pingStart = indexLabelStartX + indexLabelWidth + between;
                    float objectLabelStartX = pingStart + pingWidth + between;
                    float objectLabelWidth = width - objectLabelStartX - betweenHalf;

                    Rect indexRect = new Rect(indexLabelStartX, textY, indexLabelWidth, labelHeight);
                    Rect pingRect = new Rect(pingStart, buttonY, pingWidth, buttonHeight);
                    Rect objectLabelRect = new Rect(objectLabelStartX, textY, objectLabelWidth, labelHeight);
                    
                    GUI.Label(indexRect, (1 + cur).ToString().PadLeft(2));
                    
                    if (GUI.Button(pingRect, "Ping"))
                    {
                        EditorGUIUtility.PingObject(helper);
                    }

                    GUIContent objectContent =
                        new GUIContent(helper.name, VRC.Tools.GetGameObjectPath(helper.gameObject));
                    GUI.Label(objectLabelRect, objectContent);
                    
                    GUILayout.Space(1);
                    EditorGUILayout.EndHorizontal();
                }
            
                EditorGUILayout.EndVertical();
                GUILayout.Space(5);
            }

            EditorGUILayout.EndVertical();
        }
    }
}

