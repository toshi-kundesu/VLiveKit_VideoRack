#if UNITY_EDITOR
using System;
using System.Collections;
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
        private string statusMessage = "Ready";
        private GameObject liveRoot;
        private RenderTexture liveTexture;
        private NdiTestPatternGenerator liveGenerator;
        private Component liveSender;
        private IEnumerator liveSenderCaptureRoutine;
        private MethodInfo patternUpdateMethod;

        [MenuItem("toshi/VLiveKit/VideoRack/NDI Test Sender")]
        public static void Open()
        {
            GetWindow<NdiTestSenderWindow>(WindowTitle);
        }

        private void OnEnable()
        {
            EditorApplication.update += OnEditorUpdate;
        }

        private void OnDisable()
        {
            EditorApplication.update -= OnEditorUpdate;
            StopWindowSender();
        }

        private void OnGUI()
        {
            using (new EditorGUILayout.VerticalScope())
            {
                DrawHeader();

                using (new EditorGUILayout.VerticalScope(VideoRackEditorUI.Panel))
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

                using (new EditorGUILayout.VerticalScope(VideoRackEditorUI.Panel))
                {
                    DrawSectionTitle("Pattern");
                    width = Mathf.Max(16, EditorGUILayout.IntField(new GUIContent("Width", "NDI test texture width."), width));
                    height = Mathf.Max(8, EditorGUILayout.IntField(new GUIContent("Height", "NDI test texture height."), height));
                    patternMode = (NdiTestPatternGenerator.PatternMode)EditorGUILayout.EnumPopup("Mode", patternMode);
                    speed = EditorGUILayout.Slider(new GUIContent("Motion", "Speed of the scan marker and moving grid."), speed, 0f, 4f);

                    if (width % 16 != 0 || height % 8 != 0)
                        DrawHint("KlakNDI recommends frame dimensions that are multiples of 16 x 8.");
                }

                DrawWindowSenderSection();

                using (new EditorGUILayout.VerticalScope(VideoRackEditorUI.Panel))
                {
                    DrawSectionTitle("Scene Rig");
                    VideoRackEditorUI.DrawStatus(statusMessage);
                    using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(ndiName)))
                    {
                        if (GUILayout.Button("Create Scene Sender", VideoRackEditorUI.PrimaryButton, GUILayout.Height(30)))
                            CreateTestSender();
                    }
                }
            }
        }

        private void OnEditorUpdate()
        {
            if (liveRoot == null)
                return;

            if (liveGenerator != null)
            {
                liveGenerator.TargetTexture = liveTexture;
                liveGenerator.Pattern = patternMode;
                liveGenerator.Speed = speed;
                InvokePatternUpdate(liveGenerator);
            }

            if (liveSenderCaptureRoutine != null)
            {
                try
                {
                    liveSenderCaptureRoutine.MoveNext();
                }
                catch (Exception exception)
                {
                    statusMessage = "Window NDI sender update failed: " + exception.Message;
                    liveSenderCaptureRoutine = null;
                }
            }

            EditorApplication.QueuePlayerLoopUpdate();
            Repaint();
        }

        private void DrawWindowSenderSection()
        {
            using (new EditorGUILayout.VerticalScope(VideoRackEditorUI.Panel))
            {
                DrawSectionTitle("Window Sender");
                var isRunning = liveRoot != null;
                VideoRackEditorUI.DrawStatus(isRunning
                    ? "Sending from this editor window. No scene object is saved."
                    : "Starts a temporary editor-only sender without entering Play Mode.");

                if (liveTexture != null)
                    DrawPreviewTexture(liveTexture);

                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUI.DisabledScope(isRunning || string.IsNullOrWhiteSpace(ndiName)))
                    {
                        if (GUILayout.Button("Start Window Sender", VideoRackEditorUI.PrimaryButton, GUILayout.Height(28)))
                            StartWindowSender();
                    }

                    using (new EditorGUI.DisabledScope(!isRunning))
                    {
                        if (GUILayout.Button("Stop", GUILayout.Height(28)))
                            StopWindowSender();
                    }
                }
            }
        }

        private void StartWindowSender()
        {
            StopWindowSender();

            liveTexture = CreateRuntimeRenderTexture("VLiveKit NDI Window Sender");
            liveRoot = new GameObject("VLiveKit NDI Window Sender")
            {
                hideFlags = HideFlags.HideAndDontSave
            };

            liveGenerator = liveRoot.AddComponent<NdiTestPatternGenerator>();
            liveGenerator.hideFlags = HideFlags.HideAndDontSave;
            liveGenerator.TargetTexture = liveTexture;
            liveGenerator.Pattern = patternMode;
            liveGenerator.Speed = speed;

            liveSender = AddAndConfigureNdiSender(liveRoot, liveTexture);
            liveSenderCaptureRoutine = CreateCaptureRoutine(liveSender);
            if (liveSenderCaptureRoutine != null)
            {
                try
                {
                    liveSenderCaptureRoutine.MoveNext();
                }
                catch (Exception exception)
                {
                    statusMessage = "Window NDI sender could not start: " + exception.Message;
                    liveSenderCaptureRoutine = null;
                }
            }

            statusMessage = liveSender != null
                ? "Window NDI sender started."
                : "Window pattern started. KlakNDI sender is not available.";
            VideoRackEditorUI.ShowNotification(this, "Window NDI sender started");
        }

        private void StopWindowSender()
        {
            liveSenderCaptureRoutine = null;
            liveSender = null;
            liveGenerator = null;

            if (liveRoot != null)
                DestroyImmediate(liveRoot);
            liveRoot = null;

            if (liveTexture != null)
            {
                liveTexture.Release();
                DestroyImmediate(liveTexture);
            }
            liveTexture = null;
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
            statusMessage = "NDI test sender created.";
            VideoRackEditorUI.ShowNotification(this, "NDI test sender created");
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
            InvokePrivate(sender, "OnValidate");
            return sender;
        }

        private RenderTexture CreateRuntimeRenderTexture(string textureName)
        {
            var texture = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32)
            {
                name = textureName,
                hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                antiAliasing = 1,
                useMipMap = false,
                autoGenerateMips = false
            };
            texture.Create();
            return texture;
        }

        private static IEnumerator CreateCaptureRoutine(Component sender)
        {
            if (sender == null)
                return null;

            var method = sender.GetType().GetMethod("CaptureCoroutine", BindingFlags.Instance | BindingFlags.NonPublic);
            return method?.Invoke(sender, null) as IEnumerator;
        }

        private void InvokePatternUpdate(NdiTestPatternGenerator generator)
        {
            if (generator == null)
                return;

            if (patternUpdateMethod == null)
                patternUpdateMethod = typeof(NdiTestPatternGenerator).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic);
            patternUpdateMethod?.Invoke(generator, null);
        }

        private static void InvokePrivate(Component component, string methodName)
        {
            component?.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)?.Invoke(component, null);
        }

        private static void DrawPreviewTexture(Texture texture)
        {
            if (texture == null)
                return;

            var aspect = texture.height > 0 ? texture.width / (float)texture.height : 16f / 9f;
            var rect = GUILayoutUtility.GetAspectRect(aspect, GUILayout.MinHeight(120), GUILayout.MaxHeight(220));
            EditorGUI.DrawPreviewTexture(rect, texture, null, ScaleMode.ScaleToFit);
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

        private void DrawHeader()
        {
            VideoRackEditorUI.DrawHeader("NDI Test Sender", "Generated texture sender setup");
        }

        private void DrawSectionTitle(string text)
        {
            VideoRackEditorUI.DrawSectionTitle(text);
        }

        private void DrawHint(string text)
        {
            VideoRackEditorUI.DrawHint(text);
        }
    }
}
#endif
