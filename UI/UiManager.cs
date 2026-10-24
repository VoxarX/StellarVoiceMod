using System.IO;
using System.Reflection;
using MelonLoader;
using UnityEngine;

namespace StellarVoiceMod.UI
{
    public class UiManager
    {
        private Texture2D _mutedIcon;
        private Texture2D _unmutedIcon;

        public bool IsMuted { get; private set; } = true;

        private const float IconSize = 48f;
        private const float Padding = 20f;

        public void Initialize()
        {
            _mutedIcon = LoadIcon("mic_muted.png");
            _unmutedIcon = LoadIcon("mic_unmuted.png");
        }

        private Texture2D LoadIcon(string filename)
        {
            string resourceName = "StellarVoiceMod.Assets." + filename;

            using (Stream stream = Assembly.GetExecutingAssembly()
                .GetManifestResourceStream(resourceName))
            {
                if (stream == null)
                {
                    MelonLogger.Error(
                        $"[StellarVoice] Missing embedded resource: {resourceName}"
                    );
                    return null;
                }

                using (MemoryStream memory = new MemoryStream())
                {
                    stream.CopyTo(memory);

                    Texture2D texture = new Texture2D(2, 2);

                    if (!texture.LoadImage(memory.ToArray()))
                    {
                        UnityEngine.Object.Destroy(texture);
                        MelonLogger.Error(
                            $"[StellarVoice] Failed to load UI image: {resourceName}"
                        );
                        return null;
                    }

                    return texture;
                }
            }
        }

        public void SetMuted(bool muted)
        {
            IsMuted = muted;
        }

        public void OnGUI()
        {
            Texture2D icon = IsMuted ? _mutedIcon : _unmutedIcon;

            if (icon == null)
                return;

            GUI.DrawTexture(
                new Rect(
                    Screen.width - IconSize - Padding,
                    Screen.height - IconSize - Padding,
                    IconSize,
                    IconSize
                ),
                icon,
                ScaleMode.ScaleToFit,
                true
            );
        }

        public void Dispose()
        {
            if (_mutedIcon != null)
            {
                UnityEngine.Object.Destroy(_mutedIcon);
                _mutedIcon = null;
            }

            if (_unmutedIcon != null)
            {
                UnityEngine.Object.Destroy(_unmutedIcon);
                _unmutedIcon = null;
            }
        }
    }
}