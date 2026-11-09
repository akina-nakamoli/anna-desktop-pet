using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Speech.Synthesis;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Threading;

namespace AnnaDesktopPet
{
    internal sealed class SpeechVoiceInfo
    {
        public string Id;
        public string Name;
        public string Language;
    }

    // Japanese speech through explicitly selected Windows SAPI or loopback GPT-SoVITS.
    internal sealed class SpeechService : IDisposable
    {
        private readonly Dispatcher dispatcher;
        private readonly string testWaveFile;
        private readonly List<SpeechVoiceInfo> voices = new List<SpeechVoiceInfo>();
        private SpeechSynthesizer synthesizer;
        private Prompt activePrompt;
        private bool initialized, available, speaking, disposed, clonePending;
        private string selectedVoiceId = "", defaultVoiceId = "", status = "本机日语配音尚未初始化。";
        private int rate, volume = 80;
        private readonly GptSovitsClient cloneClient;
        private string engine = "windows", cloneEndpoint = "", cloneReferenceAudio = "", cloneReferenceText = "";
        private bool cloneConfigured, cloneConnected;
        private CancellationTokenSource cloneCancellation;
        private long cloneSequence;
        private MediaPlayer clonePlayer;
        private DispatcherTimer playbackWatchdog;
        private string cloneWaveFile;
        private SpeechStreamQueue streamQueue;
        private TaskCompletionSource<bool> streamPromptCompletion;
        private int streamCharacters;
        private bool streamShortened;
        private bool UsingClone { get { return engine == "gpt-sovits"; } }

        private bool UsingPortable { get { return engine == "portable"; } }
        private bool UsingWave { get { return UsingClone || UsingPortable; } }

        public event Action<bool> SpeakingChanged;
        public event Action<string> StatusChanged;

        public SpeechService() : this(Dispatcher.CurrentDispatcher) { }
        public SpeechService(Dispatcher uiDispatcher) : this(uiDispatcher, null) { }
        // The file-output overload exists for offline verification, never for automatic speaker playback.
        internal SpeechService(Dispatcher uiDispatcher, string waveFileForTesting) : this(uiDispatcher, waveFileForTesting, null) { }
        internal SpeechService(Dispatcher uiDispatcher, string waveFileForTesting, GptSovitsClient clientForTesting)
        {
            if (uiDispatcher == null) throw new ArgumentNullException("uiDispatcher");
            dispatcher = uiDispatcher;
            testWaveFile = waveFileForTesting;
            cloneClient = clientForTesting ?? new GptSovitsClient();
        }

        public IList<SpeechVoiceInfo> Voices { get { EnsureInitialized(); return voices.AsReadOnly(); } }
        public bool Available { get { if (!UsingWave) EnsureInitialized(); return (UsingPortable ? PortableJapaneseVoice.Available : UsingClone ? cloneConfigured : available) && !disposed; } }
        public bool IsSpeaking { get { return speaking && !disposed; } }
        public bool IsBusy { get { return !disposed && (speaking || activePrompt != null || clonePending || streamQueue != null); } }
        internal long OperationId { get { return cloneSequence; } }
        public string SelectedVoiceId { get { EnsureInitialized(); return selectedVoiceId; } }
        public string Status { get { if (!UsingWave) EnsureInitialized(); return status; } }

