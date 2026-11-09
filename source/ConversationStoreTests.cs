using System;
using System.IO;
using System.Text;
using System.Reflection;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Web.Script.Serialization;

namespace AnnaDesktopPet
{
#if CONVERSATION_STORE_TEST
    // Standalone storage tests must never load the application's settings or real data directory.
    internal static class Program
    {
        internal static bool Smoke;
        internal static string TestDataDir;
        internal static string DataDir { get { if (TestDataDir == null) throw new InvalidOperationException("Real data directory accessed by isolated test."); return TestDataDir; } }
    }
    internal sealed class ChatMessage
    {
        public string Role, Content;
        public DateTime? CreatedUtc;
        public ChatMessage(string role, string content) { Role = role; Content = content; CreatedUtc = DateTime.UtcNow; }
    }
#endif
    internal static class ConversationStoreTests
    {
        private static int checks;
        private static Type storeType;
        private static string root;
        private static string fixtureParent;
        private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        [STAThread]
        public static int Main()
        {
            Program.Smoke = true;
            // Codex/AppContainer TEMP can itself exceed 120 characters. Archive atomic writes
            // append two GUIDs; keep isolated fixtures beside this freshly compiled test exe
            // so the framework's MAX_PATH limit does not masquerade as a storage regression.
            fixtureParent = Path.GetFullPath(AppDomain.CurrentDomain.BaseDirectory);
            root = Path.Combine(fixtureParent, "AnnaConversationTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                storeType = typeof(ChatMessage).Assembly.GetType("AnnaDesktopPet.ConversationHistory");
                Check(storeType != null, "conversation persistence module exists");
                RoundTrip(); MessageTimes(); NewSession(); DeleteScope(); CorruptProtection(); BackupRecovery(); InvalidMetadata(); SaveFailure(); SmokeIsolation(); ProviderDefault();
                Console.WriteLine("PASS conversation store: " + checks + " assertions");
                return 0;
            }
            catch (Exception ex) { Console.Error.WriteLine("FAIL " + ex.Message); return 1; }
            finally
            {
                // Test-only directory has a fixed generated name and is resolved before deletion.
                string resolved = Path.GetFullPath(root);
                if (String.Equals(Path.GetDirectoryName(resolved).TrimEnd(Path.DirectorySeparatorChar), fixtureParent.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase) &&
                    Path.GetFileName(resolved).StartsWith("AnnaConversationTests-", StringComparison.Ordinal))
                    Directory.Delete(resolved, true);
            }
        }

        private static void Check(bool ok, string name) { checks++; if (!ok) throw new InvalidOperationException(name); }
        private static object Load(string file) { return storeType.GetMethod("Load", Members).Invoke(null, new object[] { file }); }
        private static object Call(object store, string method) { return storeType.GetMethod(method, Members).Invoke(store, null); }
        private static void Set(object store, string field, object value) { storeType.GetField(field, Members).SetValue(store, value); }
        private static T Field<T>(object store, string field) { return (T)storeType.GetField(field, Members).GetValue(store); }
        private static string Notice(object store) { return (string)storeType.GetProperty("StorageNotice", Members).GetValue(store, null); }
        private static IList<ChatMessage> Rows(object store) { return (IList<ChatMessage>)store; }
        private static string FileFor(string name) { string dir = Path.Combine(root, name); Directory.CreateDirectory(dir); return Path.Combine(dir, "chat-gpt.bin"); }

