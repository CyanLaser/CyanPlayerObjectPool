using System;
using UnityEditor;
using UnityEngine;

namespace Cyan.PlayerObjectPool
{
    public static class CyanPlayerObjectPoolEditorHelpers
    {
        private const string GITHUB_URL = "https://github.com/CyanLaser/CyanPlayerObjectPool";
        private const string DISCORD_URL = "https://discord.gg/hEwb9eF";
        private const string PATREON_URL = "https://www.patreon.com/CyanLaser";
        private const string GITHUB_ISSUE_URL = "https://github.com/CyanLaser/CyanPlayerObjectPool/issues";

        public static Color LineColorDark => EditorGUIUtility.isProSkin ? 
            new Color(0, 0, 0, 0.5f) : 
            new Color(0.5f, 0.5f, 0.5f, 0.5f);

        public static Color BackgroundColor => EditorGUIUtility.isProSkin ? 
            new Color(0.25f, 0.25f, 0.25f, 0.5f) : 
#if UNITY_2019_3_OR_NEWER
            new Color(0.75f, 0.75f, 0.75f, 0.25f);
#else
            new Color(1f, 1f, 1f, 0.25f);
#endif

        private static int _indent = 1;
        
        public static void AddIndent()
        {
            ++_indent;
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(_indent * 4 + 10);
            EditorGUILayout.BeginVertical();
        }

        public static void RemoveIndent()
        {
            EditorGUILayout.EndVertical();
            EditorGUILayout.EndHorizontal();
            --_indent;
        }
        
        public static bool RenderFoldout(bool value, GUIContent content)
        {
            EditorGUILayout.BeginHorizontal();
            
            GUILayout.Space(10);
            
            value = EditorGUILayout.Foldout(value, content, true);

            EditorGUILayout.EndHorizontal();

            return value;
        }
        
        public static void RenderHeader(string title)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            
            // Render title
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            GUILayout.Label(title, EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
            
            GUILayout.Space(5);
            
            // Render buttons
            float width = EditorGUIUtility.currentViewWidth;
            float buttonWidth = Mathf.Max((width - 45) * 0.5f, 100);
            
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(5);
            
            if (GUILayout.Button("Github Repo", GUILayout.Width(buttonWidth)))
            {
                Application.OpenURL(GITHUB_URL);
            }

            GUILayout.Space(5);

            if (GUILayout.Button("Report Bug", GUILayout.Width(buttonWidth)))
            {
                Application.OpenURL(GITHUB_ISSUE_URL);
            }

            GUILayout.Space(5);
            EditorGUILayout.EndHorizontal();
            
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(5);
            
            if (GUILayout.Button("Discord", GUILayout.Width(buttonWidth)))
            {
                Application.OpenURL(DISCORD_URL);
            }

            GUILayout.Space(5);

            if (GUILayout.Button("Patreon", GUILayout.Width(buttonWidth)))
            {
                Application.OpenURL(PATREON_URL);
            }

            GUILayout.Space(5);
            EditorGUILayout.EndHorizontal();
            
            EditorGUILayout.EndVertical();
        }

        public static Texture2D CreateTexture(int width, int height, Func<int, int, Color> getColor)
        {
            Texture2D ret = new Texture2D(width, height)
            {
                alphaIsTransparency = true,
                filterMode = FilterMode.Point
            };
            for (int y = 0; y < ret.height; ++y)
            {
                for (int x = 0; x < ret.width; ++x)
                {
                    ret.SetPixel(x, y, getColor(x, y));
                }
            }
            ret.Apply();
            return ret;
        }
    }
}