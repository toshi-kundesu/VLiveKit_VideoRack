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
        private string statusMessage = "Ready";
        private readonly StringBuilder log = new StringBuilder();
        private readonly List<LtcDecodedFrame> decodedFrames = new List<LtcDecodedFrame>();

        [MenuItem("toshi/VLiveKit/VideoRack/LTC Media Analyzer")]
        public static void Open()
        {
            GetWindow<LtcMediaAnalyzerWindow>(WindowTitle);
        }

        private void OnGUI()
        {
            scroll = EditorGUILayout.BeginScrollView(scroll);
            DrawHeader();

            using (new EditorGUILayout.VerticalScope(VideoRackEditorUI.Panel))
            {
                DrawSectionTitle("Source");
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

            using (new EditorGUILayout.VerticalScope(VideoRackEditorUI.Panel))
            {
                DrawSectionTitle("LTC");
                expectedLtcFps = EditorGUILayout.FloatField(new GUIContent("Expected FPS", "Used to classify LTC pulse widths. Try 24, 25, 29.97, or 30 if decoding is unstable."), expectedLtcFps);
                signalThreshold = EditorGUILayout.Slider(new GUIContent("Signal Threshold", "Average absolute sample level required before decoding."), signalThreshold, 0f, 0.25f);
                analyzeDurationSeconds = Mathf.Max(1f, EditorGUILayout.FloatField(new GUIContent("Analyze Seconds", "Only this much audio is extracted from the head of the movie."), analyzeDurationSeconds));
            }

            using (new EditorGUILayout.VerticalScope(VideoRackEditorUI.Panel))
            {
                DrawSectionTitle("Timeline Estimate");
                clipInSeconds = Mathf.Max(0f, EditorGUILayout.FloatField(new GUIContent("Clip In Seconds", "Timeline clip trim offset into the source movie."), clipInSeconds));
                timelineStartSeconds = Mathf.Max(0f, EditorGUILayout.FloatField(new GUIContent("Timeline Start Seconds", "Where the Timeline clip starts."), timelineStartSeconds));
                timelineFps = Mathf.Max(1f, EditorGUILayout.FloatField("Timeline FPS", timelineFps));

                DrawTimelineResult();
            }

            using (new EditorGUILayout.VerticalScope(VideoRackEditorUI.Panel))
            {
                DrawSectionTitle("Analyze");
                var ffmpegPath = VideoRackFfmpegLocator.GetExecutablePath();
                using (new EditorGUI.DisabledScope(true))
                    EditorGUILayout.TextField("ffmpeg", ffmpegPath);
                VideoRackEditorUI.DrawStatus(statusMessage);

                using (new EditorGUI.DisabledScope(isAnalyzing || string.IsNullOrEmpty(inputPath)))
                {
                    if (GUILayout.Button("Analyze LTC", VideoRackEditorUI.PrimaryButton, GUILayout.Height(30)))
                        Analyze();
                }
            }

            using (new EditorGUILayout.VerticalScope(VideoRackEditorUI.Panel))
            {
                DrawSectionTitle("Result");
                if (decodedFrames.Count > 0)
                {
                    var first = decodedFrames[0].Timecode;
                    EditorGUILayout.LabelField("First decoded LTC", first.ToString(), VideoRackEditorUI.Result);
                    EditorGUILayout.LabelField("Decoded frames", decodedFrames.Count.ToString(CultureInfo.InvariantCulture), VideoRackEditorUI.Result);
                }
                else
                {
                    DrawHint("No LTC frame decoded yet.");
                }

                EditorGUILayout.TextArea(log.ToString(), VideoRackEditorUI.Log, GUILayout.MinHeight(180));
            }

            EditorGUILayout.EndScrollView();
        }

        private void SelectInput()
        {
            var path = EditorUtility.OpenFilePanel("Select Movie With LTC", string.Empty, "mp4,mov,avi,mkv,webm,wav");
            if (!string.IsNullOrEmpty(path))
            {
                inputPath = path;
                statusMessage = "Input selected.";
            }
        }

        private void Analyze()
        {
            if (!File.Exists(inputPath))
            {
                statusMessage = "The selected media file could not be found.";
                VideoRackEditorUI.ShowNotification(this, "Input not found");
                return;
            }

            var ffmpegPath = VideoRackFfmpegLocator.GetExecutablePath();
            if (!File.Exists(ffmpegPath))
            {
                statusMessage = $"ffmpeg was not found. Place ffmpeg at: {ffmpegPath}";
                VideoRackEditorUI.ShowNotification(this, "ffmpeg not found");
                return;
            }

            isAnalyzing = true;
            statusMessage = "Analyzing LTC.";
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
                        statusMessage = $"Decoded {decodedFrames.Count} LTC frames.";
                        AppendLog($"First LTC: {decodedFrames[0].Timecode}\n");
                        AppendLog($"Timeline head frame: {Mathf.RoundToInt(timelineStartSeconds * timelineFps)}\n");
                        AppendLog($"Source head at clip in: {GetSourceHeadTimecode()}\n");
                    }
                    else
                    {
                        statusMessage = "No LTC frame decoded. Try another channel, threshold, or expected FPS.";
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
                statusMessage = "Analysis failed. See the log for details.";
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

            EditorGUILayout.LabelField("TL Head Frame", Mathf.RoundToInt(timelineStartSeconds * timelineFps).ToString(CultureInfo.InvariantCulture), VideoRackEditorUI.Result);
            EditorGUILayout.LabelField("Source Head LTC", GetSourceHeadTimecode(), VideoRackEditorUI.Result);
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
            process.StartInfo.RedirectStandardOutput = false;
            process.StartInfo.RedirectStandardError = true;

            process.Start();
            var stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();

            if (process.ExitCode != 0)
                throw new InvalidOperationException($"ffmpeg failed with exit code {process.ExitCode}\n{stderr}");
        }

        private void DrawHeader()
        {
            VideoRackEditorUI.DrawHeader("LTC Media Analyzer", "Media timecode analyzer", isAnalyzing ? "Analyzing" : "Ready");
        }

        private void DrawSectionTitle(string title)
        {
            VideoRackEditorUI.DrawSectionTitle(title);
        }

        private void DrawHint(string text)
        {
            VideoRackEditorUI.DrawHint(text);
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
