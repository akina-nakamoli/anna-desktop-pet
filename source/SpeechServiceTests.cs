using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows.Threading;

namespace AnnaDesktopPet
{
    internal static class SpeechServiceTests
    {
        private static int count;
        private const string Greeting = "こんにちは、アンナだよ。今日も一緒に頑張ろうね。お仕事が終わったら、おいしいものを食べに行こう！";

        [STAThread]
        public static int Main(string[] args)
        {
            string folder = args.Length > 0 ? Path.GetFullPath(args[0]) : AppDomain.CurrentDomain.BaseDirectory;
            Directory.CreateDirectory(folder);
            string report = Path.Combine(folder, "speech-test-report.txt");
            StringBuilder notes = new StringBuilder();
            try
            {
                bool shortened;
                string text = SpeechService.PrepareText("## こんにちはアンナ\n**今日も頑張ろう**。[資料](https://example.test/private) 🍞\n~~~cs\nsecret-code();\n~~~", out shortened);
                Assert(text.Contains("こんにちはアンナ") && text.Contains("今日も頑張ろう"), "Japanese kana and kanji preserved");
                Assert(text.Contains("資料") && !text.Contains("https") && !text.Contains("secret-code"), "URLs and fenced code omitted");
                Assert(!text.Contains("**") && !text.Contains("#") && !text.Contains("🍞") && !shortened, "Markdown and emoji cleaned");
                Assert(SpeechService.PrepareText(new string('杏', 1600), out shortened).Length == 1500 && shortened, "Speech length capped and truncation reported");
                Assert(SpeechService.PrepareText(null, out shortened) == "" && !shortened, "Empty speech handled");

                bool japaneseAvailable;
                string defaultId = "";
                string wave = Path.Combine(folder, "杏菜日语试听.wav");
                using (SpeechService service = new SpeechService(Dispatcher.CurrentDispatcher, wave))
                {
                    japaneseAvailable = service.Available;
                    notes.AppendLine("Enabled Japanese voices: " + service.Voices.Count);
                    foreach (SpeechVoiceInfo voice in service.Voices)
                    {
                        Assert(voice.Language.StartsWith("ja", StringComparison.OrdinalIgnoreCase), "Only Japanese voice is offered");
                        notes.AppendLine("- " + voice.Name + " [" + voice.Language + "] Id=" + voice.Id);
                    }
                    notes.AppendLine("Status: " + service.Status);
                    Assert(!service.IsSpeaking, "Initialization never speaks automatically");
                    if (japaneseAvailable)
                    {
                        defaultId = service.SelectedVoiceId;
                        bool started = false, ended = false;
                        service.SpeakingChanged += delegate(bool active) { if (active) started = true; else if (started) ended = true; };
                        service.Configure(defaultId, 0, 80);
                        service.Speak(Greeting);
                        PumpUntil(delegate { return ended; }, 20000);
                        Assert(started && ended && !service.IsSpeaking, "Japanese sample completed with UI-dispatched state events");
                        notes.AppendLine("Sample script: " + Greeting);
                    }
                    else
                    {
                        service.Speak("了解。"); service.Stop();
                        Assert(service.Voices.Count == 0 && !service.IsSpeaking && service.Status.Contains("日语"), "Missing Japanese voice stays unavailable without language fallback");
                        notes.AppendLine("Japanese synthesis unavailable in this execution environment; no fallback audio or speaker output attempted.");
                    }
                }

                if (japaneseAvailable)
                {
                    notes.AppendLine(VerifyWave(wave));
                    using (SpeechService service = new SpeechService(Dispatcher.CurrentDispatcher, Path.Combine(folder, "speech-behavior-check.wav")))
                    {
                        service.Configure("TTS_MS_ZH-CN_HUIHUI_11.0", 0, 80);
                        Assert(service.SelectedVoiceId == defaultId, "Old Chinese voice ID falls back to preferred Japanese voice");
                        service.Configure("", 0, 80);
                        Assert(service.SelectedVoiceId == defaultId, "Empty voice ID restores preferred Japanese voice");
                        service.Configure("missing-voice-id", 99, -99);
                        Assert(service.Available && service.SelectedVoiceId == defaultId, "Invalid voice restores Japanese default and controls are clamped");
                        service.Configure(defaultId, 0, 80);

                        bool started = false, ended = false;
                        service.SpeakingChanged += delegate(bool active) { if (active) started = true; else if (started) ended = true; };
                        StringBuilder cancellable = new StringBuilder();
                        for (int i = 0; i < 35; i++) cancellable.Append("今日も一緒に頑張ろうね。");
                        service.Speak(cancellable.ToString());
                        PumpUntil(delegate { return started; }, 5000);
                        service.Stop(); Assert(!service.IsSpeaking, "Stop clears speaking synchronously");
                        started = false; ended = false;
                        service.Speak("了解。");
                        PumpUntil(delegate { return ended; }, 15000);
                        Assert(started && ended && !service.IsSpeaking, "Kanji-only Japanese short reply reads normally after cancellation");
                    }
                }
                notes.Insert(0, "PASS: " + count + " Japanese speech assertions. No network requests or speaker output.\r\n");
                File.WriteAllText(report, notes.ToString(), new UTF8Encoding(false)); Console.WriteLine(notes.ToString()); return 0;
            }
            catch (Exception error)
            {
                notes.Insert(0, "FAIL: " + error.Message + "\r\n");
                File.WriteAllText(report, notes.ToString(), new UTF8Encoding(false)); Console.WriteLine(notes.ToString()); return 1;
            }
        }

