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
            VerifyModelExecution();
            Console.WriteLine("PASS: local shorthand/copy selection, conversation and cancellation boundaries, no unintended QQ calls");
            return 0;
        }
        finally {
            router.Dispose(); File.Delete(a); File.Delete(b); Directory.Delete(root);
        }
    }

    static void VerifyModelExecution()
    {
        string query = null, played = null;
        int searches = 0;
        var tracks = new List<QqTrack> {
            new QqTrack { Id = "123", Title = "夜に駆ける", Artist = "YOASOBI", Album = "THE BOOK", Type = 0 },
            new QqTrack { Id = "124", Title = "夜に駆ける", Artist = "Other", Album = "Cover", Type = 0 }
        };
        var router = new MusicRouter(delegate(string s) {}, null, new string[0],
            delegate(string q, CancellationToken token) { query = q; searches++; return Task.FromResult(tracks); },
            delegate(QqTrack track, CancellationToken token) { played = track.Id; return Task.FromResult(0); });
        try
        {
            MusicCandidateSnapshot empty = router.CaptureCandidates();
            Check(empty.Candidates.Count == 0, "无候选时快照必须为空");
            string result = Run(router.ExecuteIntentAsync(new MusicIntent { Intent = "play", Query = "夜に駆ける" }, empty, CancellationToken.None));
            Check(query == "夜に駆ける" && played == null && result.Contains("第一首"), "结构化搜索应保留歌名并展示候选");
            MusicCandidateSnapshot first = router.CaptureCandidates();
            Check(first.Candidates.Count == 2 && first.Candidates[0].Contains("YOASOBI"), "模型应收到完整候选标签");
            first.Candidates.Clear();
            Check(router.CaptureCandidates().Candidates.Count == 2, "快照必须与内部候选列表隔离");
            result = Run(router.ExecuteIntentAsync(new MusicIntent { Intent = "choose", Choice = 2 }, first, CancellationToken.None));
            Check(played == "124" && searches == 1 && result.Contains("发送播放请求"), "结构化选择应投递所选曲目且不能重新搜索");
            played = null;
            result = Run(router.ExecuteIntentAsync(new MusicIntent { Intent = "choose", Choice = 2 }, first, CancellationToken.None));
            Check(played == null && result.Contains("失效"), "已消费的候选快照不能重复播放");

            Run(router.ExecuteIntentAsync(new MusicIntent { Intent = "play", Query = "夜に駆ける" }, empty, CancellationToken.None));
            MusicCandidateSnapshot old = router.CaptureCandidates();
            Run(router.ExecuteIntentAsync(new MusicIntent { Intent = "play", Query = "别的歌曲" }, old, CancellationToken.None));
            result = Run(router.ExecuteIntentAsync(new MusicIntent { Intent = "choose", Choice = 1 }, old, CancellationToken.None));
            Check(played == null && result.Contains("失效"), "新搜索后旧快照不能选择新列表中同编号的歌曲");
            result = Run(router.ExecuteIntentAsync(new MusicIntent { Intent = "choose", Choice = 9 }, router.CaptureCandidates(), CancellationToken.None));
            Check(played == null && result.Contains("只有 2"), "结构化选择应拒绝越界编号");
            MusicCandidateSnapshot canceledList = router.CaptureCandidates();
            router.CancelPending();
            result = Run(router.ExecuteIntentAsync(new MusicIntent { Intent = "choose", Choice = 1 }, canceledList, CancellationToken.None));
            Check(played == null && result.Contains("失效"), "取消后模型不能恢复过期候选");

            Run(router.ExecuteIntentAsync(new MusicIntent { Intent = "play", Query = "放不下" }, empty, CancellationToken.None));
            Check(query == "放不下", "结构化歌名不可再被当成播放命令解析");
            Run(router.ExecuteIntentAsync(new MusicIntent { Intent = "play", Query = "好吗" }, empty, CancellationToken.None));
            Check(query == "好吗", "结构化歌名结尾不可再被删除");
            query = null;
            result = Run(router.ExecuteIntentAsync(new MusicIntent { Intent = "play", Query = "歌曲 我的手机号13800138000" }, empty, CancellationToken.None));
            Check(query == null && result.Contains("个人信息"), "结构化查询仍必须经过隐私校验");

            int count = searches;
            foreach (MusicIntent bad in new[] { new MusicIntent { Intent = "chat" }, new MusicIntent { Intent = "delete" }, new MusicIntent { Intent = "control", Command = "powershell" } })
            {
                bool rejected = false;
                try { Run(router.ExecuteIntentAsync(bad, empty, CancellationToken.None)); } catch (InvalidOperationException) { rejected = true; }
                Check(rejected && searches == count && played == null, "未知或非音乐意图不能执行操作");
            }
            using (var cts = new CancellationTokenSource())
            {
                cts.Cancel();
                bool canceled = false;
                try { Run(router.ExecuteIntentAsync(new MusicIntent { Intent = "play", Query = "夜に駆ける" }, empty, cts.Token)); } catch (OperationCanceledException) { canceled = true; }
                Check(canceled && searches == count && played == null, "预先取消的模型决定不得查曲库或播放");
            }
        }
        finally { router.Dispose(); }

        var deferred = new TaskCompletionSource<List<QqTrack>>();
        var pendingRouter = new MusicRouter(delegate(string s) {}, null, new string[0],
            delegate(string q, CancellationToken token) { return deferred.Task; },
            delegate(QqTrack track, CancellationToken token) { played = track.Id; return Task.FromResult(0); });
        try
        {
            using (var cts = new CancellationTokenSource())
            {
                Task<string> pending = pendingRouter.ExecuteIntentAsync(new MusicIntent { Intent = "play", Query = "YOASOBI 夜に駆ける" }, pendingRouter.CaptureCandidates(), cts.Token);
                cts.Cancel(); deferred.SetResult(tracks);
                bool canceled = false;
                try { Run(pending); } catch (OperationCanceledException) { canceled = true; }
                Check(canceled && played == null && pendingRouter.CaptureCandidates().Candidates.Count == 0, "搜索中途取消后不得投递或存入候选");
            }
        }
        finally { pendingRouter.Dispose(); }
        Console.WriteLine("PASS: structured music intents, versioned selections, query preservation, cancellation and execution allowlist");
    }
}
