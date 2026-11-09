using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AnnaDesktopPet
{
    internal static class SpeechStreamQueueTests
    {
        private static int checks;
        private static void Check(bool good, string name) { if (!good) throw new Exception(name); checks++; }
        private static void Wait(Func<bool> condition, string name)
        {
            DateTime until = DateTime.UtcNow.AddSeconds(3);
            while (!condition() && DateTime.UtcNow < until) Thread.Sleep(5);
            Check(condition(), name);
        }
        private static int Main()
        {
            try
            {
                var gate = new object(); var calls = new List<string>();
                var first = new TaskCompletionSource<bool>(); var second = new TaskCompletionSource<bool>();
                int completed = 0; Exception error = null; CancellationToken activeToken = default(CancellationToken);
                var queue = new SpeechStreamQueue(delegate(string text, CancellationToken token)
                {
                    lock (gate) { calls.Add(text); activeToken = token; return calls.Count == 1 ? first.Task : second.Task; }
                }, delegate(Exception problem) { error = problem; Interlocked.Increment(ref completed); });
                Check(queue.Append("こんにちは。"), "accept first completed sentence");
                Wait(delegate { lock(gate) return calls.Count == 1; }, "starts first sentence without waiting for model completion");
                Check(queue.Append("また会えたね。"), "append while speaking");
                lock (gate) Check(calls.Count == 1, "never overlap playback");
                Check(!activeToken.IsCancellationRequested, "append does not cancel active sentence");
                queue.Complete();
                Check(!queue.Append("遅れた文。"), "append after completion rejected");
                first.SetResult(true);
                Wait(delegate { lock(gate) return calls.Count == 2; }, "next sentence begins in order");
                Check(completed == 0, "complete waits for playback drain");
                second.SetResult(true);
                Wait(delegate { return completed == 1; }, "one final callback after drain");
                Check(error == null, "successful playback has no error");
                queue.Complete(); queue.Stop(); Check(completed == 1, "terminal callback once");

                calls.Clear(); completed = 0; first = new TaskCompletionSource<bool>();
                var stopped = new SpeechStreamQueue(delegate(string text, CancellationToken token)
                {
                    lock(gate) { calls.Add(text); activeToken = token; } return first.Task;
                }, delegate(Exception problem) { Interlocked.Increment(ref completed); });
                stopped.Append("古い文。");
                Wait(delegate { lock(gate) return calls.Count == 1; }, "old utterance started");
                stopped.Append("再生してはいけない文。"); stopped.Stop();
                Check(activeToken.IsCancellationRequested, "stop cancels backend request");
                Check(!stopped.Append("遅れた返答。"), "stopped generation rejects late append");
                first.SetResult(true); Thread.Sleep(30);
                lock(gate) Check(calls.Count == 1, "late completed old work cannot start queued audio");
                Check(completed == 0, "stopped generation cannot publish completion status");

                completed=0;
                var empty = new SpeechStreamQueue(delegate(string text,CancellationToken token) { throw new Exception("empty should not play"); }, delegate(Exception problem) { error=problem; Interlocked.Increment(ref completed); });
                Check(!empty.Append("  "), "empty sentence ignored"); empty.Complete();
                Wait(delegate { return completed==1; }, "empty completed stream settles");
                Check(error==null,"empty stream has no error");

                completed=0; calls.Clear(); var failedGate=new TaskCompletionSource<bool>();
                var failed=new SpeechStreamQueue(delegate(string text,CancellationToken token) { lock(gate)calls.Add(text);return failedGate.Task; },delegate(Exception problem){error=problem;Interlocked.Increment(ref completed);});
                failed.Append("故障。");failed.Append("再生しない。");failed.Complete();
                failedGate.SetException(new InvalidOperationException("test speaker error"));
                Wait(delegate{return completed==1;},"failure completes once");
                Check(error is InvalidOperationException,"backend error preserved");
                lock(gate)Check(calls.Count==1,"failure clears rest of utterance");
                Check(!failed.Append("late"),"failed stream stays closed");

                completed=0; calls.Clear();
                var synchronous=new SpeechStreamQueue(delegate(string text,CancellationToken token){lock(gate)calls.Add(text);return Task.FromResult(0);},delegate(Exception problem){Interlocked.Increment(ref completed);});
                for(int i=0;i<30;i++)Check(synchronous.Append(i.ToString()),"accept burst "+i);
                synchronous.Complete();Wait(delegate{return completed==1;},"synchronous playback burst finishes");
                lock(gate){Check(calls.Count==30,"no lost queued sentences");for(int i=0;i<30;i++)Check(calls[i]==i.ToString(),"burst FIFO "+i);}
                Console.WriteLine("PASS speech stream queue: "+checks+" assertions");return 0;
            }
            catch(Exception ex){Console.Error.WriteLine("FAIL: "+ex.Message);return 1;}
        }
    }
}
