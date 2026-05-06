#if UNITY_EDITOR
using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace VLiveKit.VideoRack.Editor
{
    public sealed class HapConverterWindow : EditorWindow
    {
        private const string WindowTitle = "HAP Converter";

        private static readonly HapPreset[] Presets =
        {
            new HapPreset("HAP", "hap", "General playback", "_HAP.mov"),
            new HapPreset("HAP Alpha", "hap_alpha", "With alpha channel", "_HAP_Alpha.mov"),
            new HapPreset("HAP Q", "hap_q", "Higher quality", "_HAP_Q.mov")
        };

        private string inputPath = string.Empty;
        private string outputPath = string.Empty;
        private OperationMode operationMode = OperationMode.Hap;
        private int presetIndex;
        private int chunks = 1;
        private bool resizeEnabled;
        private int outputWidth = 1920;
        private int outputHeight = 1080;
        private bool fpsEnabled;
        private float outputFps = 30f;
        private bool trimEnabled;
        private string trimStart = string.Empty;
        private string trimDuration = string.Empty;
        private AudioMode audioMode = AudioMode.Aac192;
        private bool advancedOpen;
        private string extraInputArguments = string.Empty;
        private string extraOutputArguments = string.Empty;
        private bool overwrite = true;
        private bool revealOnComplete = true;
        private bool isConverting;
        private bool outputPathWasEdited;
        private Process process;
        private readonly StringBuilder log = new StringBuilder();
        private Vector2 mainScroll;
        private Vector2 logScroll;
        private GUIStyle headerStyle;
        private GUIStyle panelStyle;
        private GUIStyle sectionTitleStyle;
        private GUIStyle statusStyle;
        private GUIStyle miniHintStyle;
        private GUIStyle primaryButtonStyle;
        private GUIStyle logStyle;

        [MenuItem("toshi/VLiveKit/VideoRack/HAP Converter")]
        public static void Open()
        {
            GetWindow<HapConverterWindow>(WindowTitle);
        }

        private void OnGUI()
        {
            InitializeStyles();

            var windowRect = new Rect(0f, 0f, position.width, position.height);
            EditorGUI.DrawRect(windowRect, EditorGUIUtility.isProSkin ? new Color(0.115f, 0.115f, 0.115f) : new Color(0.90f, 0.91f, 0.92f));

            mainScroll = EditorGUILayout.BeginScrollView(mainScroll);
            DrawHeader();

            using (new EditorGUILayout.VerticalScope(panelStyle))
            {
                DrawSectionTitle("SOURCE");
                DrawPathRow("Input", inputPath, "Select", SelectInput);
                using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(inputPath)))
                    DrawPathRow("Output", outputPath, "Change", SelectOutput);
            }

            using (new EditorGUILayout.VerticalScope(panelStyle))
            {
                DrawSectionTitle("MODE");

                EditorGUI.BeginChangeCheck();
                operationMode = (OperationMode)EditorGUILayout.Popup(
                    new GUIContent("Operation", "Choose what ffmpeg should do."),
                    (int)operationMode,
                    GetOperationNames());
                if (EditorGUI.EndChangeCheck() && !outputPathWasEdited && !string.IsNullOrEmpty(inputPath))
                    outputPath = BuildDefaultOutputPath(inputPath, operationMode, Presets[presetIndex]);

                DrawHint(GetOperationHelp(operationMode));

                using (new EditorGUI.DisabledScope(operationMode != OperationMode.Hap))
                {
                    EditorGUI.BeginChangeCheck();
                    presetIndex = EditorGUILayout.Popup(
                        new GUIContent("HAP Preset", "Choose the HAP variant written by ffmpeg."),
                        presetIndex,
                        GetPresetNames());
                    if (EditorGUI.EndChangeCheck() && !outputPathWasEdited && !string.IsNullOrEmpty(inputPath))
                        outputPath = BuildDefaultOutputPath(inputPath, operationMode, Presets[presetIndex]);

                    if (operationMode == OperationMode.Hap)
                    {
                        DrawHint(GetPresetHelp(Presets[presetIndex]));
                        chunks = EditorGUILayout.IntSlider(
                            new GUIContent("Chunks", "Number of HAP texture chunks per frame. Use 1 first; increase for high-resolution playback tests."),
                            chunks,
                            1,
                            64);
                        DrawHint("Chunks split each video frame for HAP playback. 1 is the safest default. Try 4 or 8 for heavy high-resolution clips if playback stutters.");
                    }
                }
            }

            using (new EditorGUILayout.VerticalScope(panelStyle))
            {
                DrawSectionTitle("FFMPEG PROCESSING");

                resizeEnabled = EditorGUILayout.ToggleLeft(new GUIContent("Resize", "Adds a scale filter before HAP encoding."), resizeEnabled);
                using (new EditorGUI.DisabledScope(!resizeEnabled))
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.PrefixLabel("Size");
                    outputWidth = EditorGUILayout.IntField(outputWidth, GUILayout.MinWidth(70));
                    GUILayout.Label("x", GUILayout.Width(12));
                    outputHeight = EditorGUILayout.IntField(outputHeight, GUILayout.MinWidth(70));
                    GUILayout.FlexibleSpace();
                }

                fpsEnabled = EditorGUILayout.ToggleLeft(new GUIContent("Conform FPS", "Adds an fps filter. Useful when playback systems expect a fixed frame rate."), fpsEnabled);
                using (new EditorGUI.DisabledScope(!fpsEnabled))
                    outputFps = EditorGUILayout.FloatField("FPS", outputFps);

                trimEnabled = EditorGUILayout.ToggleLeft(new GUIContent("Trim", "Uses ffmpeg -ss and -t before decoding."), trimEnabled);
                using (new EditorGUI.DisabledScope(!trimEnabled))
                {
                    trimStart = EditorGUILayout.TextField(new GUIContent("Start", "Examples: 3.5, 00:00:03.500"), trimStart);
                    trimDuration = EditorGUILayout.TextField(new GUIContent("Duration", "Examples: 10, 00:00:10.000"), trimDuration);
                }

                using (new EditorGUI.DisabledScope(operationMode == OperationMode.ExtractWav || operationMode == OperationMode.RemuxMov))
                    audioMode = (AudioMode)EditorGUILayout.EnumPopup(new GUIContent("Audio", "Choose how audio is written to the output movie."), audioMode);
            }

            using (new EditorGUILayout.VerticalScope(panelStyle))
            {
                DrawSectionTitle("OUTPUT");
                overwrite = EditorGUILayout.Toggle("Overwrite Output", overwrite);
                revealOnComplete = EditorGUILayout.Toggle("Reveal On Complete", revealOnComplete);

                advancedOpen = EditorGUILayout.Foldout(advancedOpen, "Advanced ffmpeg arguments", true);
                if (advancedOpen)
                {
                    extraInputArguments = EditorGUILayout.TextField(new GUIContent("Before -i", "Raw ffmpeg args inserted before the input path."), extraInputArguments);
                    extraOutputArguments = EditorGUILayout.TextField(new GUIContent("Before output", "Raw ffmpeg args inserted before the output path."), extraOutputArguments);
                    DrawHint("Advanced arguments are passed directly to ffmpeg. Keep them empty unless you need a specific ffmpeg option.");
                }

                var ffmpegPath = FfmpegLocator.GetExecutablePath();
                using (new EditorGUI.DisabledScope(true))
                    EditorGUILayout.TextField("ffmpeg", ffmpegPath);

                GUILayout.Space(6);
                using (new EditorGUI.DisabledScope(isConverting || string.IsNullOrEmpty(inputPath)))
                {
                    if (GUILayout.Button(GetActionLabel(operationMode), primaryButtonStyle, GUILayout.Height(34)))
                        StartConvert();
                }

                if (isConverting)
                {
                    DrawHint("ON AIR: ffmpeg is converting. Unity remains usable while the process runs.");

                    if (GUILayout.Button("STOP CONVERSION", GUILayout.Height(26)))
                        StopConversion();
                }
            }

            using (new EditorGUILayout.VerticalScope(panelStyle))
            {
                DrawSectionTitle("LOG");

                logScroll = EditorGUILayout.BeginScrollView(logScroll, GUILayout.MinHeight(180));
                EditorGUILayout.TextArea(log.ToString(), logStyle, GUILayout.ExpandHeight(true));
                EditorGUILayout.EndScrollView();
            }

            EditorGUILayout.EndScrollView();
        }

        private void SelectInput()
        {
            var path = EditorUtility.OpenFilePanel("Select Video", string.Empty, "mp4,mov,avi,mkv,webm");
            if (string.IsNullOrEmpty(path))
                return;

            inputPath = path;
            outputPath = BuildDefaultOutputPath(path, operationMode, Presets[presetIndex]);
            outputPathWasEdited = false;
        }

        private void SelectOutput()
        {
            if (string.IsNullOrEmpty(inputPath))
                return;

            var directory = Path.GetDirectoryName(inputPath);
            var fileName = Path.GetFileNameWithoutExtension(outputPath);
            var path = EditorUtility.SaveFilePanel("Save HAP Movie", directory, fileName, "mov");
            if (!string.IsNullOrEmpty(path))
            {
                outputPath = path;
                outputPathWasEdited = true;
            }
        }

        private void StartConvert()
        {
            var ffmpegPath = FfmpegLocator.GetExecutablePath();
            if (!File.Exists(ffmpegPath))
            {
                EditorUtility.DisplayDialog(
                    "ffmpeg not found",
                    $"Place ffmpeg here before converting:\n\n{ffmpegPath}",
                    "OK");
                return;
            }

            if (!File.Exists(inputPath))
            {
                EditorUtility.DisplayDialog("Input not found", "The selected input movie could not be found.", "OK");
                return;
            }

            if (string.IsNullOrEmpty(outputPath))
                outputPath = BuildDefaultOutputPath(inputPath, operationMode, Presets[presetIndex]);

            var preset = Presets[presetIndex];
            var options = new FfmpegOptions(
                resizeEnabled,
                outputWidth,
                outputHeight,
                fpsEnabled,
                outputFps,
                trimEnabled,
                trimStart,
                trimDuration,
                audioMode,
                extraInputArguments,
                extraOutputArguments);
            var arguments = BuildArguments(inputPath, outputPath, operationMode, preset, chunks, overwrite, options);

            isConverting = true;
            log.Length = 0;
            AppendLog($"Start {GetOperationDisplayName(operationMode)}\nffmpeg: {ffmpegPath}\nargs: {arguments}\n\n");

            process = new Process();
            process.StartInfo.FileName = ffmpegPath;
            process.StartInfo.Arguments = arguments;
            process.StartInfo.WorkingDirectory = Path.GetDirectoryName(ffmpegPath);
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.CreateNoWindow = true;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = true;
            process.EnableRaisingEvents = true;

            process.OutputDataReceived += (_, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                    AppendLogThreadSafe(e.Data);
            };

            process.ErrorDataReceived += (_, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                    AppendLogThreadSafe(e.Data);
            };

            process.Exited += (_, _) => EditorApplication.delayCall += OnProcessExited;

            try
            {
                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
            }
            catch (Exception e)
            {
                isConverting = false;
                AppendLog(e + "\n");
                Debug.LogException(e);
                process.Dispose();
                process = null;
            }
        }

        private void OnProcessExited()
        {
            if (process == null)
                return;

            var exitCode = process.ExitCode;
            isConverting = false;

            if (exitCode == 0)
            {
                AppendLog($"\nConvert complete\nOutput: {outputPath}\n");
                if (revealOnComplete)
                    EditorUtility.RevealInFinder(outputPath);
            }
            else
            {
                AppendLog($"\nConvert failed. ExitCode: {exitCode}\n");
            }

            process.Dispose();
            process = null;
            Repaint();
        }

        private void StopConversion()
        {
            if (process == null || process.HasExited)
                return;

            try
            {
                process.Kill();
                AppendLog("\nConversion stopped.\n");
            }
            catch (Exception e)
            {
                AppendLog(e + "\n");
                Debug.LogException(e);
            }
        }

        private void OnDisable()
        {
            if (process != null && !process.HasExited)
                StopConversion();
        }

        private void AppendLogThreadSafe(string line)
        {
            EditorApplication.delayCall += () =>
            {
                AppendLog(line + "\n");
                Repaint();
            };
        }

        private void AppendLog(string text)
        {
            log.Append(text);
        }

        private static string BuildDefaultOutputPath(string sourcePath, OperationMode operation, HapPreset preset)
        {
            var directory = Path.GetDirectoryName(sourcePath);
            var name = Path.GetFileNameWithoutExtension(sourcePath);
            switch (operation)
            {
                case OperationMode.Hap:
                    return Path.Combine(directory, name + preset.DefaultSuffix);
                case OperationMode.H264Mp4:
                    return Path.Combine(directory, name + "_H264.mp4");
                case OperationMode.H265Mp4:
                    return Path.Combine(directory, name + "_H265.mp4");
                case OperationMode.ProResMov:
                    return Path.Combine(directory, name + "_ProRes.mov");
                case OperationMode.ExtractWav:
                    return Path.Combine(directory, name + "_Audio.wav");
                case OperationMode.RemuxMov:
                    return Path.Combine(directory, name + "_Remux.mov");
                case OperationMode.Custom:
                    return Path.Combine(directory, name + "_FFmpeg.mov");
                default:
                    return Path.Combine(directory, name + "_Output.mov");
            }
        }

        private void InitializeStyles()
        {
            if (headerStyle != null &&
                panelStyle != null &&
                sectionTitleStyle != null &&
                statusStyle != null &&
                miniHintStyle != null &&
                primaryButtonStyle != null &&
                logStyle != null)
            {
                return;
            }

            headerStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 17,
                normal = { textColor = EditorGUIUtility.isProSkin ? new Color(0.90f, 0.91f, 0.92f) : new Color(0.12f, 0.13f, 0.14f) },
                margin = new RectOffset(0, 0, 3, 1)
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

            sectionTitleStyle = new GUIStyle(EditorStyles.miniBoldLabel)
            {
                fontSize = 10,
                normal = { textColor = AccentColor(1f) }
            };

            statusStyle = new GUIStyle(EditorStyles.miniBoldLabel)
            {
                alignment = TextAnchor.MiddleRight,
                normal = { textColor = new Color(0.72f, 0.74f, 0.76f) }
            };

            miniHintStyle = new GUIStyle(EditorStyles.helpBox)
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

            primaryButtonStyle = new GUIStyle(EditorStyles.miniButton)
            {
                fontStyle = FontStyle.Bold,
                normal = { textColor = EditorGUIUtility.isProSkin ? new Color(0.92f, 0.94f, 0.96f) : new Color(0.10f, 0.12f, 0.15f) }
            };

            logStyle = new GUIStyle(EditorStyles.textArea)
            {
                fontSize = 11,
                wordWrap = false,
                normal =
                {
                    textColor = EditorGUIUtility.isProSkin ? new Color(0.82f, 0.84f, 0.86f) : new Color(0.12f, 0.13f, 0.14f),
                    background = MakeSolidTexture(EditorGUIUtility.isProSkin ? new Color(0.10f, 0.10f, 0.10f) : Color.white)
                }
            };
        }

        private void DrawHeader()
        {
            var rect = GUILayoutUtility.GetRect(0f, 44f, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(rect, EditorGUIUtility.isProSkin ? new Color(0.145f, 0.145f, 0.145f) : new Color(0.86f, 0.87f, 0.88f));
            EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 1f, rect.width, 1f), AccentColor(0.95f));

            var titleRect = new Rect(rect.x + 12f, rect.y + 7f, rect.width - 160f, 22f);
            GUI.Label(titleRect, "VIDEO RACK / FFMPEG", headerStyle);

            var subtitleRect = new Rect(rect.x + 12f, rect.y + 26f, rect.width - 160f, 16f);
            GUI.Label(subtitleRect, "HAP Converter", EditorStyles.miniLabel);

            var statusRect = new Rect(rect.xMax - 118f, rect.y + 11f, 104f, 20f);
            statusStyle.normal.textColor = isConverting ? new Color(0.95f, 0.44f, 0.22f) : new Color(0.72f, 0.74f, 0.76f);
            GUI.Label(statusRect, isConverting ? "ENCODING" : "STANDBY", statusStyle);
        }

        private static Color AccentColor(float alpha)
        {
            return new Color(0.08f, 0.38f, 0.86f, alpha);
        }

        private static Texture2D MakeSolidTexture(Color color)
        {
            var texture = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
            texture.SetPixel(0, 0, color);
            texture.Apply();
            return texture;
        }

        private void DrawSectionTitle(string title)
        {
            EditorGUILayout.LabelField(title, sectionTitleStyle);
        }

        private void DrawPathRow(string label, string path, string buttonLabel, Action buttonAction)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.TextField(label, path);
                if (GUILayout.Button(buttonLabel, GUILayout.Width(86)))
                    buttonAction();
            }
        }

        private void DrawHint(string text)
        {
            EditorGUILayout.LabelField(text, miniHintStyle);
        }

        private static string BuildArguments(string input, string output, OperationMode operation, HapPreset preset, int chunks, bool overwriteOutput, FfmpegOptions options)
        {
            var overwriteFlag = overwriteOutput ? "-y" : "-n";
            var arguments = new StringBuilder();
            arguments.Append(overwriteFlag);

            if (options.TrimEnabled && !string.IsNullOrWhiteSpace(options.TrimStart))
                arguments.Append(" -ss ").Append(Quote(options.TrimStart.Trim()));

            if (options.TrimEnabled && !string.IsNullOrWhiteSpace(options.TrimDuration))
                arguments.Append(" -t ").Append(Quote(options.TrimDuration.Trim()));

            AppendRawArguments(arguments, options.ExtraInputArguments);
            arguments.Append(" -i ").Append(Quote(input));

            var filter = operation == OperationMode.ExtractWav || operation == OperationMode.RemuxMov
                ? string.Empty
                : BuildVideoFilter(options);
            if (!string.IsNullOrEmpty(filter))
                arguments.Append(" -vf ").Append(Quote(filter));

            switch (operation)
            {
                case OperationMode.Hap:
                    arguments.Append(" -c:v hap -format ").Append(preset.FfmpegFormat);
                    arguments.Append(" -chunks ").Append(Mathf.Clamp(chunks, 1, 64));
                    AppendAudioArguments(arguments, options.AudioMode);
                    break;
                case OperationMode.H264Mp4:
                    arguments.Append(" -c:v libx264 -pix_fmt yuv420p -crf 18 -preset medium");
                    AppendAudioArguments(arguments, options.AudioMode);
                    break;
                case OperationMode.H265Mp4:
                    arguments.Append(" -c:v libx265 -pix_fmt yuv420p -crf 22 -preset medium");
                    AppendAudioArguments(arguments, options.AudioMode);
                    break;
                case OperationMode.ProResMov:
                    arguments.Append(" -c:v prores_ks -profile:v 3 -pix_fmt yuv422p10le");
                    AppendAudioArguments(arguments, options.AudioMode);
                    break;
                case OperationMode.ExtractWav:
                    arguments.Append(" -vn -c:a pcm_s16le");
                    break;
                case OperationMode.RemuxMov:
                    arguments.Append(" -c copy");
                    break;
                case OperationMode.Custom:
                    break;
            }

            AppendRawArguments(arguments, options.ExtraOutputArguments);
            arguments.Append(" ").Append(Quote(output));
            return arguments.ToString();
        }

        private static void AppendAudioArguments(StringBuilder arguments, AudioMode audioMode)
        {
            switch (audioMode)
            {
                case AudioMode.Copy:
                    arguments.Append(" -c:a copy");
                    break;
                case AudioMode.Aac192:
                    arguments.Append(" -c:a aac -b:a 192k");
                    break;
                case AudioMode.Remove:
                    arguments.Append(" -an");
                    break;
            }
        }

        private static string BuildVideoFilter(FfmpegOptions options)
        {
            var filter = new StringBuilder();
            if (options.ResizeEnabled)
            {
                var width = Mathf.Max(2, options.Width);
                var height = Mathf.Max(2, options.Height);
                filter.Append("scale=").Append(width).Append(":").Append(height);
            }

            if (options.FpsEnabled)
            {
                if (filter.Length > 0)
                    filter.Append(",");

                filter.Append("fps=").Append(Mathf.Max(1f, options.Fps).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
            }

            return filter.ToString();
        }

        private static void AppendRawArguments(StringBuilder arguments, string rawArguments)
        {
            if (string.IsNullOrWhiteSpace(rawArguments))
                return;

            arguments.Append(" ").Append(rawArguments.Trim());
        }

        private static string Quote(string value)
        {
            return "\"" + value.Replace("\"", "\\\"") + "\"";
        }

        private static string[] GetPresetNames()
        {
            var names = new string[Presets.Length];
            for (var i = 0; i < Presets.Length; i++)
                names[i] = $"{Presets[i].Name} - {Presets[i].Description}";

            return names;
        }

        private static string[] GetOperationNames()
        {
            return new[]
            {
                "HAP Movie",
                "H.264 MP4",
                "H.265 MP4",
                "ProRes MOV",
                "Extract WAV",
                "Remux MOV",
                "Custom ffmpeg"
            };
        }

        private static string GetOperationDisplayName(OperationMode operation)
        {
            return GetOperationNames()[(int)operation];
        }

        private static string GetActionLabel(OperationMode operation)
        {
            switch (operation)
            {
                case OperationMode.ExtractWav:
                    return "EXTRACT AUDIO";
                case OperationMode.RemuxMov:
                    return "REMUX";
                case OperationMode.Custom:
                    return "RUN FFMPEG";
                default:
                    return "CONVERT";
            }
        }

        private static string GetOperationHelp(OperationMode operation)
        {
            switch (operation)
            {
                case OperationMode.Hap:
                    return "Creates a HAP .mov for media-server style playback and live visuals.";
                case OperationMode.H264Mp4:
                    return "Creates a compact H.264 .mp4 for review, upload, and general playback.";
                case OperationMode.H265Mp4:
                    return "Creates a smaller H.265 .mp4. Encoding is slower; playback compatibility varies.";
                case OperationMode.ProResMov:
                    return "Creates an edit-friendly ProRes .mov for handoff to NLEs and production pipelines.";
                case OperationMode.ExtractWav:
                    return "Extracts audio as uncompressed WAV.";
                case OperationMode.RemuxMov:
                    return "Rewraps streams without re-encoding. Fast, but only works when codecs fit the output container.";
                case OperationMode.Custom:
                    return "Runs ffmpeg with your advanced output arguments. Video/audio codec args are not added automatically.";
                default:
                    return string.Empty;
            }
        }

        private static string GetPresetHelp(HapPreset preset)
        {
            switch (preset.FfmpegFormat)
            {
                case "hap_alpha":
                    return "HAP Alpha keeps transparency. Use it for keyed or alpha-channel clips. Files are larger than standard HAP.";
                case "hap_q":
                    return "HAP Q prioritizes image quality. Use it when gradients or fine details need to hold up, with larger files and heavier playback.";
                default:
                    return "HAP is the standard preset for opaque video. It is the best first choice for general playback.";
            }
        }

        private readonly struct HapPreset
        {
            public readonly string Name;
            public readonly string FfmpegFormat;
            public readonly string Description;
            public readonly string DefaultSuffix;

            public HapPreset(string name, string ffmpegFormat, string description, string defaultSuffix)
            {
                Name = name;
                FfmpegFormat = ffmpegFormat;
                Description = description;
                DefaultSuffix = defaultSuffix;
            }
        }

        private enum AudioMode
        {
            Copy,
            Aac192,
            Remove
        }

        private enum OperationMode
        {
            Hap,
            H264Mp4,
            H265Mp4,
            ProResMov,
            ExtractWav,
            RemuxMov,
            Custom
        }

        private readonly struct FfmpegOptions
        {
            public readonly bool ResizeEnabled;
            public readonly int Width;
            public readonly int Height;
            public readonly bool FpsEnabled;
            public readonly float Fps;
            public readonly bool TrimEnabled;
            public readonly string TrimStart;
            public readonly string TrimDuration;
            public readonly AudioMode AudioMode;
            public readonly string ExtraInputArguments;
            public readonly string ExtraOutputArguments;

            public FfmpegOptions(
                bool resizeEnabled,
                int width,
                int height,
                bool fpsEnabled,
                float fps,
                bool trimEnabled,
                string trimStart,
                string trimDuration,
                AudioMode audioMode,
                string extraInputArguments,
                string extraOutputArguments)
            {
                ResizeEnabled = resizeEnabled;
                Width = width;
                Height = height;
                FpsEnabled = fpsEnabled;
                Fps = fps;
                TrimEnabled = trimEnabled;
                TrimStart = trimStart;
                TrimDuration = trimDuration;
                AudioMode = audioMode;
                ExtraInputArguments = extraInputArguments;
                ExtraOutputArguments = extraOutputArguments;
            }
        }

        private static class FfmpegLocator
        {
            public static string GetExecutablePath()
            {
#if UNITY_EDITOR_WIN
                return GetExecutablePath("Windows", "ffmpeg.exe");
#elif UNITY_EDITOR_OSX
                return GetExecutablePath("macOS", "ffmpeg");
#else
                return GetExecutablePath("Linux", "ffmpeg");
#endif
            }

            private static string GetExecutablePath(string platformDirectory, string executableName)
            {
                var packageToolRoot = GetPackageToolRoot();
                if (!string.IsNullOrEmpty(packageToolRoot))
                {
                    var packageExecutablePath = Path.Combine(packageToolRoot, platformDirectory, executableName);
                    if (File.Exists(packageExecutablePath))
                        return packageExecutablePath;

                    var payloadPath = packageExecutablePath + ".bytes";
                    if (File.Exists(payloadPath))
                        return EnsureCachedExecutable(payloadPath, platformDirectory, executableName);

                    var payloadPartPaths = GetPayloadPartPaths(packageExecutablePath);
                    if (payloadPartPaths.Length > 0)
                        return EnsureCachedExecutable(payloadPartPaths, platformDirectory, executableName);
                }

                var projectExecutablePath = Path.Combine(GetProjectToolRoot(), platformDirectory, executableName);
                if (File.Exists(projectExecutablePath))
                    return projectExecutablePath;

                return !string.IsNullOrEmpty(packageToolRoot)
                    ? Path.Combine(packageToolRoot, platformDirectory, executableName)
                    : projectExecutablePath;
            }

            private static string GetPackageToolRoot()
            {
                var packageInfo = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(HapConverterWindow).Assembly);
                if (packageInfo != null && !string.IsNullOrEmpty(packageInfo.resolvedPath))
                {
                    var packageToolRoot = Path.Combine(packageInfo.resolvedPath, "Tools", "FFmpeg");
                    if (Directory.Exists(packageToolRoot))
                        return packageToolRoot;
                }

                return null;
            }

            private static string GetProjectToolRoot()
            {
                return Path.Combine(
                    Application.dataPath,
                    "toshi.VLiveKit",
                    "VideoRack",
                    "Tools",
                    "FFmpeg");
            }

            private static string EnsureCachedExecutable(string payloadPath, string platformDirectory, string executableName)
            {
                var payloadInfo = new FileInfo(payloadPath);
                var cachedPath = GetCachedExecutablePath(platformDirectory, executableName);
                var cachedInfo = new FileInfo(cachedPath);
                if (cachedInfo.Exists && cachedInfo.Length == payloadInfo.Length)
                    return cachedPath;

                Directory.CreateDirectory(Path.GetDirectoryName(cachedPath));
                File.Copy(payloadPath, cachedPath, true);
                return cachedPath;
            }

            private static string EnsureCachedExecutable(string[] payloadPartPaths, string platformDirectory, string executableName)
            {
                var payloadLength = 0L;
                foreach (var payloadPartPath in payloadPartPaths)
                    payloadLength += new FileInfo(payloadPartPath).Length;

                var cachedPath = GetCachedExecutablePath(platformDirectory, executableName);
                var cachedInfo = new FileInfo(cachedPath);
                if (cachedInfo.Exists && cachedInfo.Length == payloadLength)
                    return cachedPath;

                Directory.CreateDirectory(Path.GetDirectoryName(cachedPath));
                using (var output = File.Open(cachedPath, FileMode.Create, FileAccess.Write))
                {
                    foreach (var payloadPartPath in payloadPartPaths)
                    {
                        using (var input = File.OpenRead(payloadPartPath))
                            input.CopyTo(output);
                    }
                }

                return cachedPath;
            }

            private static string[] GetPayloadPartPaths(string packageExecutablePath)
            {
                var directory = Path.GetDirectoryName(packageExecutablePath);
                var fileName = Path.GetFileName(packageExecutablePath);
                if (string.IsNullOrEmpty(directory) || string.IsNullOrEmpty(fileName))
                    return Array.Empty<string>();

                var payloadPartPaths = Directory.GetFiles(directory, fileName + ".part*.bytes");
                if (payloadPartPaths.Length == 0)
                    payloadPartPaths = Directory.GetFiles(directory, fileName + ".part*");
                Array.Sort(payloadPartPaths, StringComparer.Ordinal);
                return payloadPartPaths;
            }

            private static string GetCachedExecutablePath(string platformDirectory, string executableName)
            {
                var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                return Path.Combine(
                    projectRoot,
                    "Library",
                    "VLiveKit",
                    "VideoRack",
                    "FFmpeg",
                    platformDirectory,
                    executableName);
            }
        }
    }
}
#endif
