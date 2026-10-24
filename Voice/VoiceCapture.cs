
using System;
using System.Runtime.InteropServices;
using FMOD;
using FMODUnity;
using MelonLoader;

namespace StellarVoiceMod.Voice
{
    public sealed class VoiceCapture : IDisposable
    {
        private const int OutputSampleRate = 24000;
        private const int PacketSamples = 480;
        private const int BytesPerSample = 2;

        private FMOD.System _system;
        private Sound _recordSound;

        private int _deviceIndex;
        private int _sampleRate;
        private int _channels;

        private uint _bufferFrames;
        private uint _streamPosition;
        private bool _recording;

        public bool IsRecording => _recording;

        public bool StartRecording()
        {
            if (_recording)
                return true;

            _system = RuntimeManager.CoreSystem;

            RESULT result = _system.getRecordNumDrivers(
                out int driverCount,
                out int connectedCount
            );

            if (result != RESULT.OK || connectedCount <= 0)
            {
                MelonLogger.Error(
                    $"[StellarVoice] No recording devices: {result}"
                );
                return false;
            }

            bool found = false;

            for (int i = 0; i < driverCount; i++)
            {
                result = _system.getRecordDriverInfo(
                    i,
                    out string name,
                    256,
                    out Guid guid,
                    out int rate,
                    out SPEAKERMODE speakerMode,
                    out int channelCount,
                    out DRIVER_STATE state
                );

                if (result != RESULT.OK)
                    continue;

                if ((state & DRIVER_STATE.CONNECTED) == 0)
                    continue;

                if (name.IndexOf(
                    "[loopback]",
                    StringComparison.OrdinalIgnoreCase
                ) >= 0)
                    continue;

                if (rate <= 0 || channelCount <= 0)
                    continue;

                _deviceIndex = i;
                _sampleRate = rate;
                _channels = channelCount;
                found = true;

                MelonLogger.Msg(
                    $"[StellarVoice] Mic: {name}, " +
                    $"{rate} Hz, {channelCount} channels"
                );
                break;
            }

            if (!found)
            {
                MelonLogger.Error(
                    "[StellarVoice] No usable microphone found."
                );
                return false;
            }

            _bufferFrames = (uint)(_sampleRate * 10);

            CREATESOUNDEXINFO info = new CREATESOUNDEXINFO
            {
                cbsize = Marshal.SizeOf(typeof(CREATESOUNDEXINFO)),
                numchannels = _channels,
                defaultfrequency = _sampleRate,
                format = SOUND_FORMAT.PCM16,
                length = _bufferFrames * (uint)(_channels * 2)
            };

            result = _system.createSound(
                "",
                MODE.OPENUSER | MODE.LOOP_NORMAL,
                ref info,
                out _recordSound
            );

            if (result != RESULT.OK)
            {
                MelonLogger.Error(
                    $"[StellarVoice] Buffer creation failed: {result}"
                );
                _bufferFrames = 0;
                return false;
            }

            result = _system.recordStart(
                _deviceIndex,
                _recordSound,
                true
            );

            if (result != RESULT.OK)
            {
                MelonLogger.Error(
                    $"[StellarVoice] recordStart failed: {result}"
                );
                ReleaseRecordingSound();
                return false;
            }

            result = _system.getRecordPosition(
                _deviceIndex,
                out uint position
            );

            if (result != RESULT.OK)
            {
                MelonLogger.Error(
                    $"[StellarVoice] Initial position failed: {result}"
                );

                _system.recordStop(_deviceIndex);
                ReleaseRecordingSound();
                return false;
            }

            _streamPosition = position % _bufferFrames;
            _recording = true;

            MelonLogger.Msg("[StellarVoice] Recording started.");
            return true;
        }