        private static void RoundTrip()
        {
            string path = FileFor("roundtrip"); object store = Load(path);
            for (int i = 0; i < 64; i++) Rows(store).Add(new ChatMessage(i % 2 == 0 ? "user" : "assistant", "完整消息-" + i));
            Rows(store).Add(new ChatMessage("local_user", "你好 杏菜"));
            Rows(store).Add(new ChatMessage("local_assistant", "こんにちは。播放尚未确认"));
            Set(store, "Summary", "用户提到朋友小林正在准备考试，尚未说明结果。");
            Set(store, "SummarizedCount", 40); Set(store, "MemoryProcessedCount", 62);
            Check((bool)Call(store, "Save"), "save complete transcript");
            string encrypted = Encoding.UTF8.GetString(File.ReadAllBytes(path));
            Check(!encrypted.Contains("完整消息") && !encrypted.Contains("考试"), "saved file contains no clear text");
            object restart = Load(path);
            Check(Rows(restart).Count == 66 && Rows(restart)[0].Content == "完整消息-0", "restart retains older than 20 entries");
            Check(Rows(restart)[65].Role == "local_assistant" && Rows(restart)[65].Content.Contains("こんにちは"), "restart preserves Unicode and actual tool role");
            Check(Field<string>(restart, "Summary").Contains("小林") && Field<int>(restart, "SummarizedCount") == 40 && Field<int>(restart, "MemoryProcessedCount") == 62, "restart retains context metadata");
            Check(Field<string>(restart, "SessionId") == Field<string>(store, "SessionId"), "restart same session");
        }

        private static void NewSession()
        {
            string path = FileFor("new"); object store = Load(path);
            Rows(store).Add(new ChatMessage("user", "旧对话完整保留")); Set(store, "Summary", "旧摘要"); Set(store, "SummarizedCount", 1); Set(store, "MemoryProcessedCount", 1);
            string oldId = Field<string>(store, "SessionId"); Call(store, "StartNew");
            string[] archives = Directory.GetFiles(Path.Combine(Path.GetDirectoryName(path), "chats-gpt"), "*.bin");
            Check(archives.Length == 1, "new conversation archives prior session; count=" + archives.Length + "; notice=" + Notice(store));
            Check(Rows(Load(archives[0])).Count == 1, "archive retains all messages");
            Check(Rows(store).Count == 0 && Field<string>(store, "Summary") == "" && Field<int>(store, "SummarizedCount") == 0 && Field<int>(store, "MemoryProcessedCount") == 0, "new conversation clears prior context");
            Check(Field<string>(store, "SessionId") != oldId && Rows(Load(path)).Count == 0, "new session committed to disk");
            Call(store, "StartNew");
            Check(Directory.GetFiles(Path.Combine(Path.GetDirectoryName(path), "chats-gpt"), "*.bin").Length == 1, "empty new session does not create empty archives");
        }

        private static void MessageTimes()
        {
            var timestamp = typeof(ChatMessage).GetField("CreatedUtc", Members);
            Check(timestamp != null && timestamp.FieldType == typeof(DateTime?), "messages record nullable original UTC time");
            var fresh = new ChatMessage("user", "明天面试");
            var created = (DateTime?)timestamp.GetValue(fresh);
            Check(created.HasValue && created.Value.Kind == DateTimeKind.Utc && Math.Abs((DateTime.UtcNow - created.Value).TotalSeconds) < 5, "new messages timestamp actual creation");
            DateTime original = new DateTime(2026, 11, 8, 15, 30, 0, 123, DateTimeKind.Utc);
            timestamp.SetValue(fresh, (DateTime?)original);
            string path = FileFor("times"); object store = Load(path); Rows(store).Add(fresh); Call(store, "Save");
            var recovered = (DateTime?)timestamp.GetValue(Rows(Load(path))[0]);
            Check(recovered.HasValue && recovered.Value == original && recovered.Value.Kind == DateTimeKind.Utc, "restart preserves original event anchor instead of assigning restart time");
            var legacy = new { Version = 1, SessionId = Guid.NewGuid().ToString("N"), Summary = "", SummarizedCount = 0,
                MemoryProcessedCount = 0, Messages = new[] { new { Role = "user", Content = "明天面试" } } };
            File.WriteAllBytes(path, ProtectedData.Protect(Encoding.UTF8.GetBytes(new JavaScriptSerializer().Serialize(legacy)), null, DataProtectionScope.CurrentUser));
            object older = Load(path);
            Check(Rows(older).Count == 1 && timestamp.GetValue(Rows(older)[0]) == null, "legacy messages keep unknown dates instead of guessing today");
            Call(older, "Save"); Check(timestamp.GetValue(Rows(Load(path))[0]) == null, "unknown legacy date remains unknown after resave");
        }

