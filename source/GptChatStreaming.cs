using System;
using System.Threading;
using System.Windows.Controls;

namespace AnnaDesktopPet
{
    internal sealed partial class GptChatWindow
    {
        private sealed class ReplyTurn
        {
            internal CancellationTokenSource Cancellation;
            internal string UserText, PartialText = "", Receipt = "";
            internal DateTime SentUtc; internal DateTime? AudioWaitStarted; internal bool RevealStarted;
            internal TextBox Bubble;
            internal ChatMessage SavedUser, SavedReceipt, DisplayedAnswer;
            internal bool SpeechStarted, Committed, SpeakJapanese; internal string PartialJapanese = "";
        }

        private ReplyTurn activeReply;
        private long utilityGeneration;
        private readonly Action beginStreamSpeech, endStreamSpeech;
        private readonly Action<string> appendStreamSpeech;

        // Called before recording, not after recognition: generation and audio stop immediately.
        internal bool PrepareVoiceInput()
        {
            if (closing) return false;
            if (busy)
            {
                if (activeReply != null) InterruptReply("回复已打断");
                else
                {
                    utilityGeneration++;
                    if (cancelMusic != null) cancelMusic();
                    CancelMaintenance(); StopSpeech();
                    SetBusy(false); input.Clear();
                }
            }
            return CanAcceptVoice;
        }

        private bool OwnsTurn(ReplyTurn turn)
        {
            return !closing && activeReply == turn && request == turn.Cancellation && !turn.Cancellation.IsCancellationRequested;
        }

        private readonly System.Windows.Threading.DispatcherTimer revealTimer =
            new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(15) };
        private ReplyTurn revealing;
        private string revealChinese = "", revealJapanese = "";

        private static string NextCharacter(string current, string target)
        {
            if (!target.StartsWith(current, StringComparison.Ordinal)) current = "";
            if (current.Length == target.Length) return current;
            return current + System.Globalization.StringInfo.GetNextTextElement(target, current.Length);
        }

        private void RevealTick(object sender, EventArgs args)
        {
            var turn = revealing;
            if (closing || turn == null || turn.Bubble == null)
            {
                revealTimer.Stop();
                return;
            }
            var pet = Owner as PetWindow;
            if (!turn.RevealStarted && turn.SpeakJapanese && pet != null && revealJapanese.Length > 0)
            {
                if (!turn.AudioWaitStarted.HasValue) turn.AudioWaitStarted = DateTime.UtcNow;
                // Keep subtitles available even if audio initialization or synthesis fails.
                if (!pet.ReplyAudioPlaying && (DateTime.UtcNow - turn.AudioWaitStarted.Value).TotalSeconds < 3) return;
            }
            turn.RevealStarted = true;
            var chinese = turn.Bubble;
            var japanese = (TextBox)chinese.Tag;
            bool follow = conversation.ScrollableHeight - conversation.VerticalOffset < 45;
            chinese.Text = NextCharacter(chinese.Text, revealChinese);
            japanese.Text = NextCharacter(japanese.Text, revealJapanese);
            if (japanese.Text.Length > 0) japanese.Visibility = System.Windows.Visibility.Visible;
            if (follow) ScrollToEnd();
            if (chinese.Text == revealChinese && japanese.Text == revealJapanese) revealTimer.Stop();
        }