        private void EnsureInitialized()
        {
            if (initialized || disposed || dispatcher.HasShutdownStarted) return;
            if (!dispatcher.CheckAccess()) { dispatcher.Invoke(new Action(EnsureInitialized)); return; }
            initialized = true;
            try
            {
                synthesizer = new SpeechSynthesizer();
                IList<InstalledVoice> installed = synthesizer.GetInstalledVoices();
                InstalledVoice best = null;
                int bestScore = -1;
                foreach (InstalledVoice installedVoice in installed)
                {
                    if (!installedVoice.Enabled) continue;
                    VoiceInfo info = installedVoice.VoiceInfo;
                    // Japanese dubbing only: never silently fall back to another language.
                    if (!info.Culture.TwoLetterISOLanguageName.Equals("ja", StringComparison.OrdinalIgnoreCase)) continue;
                    voices.Add(new SpeechVoiceInfo { Id = info.Id, Name = info.Name, Language = info.Culture.Name });
                    int score = info.Culture.Name.Equals("ja-JP", StringComparison.OrdinalIgnoreCase) ? 50 : 40;
                    if (info.Gender == VoiceGender.Female) score += 5;
                    if (best == null || score > bestScore) { best = installedVoice; bestScore = score; }
                }
                if (best == null)
                {
                    ReleaseSynthesizer();
                    SetWindowsStatus("未找到可用的 Windows 日语声线。请安装 Windows 日语语音后重启桌宠；不会改用中文或英文配音。");
                    return;
                }
                synthesizer.SelectVoice(best.VoiceInfo.Name);
                selectedVoiceId = best.VoiceInfo.Id; defaultVoiceId = selectedVoiceId;
                synthesizer.Rate = rate; synthesizer.Volume = volume;
                if (testWaveFile != null) synthesizer.SetOutputToWaveFile(testWaveFile);
                else synthesizer.SetOutputToDefaultAudioDevice();
                synthesizer.SpeakStarted += OnSpeakStarted;
                synthesizer.SpeakCompleted += OnSpeakCompleted;
                available = true;
                if (!UsingWave) SetStatus(ReadyStatus());
            }
            catch
            {
                available = false; ReleaseSynthesizer();
                SetWindowsStatus("本机日语配音暂不可用。请检查 Windows 日语语音组件及音频设备；桌宠其他功能仍可使用。");
            }
        }

        private string ReadyStatus()
        {
            if (UsingPortable) return PortableJapaneseVoice.Available ? "离线日语声线已就绪。" : "离线日语组件缺失，请完整解压新版 ZIP。";
            if (UsingClone) return cloneConnected ? "本地参考音色已连接，可继续日语配音。" : "参考音色配置已就绪，点击试听连接本地 GPT-SoVITS；尚未验证服务。";
            SpeechVoiceInfo voice = voices.Find(delegate(SpeechVoiceInfo item) { return item.Id == selectedVoiceId; });
            return voice == null ? "本机日语配音已就绪。" : "本机日语配音已就绪：" + voice.Name + "（" + voice.Language + "）";
        }

        public void ConfigureBackend(string speechEngine, string endpoint, string referenceAudio, string referenceText)
        {
            Dispatch(delegate
            {
                StopCore(false);
                engine = speechEngine == "windows" || speechEngine == "portable" ? speechEngine : "gpt-sovits";
                cloneEndpoint = endpoint ?? ""; cloneReferenceAudio = referenceAudio ?? ""; cloneReferenceText = referenceText ?? "";
                cloneConfigured = false; cloneConnected = false;
                if (speechEngine != "gpt-sovits" && speechEngine != "windows" && speechEngine != "portable") { SetStatus("无法识别配音方式，请在设置中重新选择。"); return; }
                if (UsingClone)
                {
                    try { GptSovitsClient.ValidateConfiguration(cloneEndpoint, cloneReferenceAudio, cloneReferenceText); cloneConfigured = true; SetStatus(ReadyStatus()); }
                    catch (Exception error) { SetStatus(error.Message); }
                }
                else if (UsingPortable) SetStatus(ReadyStatus());
                else
                {
                    EnsureInitialized();
                    SetStatus(available ? ReadyStatus() : "本机日语配音不可用，请检查 Windows 日语语音组件。");
                }
            });
        }

        public void Configure(string voiceId, int speechRate, int speechVolume)
        {
            Dispatch(delegate
            {
                rate = Math.Max(-5, Math.Min(5, speechRate));
                volume = Math.Max(0, Math.Min(100, speechVolume));
                if (UsingWave) { StopCore(false); if (UsingPortable || cloneConfigured) SetStatus(ReadyStatus()); return; }
                EnsureInitialized();
                if (!available || synthesizer == null) return;
                StopCore(false);
                SpeechVoiceInfo voice = voices.Find(delegate(SpeechVoiceInfo item) { return item.Id == voiceId || item.Name == voiceId; });
                if (voice == null) voice = voices.Find(delegate(SpeechVoiceInfo item) { return item.Id == defaultVoiceId; });
                try
                {
                    if (voice != null) { synthesizer.SelectVoice(voice.Name); selectedVoiceId = voice.Id; }
                    synthesizer.Rate = rate; synthesizer.Volume = volume;
                    SetStatus(ReadyStatus());
                }
                catch { SetStatus("无法切换到该本机语音，请选择其他声音。" ); }
            });
        }

