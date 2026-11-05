using System;
using System.IO;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using AnnaDesktopPet;

class TestMusic
{
    static string Run(Task<string> task)
    {
        while (!task.IsCompleted) {
            var frame = new DispatcherFrame();
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(10) };
            timer.Tick += delegate { timer.Stop(); frame.Continue = false; };
            timer.Start(); Dispatcher.PushFrame(frame);
        }
        return task.GetAwaiter().GetResult();
    }
    static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    static void SilentWav(string file)
    {
        using (var writer = new BinaryWriter(File.Create(file))) {
            writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); writer.Write(3236);
            writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt ")); writer.Write(16);
            writer.Write((short)1); writer.Write((short)1); writer.Write(16000); writer.Write(32000);
            writer.Write((short)2); writer.Write((short)16);
            writer.Write(System.Text.Encoding.ASCII.GetBytes("data")); writer.Write(3200); writer.Write(new byte[3200]);
        }
    }
    [STAThread] static int Main()
    {
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        string root = Path.Combine(Path.GetTempPath(), "AnnaMusicTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string a = Path.Combine(root, "Moon-A.wav"), b = Path.Combine(root, "Moon-B.wav");
        SilentWav(a); SilentWav(b);
        int searches = 0, dispatched = 0;
        var router = new MusicRouter(delegate(string s){}, null, new[]{root},
            delegate(string q, CancellationToken c) { searches++; return Task.FromResult(new List<QqTrack>()); },
            delegate(QqTrack t, CancellationToken c) { dispatched++; return Task.FromResult(0); });
        try {
            foreach (string input in new[]{"你知道晴天吗", "不要放晴天", "听音乐有什么好处", "为什么播放不了", "1", "第一首"})
                Check(Run(router.Handle(input)) == null, "普通聊天误触发：" + input);
            Check(searches == 0 && dispatched == 0, "普通聊天不得触发QQ调用");
            foreach (string selection in new[]{"第二首", "2", "第 2 首", "2. Moon-B.wav"}) {
                Check(Run(router.Handle("播放本地 Moon")).Contains("第一首"), "本地候选未显示");
                Check(Run(router.Handle(selection)).Contains("Moon-B"), "本地选择错误：" + selection);
                Check(Run(router.Handle("2")) == null, "已消费候选不得继续选择");
            }
            Run(router.Handle("播放本地 Moon")); router.CancelPending();
            Check(Run(router.Handle("第一首")) == null, "取消后本地候选未清除");
            Run(router.Handle("播放本地 Moon")); Run(router.Handle("播放 不存在的在线歌曲"));
            Check(Run(router.Handle("播放第2首")).Contains("请先"), "QQ新搜索不得沿用本地候选");
            Check(MusicRouter.MatchScore("周杰伦-晴天.mp3", "林俊杰的晴天") == 0, "不得匹配错误歌手");
            Check(MusicRouter.MatchScore("光.mp3", "光") > 0, "单字歌名未匹配");
            Console.WriteLine("PASS: local shorthand/copy selection, conversation and cancellation boundaries, no unintended QQ calls");
            return 0;
        }
        finally {
            router.Dispose(); File.Delete(a); File.Delete(b); Directory.Delete(root);
        }
    }
}