        private static string VerifyWave(string path)
        {
            using (BinaryReader reader = new BinaryReader(File.OpenRead(path)))
            {
                Assert(Encoding.ASCII.GetString(reader.ReadBytes(4)) == "RIFF", "Sample is a RIFF wave");
                reader.ReadInt32(); Assert(Encoding.ASCII.GetString(reader.ReadBytes(4)) == "WAVE", "Sample WAVE signature is valid");
                ushort format = 0, channels = 0, bits = 0; int sampleRate = 0; byte[] audio = null;
                while (reader.BaseStream.Position + 8 <= reader.BaseStream.Length)
                {
                    string chunk = Encoding.ASCII.GetString(reader.ReadBytes(4)); int size = reader.ReadInt32();
                    long next = reader.BaseStream.Position + size + (size & 1);
                    if (size < 0 || next > reader.BaseStream.Length) throw new Exception("Invalid wave chunk size");
                    if (chunk == "fmt " && size >= 16)
                    {
                        format = reader.ReadUInt16(); channels = reader.ReadUInt16(); sampleRate = reader.ReadInt32();
                        reader.ReadInt32(); reader.ReadUInt16(); bits = reader.ReadUInt16();
                    }
                    else if (chunk == "data") audio = reader.ReadBytes(size);
                    reader.BaseStream.Position = next;
                }
                Assert(format == 1 && channels > 0 && sampleRate > 0 && bits == 16 && audio != null && audio.Length > 2000, "Sample contains meaningful 16-bit PCM audio");
                int peak = 0; double squares = 0;
                for (int i = 0; i + 1 < audio.Length; i += 2) { int value = BitConverter.ToInt16(audio, i); peak = Math.Max(peak, Math.Abs(value)); squares += value * (double)value; }
                Assert(peak > 0, "Japanese sample PCM is not silent");
                double duration = audio.Length / (double)(sampleRate * channels * (bits / 8));
                return "Sample: " + Path.GetFileName(path) + "; PCM=" + channels + "ch/" + bits + "bit/" + sampleRate + "Hz; duration=" + duration.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + "s; peak=" + peak + "; RMS=" + Math.Sqrt(squares / (audio.Length / 2)).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
            }
        }

        private static void Assert(bool condition, string label) { if (!condition) throw new Exception(label); count++; }
        private static void PumpUntil(Func<bool> condition, int milliseconds)
        {
            Stopwatch elapsed = Stopwatch.StartNew(); DispatcherFrame frame = new DispatcherFrame();
            DispatcherTimer timer = new DispatcherTimer(DispatcherPriority.Background); timer.Interval = TimeSpan.FromMilliseconds(15);
            timer.Tick += delegate { if (condition() || elapsed.ElapsedMilliseconds >= milliseconds) frame.Continue = false; };
            timer.Start(); Dispatcher.PushFrame(frame); timer.Stop();
            if (!condition()) throw new TimeoutException("Local Japanese synthesis test timed out.");
        }
    }
}