        public void Speak(string text, string voiceId, int speechRate, int speechVolume)
        {
            Dispatch(delegate { Configure(voiceId, speechRate, speechVolume); Speak(text); });
        }

        public void Speak(string text)
        {
            Dispatch(delegate
            {
                if (UsingWave)
                {
                    if (!Available) { SetStatus(UsingPortable ? ReadyStatus() : "参考音色配置尚未完成，请检查本地服务地址、WAV 样音与日语台词。"); return; }
                    bool shortened; string japanese = PrepareText(text, out shortened);
                    if (japanese.Length == 0) { SetStatus("没有可朗读的文字。"); return; }
                    StartClone(japanese, shortened); return;
                }
                EnsureInitialized();
                if (!available || synthesizer == null) return;
                bool truncated;
                string clean = PrepareText(text, out truncated);
                if (clean.Length == 0) { SetStatus("没有可朗读的文字。"); return; }
                StopCore(false);
                Prompt prompt = new Prompt(clean);
                activePrompt = prompt;
                try
                {
                    SetStatus(truncated ? "内容较长，本次仅朗读前 1500 字；完整内容请看聊天窗口。" : "正在使用 Windows 本机日语配音。" );
                    synthesizer.SpeakAsync(prompt);
                }
                catch
                {
                    activePrompt = null; SetSpeaking(false);
                    SetStatus("朗读未能开始，请检查本机语音和音频设备。");
                }
            });
        }

        public void Stop() { Dispatch(delegate { StopCore(true); }); }

        // One stream belongs to one model reply. Appending never interrupts an earlier sentence.
        public void BeginStream()
        {
            Dispatch(delegate
            {
                StopCore(false);
                if (UsingWave)
                {
                    if (!Available) { SetStatus(UsingPortable ? ReadyStatus() : "参考音色配置尚未完成，请检查本地服务地址、WAV 样音与日语台词。"); return; }
                }
                else { EnsureInitialized(); if (!available || synthesizer == null) return; }
                long sequence = cloneSequence;
                bool waveBackend = UsingWave, portable = UsingPortable;
                string endpoint = cloneEndpoint, referenceAudio = cloneReferenceAudio, referenceText = cloneReferenceText;
                int speechRate = rate;
                streamCharacters = 0; streamShortened = false;
                SpeechStreamQueue queue = null;
                queue = new SpeechStreamQueue(
                    delegate(string sentence, CancellationToken token)
                    {
                        return waveBackend
                            ? PlayStreamCloneAsync(sequence, portable, endpoint, referenceAudio, referenceText, speechRate, sentence, token)
                            : PlayStreamWindowsAsync(sequence, sentence, token);
                    },
                    delegate(Exception error)
                    {
                        Dispatch(delegate
                        {
                            if (sequence != cloneSequence || !Object.ReferenceEquals(streamQueue, queue)) return;
                            streamQueue = null; clonePending = false; SetSpeaking(false);
                            if (error != null)
                            {
                                if (waveBackend && !portable) cloneConnected = false;
                                SetStatus(error is InvalidOperationException ? error.Message : "分句配音未能完成，请检查音色服务或音频设备。");
                            }
                            else
                            {
                                if (waveBackend && !portable && streamCharacters > 0) cloneConnected = true;
                                SetStatus(streamShortened ? "本次已配音前 1500 字；完整内容请看聊天窗口。" : testWaveFile != null && streamCharacters > 0 ? "分句配音已生成到测试文件，未播放。" : ReadyStatus());
                            }
                        });
                    });
                streamQueue = queue;
                SetStatus("等待日语句子，生成一句就开始配音。");
            });
        }

        public void AppendStream(string sentence)
        {
            Dispatch(delegate
            {
                if (streamQueue == null) return;
                bool shortened;
                string clean = PrepareText(sentence, out shortened);
                if (clean.Length == 0) return;
                int remaining = 1500 - streamCharacters;
                if (remaining <= 0) { streamShortened = true; return; }
                if (clean.Length > remaining)
                {
                    int take = remaining;
                    if (take > 0 && Char.IsHighSurrogate(clean[take - 1])) take--;
                    clean = clean.Substring(0, take).TrimEnd(); shortened = true;
                }
                if (clean.Length == 0) return;
                if (streamQueue.Append(clean))
                {
                    streamCharacters += clean.Length; streamShortened |= shortened;
                    if (UsingWave) clonePending = true;
                    if (!speaking) SetStatus(UsingClone ? "正在生成这一句日语参考音色……" : "正在准备这一句日语配音……");
                }
            });
        }

