using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Threading;
using System.Security.Cryptography;
using System.Web.Script.Serialization;
using System.Runtime.InteropServices;

namespace AnnaDesktopPet
{
    internal sealed class QqTrack
    {
        internal string Id, Title, Artist, Album;
        internal int Type;
    }

    // Uses public catalog metadata only. QQ Music enforces its own account and playback rights.
    internal static class QqCatalog
    {
        internal static readonly object DispatchGate = new object();
        private delegate bool WindowVisitor(IntPtr window, IntPtr parameter);
        [DllImport("user32.dll")] private static extern bool EnumWindows(WindowVisitor visitor, IntPtr parameter);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
        [DllImport("user32.dll")] private static extern bool ShowWindowAsync(IntPtr window, int command);
        private static bool BackgroundWindows()
        {
            var ids = new HashSet<uint>();
            foreach (Process p in Process.GetProcessesByName("QQMusic")) using (p) { ids.Add((uint)p.Id); }
            bool found = false;
            EnumWindows(delegate(IntPtr window, IntPtr parameter) {
                uint id; GetWindowThreadProcessId(window, out id);
                if (ids.Contains(id)) { found = true; ShowWindowAsync(window, 0); }
                return true;
            }, IntPtr.Zero);
            return found;
        }
        private static async Task SettleBackground(CancellationToken cancellation)
        {
            for (int i = 0; i < 20; i++) { cancellation.ThrowIfCancellationRequested(); BackgroundWindows(); await Task.Delay(100, cancellation); }
        }
        internal static HttpWebRequest CreateRequest(string query)
        {
            string url = "https://c.y.qq.com/soso/fcgi-bin/client_search_cp?format=json&p=1&n=20&w=" + Uri.EscapeDataString(query);
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
            request.Timeout = 8000; request.ReadWriteTimeout = 8000; request.AllowAutoRedirect = false; request.Referer = "https://y.qq.com/"; request.UserAgent = "AnnaDesktopPet/1.19";
            return request;
        }
        internal static Task<List<QqTrack>> Search(string query, CancellationToken cancellation)
        {
            return Task.Run(delegate {
                HttpWebRequest request = CreateRequest(query);
                using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation))
                {
                    deadline.CancelAfter(12000);
                    try {
                        using (deadline.Token.Register(delegate { request.Abort(); })) using (var response = (HttpWebResponse)request.GetResponse()) using (var stream = response.GetResponseStream()) using (var reader = new StreamReader(stream, Encoding.UTF8))
                        {
                            if (response.StatusCode != HttpStatusCode.OK) throw new InvalidDataException("QQ 曲库响应异常。");
                            char[] buffer = new char[4096]; StringBuilder text = new StringBuilder(); int count;
                            while ((count = reader.Read(buffer, 0, buffer.Length)) > 0) { deadline.Token.ThrowIfCancellationRequested(); text.Append(buffer, 0, count); if (text.Length > 2000000) throw new InvalidDataException("曲库响应过大。"); }
                            return Parse(text.ToString());
                        }
                    } catch (Exception) { if (cancellation.IsCancellationRequested) throw new OperationCanceledException(cancellation); if (deadline.IsCancellationRequested) throw new TimeoutException("QQ 音乐搜索超时。"); throw; }
                }
            });
        }
        internal static List<QqTrack> Parse(string json)
        {
            var serializer = new JavaScriptSerializer { MaxJsonLength = 2000000 };
            var root = serializer.DeserializeObject(json) as Dictionary<string,object>;
            if (root == null || !root.ContainsKey("code") || Convert.ToInt32(root["code"]) != 0) throw new InvalidDataException("QQ 曲库返回错误。");
            var data = root["data"] as Dictionary<string,object>; var song = data == null ? null : data["song"] as Dictionary<string,object>;
            var rows = song == null ? null : song["list"] as object[]; if (rows == null) throw new InvalidDataException("QQ 曲库格式已变化。");
            var result = new List<QqTrack>(); var seen = new HashSet<string>();
            foreach (object entry in rows)
            {
                var row = entry as Dictionary<string,object>; if (row == null) continue;
                try {
                    string id = Convert.ToString(row["songid"]); long number;
                    if (!Regex.IsMatch(id, @"^[1-9][0-9]{0,9}$") || !Int64.TryParse(id, out number) || number > 4294967295L || Convert.ToInt32(row["type"]) != 0 || !seen.Add(id)) continue;
                    string title = Convert.ToString(row["songname"]), album = Convert.ToString(row["albumname"]);
                    var singers = row["singer"] as object[]; var names = new List<string>();
                    if (singers != null) foreach (object singer in singers) { var s = singer as Dictionary<string,object>; if (s != null && s.ContainsKey("name")) names.Add(Convert.ToString(s["name"])); }
                    if (String.IsNullOrWhiteSpace(title) || names.Count == 0) continue;
                    result.Add(new QqTrack { Id = id, Title = title, Artist = String.Join(" / ", names.ToArray()), Album = album, Type = 0 });
                } catch (Exception e) { if (!(e is KeyNotFoundException || e is FormatException || e is InvalidCastException || e is OverflowException)) throw; }
            }
            return result;
        }
        internal static async Task Open(CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            bool running = false;
            foreach (Process p in Process.GetProcessesByName("QQMusic")) using (p) { running = true; }
            if (running) { BackgroundWindows(); return; }
            string path = ResolvePath();
            lock (DispatchGate) {
                cancellation.ThrowIfCancellationRequested();
                using (Process child = Process.Start(new ProcessStartInfo(path) { WorkingDirectory = Path.GetDirectoryName(path), UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden })) { }
            }
            for (int i = 0; i < 60; i++) {
                await Task.Delay(500, cancellation);
                if (BackgroundWindows()) { await SettleBackground(cancellation); return; }
            }
            throw new TimeoutException("已启动 QQ 音乐，但等待窗口超时；请完成登录后重试点歌。");
        }
        internal static async Task Dispatch(QqTrack track, CancellationToken cancellation)
        {
            await Open(cancellation);
            await Task.Run(delegate {
                long id;
                if (track == null || track.Type != 0 || !Regex.IsMatch(track.Id ?? "", @"^[1-9][0-9]{0,9}$") || !Int64.TryParse(track.Id, out id) || id > 4294967295L) throw new InvalidOperationException("歌曲编号无效。");
                string path = null; int matches = 0;
                foreach (Process p in Process.GetProcessesByName("QQMusic")) using (p) {
                    try { string candidate = p.MainModule.FileName; if (String.Equals(Path.GetFileName(candidate), "QQMusic.exe", StringComparison.OrdinalIgnoreCase)) { matches++; path = candidate; } } catch (Exception e) { if (!(e is System.ComponentModel.Win32Exception || e is InvalidOperationException)) throw; }
                }
                if (matches != 1 || !File.Exists(path)) throw new InvalidOperationException("请先打开 QQ 音乐桌面客户端，确保只运行一个实例。");
                if (!String.Equals(Path.GetFileName(path), "QQMusic.exe", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("只允许向 QQ 音乐程序发送点播请求。");
                string arg = "/playbysongid \"cmd_count==1&&id_0==" + track.Id + "&&songtype_0==0\"";
                var start = new ProcessStartInfo(path, arg) { WorkingDirectory = Path.GetDirectoryName(path), UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
                Process launched;
                lock (DispatchGate) { cancellation.ThrowIfCancellationRequested(); launched = Process.Start(start); }
                using (Process child = launched) {
                    if (child == null || !child.WaitForExit(5000)) throw new TimeoutException("请求已发送，但客户端响应超时，请查看 QQ 音乐后再决定是否重试。");
                    if (child.ExitCode != 0) throw new InvalidOperationException("QQ 音乐未接受点播请求，请在客户端检查歌曲。");
                }
            });
            await SettleBackground(cancellation);
        }
        private static string ResolvePath()
        {
            string config=Path.Combine(Program.DataDir,"qqmusic-path.txt");
            string path=File.Exists(config)?File.ReadAllText(config).Trim():"";
            if(String.IsNullOrWhiteSpace(path) || !File.Exists(path) ||
               !String.Equals(Path.GetFileName(path),"QQMusic.exe",StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("未配置 QQ 音乐程序。请先启动 QQ 音乐，或在设置目录的 qqmusic-path.txt 中填写 QQMusic.exe 的完整路径。");
            return Path.GetFullPath(path);
        }
    }
}
