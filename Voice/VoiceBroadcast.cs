
using FishNet.Broadcast;
using FishNet.CodeGenerating;

namespace StellarVoiceMod.Voice
{
    [UseGlobalCustomSerializer]
    public struct VoiceBroadcast : IBroadcast
    {
        public int SenderClientId;
        public uint Sequence;
        public byte[] AudioData;
    }
}