        public void CompleteStream()
        {
            Dispatch(delegate { if (streamQueue != null) streamQueue.Complete(); });
        }

        private void StopCore(bool showStatus)
        {
            // Clear identity before cancelling: a late completion must not stop the next utterance.
            SpeechStreamQueue oldQueue = streamQueue; streamQueue = null;
            TaskCompletionSource<bool> oldCompletion = streamPromptCompletion; streamPromptCompletion = null;
            activePrompt = null;
            clonePending = false;
            cloneSequence++;
            if (oldQueue != null) oldQueue.Stop();
            if (oldCompletion != null) oldCompletion.TrySetCanceled();
            if (cloneCancellation != null) { cloneCancellation.Cancel(); cloneCancellation.Dispose(); cloneCancellation = null; }
            ReleaseClonePlayer();
            if (synthesizer != null)
            {
                try { synthesizer.SpeakAsyncCancelAll(); } catch { }
            }
            SetSpeaking(false);
            if (showStatus && (UsingPortable ? PortableJapaneseVoice.Available : UsingClone ? cloneConfigured : available)) SetStatus("已停止配音与等待中的音色请求。");
        }

        private async Task PlayStreamCloneAsync(long sequence, bool portable, string endpoint, string referenceAudio, string referenceText,
            int speechRate, string sentence, CancellationToken token)
        {
            IList<string> chunks = SplitSpeech(sentence);
            token.ThrowIfCancellationRequested();
            Task<byte[]> next = GenerateWaveAsync(portable, endpoint, referenceAudio, referenceText, chunks[0], speechRate, token);
            for (int i = 0; i < chunks.Count; i++)
            {
                byte[] wave = await next.ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                next = i + 1 < chunks.Count ? GenerateWaveAsync(portable, endpoint, referenceAudio, referenceText, chunks[i + 1], speechRate, token) : null;
                if (next != null) ObservePrefetchFault(next);
                if (testWaveFile != null) { if (i == 0) File.WriteAllBytes(testWaveFile, wave); continue; }
                string temp = Path.Combine(Path.GetTempPath(), "AnnaVoice-" + Guid.NewGuid().ToString("N") + ".wav");
                File.WriteAllBytes(temp, wave);
                await QueueClonePlayback(sequence, temp, token).ConfigureAwait(false);
            }
        }

        private async Task PlayStreamWindowsAsync(long sequence, string sentence, CancellationToken token)
        {
            TaskCompletionSource<bool> completed = new TaskCompletionSource<bool>();
            using (token.Register(delegate { completed.TrySetCanceled(); }))
            {
                token.ThrowIfCancellationRequested();
                if (disposed || dispatcher.HasShutdownStarted) throw new OperationCanceledException(token);
                DispatcherOperation pending = dispatcher.BeginInvoke(new Action(delegate
                {
                    if (disposed || sequence != cloneSequence || token.IsCancellationRequested) { completed.TrySetCanceled(); return; }
                    if (synthesizer == null) { completed.TrySetException(new InvalidOperationException("本机日语配音不可用。")); return; }
                    Prompt prompt = new Prompt(sentence); activePrompt = prompt; streamPromptCompletion = completed;
                    try { SetStatus("正在逐句使用 Windows 本机日语配音。"); synthesizer.SpeakAsync(prompt); }
                    catch { activePrompt = null; streamPromptCompletion = null; completed.TrySetException(new InvalidOperationException("本机朗读未能开始，请检查语音组件和音频设备。")); }
                }));
                pending.Aborted += delegate { completed.TrySetCanceled(); };
                if (pending.Status == DispatcherOperationStatus.Aborted) completed.TrySetCanceled();
                await completed.Task.ConfigureAwait(false);
            }
        }

        internal Task<bool> TryReferenceSampleAsync()
        {
            if (!UsingClone || !cloneConfigured) throw new InvalidOperationException(Status);
            return PlayWaveSpeechAsync("こんにちは、アンナだよ。", false);
        }

