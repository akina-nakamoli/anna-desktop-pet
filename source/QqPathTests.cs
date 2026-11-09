using System;
using System.IO;
using System.Reflection;

namespace AnnaDesktopPet {
#if QQ_PATH_TEST_STANDALONE
    internal static class Program { internal static string DataDir; }
#endif
    internal static class QqPathTests {
        private static int checks;
        private static string Resolve() {
            var method=typeof(QqCatalog).GetMethod("ResolvePath",BindingFlags.Static|BindingFlags.NonPublic);
            if(method==null)throw new Exception("QQ launch must resolve the user's configured installation rather than a developer path");
            try{return (string)method.Invoke(null,null);}catch(TargetInvocationException ex){throw ex.InnerException;}
        }
        private static void Check(bool ok,string name){checks++;if(!ok)throw new Exception(name);}
        private static void Reject(string name){bool rejected=false;try{Resolve();}catch(InvalidOperationException){rejected=true;}Check(rejected,name);}
        public static int Main(){
            string root=Path.Combine(Path.GetTempPath(),"anna-qq-path-"+Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);Program.DataDir=root;
            try{
                string installed=Path.Combine(root,"安装目录 with spaces","QQMusic.exe");
                Directory.CreateDirectory(Path.GetDirectoryName(installed));File.WriteAllText(installed,"fixture, never executed");
                string config=Path.Combine(root,"qqmusic-path.txt");
                File.WriteAllText(config,installed+Environment.NewLine);
                Check(Resolve()==Path.GetFullPath(installed),"configured installation with spaces and Unicode");
                File.WriteAllText(config,Path.Combine(root,"missing","QQMusic.exe"));Reject("missing executable");
                string other=Path.Combine(root,"Other.exe");File.WriteAllText(other,"never executed");
                File.WriteAllText(config,other);Reject("other executable rejected");
                File.WriteAllText(config,"  ");Reject("empty registration");
                File.Delete(config);Reject("missing registration has an actionable error");
                Console.WriteLine("PASS: QQ portable configuration ("+checks+" checks; no client launch/network)");return 0;
            }catch(Exception ex){Console.Error.WriteLine("FAIL: "+ex.Message);return 1;}
            finally{Directory.Delete(root,true);}
        }
    }
}
