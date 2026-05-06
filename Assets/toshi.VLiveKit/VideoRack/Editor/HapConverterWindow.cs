#if UNITY_EDITOR
using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.PackageManager;
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
        private int presetIndex;
        private int chunks = 1;
        private bool overwrite = true;
        private bool revealOnComplete = true;
        private bool isConverting;
        private bool outputPathWasEdited;
        private Process process;
        private readonly StringBuilder log = new StringBuilder();
        private Vector2 logScroll;

        [MenuItem("toshi/VLiveKit/VideoRack/HAP Converter")]
        public static void Open()
        {
            GetWindow<HapConverterWindow>(WindowTitle);
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("HAP Converter", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.TextField("Input", inputPath);
                if (GUILayout.Button("Select", GUILayout.Width(80)))
                    SelectInput();
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.TextField("Output", outputPath);
                using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(inputPath)))
                {
                    if (GUILayout.Button("Change", GUILayout.Width(80)))
                        SelectOutput();
                }
            }

            EditorGUILayout.Space();

            EditorGUI.BeginChangeCheck();
            presetIndex = EditorGUILayout.Popup("Preset", presetIndex, GetPresetNames());
            if (EditorGUI.EndChangeCheck() && !outputPathWasEdited && !string.IsNullOrEmpty(inputPath))
                outputPath = BuildDefaultOutputPath(inputPath, Presets[presetIndex]);

            chunks = EditorGUILayout.IntSlider("Chunks", chunks, 1, 64);
            overwrite = EditorGUILayout.Toggle("Overwrite Output", overwrite);
            revealOnComplete = EditorGUILayout.Toggle("Reveal On Complete", revealOnComplete);

            var ffmpegPath = FfmpegLocator.GetExecutablePath();
            using (new EditorGUI.DisabledScope(true))
                EditorGUILayout.TextField("ffmpeg", ffmpegPath);

            EditorGUILayout.Space();

            using (new EditorGUI.DisabledScope(isConverting || string.IsNullOrEmpty(inputPath)))
            {
                if (GUILayout.Button("Convert to HAP", GUILayout.Height(28)))
                    StartConvert();
            }

            if (isConverting)
            {
                EditorGUILayout.HelpBox("Converting. Unity remains usable while ffmpeg is running.", MessageType.Info);

                if (GUILayout.Button("Stop Conversion"))
                    StopConversion();
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Log", EditorStyles.boldLabel);

            logScroll = EditorGUILayout.BeginScrollView(logScroll, GUILayout.MinHeight(180));
            EditorGUILayout.TextArea(log.ToString(), GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();
        }

        private void SelectInput()
        {
            var path = EditorUtility.OpenFilePanel("Select Video", string.Empty, "mp4,mov,avi,mkv,webm");
            if (string.IsNullOrEmpty(path))
                return;

            inputPath = path;
            outputPath = BuildDefaultOutputPath(path, Presets[presetIndex]);
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
                outputPath = BuildDefaultOutputPath(inputPath, Presets[presetIndex]);

            var preset = Presets[presetIndex];
            var arguments = BuildArguments(inputPath, outputPath, preset, chunks, overwrite);

            isConverting = true;
            log.Length = 0;
            AppendLog($"Start HAP Convert\nffmpeg: {ffmpegPath}\nargs: {arguments}\n\n");

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

        private static string BuildDefaultOutputPath(string sourcePath, HapPreset preset)
        {
            var directory = Path.GetDirectoryName(sourcePath);
            var name = Path.GetFileNameWithoutExtension(sourcePath);
            return Path.Combine(directory, name + preset.DefaultSuffix);
        }

        private static string BuildArguments(string input, string output, HapPreset preset, int chunks, bool overwriteOutput)
        {
            var overwriteFlag = overwriteOutput ? "-y" : "-n";
            return $"{overwriteFlag} -i {Quote(input)} -c:v hap -format {preset.FfmpegFormat} -chunks {chunks} {Quote(output)}";
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

        private static class FfmpegLocator
        {
            public static string GetExecutablePath()
            {
#if UNITY_EDITOR_WIN
                return Path.Combine(GetToolRoot(), "Windows", "ffmpeg.exe");
#elif UNITY_EDITOR_OSX
                return Path.Combine(GetToolRoot(), "macOS", "ffmpeg");
#else
                return Path.Combine(GetToolRoot(), "Linux", "ffmpeg");
#endif
            }

            private static string GetToolRoot()
            {
                var packageInfo = PackageInfo.FindForAssembly(typeof(HapConverterWindow).Assembly);
                if (packageInfo != null && !string.IsNullOrEmpty(packageInfo.resolvedPath))
                {
                    var packageToolRoot = Path.Combine(packageInfo.resolvedPath, "Tools", "FFmpeg");
                    if (Directory.Exists(packageToolRoot))
                        return packageToolRoot;
                }

                return Path.Combine(
                    Application.dataPath,
                    "toshi.VLiveKit",
                    "VideoRack",
                    "Tools",
                    "FFmpeg");
            }
        }
    }
}
#endif
