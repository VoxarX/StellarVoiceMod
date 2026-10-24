
using FishNet.Serializing;

namespace StellarVoiceMod.Voice
{
    public static class VoiceBroadcastSerializer
    {
        public static void Register()
        {
            GenericWriter<VoiceBroadcast>.SetWrite(WriteVoiceBroadcast);
            GenericReader<VoiceBroadcast>.SetRead(ReadVoiceBroadcast);
        }

        public static void WriteVoiceBroadcast(
            this Writer writer,
            VoiceBroadcast value)
        {
            writer.WriteInt32(value.SenderClientId);
            writer.WriteUInt32(value.Sequence);
            writer.WriteArray(value.AudioData);
        }

        public static VoiceBroadcast ReadVoiceBroadcast(
            this Reader reader)
        {
            int senderClientId = reader.ReadInt32();
            uint sequence = reader.ReadUInt32();

            byte[] audio = null;
            reader.ReadArray<byte>(ref audio);

            return new VoiceBroadcast
            {
                SenderClientId = senderClientId,
                Sequence = sequence,
                AudioData = audio
            };
        }
    }
}
