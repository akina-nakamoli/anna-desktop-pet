using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using System.Text.RegularExpressions;
using System.Windows.Media;

namespace AnnaDesktopPet
{
    internal sealed class MusicRouter
    {
        private readonly MediaPlayer player = new MediaPlayer();
        private readonly Action<string> notice;
        private readonly Action<string> openWeb;
        private readonly string[] roots;
        private readonly List<string> candidates = new List<string>();
        private readonly List<QqTrack> qqCandidates = new List<QqTrack>();
        private readonly Func<string, CancellationToken, Task<List<QqTrack>>> qqSearch;
        private readonly Func<QqTrack, CancellationToken, Task> qqDispatch;
        private CancellationTokenSource pending = new CancellationTokenSource();
        private bool choosingQq;
        private QqTrack lastRequestedQq;
        private int favoriteIndex=-1;
        private bool favoriteMode;
        private readonly Random favoriteRandom=new Random();
        private bool awaitingChoice;
        private string current;
        private bool paused, disposed;
        internal MusicRouter(Action<string> notify, Action<string> web = null, string[] searchRoots = null, Func<string, CancellationToken, Task<List<QqTrack>>> searchQq = null, Func<QqTrack, CancellationToken, Task> dispatchQq = null)
        {
            notice = notify; qqSearch = searchQq ?? QqCatalog.Search; qqDispatch = dispatchQq ?? QqCatalog.Dispatch;
            roots = searchRoots ?? new [] { Environment.GetFolderPath(Environment.SpecialFolder.MyMusic) };
            openWeb = web ?? delegate(string url) { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); };
            player.MediaFailed += delegate { current = null; paused = false; notice("这首歌无法播放，请检查文件格式或选择其他版本。"); };
            player.MediaEnded += delegate { current = null; paused = false; };
        }
        internal void CancelPending() { lock (QqCatalog.DispatchGate) { var old = pending; pending = new CancellationTokenSource(); old.Cancel(); ClearSelection(); } }
        private void ClearSelection() { candidates.Clear(); qqCandidates.Clear(); choosingQq = false; awaitingChoice = false; }
        private string NotMusic() { ClearSelection(); return null; }
        private static string ChoiceText(string text) { return Regex.Replace((text ?? "").Normalize(NormalizationForm.FormKC), @"\s+", " ").Trim(); }
        private static string QqLabel(QqTrack track) { return track.Title + " — " + track.Artist + "（" + track.Album + "）"; }
        private bool TryChoice(string text, out int index, out string error)
        {
            index = 0; error = null;
            string value = ChoiceText(text);
            int count = choosingQq ? qqCandidates.Count : candidates.Count;
            Match copied = Regex.Match(value, @"^([0-9]{1,2})[.、]\s*(.+)$");
            if (awaitingChoice && copied.Success)
            {
                Int32.TryParse(copied.Groups[1].Value, out index);
                if (index < 1 || index > count) { error = "当前列表只有 " + count + " 首，请选择列表中的编号。"; return true; }
                string label = choosingQq ? QqLabel(qqCandidates[index-1]) : Path.GetFileName(candidates[index-1]);
                if (!String.Equals(ChoiceText(label), ChoiceText(copied.Groups[2].Value), StringComparison.OrdinalIgnoreCase))
                    error = "这条歌曲信息与当前候选不一致，请按当前列表回复第几首。";
                return true;
            }
            Match ordinal = Regex.Match(value, @"^(?:(?:杏菜|请|帮我|麻烦|那就|就|我要|我选|选择|选|播放(?:本地)?|放(?:本地)?|听(?:本地)?)[，,\s]*)*(?:第\s*)?([0-9]{1,2}|[一二三四五六七八九十两])\s*(?:首|个|号)?(?:吧|呀|就行|好了)?[。.!！?？\s]*$");
            if (!ordinal.Success) return false;
            bool explicitPlay = Regex.IsMatch(value, @"^(?:(?:杏菜|请|帮我|麻烦)[，,\s]*)*(?:播放|放|听)");
            if (!awaitingChoice && !explicitPlay) return false;
            string number = ordinal.Groups[1].Value;
            index = "一二三四五六七八九十".IndexOf(number, StringComparison.Ordinal) + 1;
            if (number == "两") index = 2;
            else if (index == 0) Int32.TryParse(number, out index);
            if (!awaitingChoice || count == 0) error = "请先让我查找歌曲，再回复第几首。";
            else if (index < 1 || index > count) error = "当前列表只有 " + count + " 首，请选择列表中的编号。";
            return true;
        }
        internal void Dispose() { disposed = true; CancelPending(); player.Stop(); player.Close(); }
        internal async Task<string> Handle(string input)
        {
            CancellationToken token = pending.Token;
            string t = (input ?? "").Trim();
            try {
                if(t=="收藏这首" || t=="收藏这首歌" || t=="喜欢这首")return MusicFavorites.Add(lastRequestedQq);
                if(t=="查看收藏" || t=="我的收藏" || t=="查看歌单")return MusicFavorites.List();
                Match favoriteRemove=Regex.Match(t,@"^移除收藏第?(\d+)首?$");
                if(favoriteRemove.Success)return MusicFavorites.Remove(Int32.Parse(favoriteRemove.Groups[1].Value));
                Match favoritePick=Regex.Match(t,@"^播放收藏第?(\d+)首?$");
                bool shuffle=t=="随机来一首" || t=="随机播放收藏" || t=="随机播放一首音乐" || t=="随机播放一首歌";
                bool firstFavorite=t=="放我的收藏" || t=="播放我的收藏";
                bool nextFavorite=favoriteMode && (t=="下一首" || t=="上一首");
                if(shuffle || firstFavorite || favoritePick.Success || nextFavorite) {
                    var favorites=MusicFavorites.Load();if(favorites.Count==0)return "收藏还是空的，先点歌并说「收藏这首」吧。";
                    int index=shuffle?favoriteRandom.Next(favorites.Count):favoritePick.Success?Int32.Parse(favoritePick.Groups[1].Value)-1:nextFavorite?(favoriteIndex+(t=="下一首"?1:-1)+favorites.Count)%favorites.Count:0;
                    if(index<0 || index>=favorites.Count)return "收藏编号超出范围，请先查看收藏。";
                    string response=await PlayQq(MusicFavorites.Track(favorites[index]),token);
                    if(response.StartsWith("已向")){favoriteIndex=index;favoriteMode=true;}
                    return response;
                }
            }catch(Exception){return "收藏读取或保存失败，请检查本机配置目录。";}
            string memoryResult=PetMemory.Handle(t); if(memoryResult!=null) { ClearSelection(); return memoryResult; }
            if (Regex.IsMatch(t, @"^(?:打开)?(?:音乐面板|正在播放面板)[。！!\s]*$")) { new MusicPanel().Show(); return "音乐面板已打开。"; }
            if (Regex.IsMatch(t, @"^(?:请|杏菜|帮我)?[，,\s]*(上一首|下一首|暂停音乐|继续播放|恢复播放|暂停|继续音乐)[吧。！!\s]*$") && current == null)
            {
                string command=t.Contains("上一首")?"previous":t.Contains("下一首")?"next":t.Contains("暂停")?"pause":"play";
                return await MusicPanel.Command(command);
            }
            if (Regex.IsMatch(t, @"^(?:(?:杏菜|请|帮我|麻烦|可以|能不能|可不可以|你)[，,\s]*)*(?:打开|启动)\s*QQ\s*音乐[吧呀。！!？?\s]*$", RegexOptions.IgnoreCase))
            {
                try { await QqCatalog.Open(token); return "QQ 音乐已经打开，可以告诉我想听的歌名。"; }
                catch (OperationCanceledException) { return "已取消打开 QQ 音乐。"; }
                catch (Exception ex) { return "打开 QQ 音乐未完成：" + ex.Message; }
            }
            if (Regex.IsMatch(t, @"^听音乐(?:有什么|为什么|如何|怎么|是什么)")) return NotMusic();
            if (Regex.IsMatch(t, @"^(?:(?:请|杏菜|帮我|麻烦|可以|能不能|能|可不可以|给我|你)[，,\s]*)*(暂停|停一下)(音乐|歌曲|播放|歌)?[吧呀。！!？?\s]*$"))
            { if (current == null) return "桌宠没有正在播放本地歌曲；QQ 音乐播放请在客户端控制。"; player.Pause(); paused = true; return "好的，音乐暂停了。"; }
            if (Regex.IsMatch(t, @"^(?:(?:请|杏菜|帮我|麻烦|可以|能不能|能|可不可以|给我|你)[，,\s]*)*(继续播放|继续放|继续听|恢复播放|继续音乐)[吧呀。！!？?\s]*$"))
            { if (current == null || !paused) return "桌宠没有暂停的本地歌曲；QQ 音乐播放请在客户端控制。"; player.Play(); paused = false; return "好，继续播放。"; }
            if (Regex.IsMatch(t, @"^(?:(?:请|杏菜|帮我|麻烦|可以|能不能|能|可不可以|给我|你)[，,\s]*)*(停止播放|停止放歌|停止音乐|关闭音乐|别放了|停止歌曲)[吧呀。！!？?\s]*$"))
            { CancelPending(); if (current == null) return "已取消选歌。QQ 音乐的播放请在客户端停止。"; player.Stop(); player.Close(); current = null; paused = false; return "好，音乐停止了。"; }
            int selected; string choiceError;
            if (TryChoice(t, out selected, out choiceError))
            {
                if (choiceError != null) return choiceError;
                if (choosingQq) return await PlayQq(qqCandidates[selected-1], token);
                return Play(candidates[selected-1]);
            }
            bool explicitLocal = Regex.IsMatch(t, @"^(?:(?:杏菜|请|帮我|麻烦|可以|能不能|能|可不可以|你|给我|想|我想)[，,\s]*)*本地(?:播放|放|听)");
            if (explicitLocal) t = new Regex(@"本地(?=播放|放|听)").Replace(t, "", 1);
            if (Regex.IsMatch(t, @"(?:听你|听我|听故事|听一听你|听听你|听你说|听你讲)")) return NotMusic();
            Match m = Regex.Match(t, @"^(?:(?:杏菜|请|帮我|麻烦|可以|能不能|能|可不可以|你|给我|想|我想|我想要|我想听|我想要听|来|一下|一下子|听一下|在QQ音乐里|在QQ音乐|用QQ音乐)[，,\s]*)*(?:播放|放一下|放一首|放首|放歌|放|播一首|播|听一下|听听|听一首|听|来一首|来首)\s*(.+)$", RegexOptions.IgnoreCase);
            if (!m.Success) return NotMusic();
            ClearSelection();
            string query = m.Groups[1].Value.Trim().Trim('\"','\'',' ','。','！','!','？','?');
            query = Regex.Replace(query, @"(?:这首歌|这首|歌曲|这歌)?(?:吧|呀|好吗|可以吗|给我听|吗)?[。！!？?\s]*$", "").Trim().Trim('\"','\'',' ');
            query = Regex.Replace(query, @"^(?:一首|首歌曲|首歌)\s*", "").Trim();
            if (query.StartsWith("本地", StringComparison.Ordinal)) { explicitLocal = true; query = query.Substring(2).Trim(); }
            query = Regex.Replace(query, @"^(?:QQ音乐|QQ 音乐)(?:的|里的)?", "", RegexOptions.IgnoreCase).Trim();
            Match quoted = Regex.Match(query, @"^([^《》「」]{0,30})(?:《([^《》「」]+)》|「([^《》「」]+)」)$");
            if (quoted.Success) query = (quoted.Groups[1].Value + (quoted.Groups[2].Success ? quoted.Groups[2].Value : quoted.Groups[3].Value)).Trim();
            if (query.Length == 0 || query.Length > 100 || Normalize(query).Length < 1) return "想听哪首歌？请告诉我歌名。";
            if (query.Length > 60 || query.IndexOfAny(new char[]{'\r','\n','@','\\','/',':','：','，',',','；',';','《','》','「','」','?','？','!','！'}) >= 0 || Regex.IsMatch(query, @"(?:手机号|手机号码|电话号码|联系方式|身份证|银行卡|信用卡|验证码|密码|密钥|住址|邮箱|api\s*key|顺便|帮我|请你|并且|另外|然后|同时|还有|我(?:的)?(?:账号|地址|资料|手机|电话|邮箱))|(?<!\d)1[3-9]\d{9}(?!\d)", RegexOptions.IgnoreCase)) return "请只发送歌名和歌手，不要在点歌请求里附带其他个人信息。";
            if (!explicitLocal)
            {
                List<QqTrack> found;
                try { found = await qqSearch(query, token); token.ThrowIfCancellationRequested(); }
                catch (Exception) { if (token.IsCancellationRequested) return "已取消音乐搜索。"; return "暂时无法连接 QQ 音乐曲库，请检查网络后重试；也可以说：播放本地 " + query + "。"; }
                if (disposed) return "桌宠已关闭。";
                if (found == null || found.Count == 0) return "QQ 音乐没有找到「" + query + "」。可以换个歌名或说播放本地歌曲。";
                QqTrack exact = null; int exactCount = 0;
                foreach (QqTrack track in found) {
                    string normalized = Normalize(query);
                    string artist = Normalize(track.Artist), title = Normalize(track.Title);
                    if (artist.Length > 0 && title.Length > 0 && (normalized == artist + title || normalized == artist + "的" + title || normalized == title + artist)) { exact = track; exactCount++; }
                }
                if (exactCount == 1) return await PlayQq(exact, token);
                choosingQq = true; awaitingChoice = true;
                foreach (QqTrack track in found) { if (qqCandidates.Count >= 5) break; qqCandidates.Add(track); }
                StringBuilder list = new StringBuilder("找到这些 QQ 音乐歌曲，回复“第一首”“1”或复制对应歌曲信息即可选择：");
                for (int i = 0; i < qqCandidates.Count; i++) list.Append("\n").Append(i+1).Append(". ").Append(QqLabel(qqCandidates[i]));
                return list.ToString();
            }
            Task<List<KeyValuePair<int,string>>> search = Task.Run(delegate { return Find(query, token); });
            if (await Task.WhenAny(search, Task.Delay(5000)) != search) { CancelPending(); return "本地音乐查找超时，请缩小音乐文件夹或更换歌名。"; }
            List<KeyValuePair<int,string>> matches; try { matches = await search; token.ThrowIfCancellationRequested(); } catch (OperationCanceledException) { return "已取消音乐搜索。"; }
            if (disposed) return "桌宠已关闭。";
            if (matches.Count == 0) return "我没有在本地音乐文件夹找到「" + query + "」。可以换个歌名，或把歌曲放进音乐文件夹。";
            int best = matches[0].Key;
            foreach (var entry in matches) if (entry.Key == best && candidates.Count < 5) candidates.Add(entry.Value);
            if (candidates.Count > 1)
            {
                awaitingChoice = true;
                StringBuilder response = new StringBuilder("找到多个可能的版本，回复“第一首”“1”或复制对应歌曲信息即可选择：");
                for (int i = 0; i < candidates.Count; i++) response.Append("\n").Append(i+1).Append(". ").Append(Path.GetFileName(candidates[i]));
                return response.ToString();
            }
            return Play(candidates[0]);
        }
        private async Task<string> PlayQq(QqTrack track, CancellationToken token)
        {
            try { token.ThrowIfCancellationRequested(); await qqDispatch(track, token); lastRequestedQq=track;favoriteMode=false; ClearSelection(); if (current != null) { player.Stop(); player.Close(); current = null; paused = false; }
                if (qqDispatch.Method.DeclaringType == typeof(QqCatalog))
                {
                    for (int attempt=0;attempt<3;attempt++)
                    {
                        await Task.Delay(700,token);
                        Task<string> check=MusicPanel.Command("status");
                        Task done=await Task.WhenAny(check,Task.Delay(6000,token));
                        if(done!=check)break;
                        string status=await check;
                        string[] parts=status.Split(new[]{'\r','\n'},StringSplitOptions.RemoveEmptyEntries);
                        if(parts.Length>=3 && parts[2].Trim().Equals("Playing",StringComparison.OrdinalIgnoreCase) &&
                           QqTitleMatches(parts[0],track.Title))
                            return "QQ 音乐正在播放：" + track.Title + " — " + track.Artist + "。";
                    }
                    return "已向 QQ 音乐发送点播请求，但还没确认歌曲开始播放。请检查客户端登录、播放权限或是否被其他歌曲打断。";
                }
                return "已向 QQ 音乐发送播放请求：" + track.Title + " — " + track.Artist + "。";
            }
            catch (Exception ex) { return "QQ 音乐点播未完成：" + ex.Message; }
        }
        private static bool QqTitleMatches(string playing, string requested) {
            string a=Normalize(playing),b=Normalize(requested);
            return a.Length>0 && b.Length>0 && (a==b || a.Contains(b) || b.Contains(a));
        }
        private string Play(string path)
        {
            try { player.Stop(); player.Close(); player.Open(new Uri(path, UriKind.Absolute)); player.Play(); ClearSelection(); current = path; paused = false; return "正在打开本地歌曲「" + Path.GetFileNameWithoutExtension(path) + "」。"; }
            catch { current = null; paused = false; return "无法打开这首歌曲，请检查文件或选择其他版本。"; }
        }
        internal static string Normalize(string s)
        {
            StringBuilder b = new StringBuilder(); foreach (char c in (s ?? "").ToLowerInvariant()) if (Char.IsLetterOrDigit(c)) b.Append(c); return b.ToString();
        }
        internal static int MatchScore(string file, string query)
        {
            string name = Path.GetFileNameWithoutExtension(file);
            name = Regex.Replace(name, @"^\s*\d{1,3}[.\- _]+", "");
            string full = Normalize(name), q = Normalize(query), artist = "";
            int delimiter = query.LastIndexOf('的');
            if (delimiter > 0 && delimiter + 1 < query.Length) { artist = Normalize(query.Substring(0, delimiter)); q = Normalize(query.Substring(delimiter + 1)); }
            if (q.Length < 1 || !full.Contains(q)) return 0;
            if (artist.Length > 0 && !full.Contains(artist)) return 0;
            string title = Regex.Split(name, @"\s*[-－—]\s*")[0];
            int score = (Normalize(title) == q || full == q) ? 3 : 2;
            if (artist.Length > 0 && full.Contains(artist)) score += 1;
            return score;
        }
        private List<KeyValuePair<int,string>> Find(string query, CancellationToken token)
        {
            var result = new List<KeyValuePair<int,string>>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int[] limits = new int[2];
            foreach (string root in roots) { token.ThrowIfCancellationRequested(); if (!String.IsNullOrWhiteSpace(root) && Directory.Exists(root)) Scan(root, query, result, seen, 0, limits, token); }
            result.Sort(delegate(KeyValuePair<int,string> a, KeyValuePair<int,string> b) { int n = b.Key.CompareTo(a.Key); return n != 0 ? n : StringComparer.OrdinalIgnoreCase.Compare(a.Value,b.Value); });
            return result;
        }
        private static void Scan(string dir, string query, List<KeyValuePair<int,string>> result, HashSet<string> seen, int depth, int[] limits, CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); if (depth > 8 || result.Count > 100 || limits[0]++ >= 1000 || limits[1] >= 10000) return;
            try
            {
                foreach (string f in Directory.EnumerateFiles(dir))
                {
                    token.ThrowIfCancellationRequested();
                    if (limits[1]++ >= 10000) break;
                    string ext = Path.GetExtension(f).ToLowerInvariant();
                    if (ext != ".mp3" && ext != ".wav" && ext != ".wma" && ext != ".m4a") continue;
                    int score = MatchScore(f,query); if (score > 0 && seen.Add(f)) result.Add(new KeyValuePair<int,string>(score,f));
                }
                foreach (string sub in Directory.EnumerateDirectories(dir))
                {
                    token.ThrowIfCancellationRequested(); if (limits[0] >= 1000 || limits[1] >= 10000 || result.Count > 100) break;
                    try { if ((File.GetAttributes(sub) & FileAttributes.ReparsePoint) == 0) Scan(sub,query,result,seen,depth+1,limits,token); } catch (UnauthorizedAccessException) {} catch (IOException) {}
                }
            }
            catch (UnauthorizedAccessException) {} catch (IOException) {} catch (System.Security.SecurityException) {}
        }
    }
}