        private void Reveal(ReplyTurn turn, string chinese, string japanese)
        {
            revealing = turn;
            revealChinese = chinese ?? "";
            revealJapanese = japanese ?? "";
            if (turn.Bubble == null) turn.Bubble = AddBubble("assistant", "");
            revealTimer.Start();
        }
        private void ApplyStream(ReplyTurn turn, GptStreamUpdate update)
        {
            if (update == null || Dispatcher.HasShutdownStarted) return;
            Action apply = delegate
            {
                if (!OwnsTurn(turn)) return;
                if (!String.IsNullOrEmpty(update.DisplayText) && update.DisplayText != turn.PartialText)
                {
                    turn.PartialText = update.DisplayText;
                    CallAssistantText = turn.PartialText;
                    SetStatus("杏菜正在回复……按住鼠标后退侧键可以打断并说话。", false);
                }
                if (!String.IsNullOrWhiteSpace(update.JapaneseSentence))
                    turn.PartialJapanese += update.JapaneseSentence;
                if (turn.PartialText.Length > 0)
                    Reveal(turn, turn.PartialText, turn.PartialJapanese);
                if (turn.SpeakJapanese && !String.IsNullOrWhiteSpace(update.JapaneseSentence) && appendStreamSpeech != null)
                {
                    try
                    {
                        if (!turn.SpeechStarted)
                        {
                            if (beginStreamSpeech != null) beginStreamSpeech();
                            turn.SpeechStarted = true;
                        }
                        appendStreamSpeech(update.JapaneseSentence);
                    }
                    catch { SetStatus("文字继续显示；日语配音暂时未能播放，请检查音色服务。", true); }
                }
            };
            if (Dispatcher.CheckAccess()) apply();
            else
            {
                try { Dispatcher.Invoke(apply); }
                catch (System.Threading.Tasks.TaskCanceledException) { }
                catch (InvalidOperationException) { if (!closing) throw; }
            }
        }

        private void RecordToolReceipt(ReplyTurn turn, string receipt)
        {
            if (Dispatcher.HasShutdownStarted) return;
            Action record = delegate
            {
                if (String.IsNullOrWhiteSpace(receipt)) return;
                if (!turn.Committed)
                {
                    if (OwnsTurn(turn)) turn.Receipt = receipt;
                    return;
                }
                // Dispatch can finish just after interruption. Record that real side effect
                // in its original turn, without touching the next turn's UI or speech.
                int userAt = turn.SavedUser == null ? -1 : history.IndexOf(turn.SavedUser);
                if (userAt < 0) return; // A new/cleared conversation must not resurrect old turns.
                turn.Receipt = receipt;
                if (turn.SavedReceipt != null && history.Contains(turn.SavedReceipt)) turn.SavedReceipt.Content = receipt;
                else
                {
                    turn.SavedReceipt = new ChatMessage("local_assistant", receipt);
                    history.Insert(userAt + 1, turn.SavedReceipt);
                }
                CancelMaintenance(); PersistHistory();
            };
            if (Dispatcher.CheckAccess()) record(); else Dispatcher.Invoke(record);
        }

        private string CommitInterrupted(ReplyTurn turn, string reason)
        {
            if (turn == null || turn.Committed) return "";
            turn.Committed = true;
            turn.SavedUser = new ChatMessage("user", turn.UserText) { CreatedUtc = turn.SentUtc };
            history.Add(turn.SavedUser);
            if (!String.IsNullOrWhiteSpace(turn.Receipt))
            {
                turn.SavedReceipt = new ChatMessage("local_assistant", turn.Receipt);
                history.Add(turn.SavedReceipt);
            }
            if (!String.IsNullOrWhiteSpace(turn.PartialText))
                history.Add(new ChatMessage("assistant", "【" + reason + "，未完成】\n" + turn.PartialText) { JapaneseText = turn.PartialJapanese });
            else history.Add(new ChatMessage("local_assistant", "【" + reason + "】尚未生成完整回答。"));
            return PersistHistory();
        }

        private void InterruptReply(string reason)
        {
            var turn = activeReply;
            if (turn == null) return;
            // Invalidate ownership before cancel callbacks; an old finally must not unlock a new turn.
            activeReply = null; request = null;
            turn.Cancellation.Cancel();
            if (cancelMusic != null) cancelMusic();
            CancelMaintenance(); StopSpeech();
            string notice = CommitInterrupted(turn, reason);
            input.Clear();
            if (!closing)
            {
                SetBusy(false); RebuildMessages();
                SetStatus("已打断，可以继续说话或输入。已显示的部分标为未完成；已发送的 QQ 音乐点播不会撤销。" + notice, notice.Length > 0);
            }
        }
    }
}