        private static void DeleteScope()
        {
            string path = FileFor("delete"); object store = Load(path);
            Rows(store).Add(new ChatMessage("user", "delete me")); Call(store, "StartNew"); Rows(store).Add(new ChatMessage("assistant", "current")); Call(store, "Save");
            string dir = Path.GetDirectoryName(path); string sibling = Path.Combine(dir, "chat-deepseek.bin"); File.WriteAllText(sibling, "other provider");
            string memory = Path.Combine(dir, "memories.bin"); File.WriteAllText(memory, "keep");
            string unrelated = Path.Combine(dir, "chats-gpt", "unrelated.txt"); File.WriteAllText(unrelated, "keep");
            string unrelatedBinary = Path.Combine(dir, "chats-gpt", "unrelated.bin"); File.WriteAllText(unrelatedBinary, "keep");
            string tempCurrent = path + ".tmp-" + Guid.NewGuid().ToString("N"); File.WriteAllText(tempCurrent, "interrupted write");
            string tempArchive = Path.Combine(dir, "chats-gpt", Guid.NewGuid().ToString("N") + ".bin.tmp-" + Guid.NewGuid().ToString("N")); File.WriteAllText(tempArchive, "interrupted archive");
            Check((bool)Call(store, "DeleteAll"), "explicit clear succeeds");
            Check(!File.Exists(path) && Directory.GetFiles(Path.Combine(dir, "chats-gpt"), "*.bin").Length == 1 && Rows(store).Count == 0, "clear removes active and owned archives");
            Check(File.Exists(sibling) && File.Exists(memory) && File.Exists(unrelated) && File.Exists(unrelatedBinary), "clear does not remove provider sibling memory or unrelated files");
            Check(!File.Exists(tempCurrent) && !File.Exists(tempArchive), "clear removes owned interrupted encrypted snapshots");
        }

        private static void CorruptProtection()
        {
            string path = FileFor("corrupt"); byte[] corrupt = Encoding.UTF8.GetBytes("not encrypted original data"); File.WriteAllBytes(path, corrupt);
            object store = Load(path); Rows(store).Add(new ChatMessage("user", "new message"));
            Check(Notice(store).Length > 0 && !(bool)Call(store, "Save"), "corruption blocks silent overwrite with notice");
            Check(Convert.ToBase64String(File.ReadAllBytes(path)) == Convert.ToBase64String(corrupt), "corrupt original remains untouched");
            Call(store, "StartNew"); Check(Rows(store).Count == 1, "new conversation does not discard unsaved messages");
            Check((bool)Call(store, "DeleteAll"), "explicit delete unblocks corrupt state");
            Rows(store).Add(new ChatMessage("user", "fresh")); Check((bool)Call(store, "Save"), "save works after explicit reset");
        }

        private static void BackupRecovery()
        {
            string path = FileFor("backup"); object store = Load(path);
            Rows(store).Add(new ChatMessage("user", "first")); Call(store, "Save"); Rows(store).Add(new ChatMessage("assistant", "second")); Call(store, "Save");
            Check(File.Exists(path + ".bak"), "atomic replacement preserves prior snapshot");
            File.WriteAllText(path, "broken latest"); object restart = Load(path);
            Check(Rows(restart).Count == 1 && Rows(restart)[0].Content == "first" && Notice(restart).Length > 0, "recover last valid snapshot with notice");
            Rows(restart).Add(new ChatMessage("assistant", "recovered")); Check((bool)Call(restart, "Save"), "recovery can continue saving");
            Check(Directory.GetFiles(Path.GetDirectoryName(path), "chat-gpt.bin.corrupt-*.bin").Length == 1, "damaged latest snapshot preserved for recovery");
            Check(Rows(Load(path)).Count == 2, "recovered current survives next restart");
        }

