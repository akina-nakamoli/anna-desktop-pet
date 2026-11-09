using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace AnnaDesktopPet
{
    // The archive is complete. Prompt budgeting is the caller's responsibility and must not trim this list.
    internal sealed class ConversationHistory : List<ChatMessage>
    {
        private const int MaxBytes = 64 * 1024 * 1024;
        private readonly string filePath;
        private bool blockedByCorruption;
        private bool preserveDamagedCurrent;
        private readonly object gate = new object();
        public string Summary = "";
        public int SummarizedCount;
        public int MemoryProcessedCount;
        public string SessionId = Guid.NewGuid().ToString("N");
        internal string StorageNotice { get; private set; }
        internal string FilePath { get { return filePath; } }
        internal string ArchiveDirectory
        {
            get
            {
                if (filePath == null) return null;
                string stem = Path.GetFileNameWithoutExtension(filePath);
                string name = stem.StartsWith("chat-", StringComparison.Ordinal) ? "chats-" + stem.Substring(5) : stem + ".archives";
                return Path.Combine(Path.GetDirectoryName(filePath), name);
            }
        }

        internal ConversationHistory() { StorageNotice = ""; }
        private ConversationHistory(string path) : this() { filePath = Path.GetFullPath(path); }

        internal static ConversationHistory LoadDefault()
        {
            if (Program.Smoke) return new ConversationHistory();
#if DEEPSEEK
            const string name = "chat-deepseek.bin";
#else
            const string name = "chat-gpt.bin";
#endif
            return Load(Path.Combine(Program.DataDir, name));
        }

        // Explicit paths support isolated tests; unlike LoadDefault they are not disabled by Smoke.
        internal static ConversationHistory Load(string path)
        {
            ConversationHistory history = new ConversationHistory(path);
            try
            {
                EnsureOrdinaryPath(history.filePath);
                if (File.Exists(history.filePath))
                {
                    try { history.Restore(ReadSnapshot(history.filePath)); return history; }
                    catch (Exception) { history.preserveDamagedCurrent = true; }
                }
                if (File.Exists(history.filePath + ".bak"))
                {
                    try
                    {
                        history.Restore(ReadSnapshot(history.filePath + ".bak"));
                        history.StorageNotice = "最新聊天记录未能读取，已恢复上一次成功保存的备份；损坏文件会保留。";
                        return history;
                    }
                    catch (Exception) { history.blockedByCorruption = true; }
                }
                if (history.preserveDamagedCurrent) history.blockedByCorruption = true;
                if (history.blockedByCorruption)
                    history.StorageNotice = "聊天记录未能解密或已损坏，原文件已保留；当前对话暂不能保存。请先备份记录，或使用清空聊天记录后重试。";
            }
            catch (Exception)
            {
                history.blockedByCorruption = true;
                history.StorageNotice = "聊天记录暂时无法读取，原文件未修改；当前对话暂不能保存。";
            }
            return history;
        }

        internal bool Save()
        {
            lock (gate)
            {
                if (filePath == null) return true;
                if (blockedByCorruption) return false;
                try
                {
                    Snapshot snapshot = Capture();
                    byte[] encrypted = Encode(snapshot);
                    EnsureOrdinaryPath(filePath);
                    Directory.CreateDirectory(Path.GetDirectoryName(filePath));
                    if (preserveDamagedCurrent && File.Exists(filePath))
                    {
                        string preserved = filePath + ".corrupt-" + Guid.NewGuid().ToString("N") + ".bin";
                        File.Copy(filePath, preserved, false);
                        // Replace must never overwrite a valid recovery backup with corrupt data.
                        AtomicWrite(filePath, encrypted, false);
                        preserveDamagedCurrent = false;
                    }
                    else AtomicWrite(filePath, encrypted, true);
                    StorageNotice = "";
                    return true;
                }
                catch (Exception)
                {
                    StorageNotice = "本次聊天尚未保存到本机，内容仍在窗口中。请检查磁盘空间、文件占用或记录大小后重试。";
                    return false;
                }
            }
        }

        // A failed archive/reset leaves the current in-memory conversation intact.
        internal void StartNew()
        {
            lock (gate)
            {
                if (filePath == null) { Reset(); return; }
                if (!Save()) return;
                try
                {
                    if (Count > 0)
                    {
                        EnsureOrdinaryPath(ArchiveDirectory);
                        Directory.CreateDirectory(ArchiveDirectory);
                        string archive = Path.Combine(ArchiveDirectory, SessionId + ".bin");
                        AtomicWrite(archive, Encode(Capture()), false);
                    }
                    // Prepare the new disk snapshot before mutating live state.
                    Snapshot empty = new Snapshot { Version = 1, SessionId = Guid.NewGuid().ToString("N"),
                        Summary = "", Messages = new List<StoredMessage>() };
                    AtomicWrite(filePath, Encode(empty), true);
                    Restore(empty);
                    StorageNotice = "";
                }
                catch (Exception)
                {
                    StorageNotice = "新对话暂未开始：旧对话未能完整保存或归档，当前内容已保留。";
                }
            }
        }

        // Explicit deletion only targets this provider's active file, backups and owned archive names.
        // It never recursively removes a directory, follows a reparse point, or touches memories/settings.
        internal bool DeleteAll()
        {
            lock (gate)
            {
                if (filePath == null) { Reset(); return true; }
                try
                {
                    List<string> targets = new List<string> { filePath, filePath + ".bak" };
                    string parent = Path.GetDirectoryName(filePath);
                    EnsureOrdinaryPath(parent); EnsureOrdinaryPath(ArchiveDirectory);
                    if (Directory.Exists(parent))
                    {
                        string prefix = Path.GetFileName(filePath) + ".corrupt-";
                        foreach (string file in Directory.GetFiles(parent, prefix + "*.bin"))
                            if (IsOwnedIdFile(Path.GetFileName(file).Substring(prefix.Length))) targets.Add(file);
                        string temporaryPrefix = Path.GetFileName(filePath) + ".tmp-";
                        foreach (string file in Directory.GetFiles(parent, temporaryPrefix + "*"))
                        {
                            Guid id;
                            if (Guid.TryParseExact(Path.GetFileName(file).Substring(temporaryPrefix.Length), "N", out id)) targets.Add(file);
                        }
                    }
                    if (Directory.Exists(ArchiveDirectory))
                        foreach (string file in Directory.GetFiles(ArchiveDirectory))
                        {
                            string name = Path.GetFileName(file); Guid id;
                            if (IsOwnedIdFile(name) || name.Length == 73 && IsOwnedIdFile(name.Substring(0, 36)) &&
                                name.Substring(36, 5) == ".tmp-" && Guid.TryParseExact(name.Substring(41), "N", out id)) targets.Add(file);
                        }
                    // Resolve and check the complete deletion set before removing any file.
                    foreach (string file in targets)
                    {
                        string full = Path.GetFullPath(file);
                        if (!String.Equals(Path.GetDirectoryName(full), parent, StringComparison.OrdinalIgnoreCase) &&
                            !String.Equals(Path.GetDirectoryName(full), ArchiveDirectory, StringComparison.OrdinalIgnoreCase))
                            throw new IOException("Unexpected conversation path.");
                        EnsureOrdinaryPath(full);
                    }
                    foreach (string file in targets) if (File.Exists(file)) File.Delete(file);
                    blockedByCorruption = preserveDamagedCurrent = false;
                    Reset(); StorageNotice = "";
                    return true;
                }
                catch (Exception)
                {
                    StorageNotice = "部分聊天记录未能删除，请检查文件占用后重试；长期记忆未受影响。";
                    return false;
                }
            }
        }

        private void Reset()
        {
            Clear(); Summary = ""; SummarizedCount = MemoryProcessedCount = 0;
            SessionId = Guid.NewGuid().ToString("N");
        }

        private static bool IsOwnedIdFile(string name)
        {
            Guid value;
            return name.Length == 36 && name.EndsWith(".bin", StringComparison.OrdinalIgnoreCase) &&
                Guid.TryParseExact(name.Substring(0, 32), "N", out value);
        }

        private Snapshot Capture()
        {
            Snapshot data = new Snapshot { Version = 1, SessionId = SessionId, Summary = Summary,
                SummarizedCount = SummarizedCount, MemoryProcessedCount = MemoryProcessedCount,
                Messages = new List<StoredMessage>() };
            foreach (ChatMessage message in this)
            {
                if (message == null) throw new InvalidDataException();
                data.Messages.Add(new StoredMessage { Role = message.Role, Content = message.Content,
                    CreatedUtc = message.CreatedUtc.HasValue ? (DateTime?)message.CreatedUtc.Value.ToUniversalTime() : null });
            }
            Validate(data); return data;
        }

        private void Restore(Snapshot data)
        {
            Validate(data); Clear();
            foreach (StoredMessage message in data.Messages)
                Add(new ChatMessage(message.Role, message.Content) { CreatedUtc = message.CreatedUtc });
            Summary = data.Summary; SessionId = data.SessionId;
            SummarizedCount = data.SummarizedCount; MemoryProcessedCount = data.MemoryProcessedCount;
        }

        private static void Validate(Snapshot data)
        {
            Guid id;
            if (data == null || data.Version != 1 || data.Messages == null || data.Summary == null ||
                data.Summary.Length > 6000 || !Guid.TryParseExact(data.SessionId, "N", out id) ||
                data.SummarizedCount < 0 || data.SummarizedCount > data.Messages.Count ||
                data.MemoryProcessedCount < 0 || data.MemoryProcessedCount > data.Messages.Count ||
                data.SummarizedCount > 0 && String.IsNullOrWhiteSpace(data.Summary)) throw new InvalidDataException();
            foreach (StoredMessage message in data.Messages)
                if (message == null || message.Content == null || message.Content.Length > 1024 * 1024 ||
                    Array.IndexOf(new[] { "user", "assistant", "local_user", "local_assistant" }, message.Role) < 0)
                    throw new InvalidDataException();
        }

        private static JavaScriptSerializer Serializer() { return new JavaScriptSerializer { MaxJsonLength = MaxBytes, RecursionLimit = 20 }; }
        private static byte[] Encode(Snapshot data)
        {
            Validate(data);
            byte[] plain = Encoding.UTF8.GetBytes(Serializer().Serialize(data));
            if (plain.Length > MaxBytes - 4096) throw new InvalidDataException();
            return ProtectedData.Protect(plain, null, DataProtectionScope.CurrentUser);
        }

        private static Snapshot ReadSnapshot(string path)
        {
            EnsureOrdinaryPath(path);
            if (new FileInfo(path).Length > MaxBytes) throw new InvalidDataException();
            byte[] encrypted = File.ReadAllBytes(path);
            byte[] plain = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
            Snapshot data = Serializer().Deserialize<Snapshot>(Encoding.UTF8.GetString(plain));
            Validate(data); return data;
        }

        private static void AtomicWrite(string destination, byte[] bytes, bool backup)
        {
            EnsureOrdinaryPath(destination); if (backup) EnsureOrdinaryPath(destination + ".bak");
            string temp = destination + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                using (FileStream output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                { output.Write(bytes, 0, bytes.Length); output.Flush(true); }
                if (File.Exists(destination)) File.Replace(temp, destination, backup ? destination + ".bak" : null);
                else File.Move(temp, destination);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }

        private static void EnsureOrdinaryPath(string path)
        {
            string cursor = Path.GetFullPath(path);
            while (!String.IsNullOrEmpty(cursor))
            {
                if ((File.Exists(cursor) || Directory.Exists(cursor)) &&
                    (File.GetAttributes(cursor) & FileAttributes.ReparsePoint) != 0) throw new IOException("Reparse path rejected.");
                cursor = Path.GetDirectoryName(cursor);
            }
        }

        internal sealed class Snapshot
        {
            public int Version;
            public string SessionId;
            public string Summary;
            public int SummarizedCount;
            public int MemoryProcessedCount;
            public List<StoredMessage> Messages;
        }
        internal sealed class StoredMessage { public string Role; public string Content; public DateTime? CreatedUtc; }
    }
}
