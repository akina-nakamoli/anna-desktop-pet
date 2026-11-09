using System;
using System.Globalization;
using System.IO;

namespace AnnaDesktopPet
{
    internal sealed class SpeechSettings
    {
        public string Engine = "gpt-sovits";
        public string Endpoint = "http://127.0.0.1:9880";
        public string ReferenceAudio = DefaultReference();
        public string ReferenceText = DefaultReference().Length > 0 ? "思いは届かなくても、せめて好きな人を盛大に送り出したい。" : "";
        public string VoiceId = "";
        public string VoiceLanguage = "ja";
        public int Rate = 0;
        public int Volume = 75;
        public bool AutoReadReplies = false;
        public bool ReadLocal = false;
        public bool MotionsEnabled = true;
        public bool AutonomousEnabled = true;
        private static string FileName { get { return Path.Combine(Program.DataDir, PortableJapaneseVoice.IsBundled ? "media-settings-portable.ini" : "media-settings.ini"); } }
        private static string DefaultReference()
        {
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "reference", "anna.wav");
            return File.Exists(path) ? path : "";
        }
        public static SpeechSettings Load()
        {
            SpeechSettings s = new SpeechSettings();
            try
            {
                if (!File.Exists(FileName)) return s;
                foreach (string line in File.ReadAllLines(FileName))
                {
                    int split = line.IndexOf('='); if (split < 0) continue;
                    string key = line.Substring(0, split), value = line.Substring(split + 1); int n; bool flag;
                    if (key == "engine") s.Engine = value == "windows" || value == "portable" ? value : "gpt-sovits";
                    if (key == "endpoint") s.Endpoint = value;
                    if (key == "reference_audio") s.ReferenceAudio = value == "@bundled-reference" ? DefaultReference() : value;
                    if (key == "reference_text") s.ReferenceText = value;
                    if (key == "voice") s.VoiceId = value;
                    if (key == "voice_language") s.VoiceLanguage = "ja";
                    if (key == "rate" && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out n)) s.Rate = Math.Max(-5, Math.Min(5, n));
                    if (key == "volume" && int.TryParse(value, out n)) s.Volume = Math.Max(0, Math.Min(100, n));
                    if (key == "read_gpt" && bool.TryParse(value, out flag)) s.AutoReadReplies = flag;
                    if (key == "read_local" && bool.TryParse(value, out flag)) s.ReadLocal = flag;
                    if (key == "motions" && bool.TryParse(value, out flag)) s.MotionsEnabled = flag;
                    if (key == "autonomous" && bool.TryParse(value, out flag)) s.AutonomousEnabled = flag;
                }
            }
            catch { }
            return s;
        }
        public void Save()
        {
            if (Program.Smoke) return;
            Directory.CreateDirectory(Program.DataDir);
            string bundled = DefaultReference();
            string audio = bundled.Length > 0 && String.Equals(bundled, ReferenceAudio, StringComparison.OrdinalIgnoreCase)
                ? "@bundled-reference" : Clean(ReferenceAudio);
            File.WriteAllLines(FileName, new string[] { "engine=" + Clean(Engine), "endpoint=" + Clean(Endpoint), "reference_audio=" + audio, "reference_text=" + Clean(ReferenceText), "voice=" + Clean(VoiceId), "voice_language=ja", "rate=" + Rate.ToString(CultureInfo.InvariantCulture), "volume=" + Volume.ToString(CultureInfo.InvariantCulture), "read_gpt=" + AutoReadReplies, "read_local=" + ReadLocal, "motions=" + MotionsEnabled, "autonomous=" + AutonomousEnabled });
        }
        private static string Clean(string text) { return (text ?? "").Replace("\r", "").Replace("\n", ""); }
    }
}