        private async void StartClone(string text, bool truncated)
        {
            await PlayWaveSpeechAsync(text, truncated);
        }

        private async Task<bool> PlayWaveSpeechAsync(string text, bool truncated)
        {
            StopCore(false);
            long sequence = cloneSequence;
            CancellationTokenSource cancellation = new CancellationTokenSource(); cloneCancellation = cancellation;
            clonePending = true;
            CancellationToken token = cancellation.Token;
            string endpoint = cloneEndpoint, referenceAudio = cloneReferenceAudio, referenceText = cloneReferenceText;
            int speechRate = rate;
            bool portable = UsingPortable;
            SetStatus(portable ? "正在生成离线日语配音……" : truncated ? "正在生成日语参考音色；本次仅配音前 1500 字。" : "正在连接本地服务，生成日语参考音色……");
            try
            {
                IList<string> chunks = SplitSpeech(text);
                Task<byte[]> next = GenerateWaveAsync(portable, endpoint, referenceAudio, referenceText, chunks[0], speechRate, token);
                for (int i = 0; i < chunks.Count; i++)
                {
                    byte[] wave = await next.ConfigureAwait(false);
                    token.ThrowIfCancellationRequested();
                    // Prefetch one segment while the previous one is playing; only one request is in flight.
                    next = i + 1 < chunks.Count ? GenerateWaveAsync(portable, endpoint, referenceAudio, referenceText, chunks[i + 1], speechRate, token) : null;
                    if (next != null) ObservePrefetchFault(next);
                    if (testWaveFile != null)
                    {
                        if (i == 0) File.WriteAllBytes(testWaveFile, wave);
                        continue;
                    }
                    string temp = Path.Combine(Path.GetTempPath(), "AnnaVoice-" + Guid.NewGuid().ToString("N") + ".wav");
                    File.WriteAllBytes(temp, wave);
                    await QueueClonePlayback(sequence, temp, token).ConfigureAwait(false);
                }
                Dispatch(delegate { if (sequence == cloneSequence && !token.IsCancellationRequested) { clonePending = false; cloneConnected = true; SetSpeaking(false); SetStatus(testWaveFile != null ? "日语配音已生成到测试文件，未播放。" : ReadyStatus()); } });
                return !token.IsCancellationRequested;
            }
            catch (OperationCanceledException) { return false; }
            catch (Exception error)
            {
                try { cancellation.Cancel(); } catch (ObjectDisposedException) { }
                string message = error is InvalidOperationException ? error.Message : portable ? "离线日语配音未能生成或播放，请检查解压目录和音频设备。" : "参考音色未能生成或播放，请检查本地服务和临时文件夹。";
                Dispatch(delegate { if (sequence == cloneSequence) { clonePending = false; cloneConnected = false; SetSpeaking(false); SetStatus(message); } });
                return false;
            }
        }

        private Task<byte[]> GenerateWaveAsync(bool portable, string endpoint, string audio, string reference, string text, int speechRate, CancellationToken token)
        {
            return portable ? PortableJapaneseVoice.GenerateAsync(text, speechRate, token) :
                cloneClient.GenerateAsync(endpoint, audio, reference, text, speechRate, token);
        }

