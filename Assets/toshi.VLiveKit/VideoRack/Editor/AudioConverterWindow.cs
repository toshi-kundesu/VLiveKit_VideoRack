#if UNITY_EDITOR
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace VLiveKit.VideoRack.Editor
{
    public sealed class AudioConverterWindow : EditorWindow
    {
        private const string WindowTitle = "Audio Converter";

        private static readonly int[] Mp3Bitrates = { 128, 192, 256, 320 };
        private static readonly string[] Mp3BitrateNames = { "128 kbps", "192 kbps", "256 kbps", "320 kbps" };
        private static readonly int[] SampleRates = { 0, 44100, 48000 };
        private static readonly string[] SampleRateNames = { "Keep source", "44.1 kHz", "48 kHz" };
        private static readonly int[] ChannelCounts = { 0, 1, 2 };
        private static readonly string[] ChannelNames = { "Keep source", "Mono", "Stereo" };

        private string inputPath = string.Empty;
        private string outputPath = string.Empty;
        private OutputFormat outputFormat = OutputFormat.Wav;
        private int mp3BitrateIndex = 1;
        private int oggQuality = 5;
        private int sampleRateIndex;
        private int channelIndex;
        private bool trimEnabled;
        private string trimStart = string.Empty;
        private string trimDuration = string.Empty;
        private bool advancedOpen;
        private string extraInputArguments = string.Empty;
        private string extraOutputArguments = string.Empty;
        private bool overwrite = true;
        private bool revealOnComplete = true;
        private bool isConverting;
        private bool stopRequested;
        private bool outputPathWasEdited;
        private Process process;
        private readonly StringBuilder log = new StringBuilder();
        private Vector2 mainScroll;
        private Vector2 logScroll;
        private double inputDurationSeconds = -1d;
        private double progressSeconds;
        private float conversionProgress;
        private string progressLabel = "Waiting";
        private string statusMessage = "Ready";

        [MenuItem("toshi/VLiveKit/VideoRack/Audio Converter")]
        public static void Open()
        {
            GetWindow<AudioConverterWindow>(WindowTitle);
        }

        private void OnGUI()
        {
            mainScroll = EditorGUILayout.BeginScrollView(mainScroll);
            DrawHeader();

            using (new EditorGUILayout.VerticalScope(VideoRackEditorUI.Panel))
            {
                DrawSectionTitle("Source");
                DrawPathRow("Input", inputPath, "Select", SelectInput);
                using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(inputPath)))
                    DrawPathRow("Output", outputPath, "Change", SelectOutput);
                DrawHint("Select an MP3, WAV, or OGG file, then choose the output format below.");
            }

            using (new EditorGUILayout.VerticalScope(VideoRackEditorUI.Panel))
            {
                DrawSectionTitle("Format");

                EditorGUI.BeginChangeCheck();
                outputFormat = (OutputFormat)EditorGUILayout.Popup(
                    new GUIContent("Output", "Choose the audio container and codec written by ffmpeg."),
                    (int)outputFormat,
                    GetFormatNames());
                if (EditorGUI.EndChangeCheck() && !outputPathWasEdited && !string.IsNullOrEmpty(inputPath))
                    outputPath = BuildDefaultOutputPath(inputPath, outputFormat);

                DrawHint(GetFormatHelp(outputFormat));

                switch (outputFormat)
                {
                    case OutputFormat.Mp3:
                        mp3BitrateIndex = EditorGUILayout.Popup(new GUIContent("Bitrate", "MP3 constant bitrate."), mp3BitrateIndex, Mp3BitrateNames);
                        break;
                    case OutputFormat.Ogg:
                        oggQuality = EditorGUILayout.IntSlider(new GUIContent("Quality", "Vorbis quality. 0 is smallest; 10 is largest."), oggQuality, 0, 10);
                        break;
                }
            }

            using (new EditorGUILayout.VerticalScope(VideoRackEditorUI.Panel))
            {
                DrawSectionTitle("Processing");

                sampleRateIndex = EditorGUILayout.Popup(new GUIContent("Sample Rate", "Optionally resample the output audio."), sampleRateIndex, SampleRateNames);
                channelIndex = EditorGUILayout.Popup(new GUIContent("Channels", "Optionally downmix or force stereo."), channelIndex, ChannelNames);

                trimEnabled = EditorGUILayout.ToggleLeft(new GUIContent("Trim", "Uses ffmpeg -ss and -t before decoding."), trimEnabled);
                using (new EditorGUI.DisabledScope(!trimEnabled))
                {
                    trimStart = EditorGUILayout.TextField(new GUIContent("Start", "Examples: 3.5, 00:00:03.500"), trimStart);
                    trimDuration = EditorGUILayout.TextField(new GUIContent("Duration", "Examples: 10, 00:00:10.000"), trimDuration);
                }
            }

            using (new EditorGUILayout.VerticalScope(VideoRackEditorUI.Panel))
            {
                DrawSectionTitle("Output");
                overwrite = EditorGUILayout.Toggle("Overwrite Output", overwrite);
                revealOnComplete = EditorGUILayout.Toggle("Reveal On Complete", revealOnComplete);

                advancedOpen = EditorGUILayout.Foldout(advancedOpen, "Advanced ffmpeg arguments", true);
                if (advancedOpen)
                {
                    extraInputArguments = EditorGUILayout.TextField(new GUIContent("Before -i", "Raw ffmpeg args inserted before the input path."), extraInputArguments);
                    extraOutputArguments = EditorGUILayout.TextField(new GUIContent("Before output", "Raw ffmpeg args inserted before the output path."), extraOutputArguments);
                    DrawHint("Advanced arguments are passed directly to ffmpeg. Keep them empty unless you need a specific ffmpeg option.");
                }

                var ffmpegPath = VideoRackFfmpegLocator.GetExecutablePath();
                using (new EditorGUI.DisabledScope(true))
                    EditorGUILayout.TextField("ffmpeg", ffmpegPath);
                VideoRackEditorUI.DrawStatus(statusMessage);

                GUILayout.Space(6);
                using (new EditorGUI.DisabledScope(isConverting || string.IsNullOrEmpty(inputPath)))
                {
                    if (GUILayout.Button("Convert Audio", VideoRackEditorUI.PrimaryButton, GUILayout.Height(30)))
                        StartConvert();
                }

                if (isConverting)
                {
                    DrawHint("FFmpeg is converting. Unity remains usable while the process runs.");
                    DrawConversionProgress();

                    if (GUILayout.Button("Stop Conversion", GUILayout.Height(24)))
                        StopConversion();
                }
            }

            using (new EditorGUILayout.VerticalScope(VideoRackEditorUI.Panel))
            {
                DrawSectionTitle("Log");

                logScroll = EditorGUILayout.BeginScrollView(logScroll, GUILayout.MinHeight(180));
                EditorGUILayout.TextArea(log.ToString(), VideoRackEditorUI.Log, GUILayout.ExpandHeight(true));
                EditorGUILayout.EndScrollView();
            }

            EditorGUILayout.EndScrollView();
        }

        private void SelectInput()
        {
            var path = EditorUtility.OpenFilePanel("Select Audio", string.Empty, "mp3,wav,ogg");
            if (string.IsNullOrEmpty(path))
                return;

            inputPath = path;
            outputPath = BuildDefaultOutputPath(path, outputFormat);
            outputPathWasEdited = false;
            statusMessage = "Input selected.";
        }

        private void SelectOutput()
        {
            if (string.IsNullOrEmpty(inputPath))
                return;

            var directory = Path.GetDirectoryName(inputPath);
            var fileName = Path.GetFileNameWithoutExtension(outputPath);
            var extension = GetOutputExtension(outputFormat);
            var path = EditorUtility.SaveFilePanel("Save Audio", directory, fileName, extension);
            if (!string.IsNullOrEmpty(path))
            {
                outputPath = path;
                outputPathWasEdited = true;
                statusMessage = "Output path set.";
            }
        }

        private void StartConvert()
        {
            var ffmpegPath = VideoRackFfmpegLocator.GetExecutablePath();
            if (!File.Exists(ffmpegPath))
            {
                statusMessage = $"ffmpeg was not found. Place ffmpeg at: {ffmpegPath}";
                VideoRackEditorUI.ShowNotification(this, "ffmpeg not found");
                return;
            }

            if (!File.Exists(inputPath))
            {
                statusMessage = "The selected input audio file could not be found.";
                VideoRackEditorUI.ShowNotification(this, "Input not found");
                return;
            }

            if (string.IsNullOrEmpty(outputPath))
                outputPath = BuildDefaultOutputPath(inputPath, outputFormat);

            var options = new AudioConvertOptions(
                Mp3Bitrates[Mathf.Clamp(mp3BitrateIndex, 0, Mp3Bitrates.Length - 1)],
                Mathf.Clamp(oggQuality, 0, 10),
                SampleRates[Mathf.Clamp(sampleRateIndex, 0, SampleRates.Length - 1)],
                ChannelCounts[Mathf.Clamp(channelIndex, 0, ChannelCounts.Length - 1)],
                trimEnabled,
                trimStart,
                trimDuration,
                extraInputArguments,
                extraOutputArguments);
            var arguments = BuildArguments(inputPath, outputPath, outputFormat, overwrite, options);

            isConverting = true;
            stopRequested = false;
            inputDurationSeconds = -1d;
            progressSeconds = 0d;
            conversionProgress = 0f;
            progressLabel = "Starting";
            statusMessage = "Starting ffmpeg.";
            log.Length = 0;
            AppendLog($"Start {GetFormatDisplayName(outputFormat)} conversion\nffmpeg: {ffmpegPath}\nargs: {arguments}\n\n");

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
                statusMessage = "FFmpeg is running.";
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
            }
            catch (Exception e)
            {
                isConverting = false;
                statusMessage = "Could not start ffmpeg. See the log for details.";
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

            if (stopRequested)
            {
                progressLabel = "Stopped";
                statusMessage = "Conversion stopped.";
                AppendLog("\nConversion stopped.\n");
            }
            else if (exitCode == 0)
            {
                conversionProgress = 1f;
                progressLabel = "Complete";
                statusMessage = "Conversion complete.";
                AppendLog($"\nConvert complete\nOutput: {outputPath}\n");
                if (revealOnComplete)
                    EditorUtility.RevealInFinder(outputPath);
            }
            else
            {
                progressLabel = "Failed";
                statusMessage = $"FFmpeg exited with code {exitCode}. See the log for details.";
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
                stopRequested = true;
                process.Kill();
            }
            catch (Exception e)
            {
                statusMessage = "Could not stop the ffmpeg process. See the log for details.";
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
                UpdateProgressFromFfmpegLine(line);
                AppendLog(line + "\n");
                Repaint();
            };
        }

        private void AppendLog(string text)
        {
            log.Append(text);
        }

        private static string BuildArguments(string input, string output, OutputFormat format, bool overwriteOutput, AudioConvertOptions options)
        {
            var arguments = new StringBuilder();
            arguments.Append(overwriteOutput ? "-y" : "-n");

            if (options.TrimEnabled && !string.IsNullOrWhiteSpace(options.TrimStart))
                arguments.Append(" -ss ").Append(Quote(options.TrimStart.Trim()));

            if (options.TrimEnabled && !string.IsNullOrWhiteSpace(options.TrimDuration))
                arguments.Append(" -t ").Append(Quote(options.TrimDuration.Trim()));

            AppendRawArguments(arguments, options.ExtraInputArguments);
            arguments.Append(" -i ").Append(Quote(input));
            arguments.Append(" -vn");

            switch (format)
            {
                case OutputFormat.Mp3:
                    arguments.Append(" -c:a libmp3lame -b:a ").Append(Mathf.Max(32, options.Mp3BitrateKbps)).Append("k");
                    break;
                case OutputFormat.Wav:
                    arguments.Append(" -c:a pcm_s16le");
                    break;
                case OutputFormat.Ogg:
                    arguments.Append(" -c:a libvorbis -q:a ").Append(Mathf.Clamp(options.OggQuality, 0, 10).ToString(CultureInfo.InvariantCulture));
                    break;
            }

            if (options.SampleRate > 0)
                arguments.Append(" -ar ").Append(options.SampleRate);

            if (options.Channels > 0)
                arguments.Append(" -ac ").Append(options.Channels);

            AppendRawArguments(arguments, options.ExtraOutputArguments);
            arguments.Append(" ").Append(Quote(output));
            return arguments.ToString();
        }

        private static void AppendRawArguments(StringBuilder arguments, string rawArguments)
        {
            if (string.IsNullOrWhiteSpace(rawArguments))
                return;

            arguments.Append(" ").Append(rawArguments.Trim());
        }

        private void UpdateProgressFromFfmpegLine(string line)
        {
            if (string.IsNullOrEmpty(line))
                return;

            var durationIndex = line.IndexOf("Duration:", StringComparison.Ordinal);
            if (durationIndex >= 0)
            {
                var durationText = ReadTokenAfter(line, durationIndex + "Duration:".Length).TrimEnd(',');
                if (TryParseFfmpegTime(durationText, out var duration))
                {
                    inputDurationSeconds = duration;
                    progressLabel = "Duration " + FormatSeconds(duration);
                }
            }

            var timeIndex = line.IndexOf("time=", StringComparison.Ordinal);
            if (timeIndex >= 0)
            {
                var timeText = ReadTokenAfter(line, timeIndex + "time=".Length);
                if (TryParseFfmpegTime(timeText, out var time))
                {
                    progressSeconds = time;
                    if (inputDurationSeconds > 0d)
                    {
                        conversionProgress = Mathf.Clamp01((float)(progressSeconds / inputDurationSeconds));
                        progressLabel = $"{FormatSeconds(progressSeconds)} / {FormatSeconds(inputDurationSeconds)}";
                    }
                    else
                    {
                        progressLabel = FormatSeconds(progressSeconds);
                    }
                }
            }
        }

        private static string ReadTokenAfter(string line, int start)
        {
            while (start < line.Length && char.IsWhiteSpace(line[start]))
                start++;

            var end = start;
            while (end < line.Length && !char.IsWhiteSpace(line[end]))
                end++;

            return line.Substring(start, end - start);
        }

        private static bool TryParseFfmpegTime(string value, out double seconds)
        {
            seconds = 0d;
            if (string.IsNullOrWhiteSpace(value))
                return false;

            value = value.Trim();
            var parts = value.Split(':');
            if (parts.Length == 3 &&
                double.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var hours) &&
                double.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var minutes) &&
                double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var secs))
            {
                seconds = hours * 3600d + minutes * 60d + secs;
                return true;
            }

            return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out seconds);
        }

        private static string FormatSeconds(double seconds)
        {
            if (seconds < 0d)
                seconds = 0d;

            var time = TimeSpan.FromSeconds(seconds);
            return time.Hours > 0
                ? time.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture)
                : time.ToString(@"m\:ss", CultureInfo.InvariantCulture);
        }

        private static string BuildDefaultOutputPath(string sourcePath, OutputFormat format)
        {
            var directory = Path.GetDirectoryName(sourcePath);
            var name = Path.GetFileNameWithoutExtension(sourcePath);
            var inputExtension = Path.GetExtension(sourcePath).TrimStart('.');
            var outputExtension = GetOutputExtension(format);
            var suffix = string.Equals(inputExtension, outputExtension, StringComparison.OrdinalIgnoreCase)
                ? "_Converted"
                : string.Empty;

            return Path.Combine(directory, name + suffix + "." + outputExtension);
        }

        private void DrawHeader()
        {
            VideoRackEditorUI.DrawHeader("Audio Converter", "FFmpeg audio conversion", isConverting ? "Converting" : "Ready");
        }

        private void DrawSectionTitle(string title)
        {
            VideoRackEditorUI.DrawSectionTitle(title);
        }

        private void DrawHint(string text)
        {
            VideoRackEditorUI.DrawHint(text);
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

        private void DrawConversionProgress()
        {
            var rect = GUILayoutUtility.GetRect(18f, 18f, GUILayout.ExpandWidth(true));
            EditorGUI.ProgressBar(rect, conversionProgress, progressLabel);
            GUILayout.Space(3);
        }

        private static string[] GetFormatNames()
        {
            return new[]
            {
                "MP3",
                "WAV",
                "OGG"
            };
        }

        private static string GetFormatDisplayName(OutputFormat format)
        {
            return GetFormatNames()[(int)format];
        }

        private static string GetFormatHelp(OutputFormat format)
        {
            switch (format)
            {
                case OutputFormat.Mp3:
                    return "Creates an MP3 file with libmp3lame. Useful for compact previews and general playback.";
                case OutputFormat.Wav:
                    return "Creates an uncompressed 16-bit PCM WAV file. Useful before editing, sync checks, or analysis.";
                case OutputFormat.Ogg:
                    return "Creates an OGG Vorbis file. Useful when a lightweight open audio container is preferred.";
                default:
                    return string.Empty;
            }
        }

        private static string GetOutputExtension(OutputFormat format)
        {
            switch (format)
            {
                case OutputFormat.Mp3:
                    return "mp3";
                case OutputFormat.Ogg:
                    return "ogg";
                default:
                    return "wav";
            }
        }

        private static string Quote(string value)
        {
            return "\"" + value.Replace("\"", "\\\"") + "\"";
        }

        private enum OutputFormat
        {
            Mp3,
            Wav,
            Ogg
        }

        private readonly struct AudioConvertOptions
        {
            public readonly int Mp3BitrateKbps;
            public readonly int OggQuality;
            public readonly int SampleRate;
            public readonly int Channels;
            public readonly bool TrimEnabled;
            public readonly string TrimStart;
            public readonly string TrimDuration;
            public readonly string ExtraInputArguments;
            public readonly string ExtraOutputArguments;

            public AudioConvertOptions(
                int mp3BitrateKbps,
                int oggQuality,
                int sampleRate,
                int channels,
                bool trimEnabled,
                string trimStart,
                string trimDuration,
                string extraInputArguments,
                string extraOutputArguments)
            {
                Mp3BitrateKbps = mp3BitrateKbps;
                OggQuality = oggQuality;
                SampleRate = sampleRate;
                Channels = channels;
                TrimEnabled = trimEnabled;
                TrimStart = trimStart;
                TrimDuration = trimDuration;
                ExtraInputArguments = extraInputArguments;
                ExtraOutputArguments = extraOutputArguments;
            }
        }
    }
}
#endif
