using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AnnaDesktopPet
{
    internal sealed class SpeechStreamQueue
    {
        private readonly object gate = new object();
        private readonly Queue<string> sentences = new Queue<string>();
        private readonly Func<string, CancellationToken, Task> play;
        private readonly Action<Exception> finished;
        private readonly CancellationTokenSource cancellation = new CancellationTokenSource();
        private readonly CancellationToken token;
        private bool running, completed, stopped, ended;

        internal SpeechStreamQueue(Func<string, CancellationToken, Task> play, Action<Exception> finished)
        {
            if (play == null) throw new ArgumentNullException("play");
            this.play = play; this.finished = finished; token = cancellation.Token;
        }
        internal bool Append(string text)
        {
            if (String.IsNullOrWhiteSpace(text)) return false;
            lock (gate)
            {
                if (completed || stopped || ended) return false;
                sentences.Enqueue(text);
                StartWorker();
                return true;
            }
        }
        internal void Complete()
        {
            lock (gate)
            {
                if (stopped || ended || completed) return;
                completed = true;
                StartWorker();
            }
        }
        internal void Stop()
        {
            lock (gate)
            {
                if (stopped || ended) return;
                stopped = true; sentences.Clear();
                cancellation.Cancel();
            }
        }
        // Called under gate. Even synchronously completing players never recurse through Append.
        private void StartWorker()
        {
            if (running) return;
            running = true;
            Task.Run((Func<Task>)PumpAsync);
        }
        private async Task PumpAsync()
        {
            try
            {
                while (true)
                {
                    string sentence = null; bool notify = false;
                    lock (gate)
                    {
                        if (stopped) { running = false; return; }
                        if (sentences.Count > 0) sentence = sentences.Dequeue();
                        else
                        {
                            running = false;
                            if (completed) { ended = true; notify = true; }
                        }
                    }
                    if (sentence == null)
                    {
                        if (notify && finished != null) finished(null);
                        return;
                    }
                    token.ThrowIfCancellationRequested();
                    await play(sentence, token).ConfigureAwait(false);
                    token.ThrowIfCancellationRequested();
                }
            }
            catch (Exception error)
            {
                lock (gate)
                {
                    running = false;
                    if (stopped || ended) return;
                    ended = true; sentences.Clear(); cancellation.Cancel();
                }
                if (finished != null) finished(error);
            }
        }
    }
}