        private static void ObservePrefetchFault(Task<byte[]> pending)
        {
            // A failed playback can cancel the queue before this task is awaited.
            // Observe its fault in that case; awaiting the task still propagates it normally.
            pending.ContinueWith(delegate(Task<byte[]> faulted) { GC.KeepAlive(faulted.Exception); },
                CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }

        private async Task QueueClonePlayback(long sequence, string file, CancellationToken token)
        {
            TaskCompletionSource<bool> finished = new TaskCompletionSource<bool>();
            using (token.Register(delegate { finished.TrySetCanceled(); }))
            {
                if (token.IsCancellationRequested || disposed || dispatcher.HasShutdownStarted) { DeleteWave(file); token.ThrowIfCancellationRequested(); throw new InvalidOperationException("播放已停止。"); }
                try
                {
                    DispatcherOperation pending = dispatcher.BeginInvoke(new Action(delegate
                    {
                        if (disposed || sequence != cloneSequence || token.IsCancellationRequested) { DeleteWave(file); finished.TrySetCanceled(); return; }
                        BeginClonePlayback(sequence, file, delegate(bool success) { if (success) finished.TrySetResult(true); else finished.TrySetException(new InvalidOperationException("日语配音已生成，但播放失败，请检查音频设备。")); });
                    }));
                    pending.Aborted += delegate { DeleteWave(file); finished.TrySetCanceled(); };
                    if (pending.Status == DispatcherOperationStatus.Aborted) { DeleteWave(file); finished.TrySetCanceled(); }
                    await finished.Task.ConfigureAwait(false);
                }
                catch { DeleteWave(file); throw; }
            }
        }

        private void BeginClonePlayback(long sequence, string file, Action<bool> completed)
        {
            cloneConnected = true; cloneWaveFile = file;
            MediaPlayer player = new MediaPlayer(); clonePlayer = player; player.Volume = volume / 100.0;
            player.MediaOpened += delegate
            {
                if (sequence != cloneSequence || clonePlayer != player) return;
                SetSpeaking(true); SetStatus(UsingPortable ? "正在播放离线日语配音。" : "正在播放日语参考音色（AI 合成）。"); player.Play();
                if (player.NaturalDuration.HasTimeSpan && playbackWatchdog != null)
                    playbackWatchdog.Interval = player.NaturalDuration.TimeSpan + TimeSpan.FromSeconds(5);
            };
            player.MediaEnded += delegate
            {
                if (sequence != cloneSequence || clonePlayer != player) return;
                ReleaseClonePlayer(); completed(true);
            };
            player.MediaFailed += delegate
            {
                if (sequence != cloneSequence || clonePlayer != player) return;
                ReleaseClonePlayer(); SetSpeaking(false); completed(false);
            };
            playbackWatchdog = new DispatcherTimer(DispatcherPriority.Background, dispatcher);
            playbackWatchdog.Interval = TimeSpan.FromSeconds(20);
            playbackWatchdog.Tick += delegate
            {
                if (sequence != cloneSequence || clonePlayer != player) return;
                ReleaseClonePlayer(); SetSpeaking(false); completed(false);
            };
            playbackWatchdog.Start();
            try { player.Open(new Uri(file, UriKind.Absolute)); }
            catch { ReleaseClonePlayer(); SetSpeaking(false); completed(false); }
        }

        // Bound the first request to a short phrase so long answers begin sooner.
        internal static IList<string> SplitSpeech(string text)
        {
            List<string> chunks = new List<string>();
            string remaining = (text ?? "").Trim();
            while (remaining.Length > 0)
            {
                int limit = chunks.Count == 0 ? 42 : 100;
                int end = Math.Min(limit, remaining.Length), cut = 0;
                for (int i = 0; i < end; i++)
                    if ("。！？!?\n".IndexOf(remaining[i]) >= 0) { cut = i + 1; break; }
                if (cut == 0 && remaining.Length > end)
                {
                    for (int i = end - 1; i >= Math.Min(12, end - 1); i--)
                        if ("、，,；;：: ".IndexOf(remaining[i]) >= 0) { cut = i + 1; break; }
                    if (cut == 0) cut = end;
                }
                if (cut == 0) cut = remaining.Length;
                if (cut < remaining.Length && cut > 0 && Char.IsHighSurrogate(remaining[cut - 1]) && Char.IsLowSurrogate(remaining[cut])) cut--;
                string part = remaining.Substring(0, cut).Trim();
                if (part.Length > 0) chunks.Add(part);
                remaining = remaining.Substring(cut).TrimStart();
            }
            return chunks;
        }

        private void ReleaseClonePlayer()
        {
            if (playbackWatchdog != null) { playbackWatchdog.Stop(); playbackWatchdog = null; }
            MediaPlayer player = clonePlayer; clonePlayer = null;
            if (player != null) { try { player.Stop(); player.Close(); } catch { } }
            string file = cloneWaveFile; cloneWaveFile = null; DeleteWave(file);
        }

        private static void DeleteWave(string file)
        {
            if (String.IsNullOrEmpty(file)) return;
            try { File.Delete(file); } catch { }
        }

        private void OnSpeakStarted(object sender, SpeakStartedEventArgs e)
        {
            Prompt prompt = e.Prompt;
            Dispatch(delegate { if (Object.ReferenceEquals(activePrompt, prompt)) SetSpeaking(true); });
        }

        private void OnSpeakCompleted(object sender, SpeakCompletedEventArgs e)
        {
            Prompt prompt = e.Prompt;
            bool cancelled = e.Cancelled, failed = e.Error != null;
            Dispatch(delegate
            {
                if (!Object.ReferenceEquals(activePrompt, prompt)) return;
                activePrompt = null;
                if (streamPromptCompletion != null)
                {
                    TaskCompletionSource<bool> completed = streamPromptCompletion; streamPromptCompletion = null;
                    if (failed) completed.TrySetException(new InvalidOperationException("本机朗读未能完成，请检查语音组件和音频设备。"));
                    else if (cancelled) completed.TrySetCanceled();
                    else completed.TrySetResult(true);
                    return;
                }
                SetSpeaking(false);
                SetStatus(failed ? "本机朗读未能完成，请检查语音组件和音频设备。" : cancelled ? "已停止朗读。" : ReadyStatus());
            });
        }

        private void SetSpeaking(bool value)
        {
            if (speaking == value) return;
            speaking = value;
            Action<bool> handler = SpeakingChanged;
            if (handler != null) handler(value);
        }

        private void SetStatus(string value)
        {
            if (status == value) return;
            status = value;
            Action<string> handler = StatusChanged;
            if (handler != null) handler(value);
        }

        private void SetWindowsStatus(string value) { if (!UsingWave) SetStatus(value); }

        private void Dispatch(Action action)
        {
            if (disposed || dispatcher.HasShutdownStarted) return;
            if (dispatcher.CheckAccess()) action();
            else dispatcher.BeginInvoke(new Action(delegate { if (!disposed) action(); }));
        }

        internal static string PrepareText(string input, out bool truncated)
        {
            string text = input ?? "";
            // Read prose, not code fences, URL destinations, markdown markers or emoji.
            text = Regex.Replace(text, @"```[\s\S]*?```|~~~[\s\S]*?~~~", " ");
            text = Regex.Replace(text, @"!\[[^\]]*\]\([^\)]*\)", " ");
            text = Regex.Replace(text, @"\[([^\]]+)\]\([^\)]*\)", "$1");
            text = Regex.Replace(text, @"https?://[^\s<>]+|www\.[^\s<>]+", " ", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"(?m)^\s{0,3}(?:#{1,6}\s+|>\s*|[-*+]\s+)", "");
            text = text.Replace("**", "").Replace("__", "").Replace("`", "").Replace("*", "");
            StringBuilder result = new StringBuilder();
            for (int i = 0; i < text.Length; i++)
            {
                char ch = text[i];
                UnicodeCategory category = CharUnicodeInfo.GetUnicodeCategory(text, i);
                bool pair = Char.IsHighSurrogate(ch) && i + 1 < text.Length && Char.IsLowSurrogate(text[i + 1]);
                bool skip = category == UnicodeCategory.OtherSymbol || category == UnicodeCategory.ModifierSymbol ||
                    category == UnicodeCategory.Format || ch == '\uFE0F' || ch == '\uFE0E' ||
                    category == UnicodeCategory.Surrogate;
                if (!skip) { if (Char.IsControl(ch)) result.Append(' '); else result.Append(ch); if (pair) result.Append(text[i + 1]); }
                if (pair) i++;
            }
            string clean = Regex.Replace(result.ToString(), @"\s+", " ").Trim();
            truncated = clean.Length > 1500;
            if (truncated)
            {
                int take = 1500;
                if (Char.IsHighSurrogate(clean[take - 1])) take--;
                clean = clean.Substring(0, take).TrimEnd();
            }
            return clean;
        }

        private void ReleaseSynthesizer()
        {
            SpeechSynthesizer old = synthesizer; synthesizer = null;
            if (old == null) return;
            try { old.SpeakStarted -= OnSpeakStarted; old.SpeakCompleted -= OnSpeakCompleted; old.SpeakAsyncCancelAll(); old.Dispose(); } catch { }
        }

        public void Dispose()
        {
            if (disposed) return;
            if (!dispatcher.CheckAccess() && !dispatcher.HasShutdownStarted) { dispatcher.Invoke(new Action(Dispose)); return; }
            StopCore(false);
            activePrompt = null; speaking = false; available = false; disposed = true;
            ReleaseSynthesizer(); SpeakingChanged = null; StatusChanged = null;
        }
    }
}
