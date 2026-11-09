using System;
using System.IO;

namespace AnnaDesktopPet {
#if ENGINE_PATH_TEST_STANDALONE
    internal static class Program {
        internal static bool Smoke;
        internal static string DataDir;
        internal static void Log(string value) { }
    }
#endif
    internal static class EnginePathTests {
        private static int checks;
        private static void Check(bool ok, string name) { checks++; if (!ok) throw new Exception(name); }
        private static string EngineFolder(string path) {
            Directory.CreateDirectory(Path.Combine(path,"runtime"));
            File.WriteAllText(Path.Combine(path,"api_v2.py"), "test marker");
            File.WriteAllText(Path.Combine(path,"runtime","python.exe"), "test marker, never executed");
            return Path.GetFullPath(path);
        }
        private static void Register(string baseDir,string value) {
            Directory.CreateDirectory(Path.Combine(baseDir,"voice-engine"));
            File.WriteAllText(Path.Combine(baseDir,"voice-engine","engine-path.txt"),value);
        }
        private static void Reject(string baseDir,string name) {
            bool rejected=false;
            try { AsrWorker.ResolveEngine(baseDir); }
            catch (InvalidOperationException ex) { rejected=ex.Message.Contains("引擎") && ex.Message.Length>15; }
            Check(rejected,name);
        }
        public static int Main() {
            string root=Path.Combine(Path.GetTempPath(),"anna-asr-"+Guid.NewGuid().ToString("N").Substring(0,12));
            Directory.CreateDirectory(root);
            try {
                string oldEnv=Environment.GetEnvironmentVariable("ANNA_ASR_ENGINE");
                string userSettings=Path.Combine(root,"old-user-settings");Directory.CreateDirectory(userSettings);
                Program.DataDir=userSettings;
                string priorEngine=EngineFolder(Path.Combine(root,"engine"));
                string oldConfig=Path.Combine(userSettings,"asr-engine-path.txt");
                try {
                    Environment.SetEnvironmentVariable("ANNA_ASR_ENGINE",priorEngine);
                    Check(AsrWorker.Engine==priorEngine,"previous release environment setting still works");
                    Environment.SetEnvironmentVariable("ANNA_ASR_ENGINE",null);
                    bool incompleteRejected=false;
                    try { AsrWorker.RegisterInputEngine(priorEngine); } catch(InvalidOperationException) { incompleteRejected=true; }
                    Check(incompleteRejected && !File.Exists(oldConfig),"a TTS-only installation cannot be saved as the input engine");
                    string model=Path.Combine(priorEngine,"tools","asr","models","speech_paraformer-large_asr_nat-zh-cn-16k-common-vocab8404-pytorch","model.pt");
                    Directory.CreateDirectory(Path.GetDirectoryName(model));
                    File.WriteAllText(model,"test marker");
                    AsrWorker.RegisterInputEngine(priorEngine);
                    Check(AsrWorker.Engine==priorEngine,"engine chosen in settings is available to an app outside the installation tree");
                    Check(AsrWorker.Engine==priorEngine,"previous release per-user setting still works");
                    Environment.SetEnvironmentVariable("ANNA_ASR_ENGINE",Path.Combine(root,"invalid-override"));
                    bool rejected=false;try { var ignored=AsrWorker.Engine; } catch(InvalidOperationException){ rejected=true; }
                    Check(rejected,"invalid explicit environment setting cannot silently fall back");
                } finally { Environment.SetEnvironmentVariable("ANNA_ASR_ENGINE",oldEnv);if(File.Exists(oldConfig))File.Delete(oldConfig); }
                string absApp=Path.Combine(root,"absolute","app");
                string absEngine=EngineFolder(Path.Combine(root,"absolute","custom-engine"));
                Register(absApp,absEngine);
                Check(AsrWorker.ResolveEngine(absApp)==absEngine,"new absolute registration");

                string relApp=Path.Combine(root,"relative","app");
                string relEngine=EngineFolder(Path.Combine(relApp,"voice-engine","portable"));
                Register(relApp,"portable");
                Check(AsrWorker.ResolveEngine(relApp)==relEngine,"relative registration based on configuration directory");

                string parent=Path.Combine(root,"ancestor-config","pet");
                string parentEngine=EngineFolder(Path.Combine(root,"ancestor-config","shared"));
                string child=Path.Combine(parent,"build","DeepSeek");Directory.CreateDirectory(child);
                Register(parent,parentEngine);
                Check(AsrWorker.ResolveEngine(child)==parentEngine,"nearest ancestor registration");

                string tree=Path.Combine(root,"ancestor-folder","private");
                string discovered=EngineFolder(Path.Combine(tree,"GPT-SoVITS-v3lora-test"));
                string app=Path.Combine(tree,"pet","build","GPT");Directory.CreateDirectory(app);
                Check(AsrWorker.ResolveEngine(app)==discovered,"nearby ancestor engine folder discovery");

                string unconfigured=Path.Combine(root,"missing","app");Directory.CreateDirectory(unconfigured);
                Reject(unconfigured,"no configuration or engine must report error");

                string invalid=Path.Combine(root,"invalid","app");
                EngineFolder(Path.Combine(invalid,"voice-engine","GPT-SoVITS"));
                Register(invalid,Path.Combine(root,"does-not-exist"));
                Reject(invalid,"invalid explicit registration must not silently fall back");

                string blank=Path.Combine(root,"blank","app");Register(blank,"  \r\n");
                Reject(blank,"blank explicit registration reports configuration error");

                string incomplete=Path.Combine(root,"incomplete","app");
                string incompleteEngine=EngineFolder(Path.Combine(root,"incomplete","engine"));
                File.Delete(Path.Combine(incompleteEngine,"runtime","python.exe"));Register(incomplete,incompleteEngine);
                Reject(incomplete,"python runtime is required");
                File.WriteAllText(Path.Combine(incompleteEngine,"runtime","python.exe"),"test");File.Delete(Path.Combine(incompleteEngine,"api_v2.py"));
                Reject(incomplete,"api entry is required");

                Register(child,Path.Combine(root,"invalid-child-registration"));
                Reject(child,"nearest invalid registration must not use valid ancestor");

                string nested=Path.Combine(root,"embedded","app");
                string embedded=EngineFolder(Path.Combine(nested,"voice-engine","GPT-SoVITS"));
                Check(AsrWorker.ResolveEngine(nested)==embedded,"embedded portable voice engine discovery");

                string ambiguous=Path.Combine(root,"ambiguous","app");
                EngineFolder(Path.Combine(ambiguous,"GPT-SoVITS-one"));EngineFolder(Path.Combine(ambiguous,"GPT-SoVITS-two"));
                Reject(ambiguous,"multiple matching engines require explicit registration");

                Console.WriteLine("PASS engine paths: "+checks+" checks; synthetic files only, no Python executed.");return 0;
            } catch(Exception ex) { Console.Error.WriteLine(ex); return 1; }
            finally { Directory.Delete(root,true); }
        }
    }
}