        private static void InvalidMetadata()
        {
            string path = FileFor("invalid"); object store = Load(path); Rows(store).Add(new ChatMessage("user", "one"));
            Set(store, "SummarizedCount", 4); Check(!(bool)Call(store, "Save") && !File.Exists(path), "invalid summary cursor rejected instead of losing context");
            Set(store, "SummarizedCount", 0); Set(store, "MemoryProcessedCount", -1); Check(!(bool)Call(store, "Save"), "negative extraction cursor rejected");
            Set(store, "MemoryProcessedCount", 0); Set(store, "Summary", new string('a', 6001)); Check(!(bool)Call(store, "Save"), "oversized summary is not silently truncated");
            var malformed = new { Version = 1, SessionId = Guid.NewGuid().ToString("N"), Summary = "摘要", SummarizedCount = 20,
                MemoryProcessedCount = 0, Messages = new[] { new { Role = "user", Content = "不能丢弃" } } };
            byte[] bytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(new JavaScriptSerializer().Serialize(malformed)), null, DataProtectionScope.CurrentUser);
            File.WriteAllBytes(path, bytes); object loaded = Load(path);
            Check(Notice(loaded).Length > 0 && !(bool)Call(loaded, "Save"), "invalid persisted cursor cannot skip unsummarized history");
            Check(Convert.ToBase64String(File.ReadAllBytes(path)) == Convert.ToBase64String(bytes), "invalid schema kept for recovery");
        }

        private static void SaveFailure()
        {
            string path = FileFor("locked"); object store = Load(path); Rows(store).Add(new ChatMessage("user", "keep this")); Call(store, "Save");
            using (FileStream locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                Rows(store).Add(new ChatMessage("assistant", "pending"));
                Check(!(bool)Call(store, "Save") && Notice(store).Length > 0, "file lock save failure surfaced");
                string id = Field<string>(store, "SessionId"); Call(store, "StartNew");
                Check(Rows(store).Count == 2 && Field<string>(store, "SessionId") == id, "failed archive does not clear current conversation");
            }
            Check(Rows(Load(path)).Count == 1 && Directory.GetFiles(Path.GetDirectoryName(path), "*.tmp-*").Length == 0, "failed atomic save preserves prior file and removes own temp");
            Check((bool)Call(store, "Save") && Rows(Load(path)).Count == 2, "save resumes when lock released");
        }

        private static void SmokeIsolation()
        {
            object store = storeType.GetMethod("LoadDefault", Members).Invoke(null, null);
            Check(Rows(store).Count == 0, "smoke load ignores real persisted user history");
            Rows(store).Add(new ChatMessage("user", "smoke")); Check((bool)Call(store, "Save"), "smoke save is harmless success");
            Check(String.IsNullOrEmpty((string)storeType.GetProperty("FilePath", Members).GetValue(store, null)), "smoke store has no disk target");
        }

        private static void ProviderDefault()
        {
#if CONVERSATION_STORE_TEST
            Program.TestDataDir = Path.Combine(root, "provider"); Program.Smoke = false;
            try
            {
                object store = storeType.GetMethod("LoadDefault", Members).Invoke(null, null);
#if DEEPSEEK
                const string expected = "chat-deepseek.bin", other = "chat-gpt.bin";
#else
                const string expected = "chat-gpt.bin", other = "chat-deepseek.bin";
#endif
                Check(Path.GetFileName((string)storeType.GetProperty("FilePath", Members).GetValue(store, null)) == expected, "default filename separates providers");
                Rows(store).Add(new ChatMessage("user", "isolated provider")); Check((bool)Call(store, "Save"), "provider-specific file saved");
                Check(File.Exists(Path.Combine(Program.TestDataDir, expected)) && !File.Exists(Path.Combine(Program.TestDataDir, other)), "default store never writes other provider path");
            }
            finally { Program.Smoke = true; Program.TestDataDir = null; }
#endif
        }
    }
}
