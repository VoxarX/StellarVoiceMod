using System;
using System.Reflection;
using System.Linq;
using MelonLoader;
using StellarVoiceMod.Voice;
using UnityEngine.InputSystem;
using StellarVoiceMod.UI;

[assembly: MelonInfo(typeof(StellarVoiceMod.VoiceMod), "StellarVoiceMod", "0.1.0", "VoxarDev")]
[assembly: MelonGame(null, null)]


namespace StellarVoiceMod
{
    public class VoiceMod : MelonMod
    {
        private VoiceCapture _microphone;
        private VoiceNetwork _voiceNetwork;
        private UiManager _ui;

        private void InitializeFishNetSerializers()
        {
            Assembly fishNetAssembly = typeof(FishNet.Serializing.Writer).Assembly;

            foreach (Type type in fishNetAssembly.GetTypes())
            {
                if (type.Namespace != "FishNet.Serializing.Generated")
                    continue;

                MethodInfo method = type.GetMethod(
                    "InitializeOnce",
                    BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static
                );

                if (method == null)
                    continue;

                try
                {
                    method.Invoke(null, null);
                    LoggerInstance.Msg(
                        $"Initialized FishNet serializers: {type.FullName}"
                    );
                }
                catch (Exception ex)
                {
                    LoggerInstance.Error(
                        $"Serializer initialization failed for {type.FullName}: {ex}"
                    );
                }
            }
        }

        public override void OnGUI()
        {
            _ui?.OnGUI();
        }

        public override void OnInitializeMelon()
        {
            InitializeFishNetSerializers();

            VoiceBroadcastSerializer.Register();

            _microphone = new VoiceCapture();
            _voiceNetwork = new VoiceNetwork();
            _ui = new UiManager();
            _ui.Initialize();

            LoggerInstance.Msg("StellarVoiceMod loaded :3!!!");
        }


        public override void OnUpdate()
        {
            _voiceNetwork?.Tick();

            if (_microphone != null && _microphone.IsRecording)
            {
                // Limit work per frame to avoid sending large bursts.
                for (int i = 0; i < 4; i++)
                {
                    byte[] chunk = _microphone.ReadAudioChunk();

                    if (chunk.Length == 0)
                        break;

                    _voiceNetwork?.SendAudio(chunk);
                }
            }

            if (Keyboard.current == null || _microphone == null)
                return;

            if (Keyboard.current.vKey.wasPressedThisFrame)
            {
                if (_microphone.IsRecording)
                {
                    _microphone.StopRecording();
                }
                else
                {
                    _microphone.StartRecording();
                }

                _ui?.SetMuted(!_microphone.IsRecording);
            }
        }



        public override void OnDeinitializeMelon()
        {
            _voiceNetwork?.Dispose();
            _microphone?.Dispose();

            LoggerInstance.Msg("StellarVoice unloaded :(");
        }
    }
}