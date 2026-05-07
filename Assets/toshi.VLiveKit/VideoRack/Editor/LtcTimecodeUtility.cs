#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace VLiveKit.VideoRack.Editor
{
    internal struct LtcTimecode
    {
        public readonly int Hours;
        public readonly int Minutes;
        public readonly int Seconds;
        public readonly int Frames;
        public readonly bool DropFrame;

        public LtcTimecode(int hours, int minutes, int seconds, int frames, bool dropFrame)
        {
            Hours = hours;
            Minutes = minutes;
            Seconds = seconds;
            Frames = frames;
            DropFrame = dropFrame;
        }

        public int ToFrameNumber(float frameRate)
        {
            var nominalRate = Mathf.Max(1, Mathf.RoundToInt(frameRate));
            return (((Hours * 60 + Minutes) * 60 + Seconds) * nominalRate) + Frames;
        }

        public static LtcTimecode FromFrameNumber(int frameNumber, float frameRate, bool dropFrame)
        {
            var nominalRate = Mathf.Max(1, Mathf.RoundToInt(frameRate));
            frameNumber = Mathf.Max(0, frameNumber);

            var totalSeconds = frameNumber / nominalRate;
            var frames = frameNumber % nominalRate;
            var seconds = totalSeconds % 60;
            var totalMinutes = totalSeconds / 60;
            var minutes = totalMinutes % 60;
            var hours = (totalMinutes / 60) % 24;
            return new LtcTimecode(hours, minutes, seconds, frames, dropFrame);
        }

        public override string ToString()
        {
            var separator = DropFrame ? ";" : ":";
            return string.Format(CultureInfo.InvariantCulture, "{0:D2}:{1:D2}:{2:D2}{3}{4:D2}", Hours, Minutes, Seconds, separator, Frames);
        }
    }

    internal struct LtcDecodedFrame
    {
        public readonly LtcTimecode Timecode;
        public readonly long SampleIndex;

        public LtcDecodedFrame(LtcTimecode timecode, long sampleIndex)
        {
            Timecode = timecode;
            SampleIndex = sampleIndex;
        }
    }

    internal sealed class LtcDecoder
    {
        private const string SyncWord = "0011111111111101";

        private readonly StringBuilder bitPattern = new StringBuilder(192);
        private int sameAudioLevelCount;
        private int lastAudioLevel;
        private int lastBitCount;
        private long processedSamples;

        public List<LtcDecodedFrame> Decode(float[] samples, int sampleRate, int channels, float expectedFrameRate, float signalThreshold, int maxFrames)
        {
            var frames = new List<LtcDecodedFrame>();
            if (samples == null || samples.Length == 0 || sampleRate <= 0 || channels <= 0)
                return frames;

            var averageGain = 0f;
            for (var i = 0; i < samples.Length; i += channels)
                averageGain += Mathf.Abs(samples[i]);

            var monoSampleCount = Mathf.Max(1, samples.Length / channels);
            averageGain /= monoSampleCount;
            if (averageGain < signalThreshold)
            {
                processedSamples += monoSampleCount;
                return frames;
            }

            var bitThreshold = Mathf.Max(2, Mathf.RoundToInt(sampleRate / (Mathf.Max(1f, expectedFrameRate) * 103.333f)));
            var pos = 0;
            while (pos < samples.Length)
            {
                var count = CountUntilLevelChange(samples, ref pos, channels);
                if (count <= 0)
                    continue;

                if (count < bitThreshold)
                {
                    if (lastBitCount < bitThreshold)
                    {
                        bitPattern.Append('1');
                        lastBitCount = bitThreshold;
                    }
                    else
                    {
                        lastBitCount = count;
                    }
                }
                else
                {
                    bitPattern.Append('0');
                    lastBitCount = count;
                }

                ExtractFrames(frames, maxFrames);
                if (frames.Count >= maxFrames)
                    break;
            }

            if (bitPattern.Length > 320)
                bitPattern.Remove(0, bitPattern.Length - 160);

            processedSamples += monoSampleCount;
            return frames;
        }

        private int CountUntilLevelChange(float[] data, ref int pos, int channels)
        {
            while (pos < data.Length)
            {
                var sample = data[pos];
                var nowLevel = sample >= 0f ? 1 : -1;
                if (lastAudioLevel != 0 && lastAudioLevel != nowLevel)
                {
                    var count = sameAudioLevelCount;
                    sameAudioLevelCount = 0;
                    lastAudioLevel = nowLevel;
                    return count;
                }

                lastAudioLevel = nowLevel;
                sameAudioLevelCount++;
                pos += channels;
            }

            return -1;
        }

        private void ExtractFrames(List<LtcDecodedFrame> frames, int maxFrames)
        {
            while (bitPattern.Length >= 80 && frames.Count < maxFrames)
            {
                var pattern = bitPattern.ToString();
                var syncPos = pattern.IndexOf(SyncWord, StringComparison.Ordinal);
                if (syncPos < 0)
                    return;

                var frameEnd = syncPos + SyncWord.Length;
                if (frameEnd < 80)
                {
                    bitPattern.Remove(0, frameEnd);
                    continue;
                }

                var frameBits = pattern.Substring(frameEnd - 80, 80);
                bitPattern.Remove(0, frameEnd);
                if (TryDecodeFrame(frameBits, out var timecode))
                    frames.Add(new LtcDecodedFrame(timecode, processedSamples));
            }
        }

        private static bool TryDecodeFrame(string bits, out LtcTimecode timecode)
        {
            var frames = DecodeBits(bits, 0, 4) + DecodeBits(bits, 8, 2) * 10;
            var seconds = DecodeBits(bits, 16, 4) + DecodeBits(bits, 24, 3) * 10;
            var minutes = DecodeBits(bits, 32, 4) + DecodeBits(bits, 40, 3) * 10;
            var hours = DecodeBits(bits, 48, 4) + DecodeBits(bits, 56, 2) * 10;
            var dropFrame = bits[10] == '1';

            var valid = frames < 60 && seconds < 60 && minutes < 60 && hours < 24;
            timecode = new LtcTimecode(hours, minutes, seconds, frames, dropFrame);
            return valid;
        }

        private static int DecodeBits(string bits, int start, int count)
        {
            var value = 0;
            for (var i = 0; i < count; i++)
            {
                if (bits[start + i] == '1')
                    value += 1 << i;
            }

            return value;
        }
    }
}
#endif
