using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using System.Security.Cryptography;
using System.Web.Script.Serialization;

namespace AnnaDesktopPet {
    // The model proposes data; only validated, user-grounded proposals reach persistent memory.
    internal sealed class SemanticMemory {
        internal string Key, Text, Kind, Certainty, Evidence, Replaces;
        internal DateTime? EvidenceUtc;
        internal static List<SemanticMemory> Parse(string json, IList<ChatMessage> evidence) {
            if(String.IsNullOrWhiteSpace(json)||json.Length>18000)throw new FormatException("Invalid memory batch.");
            var parser=new JavaScriptSerializer { MaxJsonLength=20000, RecursionLimit=8 };
            var root=parser.DeserializeObject(json) as Dictionary<string,object>;
            if(root==null||root.Count!=1||!root.ContainsKey("memories"))throw new FormatException("Invalid memory batch.");
            var rows=root["memories"] as object[];
            if(rows==null||rows.Length>8)throw new FormatException("Invalid memory batch.");
            var result=new List<SemanticMemory>();
            var keys=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach(object row in rows) {
                var fields=row as Dictionary<string,object>;
                if(fields==null||fields.Count!=6)throw new FormatException("Invalid memory fields.");
                foreach(string field in new[]{"key","text","kind","certainty","evidence","replaces"})
                    if(!fields.ContainsKey(field)||!(fields[field] is string))throw new FormatException("Invalid memory field type.");
                var candidate=new SemanticMemory {Key=((string)fields["key"]).Trim(),Text=((string)fields["text"]).Trim(),
                    Kind=(string)fields["kind"],Certainty=(string)fields["certainty"],Evidence=((string)fields["evidence"]).Trim(),Replaces=((string)fields["replaces"]).Trim()};
                if(!Regex.IsMatch(candidate.Key,@"^[\p{L}\p{N}:_. -]{1,80}$")||!keys.Add(candidate.Key)||candidate.Text.Length<2||candidate.Text.Length>200||candidate.Evidence.Length<2||candidate.Evidence.Length>300||candidate.Replaces.Length>220||
                    Array.IndexOf(new[]{"preference","event","communication"},candidate.Kind)<0||Array.IndexOf(new[]{"explicit","tentative"},candidate.Certainty)<0)
                    throw new FormatException("Invalid memory values.");
                if(IsSensitive(candidate.Key+" "+candidate.Text+" "+candidate.Evidence)||IsUnsafeInference(candidate))continue;
                bool grounded=false;
                if(evidence!=null)foreach(ChatMessage message in evidence) {
                    if(message==null||message.Role!="user"||String.IsNullOrEmpty(message.Content)||message.Content.IndexOf(candidate.Evidence,StringComparison.Ordinal)<0)continue;
                    if(IsSensitive(message.Content)||IsFictionalEvidence(message.Content)||Regex.IsMatch(message.Content,@"(?:他说|她说|角色说|引用|假设|假如|如果我|帮我写|替我写|翻译|小说里|故事里|举例|示例|台词)"))continue;
                    grounded=true;candidate.EvidenceUtc=message.CreatedUtc;
                }
                if(grounded) {
                    // A repeated action can support a hypothesis, but is not an explicit statement
                    // of liking. Do not trust the model's certainty label for that distinction.
                    if(candidate.Kind=="preference"&&candidate.Certainty=="explicit"&&!HasExplicitPreference(candidate.Evidence)) {
                        candidate.Certainty="tentative";
                        if(candidate.Text.IndexOf("可能",StringComparison.Ordinal)<0)
                            candidate.Text=candidate.Text.StartsWith("用户",StringComparison.Ordinal)?"用户可能"+candidate.Text.Substring(2):"可能："+candidate.Text;
                        if(candidate.Text.Length>200)candidate.Text=candidate.Text.Substring(0,200);
                    }
                    if(IsUnsafeInference(candidate))continue;
                    result.Add(candidate);
                }
            }
            return result;
        }
        internal static bool IsFictionalEvidence(string text) {
            // Conservative local guard for explicit story/dream markers, even if the model
            // labels the proposal explicit. Interest in reading/writing fiction is still real.
            return Regex.IsMatch(text??"",@"(?:(?<!退出)(?<!结束)(?<!停止)(?<!取消)(?:角色扮演|角色设定|人设)\s*[：:]|(?:在|当前|这段|我们的)(?:原创)?角色扮演[里中]|剧情[里中]|梦[里中]|(?:我|昨晚|昨天)梦见|我扮演|(?:^|\n)\s*role[ -]?play\s*[:：]|in (?:my |a )?dream)",RegexOptions.IgnoreCase);
        }
        internal static bool IsSensitive(string text) {
            return Regex.IsMatch(text??"",@"(?:密码|密钥|口令|银行卡|身份证|手机号|电话号码|家庭住址|详细地址|验证码|私钥|助记词|性取向|宗教信仰|政治立场|种族|诊断|抑郁症|精神病|双相|躁郁|自闭症|焦虑症|password|api[_ -]?key|secret|access[_ -]?token|sk-[A-Za-z0-9_-]{10,}|\b1[3-9]\d{9}\b|\b\d{17}[0-9Xx]\b)",RegexOptions.IgnoreCase);
        }
        static bool IsUnsafeInference(SemanticMemory value) {
            if(value.Certainty!="tentative")return false;
            return Regex.IsMatch(value.Key+" "+value.Text,@"(?:personality|diagnos|性格|人格|内向|外向|焦虑|抑郁|依赖型|回避型|讨好型|偏执|心理疾病)",RegexOptions.IgnoreCase);
        }
        static bool HasExplicitPreference(string evidence) {
            return Regex.IsMatch(evidence??"",@"(?:喜欢|讨厌|偏好|更爱|爱吃|爱喝|不爱|不喝|不吃|不碰|戒(?:掉|了)?|(?:不要|别)(?:再)?推荐|只(?:吃|喝)|怕(?:辣|甜|苦|酸)|prefer|dislike|hate|\blike\b|\blove\b|don't\s+(?:eat|drink)|do not\s+(?:eat|drink))",RegexOptions.IgnoreCase);
        }
        internal static string Fingerprint(string value) {
            string normalized=Regex.Replace((value??"").Normalize(NormalizationForm.FormKC).ToLowerInvariant(),@"[\s\p{P}\p{S}]","");
            using(var hash=SHA256.Create())return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(normalized))).Replace("-", "").ToLowerInvariant();
        }
    }
    internal sealed class MemoryEntry {
        public string Key {get;set;}
        public string Text {get;set;}
        public string Kind {get;set;}
        public string Certainty {get;set;}
        public string Evidence {get;set;}
        public string EvidenceUtc {get;set;}
        public string UpdatedUtc {get;set;}
        public string Source {get;set;}
        internal string Display {get{return Certainty=="tentative"?"【待确认】"+Text:Text;}}
    }
    internal sealed class MemoryDocument {
        public int Version {get;set;}
        public int Generation {get;set;}
        public List<MemoryEntry> Entries {get;set;}
        public List<string> Exclusions {get;set;}
        public MemoryDocument(){Version=2;Entries=new List<MemoryEntry>();Exclusions=new List<string>();}
    }
}
