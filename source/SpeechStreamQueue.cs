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
        private readonly Func<string, CancellationToken, Task<byte[]>> generateWave;
        private readonly Func<byte[], CancellationToken, Task> playWave;
        private readonly Action<Exception> finished;
        private readonly CancellationTokenSource cancellation = new CancellationTokenSource();
        private readonly CancellationToken token;
        private bool running, completed, stopped, ended;
        private bool playingWave;
        private Task<byte[]> prefetchedWave;

        internal SpeechStreamQueue(Func<string, CancellationToken, Task> play, Action<Exception> finished)
        {
            if (play == null) throw new ArgumentNullException("play");
            this.play = play; this.finished = finished; token = cancellation.Token;
        }

        internal SpeechStreamQueue(Func<string, CancellationToken, Task<byte[]>> generateWave,
            Func<byte[], CancellationToken, Task> playWave, Action<Exception> finished)
        {
            if (generateWave == null) throw new ArgumentNullException("generateWave");
            if (playWave == null) throw new ArgumentNullException("playWave");
            this.generateWave = generateWave;
            this.playWave = playWave;
            this.finished = finished;
            token = cancellation.Token;
        }
        internal bool Append(string text)
        {
            if (String.IsNullOrWhiteSpace(text)) return false;
            lock (gate)
            {
                if (completed || stopped || ended) return false;
                sentences.Enqueue(text);
                PrefetchWhilePlaying();
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
                stopped = true; sentences.Clear(); prefetchedWave = null;
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

        // Called under gate. A playback may have started before the model supplied the next sentence.
        private void PrefetchWhilePlaying()
        {
            if (!playingWave || prefetchedWave != null || sentences.Count == 0) return;
            string text = sentences.Dequeue();
            prefetchedWave = Task.Run(delegate
            {
                token.ThrowIfCancellationRequested();
                return generateWave(text, token);
            });
            // Playback may fail or be interrupted before the pump awaits this task.
            prefetchedWave.ContinueWith(delegate(Task<byte[]> failed) { GC.KeepAlive(failed.Exception); },
                CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }

        private async Task PumpAsync()
        {
            try
            {
                while (true)
                {
                    string sentence = null; Task<byte[]> wave = null; bool notify = false;
                    lock (gate)
                    {
                        if (stopped) { running = false; return; }
                        if (prefetchedWave != null) { wave = prefetchedWave; prefetchedWave = null; }
                        else if (sentences.Count > 0) sentence = sentences.Dequeue();
                        else
                        {
                            running = false;
                            if (completed) { ended = true; notify = true; }
                        }
                    }
                    if (sentence == null && wave == null)
                    {
                        if (notify && finished != null) finished(null);
                        return;
                    }
                    token.ThrowIfCancellationRequested();
                    if (generateWave == null) await play(sentence, token).ConfigureAwait(false);
                    else
                    {
                        byte[] audio = await (wave ?? generateWave(sentence, token)).ConfigureAwait(false);
                        lock (gate)
                        {
                            if (stopped) { running = false; return; }
                            playingWave = true;
                            PrefetchWhilePlaying();
                        }
                        token.ThrowIfCancellationRequested();
                        await playWave(audio, token).ConfigureAwait(false);
                        lock (gate) playingWave = false;
                    }
                    token.ThrowIfCancellationRequested();
                }
            }
            catch (Exception error)
            {
                lock (gate)
                {
                    running = false;
                    if (stopped || ended) return;
                    ended = true; sentences.Clear(); prefetchedWave = null; cancellation.Cancel();
                }
                if (finished != null) finished(error);
            }
        }
    }
}
