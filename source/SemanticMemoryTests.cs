using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using System.Reflection;
using System.Diagnostics;
using System.Threading;
using System.Security.Cryptography;
using System.Web.Script.Serialization;

namespace AnnaDesktopPet {
#if MEMORY_TEST_STANDALONE
    internal static class Program { internal static string DataDir; internal static bool Smoke; }
    internal sealed class ChatMessage { public string Role, Content; public string JapaneseText; public DateTime? CreatedUtc; public ChatMessage(string role,string content){Role=role;Content=content;CreatedUtc=DateTime.UtcNow;} }
#endif
    internal static class SemanticMemoryTests {
        static int checks;
        static void Check(bool condition,string name){checks++;if(!condition)throw new Exception(name);}
        static string Packet(string key,string text,string certainty,string evidence,string replaces="",string kind="preference") {
            return new JavaScriptSerializer().Serialize(new {memories=new[]{new {key=key,text=text,kind=kind,certainty=certainty,evidence=evidence,replaces=replaces}}});
        }
        static int Apply(string raw, params ChatMessage[] evidence) {
            MethodInfo m=typeof(PetMemory).GetMethod("ApplySemantic",BindingFlags.Static|BindingFlags.NonPublic,null,new[]{typeof(string),typeof(IList<ChatMessage>),typeof(DateTime)},null);
            Check(m!=null,"semantic memory application has not been implemented");
            return (int)m.Invoke(null,new object[]{raw,evidence,new DateTime(2026,11,9,12,0,0,DateTimeKind.Utc)});
        }
        static void CheckLocalObservation(string root) {
            Program.DataDir=Path.Combine(root,"autonomous");
            var start=new DateTime(2026,10,4,8,30,0,DateTimeKind.Utc);
            var empty=new List<ChatMessage>();
            string request="来首Ado的歌";
            foreach(string kind in new[]{"preference","event","fact"})
                Check(Apply(Packet("artist:ado","用户喜欢 Ado 的歌","explicit","Ado","",kind),new ChatMessage("user",request))==0,"one-off music request stays out when quote only names artist: "+kind);
            foreach(string ignored in new[]{request, "我喜欢抹茶吗？", "我喜欢咖啡，但是别记", "我喜欢咖啡，但不要长期保存", "我喜欢汉堡。如果我喜欢面包呢？", "她说：我喜欢跑步", "我喜欢咖啡，验证码123456", "我的昵称是小葵吗？", "她说：我家的猫叫布丁", "我家的猫叫什么"}) {
                Check(PetMemory.Observe(ignored,start,empty)==null,"ordinary task, question, quote, private or opt-out is ignored");
            }
            var roleplay=new List<ChatMessage>{new ChatMessage("user","我们来角色扮演"),new ChatMessage("assistant","你是店长。")};
            Check(PetMemory.Observe("我喜欢芒果",start,roleplay)==null,"roleplay context carries across turns");
            roleplay.Add(new ChatMessage("user","退出角色扮演"));
            string statement="我喜欢抹茶，你呢？";
            Check(PetMemory.Observe(statement,start,roleplay)=="用户喜欢抹茶","first-person fact before a separate question saved automatically");
            string path=Path.Combine(Program.DataDir,"memory-v2.bin");
            byte[] first=File.ReadAllBytes(path);
            Check(PetMemory.Observe("我喜欢抹茶。你呢？",start.AddMinutes(1),empty)=="用户喜欢抹茶"&&
                Convert.ToBase64String(first)==Convert.ToBase64String(File.ReadAllBytes(path)),"same existing fact acknowledges without another disk write");
            Check(!Encoding.UTF8.GetString(first).Contains("抹茶")&&PetMemory.Context().Contains("用户喜欢抹茶"),"automatic fact encrypted and available to next prompt");
            Check(PetMemory.Observe("我现在不喜欢抹茶了",start.AddMinutes(2),empty)=="用户不喜欢抹茶"&&PetMemory.Items().Length==1,"later ordinary preference replaces earlier polarity");
            Check(Apply(Packet("food:tea","用户偏爱抹茶","explicit","我喜欢抹茶"),new ChatMessage("user",statement){CreatedUtc=start})==0,"earlier semantic paraphrase cannot resurrect stale preference");
            Check(Apply(Packet("food:tea","用户偏爱抹茶","explicit","我喜欢抹茶"),new ChatMessage("user",statement){CreatedUtc=null})==0,"undated semantic paraphrase cannot resurrect stale preference");
            string multi="我喜欢跑步，我喜欢摄影";
            Check(PetMemory.Observe(multi,start.AddMinutes(3),empty)!=null&&PetMemory.Items().Length==3,"two different preference clauses both committed");
            var original=new ChatMessage("user",multi){CreatedUtc=start.AddMinutes(3)};
            Check(Apply(Packet("sport:run","用户的爱好是跑步","explicit","我喜欢跑步"),original)==0&&
                Apply(Packet("hobby:photo","用户的爱好是摄影","explicit","我喜欢摄影"),original)==0&&PetMemory.Items().Length==3,"semantic paraphrases of each local fact are idempotent");
            Check(PetMemory.Observe("以后请叫我小禾",start.AddMinutes(4),empty)!=null&&PetMemory.Observe("以后请叫我阿禾",start.AddMinutes(5),empty)!=null,"nickname updates from normal conversation");
            Check(PetMemory.Observe("以后回复尽量简短",start,empty)!=null&&PetMemory.Observe("回答详细一些",start.AddMinutes(1),empty)!=null,"communication style corrected");
            Check(PetMemory.Observe("我养了一只叫奶糖的猫",start,empty)!=null&&PetMemory.Observe("我明天要参加面试",start,empty)!=null,"pet fact and dated event saved offline");
            var rows=(object[])new JavaScriptSerializer().DeserializeObject(PetMemory.SnapshotJson());
            var kinds=new Dictionary<string,string>();
            foreach(Dictionary<string,object> row in rows)kinds[(string)row["text"]]=(string)row["kind"];
            Check(kinds.Count==7&&kinds["用户希望被称为阿禾"]=="communication"&&kinds["用户希望回复详细"]=="communication"&&kinds["用户养了一只叫奶糖的猫"]=="fact"&&kinds["用户明天要参加面试"]=="event","locally observed facts separated by kind and corrections persisted");
            Check(PetMemory.Observe("我的昵称是小葵",start.AddMinutes(6),empty)=="用户希望被称为小葵"&&PetMemory.Observe("我家的猫叫布丁",start,empty)=="用户养了一只叫布丁的猫","nickname and pet naming alternate phrasing recognized");
            Check(PetMemory.Observe("我养了一只叫布丁的猫",start,empty)=="用户养了一只叫布丁的猫"&&PetMemory.Items().Length==8,"equivalent pet phrase shares a stable key");
            Check(Apply(Packet("event:second","用户周末出差","explicit","周末出差","","event"),new ChatMessage("user","周末出差，来首Ado的歌"))==1,"one-off music request does not discard independent event in same turn");
            string store=Program.DataDir;Program.DataDir=Path.Combine(root,"other-profile");
            Check(PetMemory.Context()=="","an empty profile has no memory");
            Program.DataDir=store;Check(PetMemory.Context().Contains("奶糖")&&PetMemory.Context().Contains(start.ToString("yyyy-MM-dd")),"reopen original data from disk for a fresh conversation");
            Program.DataDir=root;
        }
        static int ConcurrentWorker(string dir,string side) {
            Program.DataDir=dir;Program.Smoke=false;
            string start=Path.Combine(dir,"start.signal");
            File.WriteAllText(Path.Combine(dir,side+".ready"),"");
            var timer=Stopwatch.StartNew();
            while(!File.Exists(start)){if(timer.ElapsedMilliseconds>10000)return 2;Thread.Sleep(10);}
            for(int i=0;i<18;i++) {
                if(PetMemory.Observe("我喜欢"+side+i)==null)return 3;
                if(side=="A"&&i==8&&!PetMemory.Handle("忘记：用户喜欢旧面包").Contains("已删除"))return 4;
            }
            return 0;
        }
        static Process StartWorker(string dir,string side) {
            var info=new ProcessStartInfo(Assembly.GetExecutingAssembly().Location,"--memory-worker \""+dir+"\" "+side);
            info.UseShellExecute=false;info.CreateNoWindow=true;info.WindowStyle=ProcessWindowStyle.Hidden;
            return Process.Start(info);
        }
        static void CheckConcurrentWriters(string root) {
            string dir=Path.Combine(root,"concurrent");
            Program.DataDir=dir;Directory.CreateDirectory(dir);
            PetMemory.Observe("我喜欢旧面包");
            int generation=PetMemory.MemoryGeneration;
            using(var a=StartWorker(dir,"A"))using(var b=StartWorker(dir,"B")) {
                var timer=Stopwatch.StartNew();
                while(!File.Exists(Path.Combine(dir,"A.ready"))||!File.Exists(Path.Combine(dir,"B.ready"))) {
                    if(a.HasExited||b.HasExited||timer.ElapsedMilliseconds>=10000)throw new Exception("isolated memory worker startup failed");
                    Thread.Sleep(10);
                }
                File.WriteAllText(Path.Combine(dir,"start.signal"),"");
                Check(a.WaitForExit(15000)&&b.WaitForExit(15000)&&a.ExitCode==0&&b.ExitCode==0,"both independent memory writers completed");
            }
            var rows=new HashSet<string>(PetMemory.Items());
            Check(rows.Count==36&&!rows.Contains("用户喜欢旧面包"),"both writers and concurrent forgetting survived reopen");
            for(int i=0;i<18;i++)Check(rows.Contains("用户喜欢A"+i)&&rows.Contains("用户喜欢B"+i),"concurrent appends preserved");
            var old=new ChatMessage("user","我喜欢旧面包");
            Check(PetMemory.ApplySemantic(Packet("bakery","用户偏好旧面包","explicit",old.Content),new[]{old},DateTime.UtcNow,generation)==0,"prior process cannot reapply pre-forget batch");
            Program.DataDir=root;
        }
        static void CheckRecallBeyondLimit(string root) {
            Program.DataDir=Path.Combine(root,"recall");
            Check(PetMemory.Observe("我喜欢山间观鸟")!=null&&PetMemory.Observe("以后叫我小秋")!=null,"early personal records written");
            for(int i=0;i<75;i++)if(PetMemory.Observe("我喜欢测试爱好"+i)==null)throw new Exception("failed to append memory "+i);
            Check(PetMemory.Items().Length==77,"automatic memory keeps learning past sixty entries");
            Check(PetMemory.Handle("记住：用户很喜欢晴天").Contains("记住")&&Apply(Packet("habit:reading","用户每天阅读古典文学","explicit","我每天阅读古典文学","","fact"),new ChatMessage("user","我每天阅读古典文学"))==1&&PetMemory.Items().Length==79,"manual and semantic writers share the expanded store");
            string query="你还记得我喜欢在哪里观鸟吗？";
            var recall=new HashSet<string>(PetMemory.Items(query));
            Check(recall.Count==60&&recall.Contains("用户喜欢山间观鸟")&&recall.Contains("用户希望被称为小秋")&&recall.Contains("用户喜欢测试爱好74"),"bounded recall includes an old relevant fact, preferred name and recent facts");
            Check(Array.IndexOf(PetMemory.Items(""),"用户喜欢山间观鸟")<0,"unrelated old memory falls outside default context");
            Check(PetMemory.Context(query).Contains("山间观鸟")&&PetMemory.SnapshotJson(query).Contains("山间观鸟")&&((object[])new JavaScriptSerializer().DeserializeObject(PetMemory.SnapshotJson(query))).Length==60,"all model request paths share bounded contextual recall");
            Check(PetMemory.Observe("我不喜欢山间观鸟")=="用户不喜欢山间观鸟"&&PetMemory.Items().Length==79,"full collection supports ordinary corrections");
            Check(PetMemory.Handle("忘记：用户不喜欢山间观鸟").Contains("删除")&&PetMemory.Items().Length==78,"full collection supports forgetting");
            string storePath=Path.Combine(Program.DataDir,"memory-v2.bin");
            byte[] good=File.ReadAllBytes(storePath);
            var oversized=new MemoryDocument();
            oversized.Exclusions.Add(new string('测',1400000));
            var save=typeof(PetMemory).GetMethod("Save",BindingFlags.Static|BindingFlags.NonPublic);
            bool rejected=false;
            try {save.Invoke(null,new object[]{oversized});}catch(TargetInvocationException e){rejected=e.InnerException is IOException;}
            Check(rejected&&Convert.ToBase64String(File.ReadAllBytes(storePath))==Convert.ToBase64String(good)&&Directory.GetFiles(Program.DataDir,"*.tmp").Length==0,"oversized UTF-8 store rejected before replacing existing records");
            Program.DataDir=root;
        }
        [STAThread] static int Main(string[] args) {
            if(args.Length==3&&args[0]=="--memory-worker"&&(args[2]=="A"||args[2]=="B"))return ConcurrentWorker(args[1],args[2]);
            string temp=Path.Combine(Path.GetTempPath(),"anna-memory-tests-"+Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temp);Program.DataDir=temp;Program.Smoke=false;
            try {
                CheckLocalObservation(temp);
                CheckConcurrentWriters(temp);
                CheckRecallBeyondLimit(temp);
                string quote="每天上班路上我都会买一杯无糖咖啡。";
                var user=new ChatMessage("user",quote);
                var liveCandidate=SemanticMemory.Parse(Packet("drink:coffee","用户习惯每天上班路上买一杯无糖咖啡，偏好无糖口味，不选甜的饮料","explicit",quote),new[]{user});
                Check(liveCandidate.Count==1&&liveCandidate[0].Certainty=="tentative"&&liveCandidate[0].Text.Contains("可能"),"live implicit habit inference downgraded despite explicit model label");
                var clearCandidate=SemanticMemory.Parse(Packet("drink:coffee","用户喜欢无糖咖啡","explicit","我喜欢无糖咖啡"),new[]{new ChatMessage("user","我喜欢无糖咖啡")});
                Check(clearCandidate.Count==1&&clearCandidate[0].Certainty=="explicit","direct preference certainty preserved");
                foreach(string fictional in new[]{"角色扮演：我喜欢咖啡", "角色设定：我喜欢咖啡", "梦里我喜欢咖啡", "我梦见自己说：我喜欢咖啡", "剧情里我喜欢咖啡", "我扮演的学生说：我喜欢咖啡"})
                    Check(SemanticMemory.Parse(Packet("drink:fiction","用户喜欢咖啡","explicit","我喜欢咖啡"),new[]{new ChatMessage("user",fictional)}).Count==0,"fiction cannot become a personal preference: "+fictional);
                Check(SemanticMemory.Parse(Packet("hobby:fiction","用户喜欢读小说","explicit","我喜欢读小说"),new[]{new ChatMessage("user","我喜欢读小说")}).Count==1,"real interest in fiction is still a real preference");
                foreach(string real in new[]{"我喜欢角色扮演游戏", "我喜欢设计角色设定", "I like role-playing games", "退出角色扮演，现在说现实里的我：我喜欢咖啡"})
                    Check(SemanticMemory.Parse(Packet("hobby:real", "用户的现实偏好", "explicit", real),new[]{new ChatMessage("user",real)}).Count==1,"real preference must not be blocked by fiction vocabulary: "+real);
                Check(SemanticMemory.Parse(Packet("personality:social","用户性格外向","explicit","我每天都去人多的店"),new[]{new ChatMessage("user","我每天都去人多的店")}).Count==0,"downgraded behavior cannot become personality inference");
                Check(Apply(Packet("drink:coffee","用户可能偏好无糖咖啡","tentative",quote),user)==1,"implicit candidate saved");
                Check(PetMemory.Items().Length==1&&PetMemory.Items()[0].Contains("待确认"),"inference shown as tentative");
                Check(PetMemory.Context().Contains("数据")&&PetMemory.Context().Contains("待确认"),"context marks uncertain data");
                string file=Path.Combine(temp,"memory-v2.bin");
                Check(File.Exists(file)&&!Encoding.UTF8.GetString(File.ReadAllBytes(file)).Contains("咖啡"),"disk encryption");
                Check(Apply(Packet("food:bread","用户喜欢面包","explicit","我喜欢面包"),new ChatMessage("assistant","我喜欢面包"))==0,"assistant cannot supply user facts");
                Check(Apply(Packet("food:bread","用户喜欢面包","explicit","我喜欢面包"),new ChatMessage("local_assistant","我喜欢面包"))==0,"tool cannot supply user facts");
                Check(Apply(Packet("food:bread","用户喜欢面包","explicit","我喜欢面包"),user)==0,"hallucinated evidence rejected");
                string correction="不是喜欢咖啡，我已经戒咖啡了，以后别再推荐。";
                string old=PetMemory.Items()[0];
                Check(Apply(Packet("drink:coffee","用户已戒咖啡，不希望推荐咖啡","explicit",correction,"用户可能偏好无糖咖啡"),new ChatMessage("user",correction))==1,"explicit correction replaces prior");
                Check(PetMemory.Items().Length==1&&!PetMemory.Items()[0].Contains("待确认"),"correction sole active fact");
                Check(Apply(Packet("drink:coffee","用户可能喜欢咖啡","tentative",quote),user)==0,"tentative cannot overwrite explicit");
                var oldQuote=new ChatMessage("user","我喜欢加糖咖啡") {CreatedUtc=new DateTime(2020,1,1,0,0,0,DateTimeKind.Utc)};
                Check(Apply(Packet("drink:coffee","用户喜欢加糖咖啡","explicit","我喜欢加糖咖啡","用户已戒咖啡，不希望推荐咖啡"),oldQuote)==0,"old backlog cannot overwrite newer correction");
                Check(Apply(Packet("food:bread","用户喜欢面包","explicit","我喜欢面包","不存在的旧记忆"),new ChatMessage("user","我喜欢面包"))==-1,"unknown replacement fails for retry");
                int before=PetMemory.Items().Length;
                string bad="{\"memories\":[{\"key\":\"food:bread\",\"text\":\"用户喜欢面包\",\"kind\":\"preference\",\"certainty\":\"explicit\",\"evidence\":\"我喜欢面包\",\"replaces\":\"\"},{\"key\":3}]}";
                Check(Apply(bad,new ChatMessage("user","我喜欢面包"))==-1&&PetMemory.Items().Length==before,"invalid batch applies nothing and allows retry");
                Check(Apply(Packet("secret:key","用户密钥 sk-" + new string('x', 20) + "","explicit","密钥 sk-" + new string('x', 20) + ""),new ChatMessage("user","密钥 sk-" + new string('x', 20) + ""))==0,"secrets excluded");
                Check(Apply(Packet("personality:diagnosis","用户有抑郁症","tentative","今天有点难过"),new ChatMessage("user","今天有点难过"))==0,"no diagnostic inference");
                string forgot=PetMemory.Items()[0];
                Check(PetMemory.Handle("忘记："+forgot).Contains("删除"),"delete displayed memory");
                Check(PetMemory.Items().Length==0,"delete actually removes memory");
                Check(Apply(Packet("drink:coffee","用户已戒咖啡，不希望推荐咖啡","explicit",correction),new ChatMessage("user",correction))==0,"deleted semantic key blocked");
                Check(Apply(Packet("different:key","用户已戒咖啡，不希望推荐咖啡","explicit",correction),new ChatMessage("user",correction))==0,"same forgotten evidence under new key blocked");
                Check(PetMemory.Handle("记住：用户已戒咖啡，不希望推荐咖啡").Contains("记住"),"manual restoration permitted");
                Check(PetMemory.Items().Length==1,"manual restored");
                int manualGeneration=PetMemory.MemoryGeneration;
                PetMemory.Handle("记住：回答尽量简短");
                Check(PetMemory.ApplySemantic(Packet("communication:length","用户偏好长篇回复","explicit","回答可以详细点", "", "communication"),new[]{new ChatMessage("user","回答可以详细点")},DateTime.UtcNow,manualGeneration)==0,"new manual preference invalidates older in-flight extraction");
                PetMemory.Handle("清空记忆");
                Check(PetMemory.Items().Length==0,"clear empty");
                Check(Apply(Packet("drink:coffee","用户已戒咖啡，不希望推荐咖啡","explicit",correction),new ChatMessage("user",correction))==0,"clear blocks reimport");
                Check(Apply(Packet("event:exam","用户下周二参加考试","explicit","下周二我要考试", "", "event"),new ChatMessage("user","下周二我要考试"))==1,"event retained with time");
                Check(PetMemory.Context().Contains("2026-11-09"),"event anchored to recorded date");
                Check(PetMemory.SnapshotJson().Contains("event:exam"),"extractor snapshot preserves stable keys");
                var yesterday=new ChatMessage("user","明天我要参加面试") {CreatedUtc=new DateTime(2026,11,8,9,0,0,DateTimeKind.Utc)};
                Check(Apply(Packet("event:interview","用户明天参加面试","explicit","明天我要参加面试","","event"),yesterday)==1,"delayed event memory saved");
                var eventRows=(object[])new JavaScriptSerializer().DeserializeObject(PetMemory.SnapshotJson());
                Dictionary<string,object> eventRow=null;
                foreach(Dictionary<string,object> candidate in eventRows)if((string)candidate["key"]=="event:interview")eventRow=candidate;
                Check(eventRow!=null&&eventRow.ContainsKey("evidence_at")&&((string)eventRow["evidence_at"]).StartsWith("2026-11-08"),"tomorrow anchored to original evidence date not processing date");
                var repeated=new ChatMessage("user","明天我要参加面试") {CreatedUtc=new DateTime(2026,11,9,9,0,0,DateTimeKind.Utc)};
                Check(Apply(Packet("event:interview","用户明天参加面试","explicit","明天我要参加面试","","event"),yesterday,repeated)==1,"repeated relative event updates source timestamp");
                eventRows=(object[])new JavaScriptSerializer().DeserializeObject(PetMemory.SnapshotJson());
                foreach(Dictionary<string,object> candidate in eventRows)if((string)candidate["key"]=="event:interview")Check(((string)candidate["evidence_at"]).StartsWith("2026-11-09"),"latest matching user evidence wins");
                var unknown=new ChatMessage("user","下周我要去旅行") {CreatedUtc=null};
                Check(Apply(Packet("event:travel","用户说下周旅行，日期未知","explicit","下周我要去旅行","","event"),unknown)==1,"unknown source time supported");
                eventRows=(object[])new JavaScriptSerializer().DeserializeObject(PetMemory.SnapshotJson());
                foreach(Dictionary<string,object> candidate in eventRows)if((string)candidate["key"]=="event:travel")Check(String.IsNullOrEmpty((string)candidate["evidence_at"]),"unknown timestamp never fabricated");
                Check(Apply(Packet("food:spicy","用户怕辣","explicit","我怕辣"),new ChatMessage("user","我怕辣"))==1,"short clear user preference accepted");
                int generation=PetMemory.MemoryGeneration;
                PetMemory.Handle("清空记忆");
                Check(PetMemory.MemoryGeneration>generation,"clear advances generation");
                Check(PetMemory.ApplySemantic(Packet("event:new","用户准备周末爬山","explicit","周末我准备去爬山", "", "event"),new[]{new ChatMessage("user","周末我准备去爬山")},DateTime.UtcNow,generation)==0,"in-flight stale extraction rejected atomically");
                Check(PetMemory.Observe("我喜欢汉堡")!=null,"legacy explicit observe remains supported");
                Check(PetMemory.Observe("我不喜欢汉堡")=="用户不喜欢汉堡"&&PetMemory.Items().Length==1,"legacy polarity correction");
                var snapshot=(object[])new JavaScriptSerializer().DeserializeObject(PetMemory.SnapshotJson());
                var record=(Dictionary<string,object>)snapshot[0];
                Check(Apply(Packet((string)record["key"],"用户现在只接受不加酱的汉堡","explicit","现在只吃不加酱的汉堡","用户不喜欢汉堡"),new ChatMessage("user","现在只吃不加酱的汉堡"))==1,"extractor can reuse regex-generated stable keys");
                Check(PetMemory.ExtractCandidate("我喜欢密码123")==null,"legacy secrets filtered");
                string legacyTemp=Path.Combine(temp,"legacy");Directory.CreateDirectory(legacyTemp);Program.DataDir=legacyTemp;
                string legacyFile=Path.Combine(legacyTemp,"memories.bin");
                File.WriteAllBytes(legacyFile,ProtectedData.Protect(Encoding.UTF8.GetBytes("用户喜欢面包\n用户希望被称为小明"),null,DataProtectionScope.CurrentUser));
                Check(PetMemory.Items().Length==2,"legacy read");
                PetMemory.Handle("记住：用户喜欢雨天");
                Check(PetMemory.Items().Length==3&&File.Exists(legacyFile)&&File.Exists(Path.Combine(legacyTemp,"memory-v2.bin")),"legacy migration preserves original");
                PetMemory.Handle("忘记：用户喜欢面包");
                Check(!File.Exists(legacyFile)||!Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(legacyFile),null,DataProtectionScope.CurrentUser)).Contains("面包"),"explicit forgetting scrubs legacy copy");
                File.WriteAllBytes(legacyFile,new byte[]{9,8,7});
                Check(PetMemory.Handle("记住：用户现在更喜欢晴天").Contains("记住"),"valid active store ignores corrupt legacy backup");
                Check(Apply(Packet("weather:preference","用户只喜欢下雨","explicit","我只喜欢下雨","用户现在更喜欢晴天"),new ChatMessage("user","我只喜欢下雨"){CreatedUtc=null})==0,"unknown old evidence cannot override dated manual correction");
                Check(PetMemory.Handle("忘记：用户喜欢雨天").Contains("删除"),"forget works despite corrupt legacy backup");
                Check(Array.IndexOf(PetMemory.Items(),"用户现在更喜欢晴天")>=0,"forget preserves newer manual memory");
                Check(Apply(Packet("quote:other","用户喜欢面包","explicit","我喜欢面包"),new ChatMessage("user","他说：我喜欢面包"))==0,"quoted first-person is not a user fact");
                byte[] corrupt={1,2,3,4,5};File.WriteAllBytes(Path.Combine(legacyTemp,"memory-v2.bin"),corrupt);
                Check(PetMemory.Handle("记住：用户喜欢书店").Contains("失败"),"corrupt store reports failure");
                Check(Apply(Packet("food:rice","用户喜欢米饭","explicit","我喜欢米饭"),new ChatMessage("user","我喜欢米饭"))==-1,"corrupt read keeps semantic batch retryable");
                Check(Convert.ToBase64String(File.ReadAllBytes(Path.Combine(legacyTemp,"memory-v2.bin")))==Convert.ToBase64String(corrupt),"corrupt data never clobbered");
                Program.Smoke=true;Check(PetMemory.Items().Length==0,"smoke never reads disk memory");
                Check(Apply(bad,new ChatMessage("user","我喜欢面包"))==-1,"smoke still validates semantic schema");
                PetMemory.Handle("记住：烟雾测试内容");
                Check(Convert.ToBase64String(File.ReadAllBytes(Path.Combine(legacyTemp,"memory-v2.bin")))==Convert.ToBase64String(corrupt),"smoke never writes disk memory");
                string blocked=Path.Combine(temp,"blocked");Directory.CreateDirectory(Path.Combine(blocked,"memory-v2.bin"));Program.DataDir=blocked;Program.Smoke=false;
                Check(Apply(Packet("food:rice","用户喜欢米饭","explicit","我喜欢米饭"),new ChatMessage("user","我喜欢米饭"))==-1,"save failure keeps semantic batch retryable");
                Check(Directory.GetFiles(blocked,"*.tmp").Length==0,"failed save removes temporary plaintext-free file");
                Console.WriteLine("PASS semantic memory: "+checks+" checks");return 0;
            } catch(Exception ex){Console.Error.WriteLine(ex.InnerException??ex);return 1;}
            finally {Program.Smoke=true;Directory.Delete(temp,true);}
        }
    }
}
