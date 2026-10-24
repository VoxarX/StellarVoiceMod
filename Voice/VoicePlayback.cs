
using System;
using System.Collections.Concurrent;
using System.Threading;
using UnityEngine;
using MelonLoader;

namespace StellarVoiceMod.Voice
{
    public sealed class VoicePlayback : IDisposable
    {
        private const int SampleRate = 48000;
        private const int Channels = 2;

        private const int PacketBytes = 960;
        private const int PacketSamples = 480;
        private const int MaxQueuedSamples = 24000;

        private readonly ConcurrentQueue<float> _samples =
            new ConcurrentQueue<float>();

        private int _queuedSamples;
        private GameObject _gameObject;
        private AudioSource _source;
        private AudioClip _clip;
        private volatile bool _disposed;

        public bool Initialize()
        {
            if (_disposed)
                return false;

            try
            {
                _gameObject = new GameObject("StellarVoicePlayback");
                UnityEngine.Object.DontDestroyOnLoad(_gameObject);

                _source = _gameObject.AddComponent<AudioSource>();
                _source.playOnAwake = false;
                _source.loop = true;
                _source.spatialBlend = 0f;
                _source.volume = 1f;

                _clip = AudioClip.Create(
                    "StellarVoiceStream",
                    SampleRate,
                    Channels,
                    SampleRate,
                    true,
                    ReadAudio
                );

                if (_clip == null)
                {
                    MelonLogger.Error(
                        "[StellarVoice] Could not create audio stream."
                    );
                    Dispose();
                    return false;
                }

                _source.clip = _clip;
                _source.Play();

                MelonLogger.Msg(
                    "[StellarVoice] Playback initialized."
                );
                return true;
            }
            catch (Exception ex)
            {
                MelonLogger.Error(
                    $"[StellarVoice] Playback initialization failed: {ex}"
                );
                Dispose();
                return false;
            }
        }

        public void Enqueue(byte[] audio)
        {
            if (_disposed || audio == null ||
                audio.Length != PacketBytes)
                return;

            // Drop incoming packets if the queue is already too far behind.
            if (Volatile.Read(ref _queuedSamples) + PacketSamples
                > MaxQueuedSamples)
            {
                return;
            }

            for (int i = 0; i < audio.Length; i += 2)
            {
                short pcm = (short)(
                    audio[i] | (audio[i + 1] << 8)
                );

                _samples.Enqueue(pcm / 32768f);
            }


            Interlocked.Add(ref _queuedSamples, PacketSamples);

            for (int i = 0; i < audio.Length; i += 2)
            {
                short pcm = (short)(
                    audio[i] | (audio[i + 1] << 8)
                );

                _samples.Enqueue(pcm / 32768f);
            }

        }

        // Unity requests 48 kHz stereo. Input is 24 kHz mono,
        // so each mono sample is duplicated into two stereo frames.
        private void ReadAudio(float[] output)
        {
            Array.Clear(output, 0, output.Length);

            if (_disposed)
                return;

            int outputFrames = output.Length / Channels;

            for (int frame = 0; frame < outputFrames; frame += 2)
            {
                if (!_samples.TryDequeue(out float sample))
                    break;

                Interlocked.Decrement(ref _queuedSamples);

                int first = frame * Channels;
                output[first] = sample;
                output[first + 1] = sample;

                if (frame + 1 < outputFrames)
                {
                    int second = (frame + 1) * Channels;
                    output[second] = sample;
                    output[second + 1] = sample;
                }
            }
        }

        public void Tick()
        {
            // Unity consumes the stream on its audio thread.
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;

            while (_samples.TryDequeue(out _))
            {
            }

            Interlocked.Exchange(ref _queuedSamples, 0);

            if (_source != null)
                _source.Stop();

            if (_clip != null)
            {
                UnityEngine.Object.Destroy(_clip);
                _clip = null;
            }

            if (_gameObject != null)
            {
                UnityEngine.Object.Destroy(_gameObject);
                _gameObject = null;
            }

            _source = null;
        }
    }
}
