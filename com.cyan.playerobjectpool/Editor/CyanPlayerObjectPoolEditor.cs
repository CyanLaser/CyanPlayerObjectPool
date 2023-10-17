
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using VRC.Core;
using VRC.SDKBase.Editor.Api;
using VRC.Udon;
using VRC.SDK3.Editor;
using VRC.SDKBase.Editor;

namespace Cyan.PlayerObjectPool
{
    [CustomEditor(typeof(CyanPlayerObjectPool))]
    public class CyanPlayerObjectPoolEditor : Editor
    {
        private readonly GUIContent _settingsFoldoutGuiContent = new GUIContent("Pool Settings", "");
        private readonly GUIContent _poolAssignersFoldoutGuiContent = new GUIContent("Pool Types", "");

        // Minimum world capacity is 1.
        private const int MinPoolObjects = 1;
        // Maximum world capacity is ~82, but allowing more since it doesn't break the system.
        private const int MaxPoolObjects = 100;

        private Texture2D _typesBackgroundTexture;

        private CyanPlayerObjectPool _instance;
        private UdonBehaviour _udon;

        private int _poolSize;
        private bool _multiplePools;
        private static VRCWorld world;

        private SerializedProperty _autoPoolSizeProp;
        private SerializedProperty _sizeProp;
        private SerializedProperty _debugProp;

        private bool _showSettings = true;
        private bool _showPoolAssigners = true;


        private void Awake()
        {
            _instance = (CyanPlayerObjectPool)target;
            _udon = UdonSharpEditorUtility.GetBackingUdonBehaviour(_instance);
            _poolSize = _instance.poolSize;

            _autoPoolSizeProp = serializedObject.FindProperty(nameof(CyanPlayerObjectPool.autoPoolSize));
            _sizeProp = serializedObject.FindProperty(nameof(CyanPlayerObjectPool.poolSize));
            _debugProp = serializedObject.FindProperty(nameof(CyanPlayerObjectPool.printDebugLogs));

            _typesBackgroundTexture = CyanPlayerObjectPoolEditorHelpers.CreateTexture(3, 3,
                (x, y) => x == 1 && y == 1
                    ? CyanPlayerObjectPoolEditorHelpers.BackgroundColor
                    : CyanPlayerObjectPoolEditorHelpers.LineColorDark);

            _multiplePools = false;
            if (ShouldCheckScene())
            {
                // Go through all objects to find if there are multiple Object Pool scripts.
                int count = 0;
                foreach (var obj in _instance.gameObject.scene.GetRootGameObjects())
                {
                    CyanPlayerObjectPool[] pools = obj.GetComponentsInChildren<CyanPlayerObjectPool>(true);
                    count += pools.Length;
                }

                _multiplePools = count > 1;

                // Go through each setup helper and verify their pool size.
                foreach (var helper in FindObjectsOfType<CyanPoolSetupHelper>())
                {
                    helper.VerifyPoolSize();
                }

                AutoSizePool();
            }
        }

        [InitializeOnLoadMethod]
        private static void Initialize()
        {
            EditorApplication.update += CheckForSDKFocus;

            // Go through each setup helper and verify their pool size.
            foreach (var helper in FindObjectsOfType<CyanPoolSetupHelper>())
            {
                helper.VerifyPoolSize();
            }

            AutoSizePool();
        }

        private static string previousFocusedWindow = string.Empty;
        // Checks if the SDK window becomes focused, and if so will verify the pool size.
        private static void CheckForSDKFocus()
        {
            if (UnityEditor.EditorWindow.focusedWindow.ToString().Contains("VRCSdkControlPanel") && UnityEditor.EditorWindow.focusedWindow.ToString() != previousFocusedWindow)
            {
                // Go through each setup helper and verify their pool size.
                foreach (var helper in FindObjectsOfType<CyanPoolSetupHelper>())
                {
                    helper.VerifyPoolSize();
                }

                AutoSizePool();
            }

            previousFocusedWindow = UnityEditor.EditorWindow.focusedWindow.ToString();
        }

