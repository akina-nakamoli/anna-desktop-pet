using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace AnnaDesktopPet
{
    internal sealed class VoicePerformance
    {
        internal static string ProfileRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AnnaDesktopPet");
        internal string Emotion = "calm", Text, Audio, Transcript;
        internal int Rate;
        private static string FileName { get { return Path.Combine(ProfileRoot, "emotion-voices.json"); } }
        private static readonly Regex Cue = new Regex(@"^\s*\[(calm|happy|tease)\]\s*", RegexOptions.CultureInvariant);

        internal static string StripCue(string text) { return Cue.Replace(text ?? "", ""); }
        internal static string Prefix(string text)
        {
            Match match = Cue.Match(text ?? "");
            return match.Success ? "[" + match.Groups[1].Value + "]" : "";
        }
        internal static Dictionary<string, string[]> Load()
        {
            if (!File.Exists(FileName)) return new Dictionary<string, string[]>();
            return new JavaScriptSerializer().Deserialize<Dictionary<string, string[]>>(File.ReadAllText(FileName)) ?? new Dictionary<string, string[]>();
        }
        internal static void Save(Dictionary<string, string[]> profiles)
        {
            foreach (var pair in profiles)
            {
                if (pair.Key != "calm" && pair.Key != "happy" && pair.Key != "tease") throw new InvalidOperationException("情绪类型无效。");
                if (pair.Value == null || pair.Value.Length != 2) throw new InvalidOperationException("请同时填写样音和准确台词。");
                GptSovitsClient.ValidateConfiguration("http://127.0.0.1:9880", pair.Value[0], pair.Value[1]);
            }
            Directory.CreateDirectory(ProfileRoot);
            string temp = FileName + ".tmp";
            File.WriteAllText(temp, new JavaScriptSerializer().Serialize(profiles));
            if (File.Exists(FileName)) File.Replace(temp, FileName, null); else File.Move(temp, FileName);
        }
        internal static VoicePerformance Resolve(string text, string audio, string transcript, int rate)
        {
            var result = new VoicePerformance { Text = StripCue(text), Audio = audio, Transcript = transcript, Rate = rate };
            Match cue = Cue.Match(text ?? "");
            if (!cue.Success) return result;
            result.Emotion = cue.Groups[1].Value;
            result.Rate = Math.Max(-5, Math.Min(5, rate + (result.Emotion == "happy" ? 1 : result.Emotion == "tease" ? -1 : 0)));
            try
            {
                string[] profile;
                if (Load().TryGetValue(result.Emotion, out profile) && profile != null && profile.Length == 2 &&
                    File.Exists(profile[0]) && !String.IsNullOrWhiteSpace(profile[1]))
                {
                    result.Audio = profile[0]; result.Transcript = profile[1];
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            catch (ArgumentException) { }
            catch (InvalidOperationException) { }
            return result;
        }
    }
}
