
using System.Collections.Generic;
using FishNet;
using FishNet.Connection;
using FishNet.Managing;
using FishNet.Transporting;
using MelonLoader;
using StellarVoiceMod.Voice;

namespace StellarVoiceMod
{
    public sealed class VoiceNetwork
    {
        private const int MaxPacketBytes = 1000;

        private NetworkManager _manager;
        private VoicePlayback _playback;
        private bool _clientRegistered;
        private bool _serverRegistered;
        private bool _disposed;

        private uint _sequence;
        private readonly Dictionary<int, uint> _lastSequence =
            new Dictionary<int, uint>();

        public void Tick()
        {
            if (_disposed)
                return;

            NetworkManager current = InstanceFinder.NetworkManager;

            if (current == null)
                return;

            if (_manager != current)
            {
                Unregister();
                _manager = current;
                _lastSequence.Clear();
                _sequence = 0;
            }

            if (_manager.ClientManager.Started && !_clientRegistered)
            {
                _manager.ClientManager
                    .RegisterBroadcast<VoiceBroadcast>(OnVoiceReceived);

                _clientRegistered = true;
            }

            if (_manager.ServerManager.Started && !_serverRegistered)
            {
                _manager.ServerManager
                    .RegisterBroadcast<VoiceBroadcast>(OnVoiceReceivedServer);

                _serverRegistered = true;
            }

            if (_manager.ClientManager.Started && _playback == null)
            {
                var playback = new VoicePlayback();

                if (playback.Initialize())
                    _playback = playback;
                else
                    playback.Dispose();
            }

            _playback?.Tick();
        }

        public void SendAudio(byte[] audio)
        {
            if (_disposed || audio == null ||
                audio.Length == 0 ||
                audio.Length > MaxPacketBytes ||
                audio.Length % 4 != 0)
                return;

            if (_manager == null ||
                !_manager.ClientManager.Started ||
                !_clientRegistered)
                return;

            var connection = _manager.ClientManager.Connection;

            if (connection == null)
                return;

            var message = new VoiceBroadcast
            {
                SenderClientId = connection.ClientId,
                Sequence = _sequence++,
                AudioData = audio
            };

            _manager.ClientManager.Broadcast(
                message,
                Channel.Unreliable
            );
        }

        private void OnVoiceReceivedServer(
            NetworkConnection sender,
            VoiceBroadcast message,
            Channel channel)
        {
            if (_disposed || sender == null ||
                message.AudioData == null ||
                message.AudioData.Length == 0 ||
                message.AudioData.Length > MaxPacketBytes ||
                message.AudioData.Length % 4 != 0)
                return;

            // The server is authoritative about the sender identity.
            message.SenderClientId = sender.ClientId;

            _manager.ServerManager.BroadcastExcept(
                sender,
                message,
                true,
                Channel.Unreliable
            );
        }

        private void OnVoiceReceived(
            VoiceBroadcast message,
            Channel channel)
        {
            if (_disposed || message.AudioData == null ||
                message.AudioData.Length == 0 ||
                message.AudioData.Length > MaxPacketBytes ||
                message.AudioData.Length % 4 != 0)
                return;

            // Ignore our own audio if it is ever received back.
            if (_manager.ClientManager.Connection != null &&
                message.SenderClientId ==
                _manager.ClientManager.Connection.ClientId)
                return;

            if (_lastSequence.TryGetValue(
                message.SenderClientId, out uint last))
            {
                // Sequence numbers wrap naturally at uint.MaxValue.
                if (unchecked((int)(message.Sequence - last)) <= 0)
                    return;
            }

            _lastSequence[message.SenderClientId] = message.Sequence;

            _playback?.Enqueue(message.AudioData);
        }

        private void Unregister()
        {
            if (_manager == null)
                return;

            if (_clientRegistered)
            {
                _manager.ClientManager
                    .UnregisterBroadcast<VoiceBroadcast>(OnVoiceReceived);

                _clientRegistered = false;
            }

            if (_serverRegistered)
            {
                _manager.ServerManager
                    .UnregisterBroadcast<VoiceBroadcast>(
                        OnVoiceReceivedServer
                    );

                _serverRegistered = false;
            }

            _playback?.Dispose();
            _playback = null;
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            Unregister();
            _manager = null;
            _lastSequence.Clear();
            _disposed = true;
        }
    }
}