        private static async void AutoSizePool()
        {
            // find CyanPlayerObjectPool
            CyanPlayerObjectPool cyanPlayerObjectPool = FindObjectOfType<CyanPlayerObjectPool>();

            // print pool size
            if (cyanPlayerObjectPool != null)
            {
                if (cyanPlayerObjectPool.autoPoolSize)
                {
                    // get world
                    await GetWorld();

                    // check if the pool size matches the world size
                    if (cyanPlayerObjectPool.poolSize != world.Capacity + 2)
                    {
                        Debug.Log("Automatically adjusting pools to match world size");

                        // set pool size to world size
                        cyanPlayerObjectPool.poolSize = world.Capacity + 2;

                        //reverify pool size
                        foreach (var helper in FindObjectsOfType<CyanPoolSetupHelper>())
                        {
                            helper.VerifyPoolSize();
                        }
                    }
                }
            }
        }

        private static async Task GetWorld()
        {
            //get the pipeline manager by finding the PipelineManager component in the current scene
            PipelineManager pipelineManager = GameObject.FindObjectOfType<PipelineManager>();
            var worldId = pipelineManager.blueprintId;

            //if its a new world (no id), skip
            if (worldId != null)
            {
                //get the world from the api
                world = await VRCApi.GetWorld(worldId, false);
            }
        }

        private bool ShouldCheckScene()
        {
            return _udon != null
                   && _udon.gameObject.scene.isLoaded
                   && !EditorSceneManager.IsPreviewSceneObject(_udon);
        }

        public override void OnInspectorGUI()
        {
            if (UdonSharpGUI.DrawDefaultUdonSharpBehaviourHeader(target)) return;
            if (!UdonSharpEditorUtility.IsProxyBehaviour(_instance)) return;

            CyanPlayerObjectPoolEditorHelpers.RenderHeader("Cyan Player Object Pool");

            if (_multiplePools)
            {
                GUILayout.Space(5);
                EditorGUILayout.HelpBox("Multiple Object Pools exist in the scene! Please only have one Object Pool!", MessageType.Error);
                // TODO list all object pools
                return;
            }

            serializedObject.UpdateIfRequiredOrScript();

            RenderSettings();

            serializedObject.ApplyModifiedProperties();

            // Prevent modifying the scene if this is part of a prefab editor.
            if (!ShouldCheckScene())
            {
                return;
            }

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

                EditorGUI.BeginChangeCheck();
                EditorGUILayout.PropertyField(_autoPoolSizeProp);
                if (EditorGUI.EndChangeCheck())
                {
                    serializedObject.ApplyModifiedProperties();
                    AutoSizePool();
                }

                GUI.enabled = !_autoPoolSizeProp.boolValue;
                EditorGUILayout.PropertyField(_sizeProp);
                GUI.enabled = true;
                EditorGUILayout.PropertyField(_debugProp);

                // Hard-cap the min and max size of the object pool.
                int value = _sizeProp.intValue;
                if (value < MinPoolObjects || value > MaxPoolObjects)
                {
                    _sizeProp.intValue = Mathf.Clamp(value, MinPoolObjects, MaxPoolObjects);
                }

                // Only display button to respawn objects if the scene can be edited.  
                if (ShouldCheckScene() && GUILayout.Button("Respawn All Pool Objects"))
                {
                    foreach (var helper in FindObjectsOfType<CyanPoolSetupHelper>())
                    {
                        helper.RespawnAllPoolObjects();
                    }
                }

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
                GUIContent errorIcon = EditorGUIUtility.TrIconContent("console.erroricon", "This pool type does not have the correct number of objects!");
                GUIContent warningIcon = EditorGUIUtility.TrIconContent("console.warnicon", "This pool type does not have any objects.");

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
                float iconWidth = 25;

                // Create vertical bars separating the sections
                GUI.Box(new Rect(rect.x + indexLabelWidth + between, rect.y + 1, 1, height), GUIContent.none, boxStyle);
                GUI.Box(new Rect(rect.x + indexLabelWidth + pingWidth + between * 2, rect.y + 1, 1, height), GUIContent.none, boxStyle);

                if (helpers.Count == 0)
                {
                    GUILayout.Space(sectionHeight);
                }

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

                    int objCount = helper.GetObjectCount();
                    if (objCount != _poolSize)
                    {
                        GUIContent content = null;
                        if (objCount == 0)
                        {
                            // Display warning saying no objects 
                            content = warningIcon;
                        }
                        else
                        {
                            // Display error saying object count does not match
                            content = errorIcon;
                        }

                        Rect iconRect = new Rect(objectLabelRect.xMax, buttonY, iconWidth, buttonHeight);
                        GUI.Label(iconRect, content);
                    }

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

