#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace VLiveKit.VideoRack.Editor
{
    public sealed class NdiTestReceiverWindow : EditorWindow
    {
        private const string WindowTitle = "NDI Test Receiver";
        private const string DefaultAssetFolder = "Assets/VLiveKitGenerated/VideoRack/NDI Test Receiver";

        private string ndiName = "";
        private int width = 1920;
        private int height = 1080;
        private bool createPreviewSurface = true;
        private Vector2 scrollPosition;
        private readonly List<string> sourceNames = new List<string>();
        private string statusMessage = "Ready";
        private GUIStyle headerStyle;
        private GUIStyle subtitleStyle;
        private GUIStyle panelStyle;
        private GUIStyle sectionTitleStyle;
        private GUIStyle hintStyle;
        private GUIStyle statusStyle;
        private GUIStyle primaryButtonStyle;

        [MenuItem("toshi/VLiveKit/VideoRack/NDI Test Receiver")]
        public static void Open()
        {
            GetWindow<NdiTestReceiverWindow>(WindowTitle);
        }

        private void OnEnable()
        {
            RefreshSourceNames();
        }

        private void OnGUI()
        {
            EnsureStyles();

            using (new EditorGUILayout.VerticalScope())
            {
                DrawHeader();

                scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
                DrawNdiSettings();
                DrawTargetSettings();
                DrawCreateSection();
                EditorGUILayout.EndScrollView();
            }
        }

        private void DrawNdiSettings()
        {
            using (new EditorGUILayout.VerticalScope(panelStyle))
            {
                DrawSectionTitle("NDI");

                using (new EditorGUILayout.HorizontalScope())
                {
                    ndiName = EditorGUILayout.TextField(new GUIContent("NDI Source", "Exact NDI source name to receive."), ndiName);

                    using (new EditorGUI.DisabledScope(sourceNames.Count == 0))
                    {
                        if (GUILayout.Button("Select", GUILayout.Width(72f)))
                            ShowSourceMenu();
                    }

                    if (GUILayout.Button("Refresh", GUILayout.Width(72f)))
                        RefreshSourceNames();
                }

                var receiverType = FindType("Klak.Ndi.NdiReceiver");
                var resources = FindNdiResources();
                if (receiverType == null)
                    DrawHint("KlakNDI is not loaded. Install or enable KlakNDI before creating an NDI receiver.");
                else if (resources == null)
                    DrawHint("KlakNDI is loaded, but NdiResources.asset was not found. The receiver will be added and left selected for manual resource assignment.");
                else if (sourceNames.Count == 0)
                    DrawHint("KlakNDI is visible. No NDI sources are currently advertised; press Refresh after starting a sender.");
                else
                    DrawHint("KlakNDI is visible. Select a source, then create a receiver rig with a target RenderTexture.");
            }
        }

        private void DrawTargetSettings()
        {
            using (new EditorGUILayout.VerticalScope(panelStyle))
            {
                DrawSectionTitle("Target");
                width = Mathf.Max(16, EditorGUILayout.IntField(new GUIContent("Width", "Receiver target texture width."), width));
                height = Mathf.Max(8, EditorGUILayout.IntField(new GUIContent("Height", "Receiver target texture height."), height));
                createPreviewSurface = EditorGUILayout.Toggle(new GUIContent("Preview Surface", "Create a quad that displays the receiver target texture."), createPreviewSurface);

                if (width % 16 != 0 || height % 8 != 0)
                    DrawHint("KlakNDI commonly uses frame dimensions that are multiples of 16 x 8.");
            }
        }

        private void DrawCreateSection()
        {
            using (new EditorGUILayout.VerticalScope(panelStyle))
            {
                DrawSectionTitle("Create");
                DrawStatus(statusMessage);

                using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(ndiName) || FindType("Klak.Ndi.NdiReceiver") == null))
                {
                    if (GUILayout.Button("Create Test Receiver", primaryButtonStyle, GUILayout.Height(30)))
                        CreateTestReceiver();
                }
            }
        }

        private void CreateTestReceiver()
        {
            EnsureAssetFolder(DefaultAssetFolder);

            var safeName = MakeAssetSafeName(ndiName);
            var texturePath = AssetDatabase.GenerateUniqueAssetPath(Path.Combine(DefaultAssetFolder, safeName + ".renderTexture"));
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

            var root = new GameObject("VLiveKit NDI Test Receiver");
            Undo.RegisterCreatedObjectUndo(root, "Create NDI Test Receiver");

            var receiver = AddAndConfigureNdiReceiver(root, texture);
            var previewCreated = !createPreviewSurface || CreatePreviewSurface(root.transform, texture, safeName);

            AssetDatabase.SaveAssets();

            Selection.activeObject = receiver != null ? receiver : (UnityEngine.Object)root;
            EditorGUIUtility.PingObject(texture);
            statusMessage = previewCreated
                ? "NDI test receiver created."
                : "NDI test receiver created without a preview surface.";
            ShowNotification(new GUIContent("NDI test receiver created"));
        }

        private Component AddAndConfigureNdiReceiver(GameObject target, RenderTexture texture)
        {
            var receiverType = FindType("Klak.Ndi.NdiReceiver");
            if (receiverType == null || !typeof(Component).IsAssignableFrom(receiverType))
                return null;

            var receiver = target.AddComponent(receiverType);
            var serialized = new SerializedObject(receiver);
            SetString(serialized, "_ndiName", ndiName.Trim());
            SetObject(serialized, "_targetTexture", texture);
            SetObject(serialized, "_resources", FindNdiResources());
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return receiver;
        }

        private bool CreatePreviewSurface(Transform parent, RenderTexture texture, string safeName)
        {
            var shader = FindPreviewShader();
            if (shader == null)
                return false;

            var materialPath = AssetDatabase.GenerateUniqueAssetPath(Path.Combine(DefaultAssetFolder, safeName + " Preview.mat"));
            var material = new Material(shader)
            {
                name = Path.GetFileNameWithoutExtension(materialPath)
            };
            AssignTextureToMaterial(material, texture);
            AssetDatabase.CreateAsset(material, materialPath);

            var preview = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Undo.RegisterCreatedObjectUndo(preview, "Create NDI Preview Surface");
            preview.name = "NDI Preview Surface";
            preview.transform.SetParent(parent, false);
            preview.transform.localPosition = new Vector3(0f, 0f, 2f);
            preview.transform.localScale = GetPreviewScale();

            var collider = preview.GetComponent<Collider>();
            if (collider != null)
                Undo.DestroyObjectImmediate(collider);

            var renderer = preview.GetComponent<MeshRenderer>();
            if (renderer != null)
                renderer.sharedMaterial = material;

            return true;
        }

        private Vector3 GetPreviewScale()
        {
            var aspect = height > 0 ? width / (float)height : 16f / 9f;
            return aspect >= 1f ? new Vector3(aspect, 1f, 1f) : new Vector3(1f, 1f / Mathf.Max(0.01f, aspect), 1f);
        }

        private static Shader FindPreviewShader()
        {
            return Shader.Find("HDRP/Unlit") ??
                Shader.Find("Unlit/Texture") ??
                Shader.Find("Sprites/Default") ??
                Shader.Find("Standard");
        }

        private static void AssignTextureToMaterial(Material material, Texture texture)
        {
            if (material == null || texture == null)
                return;

            if (material.HasProperty("_BaseColorMap"))
                material.SetTexture("_BaseColorMap", texture);
            if (material.HasProperty("_BaseMap"))
                material.SetTexture("_BaseMap", texture);
            if (material.HasProperty("_MainTex"))
                material.SetTexture("_MainTex", texture);
        }

        private void RefreshSourceNames()
        {
            sourceNames.Clear();
            sourceNames.AddRange(EnumerateNdiSourceNames());
            sourceNames.Sort(StringComparer.OrdinalIgnoreCase);

            if (string.IsNullOrWhiteSpace(ndiName) && sourceNames.Count > 0)
                ndiName = sourceNames[0];

            statusMessage = sourceNames.Count == 0
                ? "No NDI sources found."
                : "Found " + sourceNames.Count + " NDI source(s).";
            Repaint();
        }

        private void ShowSourceMenu()
        {
            var menu = new GenericMenu();
            foreach (var sourceName in sourceNames)
            {
                var name = sourceName;
                menu.AddItem(new GUIContent(name), name == ndiName, () =>
                {
                    ndiName = name;
                    Repaint();
                });
            }

            menu.ShowAsContext();
        }

        private static IEnumerable<string> EnumerateNdiSourceNames()
        {
            var finderType = FindType("Klak.Ndi.NdiFinder");
            var property = finderType?.GetProperty("sourceNames", BindingFlags.Static | BindingFlags.Public);
            var values = property?.GetValue(null) as IEnumerable;
            if (values == null)
                yield break;

            foreach (var value in values)
            {
                var name = value as string;
                if (!string.IsNullOrEmpty(name))
                    yield return name;
            }
        }

        private static void SetString(SerializedObject serialized, string propertyName, string value)
        {
            var property = serialized.FindProperty(propertyName);
            if (property != null)
                property.stringValue = value;
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
            return string.IsNullOrEmpty(value) ? "NDI Test Receiver" : value;
        }

        private void DrawHeader()
        {
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("NDI Test Receiver", headerStyle);
            EditorGUILayout.LabelField("Source receiver and preview setup", subtitleStyle);
            DrawSeparator();
            EditorGUILayout.Space(4);
        }

        private void DrawSectionTitle(string text)
        {
            EditorGUILayout.LabelField(text, sectionTitleStyle);
        }

        private void DrawHint(string text)
        {
            if (!string.IsNullOrEmpty(text))
                EditorGUILayout.LabelField(text, hintStyle);
        }

        private void DrawStatus(string text)
        {
            if (!string.IsNullOrEmpty(text))
                EditorGUILayout.LabelField(text, statusStyle);
        }

        private void DrawSeparator()
        {
            var rect = GUILayoutUtility.GetRect(1f, 1f, GUILayout.ExpandWidth(true));
            var color = EditorGUIUtility.isProSkin
                ? new Color(1f, 1f, 1f, 0.12f)
                : new Color(0f, 0f, 0f, 0.18f);
            EditorGUI.DrawRect(rect, color);
        }

        private void EnsureStyles()
        {
            if (headerStyle != null &&
                subtitleStyle != null &&
                panelStyle != null &&
                sectionTitleStyle != null &&
                hintStyle != null &&
                statusStyle != null &&
                primaryButtonStyle != null)
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

            statusStyle = new GUIStyle(EditorStyles.wordWrappedMiniLabel)
            {
                padding = new RectOffset(2, 2, 2, 2)
            };

            primaryButtonStyle = new GUIStyle(GUI.skin.button)
            {
                fontStyle = FontStyle.Normal
            };
        }
    }
}
#endif
