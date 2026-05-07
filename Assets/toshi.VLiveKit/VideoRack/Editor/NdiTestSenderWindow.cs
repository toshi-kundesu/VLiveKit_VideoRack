#if UNITY_EDITOR
using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace VLiveKit.VideoRack.Editor
{
    public sealed class NdiTestSenderWindow : EditorWindow
    {
        private const string WindowTitle = "NDI Test Sender";
        private const string DefaultAssetFolder = "Assets/VLiveKitGenerated/VideoRack/NDI Test Sender";

        private string ndiName = "VLiveKit VideoRack Test";
        private int width = 1920;
        private int height = 1080;
        private bool keepAlpha;
        private NdiTestPatternGenerator.PatternMode patternMode = NdiTestPatternGenerator.PatternMode.BarsAndGrid;
        private float speed = 1f;
        private GUIStyle headerStyle;
        private GUIStyle panelStyle;
        private GUIStyle hintStyle;
        private GUIStyle sectionTitleStyle;

        [MenuItem("toshi/VLiveKit/VideoRack/NDI Test Sender")]
        public static void Open()
        {
            GetWindow<NdiTestSenderWindow>(WindowTitle);
        }

        private void OnGUI()
        {
            InitializeStyles();

            var background = EditorGUIUtility.isProSkin ? new Color(0.115f, 0.115f, 0.115f) : new Color(0.90f, 0.91f, 0.92f);
            EditorGUI.DrawRect(new Rect(0f, 0f, position.width, position.height), background);

            using (new EditorGUILayout.VerticalScope())
            {
                DrawHeader();

                using (new EditorGUILayout.VerticalScope(panelStyle))
                {
                    DrawSectionTitle("NDI");
                    ndiName = EditorGUILayout.TextField(new GUIContent("NDI Name", "Name shown in NDI receivers."), ndiName);
                    keepAlpha = EditorGUILayout.Toggle(new GUIContent("Keep Alpha", "Enable alpha in the KlakNDI sender."), keepAlpha);

                    var senderType = FindType("Klak.Ndi.NdiSender");
                    var resources = FindNdiResources();
                    if (senderType == null)
                        DrawHint("KlakNDI is not loaded. The tool will still create a test pattern RenderTexture, but no NdiSender component can be added yet.");
                    else if (resources == null)
                        DrawHint("KlakNDI is loaded, but NdiResources.asset was not found. The sender will be added and left selected for manual resource assignment.");
                    else
                        DrawHint("KlakNDI is visible. The created rig will use Texture capture and send the generated RenderTexture.");
                }

                using (new EditorGUILayout.VerticalScope(panelStyle))
                {
                    DrawSectionTitle("PATTERN");
                    width = Mathf.Max(16, EditorGUILayout.IntField(new GUIContent("Width", "NDI test texture width."), width));
                    height = Mathf.Max(8, EditorGUILayout.IntField(new GUIContent("Height", "NDI test texture height."), height));
                    patternMode = (NdiTestPatternGenerator.PatternMode)EditorGUILayout.EnumPopup("Mode", patternMode);
                    speed = EditorGUILayout.Slider(new GUIContent("Motion", "Speed of the scan marker and moving grid."), speed, 0f, 4f);

                    if (width % 16 != 0 || height % 8 != 0)
                        DrawHint("KlakNDI recommends frame dimensions that are multiples of 16 x 8.");
                }

                using (new EditorGUILayout.VerticalScope(panelStyle))
                {
                    DrawSectionTitle("CREATE");
                    using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(ndiName)))
                    {
                        if (GUILayout.Button("CREATE TEST SENDER", GUILayout.Height(34)))
                            CreateTestSender();
                    }
                }
            }
        }

        private void CreateTestSender()
        {
            EnsureAssetFolder(DefaultAssetFolder);

            var texturePath = AssetDatabase.GenerateUniqueAssetPath(Path.Combine(DefaultAssetFolder, MakeAssetSafeName(ndiName) + ".renderTexture"));
            var texture = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32)
            {
                name = Path.GetFileNameWithoutExtension(texturePath),
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                antiAliasing = 1,
                useMipMap = false,
                autoGenerateMips = false
            };
            AssetDatabase.CreateAsset(texture, texturePath);

            var root = new GameObject("VLiveKit NDI Test Sender");
            Undo.RegisterCreatedObjectUndo(root, "Create NDI Test Sender");

            var generator = root.AddComponent<NdiTestPatternGenerator>();
            generator.TargetTexture = texture;
            generator.Pattern = patternMode;
            generator.Speed = speed;

            var sender = AddAndConfigureNdiSender(root, texture);
            AssetDatabase.SaveAssets();

            Selection.activeObject = sender != null ? sender : (UnityEngine.Object)root;
            EditorGUIUtility.PingObject(texture);
        }

        private Component AddAndConfigureNdiSender(GameObject target, RenderTexture texture)
        {
            var senderType = FindType("Klak.Ndi.NdiSender");
            if (senderType == null || !typeof(Component).IsAssignableFrom(senderType))
                return null;

            var sender = target.AddComponent(senderType);
            var serialized = new SerializedObject(sender);
            SetString(serialized, "_ndiName", ndiName.Trim());
            SetBool(serialized, "_keepAlpha", keepAlpha);
            SetEnumIndex(serialized, "_captureMethod", 2);
            SetObject(serialized, "_sourceTexture", texture);
            SetObject(serialized, "_resources", FindNdiResources());
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return sender;
        }

        private static void SetString(SerializedObject serialized, string propertyName, string value)
        {
            var property = serialized.FindProperty(propertyName);
            if (property != null)
                property.stringValue = value;
        }

        private static void SetBool(SerializedObject serialized, string propertyName, bool value)
        {
            var property = serialized.FindProperty(propertyName);
            if (property != null)
                property.boolValue = value;
        }

        private static void SetEnumIndex(SerializedObject serialized, string propertyName, int value)
        {
            var property = serialized.FindProperty(propertyName);
            if (property != null)
                property.enumValueIndex = value;
        }

        private static void SetObject(SerializedObject serialized, string propertyName, UnityEngine.Object value)
        {
            var property = serialized.FindProperty(propertyName);
            if (property != null)
                property.objectReferenceValue = value;
        }

        private static Type FindType(string fullName)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type type = null;
                try
                {
                    type = assembly.GetType(fullName, false);
                }
                catch (ReflectionTypeLoadException)
                {
                }

                if (type != null)
                    return type;
            }

            return null;
        }

        private static UnityEngine.Object FindNdiResources()
        {
            foreach (var guid in AssetDatabase.FindAssets("NdiResources"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.EndsWith("NdiResources.asset", StringComparison.OrdinalIgnoreCase))
                    continue;

                var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);
                if (asset != null && asset.GetType().FullName == "Klak.Ndi.NdiResources")
                    return asset;
            }

            return null;
        }

        private static void EnsureAssetFolder(string folder)
        {
            var parts = folder.Split('/');
            var current = parts[0];
            for (var i = 1; i < parts.Length; i++)
            {
                var next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }

        private static string MakeAssetSafeName(string value)
        {
            var invalid = Path.GetInvalidFileNameChars();
            foreach (var character in invalid)
                value = value.Replace(character, '_');

            value = value.Trim();
            return string.IsNullOrEmpty(value) ? "NDI Test Sender" : value;
        }

        private void InitializeStyles()
        {
            if (headerStyle != null && panelStyle != null && hintStyle != null && sectionTitleStyle != null)
                return;

            headerStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 17,
                normal = { textColor = EditorGUIUtility.isProSkin ? new Color(0.90f, 0.91f, 0.92f) : new Color(0.12f, 0.13f, 0.14f) },
                margin = new RectOffset(8, 8, 8, 2)
            };

            panelStyle = new GUIStyle(EditorStyles.helpBox)
            {
                padding = new RectOffset(10, 10, 8, 9),
                margin = new RectOffset(8, 8, 5, 6),
                normal =
                {
                    background = MakeSolidTexture(EditorGUIUtility.isProSkin ? new Color(0.19f, 0.19f, 0.19f) : new Color(0.84f, 0.85f, 0.86f))
                }
            };

            hintStyle = new GUIStyle(EditorStyles.helpBox)
            {
                fontSize = 10,
                wordWrap = true,
                padding = new RectOffset(6, 6, 3, 4),
                normal =
                {
                    textColor = EditorGUIUtility.isProSkin ? new Color(0.67f, 0.68f, 0.70f) : new Color(0.34f, 0.35f, 0.37f),
                    background = MakeSolidTexture(EditorGUIUtility.isProSkin ? new Color(0.145f, 0.145f, 0.145f) : new Color(0.78f, 0.79f, 0.81f))
                }
            };

            sectionTitleStyle = new GUIStyle(EditorStyles.miniBoldLabel)
            {
                fontSize = 10,
                normal = { textColor = new Color(0.25f, 0.84f, 0.92f) }
            };
        }

        private void DrawHeader()
        {
            EditorGUILayout.LabelField("NDI Test Sender", headerStyle);
        }

        private void DrawSectionTitle(string text)
        {
            EditorGUILayout.LabelField(text, sectionTitleStyle);
        }

        private void DrawHint(string text)
        {
            EditorGUILayout.LabelField(text, hintStyle);
        }

        private static Texture2D MakeSolidTexture(Color color)
        {
            var texture = new Texture2D(1, 1)
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            texture.SetPixel(0, 0, color);
            texture.Apply();
            return texture;
        }
    }
}
#endif