        // Returns one 20 ms packet: 480 mono PCM16 samples at 24 kHz.
        // Returns an empty array until a complete packet is available.
        public byte[] ReadAudioChunk()
        {
            if (!_recording || !_recordSound.hasHandle())
                return Array.Empty<byte>();

            if (_bufferFrames == 0 || _sampleRate <= 0 || _channels <= 0)
                return Array.Empty<byte>();

            RESULT result = _system.getRecordPosition(
                _deviceIndex,
                out uint currentPosition
            );

            if (result != RESULT.OK)
                return Array.Empty<byte>();

            currentPosition %= _bufferFrames;

            uint availableFrames = currentPosition >= _streamPosition
                ? currentPosition - _streamPosition
                : _bufferFrames - _streamPosition + currentPosition;

            // Read exactly 20 ms from the input device.
            int inputFrames = Math.Max(1, _sampleRate / 50);

            if (availableFrames < inputFrames)
                return Array.Empty<byte>();

            byte[] input = new byte[
                inputFrames * _channels * BytesPerSample
            ];

            if (!CopyPcmFrames(
                _streamPosition,
                (uint)inputFrames,
                input))
            {
                return Array.Empty<byte>();
            }

            _streamPosition =
                (_streamPosition + (uint)inputFrames) % _bufferFrames;

            // Downmix all input channels to mono.
            float[] monoInput = new float[inputFrames];

            for (int frame = 0; frame < inputFrames; frame++)
            {
                float sum = 0f;

                for (int channel = 0; channel < _channels; channel++)
                {
                    int offset =
                        (frame * _channels + channel) * BytesPerSample;

                    short sample = (short)(
                        input[offset] | (input[offset + 1] << 8)
                    );

                    sum += sample / 32768f;
                }

                monoInput[frame] = sum / _channels;
            }

            // Resample to exactly 480 samples at 24 kHz.
            byte[] output = new byte[
                PacketSamples * BytesPerSample
            ];

            for (int i = 0; i < PacketSamples; i++)
            {
                float sourcePosition =
                    i * (float)_sampleRate / OutputSampleRate;

                int index = (int)sourcePosition;
                float fraction = sourcePosition - index;

                if (index >= inputFrames - 1)
                    index = inputFrames - 1;

                float a = monoInput[index];
                float b = monoInput[Math.Min(index + 1, inputFrames - 1)];

                float sample = a + (b - a) * fraction;
                sample = Math.Max(-1f, Math.Min(1f, sample));

                short pcm = (short)Math.Round(sample * 32767f);

                output[i * 2] = (byte)(pcm & 0xFF);
                output[i * 2 + 1] = (byte)((pcm >> 8) & 0xFF);
            }

            return output;
        }

        private bool CopyPcmFrames(
            uint startFrame,
            uint frameCount,
            byte[] destination)
        {
            uint bytesPerFrame = (uint)(_channels * 2);
            uint remainingFrames = frameCount;
            int destinationOffset = 0;

            while (remainingFrames > 0)
            {
                uint contiguousFrames = Math.Min(
                    remainingFrames,
                    _bufferFrames - startFrame
                );

                uint bytesToCopy = contiguousFrames * bytesPerFrame;
                uint offsetBytes = startFrame * bytesPerFrame;

                RESULT result = _recordSound.@lock(
                    offsetBytes,
                    bytesToCopy,
                    out IntPtr source1,
                    out IntPtr source2,
                    out uint length1,
                    out uint length2
                );

                if (result != RESULT.OK)
                {
                    MelonLogger.Warning(
                        $"[StellarVoice] Buffer lock failed: {result}"
                    );
                    return false;
                }

                bool success = false;

                try
                {
                    if (length1 + length2 != bytesToCopy)
                        return false;

                    if (length1 > 0)
                    {
                        Marshal.Copy(
                            source1,
                            destination,
                            destinationOffset,
                            (int)length1
                        );
                        destinationOffset += (int)length1;
                    }

                    if (length2 > 0)
                    {
                        Marshal.Copy(
                            source2,
                            destination,
                            destinationOffset,
                            (int)length2
                        );
                        destinationOffset += (int)length2;
                    }

                    success = true;
                }
                finally
                {
                    _recordSound.unlock(
                        source1,
                        source2,
                        length1,
                        length2
                    );
                }

                if (!success)
                    return false;

                remainingFrames -= contiguousFrames;
                startFrame = 0;
            }

            return true;
        }

        public void StopRecording()
        {
            if (!_recording)
                return;

            RESULT result = _system.recordStop(_deviceIndex);
            _recording = false;

            if (result != RESULT.OK)
            {
                MelonLogger.Warning(
                    $"[StellarVoice] recordStop returned: {result}"
                );
            }

            ReleaseRecordingSound();
            _bufferFrames = 0;
            _streamPosition = 0;

            MelonLogger.Msg("[StellarVoice] Recording stopped.");
        }

        private void ReleaseRecordingSound()
        {
            if (_recordSound.hasHandle())
            {
                _recordSound.release();
                _recordSound.clearHandle();
            }
        }

        public void Dispose()
        {
            StopRecording();
            ReleaseRecordingSound();
        }
    }
}
