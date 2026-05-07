#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace VLiveKit.VideoRack.Editor
{
    public sealed class LtcMediaAnalyzerWindow : EditorWindow
    {
        private const string WindowTitle = "LTC Media Analyzer";

        private string inputPath = string.Empty;
        private int audioStreamIndex;
        private int channelIndex = -1;
        private float expectedLtcFps = 30f;
        private float signalThreshold = 0.02f;
        private float analyzeDurationSeconds = 15f;
        private float clipInSeconds;
        private float timelineStartSeconds;
        private float timelineFps = 30f;
        private bool isAnalyzing;
        private Vector2 scroll;
        private readonly StringBuilder log = new StringBuilder();
        private readonly List<LtcDecodedFrame> decodedFrames = new List<LtcDecodedFrame>();
        private GUIStyle headerStyle;
        private GUIStyle panelStyle;
        private GUIStyle sectionTitleStyle;
        private GUIStyle hintStyle;
        private GUIStyle resultStyle;

        [MenuItem("toshi/VLiveKit/VideoRack/LTC Media Analyzer")]
        public static void Open()
        {
            GetWindow<LtcMediaAnalyzerWindow>(WindowTitle);
        }

        private void OnGUI()
        {
            InitializeStyles();

            var windowRect = new Rect(0f, 0f, position.width, position.height);
            EditorGUI.DrawRect(windowRect, EditorGUIUtility.isProSkin ? new Color(0.115f, 0.115f, 0.115f) : new Color(0.90f, 0.91f, 0.92f));

            scroll = EditorGUILayout.BeginScrollView(scroll);
            DrawHeader();

            using (new EditorGUILayout.VerticalScope(panelStyle))
            {
                DrawSectionTitle("SOURCE");
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.TextField("Movie", inputPath);
                    if (GUILayout.Button("Select", GUILayout.Width(86)))
                        SelectInput();
                }

                audioStreamIndex = Mathf.Max(0, EditorGUILayout.IntField(new GUIContent("Audio Stream", "0 means the first audio stream in the movie."), audioStreamIndex));
                channelIndex = EditorGUILayout.IntField(new GUIContent("Channel", "-1 mixes the selected stream to mono. 0 reads left/first channel."), channelIndex);
                DrawHint("If LTC is recorded on one side of a stereo track, set Channel to 0 or 1. Use -1 when the file is already mono or you want ffmpeg to downmix.");
            }

            using (new EditorGUILayout.VerticalScope(panelStyle))
            {
                DrawSectionTitle("LTC");
                expectedLtcFps = EditorGUILayout.FloatField(new GUIContent("Expected FPS", "Used to classify LTC pulse widths. Try 24, 25, 29.97, or 30 if decoding is unstable."), expectedLtcFps);
                signalThreshold = EditorGUILayout.Slider(new GUIContent("Signal Threshold", "Average absolute sample level required before decoding."), signalThreshold, 0f, 0.25f);
                analyzeDurationSeconds = Mathf.Max(1f, EditorGUILayout.FloatField(new GUIContent("Analyze Seconds", "Only this much audio is extracted from the head of the movie."), analyzeDurationSeconds));
            }

            using (new EditorGUILayout.VerticalScope(panelStyle))
            {
                DrawSectionTitle("TIMELINE ESTIMATE");
                clipInSeconds = Mathf.Max(0f, EditorGUILayout.FloatField(new GUIContent("Clip In Seconds", "Timeline clip trim offset into the source movie."), clipInSeconds));
                timelineStartSeconds = Mathf.Max(0f, EditorGUILayout.FloatField(new GUIContent("Timeline Start Seconds", "Where the Timeline clip starts."), timelineStartSeconds));
                timelineFps = Mathf.Max(1f, EditorGUILayout.FloatField("Timeline FPS", timelineFps));

                DrawTimelineResult();
            }

            using (new EditorGUILayout.VerticalScope(panelStyle))
            {
                DrawSectionTitle("ANALYZE");
                var ffmpegPath = VideoRackFfmpegLocator.GetExecutablePath();
                using (new EditorGUI.DisabledScope(true))
                    EditorGUILayout.TextField("ffmpeg", ffmpegPath);

                using (new EditorGUI.DisabledScope(isAnalyzing || string.IsNullOrEmpty(inputPath)))
                {
                    if (GUILayout.Button("ANALYZE LTC", GUILayout.Height(34)))
                        Analyze();
                }
            }

            using (new EditorGUILayout.VerticalScope(panelStyle))
            {
                DrawSectionTitle("RESULT");
                if (decodedFrames.Count > 0)
                {
                    var first = decodedFrames[0].Timecode;
                    EditorGUILayout.LabelField("First decoded LTC", first.ToString(), resultStyle);
                    EditorGUILayout.LabelField("Decoded frames", decodedFrames.Count.ToString(CultureInfo.InvariantCulture), resultStyle);
                }
                else
                {
                    DrawHint("No LTC frame decoded yet.");
                }

                EditorGUILayout.TextArea(log.ToString(), GUILayout.MinHeight(180));
            }

            EditorGUILayout.EndScrollView();
        }

        private void SelectInput()
        {
            var path = EditorUtility.OpenFilePanel("Select Movie With LTC", string.Empty, "mp4,mov,avi,mkv,webm,wav");
            if (!string.IsNullOrEmpty(path))
                inputPath = path;
        }

        private void Analyze()
        {
            if (!File.Exists(inputPath))
            {
                EditorUtility.DisplayDialog("Input not found", "The selected media file could not be found.", "OK");
                return;
            }

            var ffmpegPath = VideoRackFfmpegLocator.GetExecutablePath();
            if (!File.Exists(ffmpegPath))
            {
                EditorUtility.DisplayDialog("ffmpeg not found", $"Place ffmpeg here before analyzing:\n\n{ffmpegPath}", "OK");
                return;
            }

            isAnalyzing = true;
            decodedFrames.Clear();
            log.Length = 0;
            Repaint();

            try
            {
                var wavPath = BuildTempWavPath();
                try
                {
                    ExtractWav(ffmpegPath, inputPath, wavPath, audioStreamIndex, channelIndex, analyzeDurationSeconds);
                    var audio = WavPcmReader.Read(wavPath);
                    AppendLog($"Extracted WAV: {audio.SampleRate} Hz, {audio.Channels} ch, {audio.Samples.Length} samples\n");

                    var decoder = new LtcDecoder();
                    var frames = decoder.Decode(audio.Samples, audio.SampleRate, audio.Channels, expectedLtcFps, signalThreshold, 240);
                    decodedFrames.AddRange(frames);
                    if (decodedFrames.Count > 0)
                    {
                        AppendLog($"First LTC: {decodedFrames[0].Timecode}\n");
                        AppendLog($"Timeline head frame: {Mathf.RoundToInt(timelineStartSeconds * timelineFps)}\n");
                        AppendLog($"Source head at clip in: {GetSourceHeadTimecode()}\n");
                    }
                    else
                    {
                        AppendLog("No LTC frame could be decoded. Try another channel, lower threshold, or another expected FPS.\n");
                    }
                }
                finally
                {
                    if (File.Exists(wavPath))
                        File.Delete(wavPath);
                }
            }
            catch (Exception e)
            {
                AppendLog(e + "\n");
                Debug.LogException(e);
            }
            finally
            {
                isAnalyzing = false;
                Repaint();
            }
        }

        private void DrawTimelineResult()
        {
            if (decodedFrames.Count == 0)
            {
                DrawHint("After analysis, this estimates the LTC visible at the Timeline clip head using Clip In.");
                return;
            }

            EditorGUILayout.LabelField("TL Head Frame", Mathf.RoundToInt(timelineStartSeconds * timelineFps).ToString(CultureInfo.InvariantCulture), resultStyle);
            EditorGUILayout.LabelField("Source Head LTC", GetSourceHeadTimecode(), resultStyle);
        }

        private string GetSourceHeadTimecode()
        {
            if (decodedFrames.Count == 0)
                return "n/a";

            var first = decodedFrames[0].Timecode;
            var frameOffset = Mathf.RoundToInt(clipInSeconds * expectedLtcFps);
            var sourceHeadFrame = first.ToFrameNumber(expectedLtcFps) + frameOffset;
            return LtcTimecode.FromFrameNumber(sourceHeadFrame, expectedLtcFps, first.DropFrame).ToString();
        }

        private static string BuildTempWavPath()
        {
            var directory = Path.Combine(Path.GetTempPath(), "VLiveKit", "VideoRack", "LTC");
            Directory.CreateDirectory(directory);
            return Path.Combine(directory, Guid.NewGuid().ToString("N") + ".wav");
        }

        private static void ExtractWav(string ffmpegPath, string input, string output, int streamIndex, int channel, float durationSeconds)
        {
            var arguments = new StringBuilder();
            arguments.Append("-y -i ").Append(Quote(input));
            arguments.Append(" -map 0:a:").Append(Mathf.Max(0, streamIndex));
            arguments.Append(" -t ").Append(Mathf.Max(1f, durationSeconds).ToString("0.###", CultureInfo.InvariantCulture));
            arguments.Append(" -vn -acodec pcm_s16le -ar 48000");
            if (channel >= 0)
                arguments.Append(" -af ").Append(Quote("pan=mono|c0=c" + channel));
            else
                arguments.Append(" -ac 1");

            arguments.Append(" ").Append(Quote(output));

            var process = new Process();
            process.StartInfo.FileName = ffmpegPath;
            process.StartInfo.Arguments = arguments.ToString();
            process.StartInfo.WorkingDirectory = Path.GetDirectoryName(ffmpegPath);
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.CreateNoWindow = true;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = true;

            process.Start();
            var stdout = process.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();

            if (process.ExitCode != 0)
                throw new InvalidOperationException($"ffmpeg failed with exit code {process.ExitCode}\n{stdout}\n{stderr}");
        }

        private void InitializeStyles()
        {
            if (headerStyle != null && panelStyle != null && sectionTitleStyle != null && hintStyle != null && resultStyle != null)
                return;

            headerStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 17,
                normal = { textColor = EditorGUIUtility.isProSkin ? new Color(0.90f, 0.91f, 0.92f) : new Color(0.12f, 0.13f, 0.14f) },
                margin = new RectOffset(0, 0, 3, 1)
            };

            panelStyle = new GUIStyle(EditorStyles.helpBox)
            {
                padding = new RectOffset(10, 10, 8, 9),
                margin = new RectOffset(8, 8, 5, 6)
            };

            sectionTitleStyle = new GUIStyle(EditorStyles.miniBoldLabel)
            {
                fontSize = 10,
                normal = { textColor = new Color(0.08f, 0.38f, 0.86f) }
            };

            hintStyle = new GUIStyle(EditorStyles.helpBox)
            {
                fontSize = 10,
                wordWrap = true
            };

            resultStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                normal = { textColor = EditorGUIUtility.isProSkin ? new Color(0.88f, 0.90f, 0.92f) : new Color(0.10f, 0.12f, 0.14f) }
            };
        }

        private void DrawHeader()
        {
            var rect = GUILayoutUtility.GetRect(0f, 44f, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(rect, EditorGUIUtility.isProSkin ? new Color(0.145f, 0.145f, 0.145f) : new Color(0.86f, 0.87f, 0.88f));
            EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 1f, rect.width, 1f), new Color(0.08f, 0.38f, 0.86f, 0.95f));
            GUI.Label(new Rect(rect.x + 12f, rect.y + 7f, rect.width - 24f, 22f), "VIDEO RACK / LTC", headerStyle);
            GUI.Label(new Rect(rect.x + 12f, rect.y + 26f, rect.width - 24f, 16f), "Media timecode analyzer", EditorStyles.miniLabel);
        }

        private void DrawSectionTitle(string title)
        {
            EditorGUILayout.LabelField(title, sectionTitleStyle);
        }

        private void DrawHint(string text)
        {
            EditorGUILayout.LabelField(text, hintStyle);
        }

        private void AppendLog(string text)
        {
            log.Append(text);
        }

        private static string Quote(string value)
        {
            return "\"" + value.Replace("\"", "\\\"") + "\"";
        }
    }

    internal struct WavPcmData
    {
        public readonly float[] Samples;
        public readonly int SampleRate;
        public readonly int Channels;

        public WavPcmData(float[] samples, int sampleRate, int channels)
        {
            Samples = samples;
            SampleRate = sampleRate;
            Channels = channels;
        }
    }

    internal static class WavPcmReader
    {
        public static WavPcmData Read(string path)
        {
            using (var stream = File.OpenRead(path))
            using (var reader = new BinaryReader(stream))
            {
                if (ReadFourCc(reader) != "RIFF")
                    throw new InvalidDataException("WAV is missing RIFF header.");

                reader.ReadInt32();
                if (ReadFourCc(reader) != "WAVE")
                    throw new InvalidDataException("WAV is missing WAVE header.");

                ushort audioFormat = 0;
                ushort channels = 0;
                var sampleRate = 0;
                ushort bitsPerSample = 0;
                byte[] data = null;

                while (reader.BaseStream.Position + 8 <= reader.BaseStream.Length)
                {
                    var chunkId = ReadFourCc(reader);
                    var chunkSize = reader.ReadInt32();
                    var chunkStart = reader.BaseStream.Position;

                    if (chunkId == "fmt ")
                    {
                        audioFormat = reader.ReadUInt16();
                        channels = reader.ReadUInt16();
                        sampleRate = reader.ReadInt32();
                        reader.ReadInt32();
                        reader.ReadUInt16();
                        bitsPerSample = reader.ReadUInt16();
                    }
                    else if (chunkId == "data")
                    {
                        data = reader.ReadBytes(chunkSize);
                    }

                    reader.BaseStream.Position = chunkStart + chunkSize + (chunkSize % 2);
                }

                if (data == null || channels == 0 || sampleRate == 0)
                    throw new InvalidDataException("WAV is missing audio data.");

                if (audioFormat != 1 || bitsPerSample != 16)
                    throw new InvalidDataException("Only 16-bit PCM WAV is supported.");

                var sampleCount = data.Length / 2;
                var samples = new float[sampleCount];
                for (var i = 0; i < sampleCount; i++)
                {
                    var value = BitConverter.ToInt16(data, i * 2);
                    samples[i] = value / 32768f;
                }

                return new WavPcmData(samples, sampleRate, channels);
            }
        }

        private static string ReadFourCc(BinaryReader reader)
        {
            return Encoding.ASCII.GetString(reader.ReadBytes(4));
        }
    }

    internal static class VideoRackFfmpegLocator
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
            var packageInfo = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(LtcMediaAnalyzerWindow).Assembly);
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
            return Path.Combine(Application.dataPath, "toshi.VLiveKit", "VideoRack", "Tools", "FFmpeg");
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
            return Path.Combine(projectRoot, "Library", "VLiveKit", "VideoRack", "FFmpeg", platformDirectory, executableName);
        }
    }
}
#endif
