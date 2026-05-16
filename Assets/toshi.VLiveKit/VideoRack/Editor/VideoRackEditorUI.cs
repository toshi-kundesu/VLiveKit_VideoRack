#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace VLiveKit.VideoRack.Editor
{
    internal static class VideoRackEditorUI
    {
        private static GUIStyle headerStyle;
        private static GUIStyle subtitleStyle;
        private static GUIStyle panelStyle;
        private static GUIStyle sectionTitleStyle;
        private static GUIStyle hintStyle;
        private static GUIStyle headerStatusStyle;
        private static GUIStyle statusStyle;
        private static GUIStyle resultStyle;
        private static GUIStyle primaryButtonStyle;
        private static GUIStyle logStyle;

        public static GUIStyle Panel
        {
            get
            {
                EnsureStyles();
                return panelStyle;
            }
        }

        public static GUIStyle Result
        {
            get
            {
                EnsureStyles();
                return resultStyle;
            }
        }

        public static GUIStyle PrimaryButton
        {
            get
            {
                EnsureStyles();
                return primaryButtonStyle;
            }
        }

        public static GUIStyle Log
        {
            get
            {
                EnsureStyles();
                return logStyle;
            }
        }

        public static void DrawHeader(string title, string subtitle, string status = null)
        {
            EnsureStyles();

            EditorGUILayout.Space(4);
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUILayout.VerticalScope())
                {
                    EditorGUILayout.LabelField(title, headerStyle);
                    if (!string.IsNullOrEmpty(subtitle))
                        EditorGUILayout.LabelField(subtitle, subtitleStyle);
                }

                if (!string.IsNullOrEmpty(status))
                    EditorGUILayout.LabelField(status, headerStatusStyle, GUILayout.Width(110f));
            }

            DrawSeparator();
            EditorGUILayout.Space(4);
        }

        public static void DrawSectionTitle(string title)
        {
            EnsureStyles();
            EditorGUILayout.LabelField(title, sectionTitleStyle);
        }

        public static void DrawHint(string text)
        {
            if (string.IsNullOrEmpty(text))
                return;

            EnsureStyles();
            EditorGUILayout.LabelField(text, hintStyle);
        }

        public static void DrawStatus(string text)
        {
            if (string.IsNullOrEmpty(text))
                return;

            EnsureStyles();
            EditorGUILayout.LabelField(text, statusStyle);
        }

        public static void ShowNotification(EditorWindow window, string message)
        {
            if (window == null || string.IsNullOrEmpty(message))
                return;

            window.ShowNotification(new GUIContent(message));
        }

        private static void DrawSeparator()
        {
            var rect = GUILayoutUtility.GetRect(1f, 1f, GUILayout.ExpandWidth(true));
            var color = EditorGUIUtility.isProSkin
                ? new Color(1f, 1f, 1f, 0.12f)
                : new Color(0f, 0f, 0f, 0.18f);
            EditorGUI.DrawRect(rect, color);
        }

        private static void EnsureStyles()
        {
            if (headerStyle != null &&
                subtitleStyle != null &&
                panelStyle != null &&
                sectionTitleStyle != null &&
                hintStyle != null &&
                headerStatusStyle != null &&
                statusStyle != null &&
                resultStyle != null &&
                primaryButtonStyle != null &&
                logStyle != null)
            {
                return;
            }

            headerStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 14,
                margin = new RectOffset(8, 8, 4, 0)
            };

            subtitleStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                wordWrap = true,
                margin = new RectOffset(8, 8, 0, 3)
            };

            panelStyle = new GUIStyle(EditorStyles.helpBox)
            {
                padding = new RectOffset(10, 10, 8, 9),
                margin = new RectOffset(8, 8, 5, 6)
            };

            sectionTitleStyle = new GUIStyle(EditorStyles.miniBoldLabel)
            {
                fontSize = 10
            };

            hintStyle = new GUIStyle(EditorStyles.wordWrappedMiniLabel)
            {
                padding = new RectOffset(2, 2, 3, 4)
            };

            headerStatusStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleRight,
                padding = new RectOffset(2, 2, 2, 2)
            };

            statusStyle = new GUIStyle(EditorStyles.wordWrappedMiniLabel)
            {
                alignment = TextAnchor.MiddleLeft,
                padding = new RectOffset(2, 2, 2, 2)
            };

            resultStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                wordWrap = true
            };

            primaryButtonStyle = new GUIStyle(GUI.skin.button)
            {
                fontStyle = FontStyle.Normal
            };

            logStyle = new GUIStyle(EditorStyles.textArea)
            {
                fontSize = 11,
                wordWrap = false
            };
        }
    }
}
#endif
