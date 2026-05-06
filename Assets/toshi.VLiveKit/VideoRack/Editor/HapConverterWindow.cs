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
            presetIndex = EditorGUILayout.Popup(
                new GUIContent("Preset", "Choose the HAP variant written by ffmpeg."),
                presetIndex,
                GetPresetNames());
            if (EditorGUI.EndChangeCheck() && !outputPathWasEdited && !string.IsNullOrEmpty(inputPath))
                outputPath = BuildDefaultOutputPath(inputPath, Presets[presetIndex]);

            EditorGUILayout.HelpBox(GetPresetHelp(Presets[presetIndex]), MessageType.None);

            chunks = EditorGUILayout.IntSlider(
                new GUIContent("Chunks", "Number of HAP texture chunks per frame. Use 1 first; increase for high-resolution playback tests."),
                chunks,
                1,
                64);
            EditorGUILayout.HelpBox("Chunks split each video frame for HAP playback. 1 is the safest default. Try 4 or 8 for heavy high-resolution clips if playback stutters.", MessageType.None);

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
