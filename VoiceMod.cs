using MelonLoader;

[assembly: MelonInfo(typeof(StellarVoiceMod.VoiceMod), "StellarVoiceMod", "0.1.0", "Voxar")]
[assembly: MelonGame(null, null)]

namespace StellarVoiceMod
{
    public class VoiceMod : MelonMod
    {
        public override void OnInitializeMelon()
        {
            LoggerInstance.Msg("StellarVoiceMod loaded");
        }
    }
}