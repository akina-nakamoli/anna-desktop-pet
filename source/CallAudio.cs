using System;
using System.Collections.Generic;
using System.IO;

namespace AnnaDesktopPet
{
    // 16 kHz, mono PCM16. All durations derive from sample counts, not UI ticks.
    internal sealed class CallAudio
    {
        private readonly MemoryStream utterance = new MemoryStream();
        private readonly Queue<byte> lead = new Queue<byte>();
        private int voiced, silent;
        internal bool HasSpeech { get { return voiced >= 6400; } }
        internal double Threshold = .015;

        internal byte[] Feed(byte[] pcm)
        {
            if (pcm == null || pcm.Length < 2) return null;
            double sum = 0;
            for (int i = 0; i + 1 < pcm.Length; i += 2)
            {
                double sample = (short)(pcm[i] | pcm[i + 1] << 8) / 32768.0;
                sum += sample * sample;
            }
            bool sound = Math.Sqrt(sum / (pcm.Length / 2)) >= Threshold;
            if (utterance.Length == 0)
            {
                if (!sound)
                {
                    foreach (byte value in pcm) { lead.Enqueue(value); if (lead.Count > 9600) lead.Dequeue(); }
                    return null;
                }
                byte[] prefix = lead.ToArray(); lead.Clear();
                utterance.Write(prefix, 0, prefix.Length);
            }
            utterance.Write(pcm, 0, pcm.Length);
            if (sound) { voiced += pcm.Length; silent = 0; }
            else silent += pcm.Length;
            if (silent < 25600 && utterance.Length < 640000) return null;
            byte[] result = HasSpeech ? utterance.ToArray() : null;
            Reset();
            return result;
        }

        internal void Reset() { utterance.SetLength(0); lead.Clear(); voiced = silent = 0; }

        internal static void SaveWave(string path, byte[] pcm)
        {
            using (var writer = new BinaryWriter(File.Create(path)))
            {
                writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); writer.Write(36 + pcm.Length);
                writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt ")); writer.Write(16);
                writer.Write((short)1); writer.Write((short)1); writer.Write(16000); writer.Write(32000);
                writer.Write((short)2); writer.Write((short)16);
                writer.Write(System.Text.Encoding.ASCII.GetBytes("data")); writer.Write(pcm.Length); writer.Write(pcm);
            }
        }
    }
}
