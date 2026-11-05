using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
namespace AnnaDesktopPet {
    internal static class PetMemory {
        private static string PathName { get { return Path.Combine(Program.DataDir,"memories.bin"); } }
        private static List<string> Read() {
            if(Program.Smoke || !File.Exists(PathName))return new List<string>();
            return new List<string>(Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(PathName),null,DataProtectionScope.CurrentUser)).Split(new[]{'\n'},StringSplitOptions.RemoveEmptyEntries));
        }
        internal static string Context() { try { var rows=Read(); return rows.Count==0?"":"\n用户偏好与交流方式（部分由对话自动提取，可能不准确；请自然参考，不要反复提起，也不要当成不可更改的性格标签）：\n"+String.Join("\n",rows.ToArray()); } catch {return "";} }
        internal static string[] Items() { try { return Read().ToArray(); } catch { return new string[0]; } }
        internal static string ExtractCandidate(string input) {
            string text=(input??"").Trim();
            if(text.Length==0 || text.Length>300 || text.Contains("密码") || text.Contains("密钥") || text.Contains("银行卡") || text.Contains("身份证") || text.Contains("电话"))return null;
            Match m=Regex.Match(text,@"^(?:我|本人)(?:现在|最近|已经)?(喜欢|不喜欢|讨厌|爱吃|不爱吃)([^，。！？?！\r\n]{1,24})");
            if(m.Success){string subject=m.Groups[2].Value.Trim('了','呢','呀','啊',' ');if(subject.Length>=2)return "用户"+m.Groups[1].Value+subject;}
            m=Regex.Match(text,@"^(?:以后|今后)?(?:请)?叫我([^，。！？?！\r\n]{1,12})");
            if(m.Success)return "用户希望被称为"+m.Groups[1].Value.Trim();
            if(Regex.IsMatch(text,@"^(?:请|以后请|回答时请)?(?:说话|回答|回复)(?:尽量)?(?:简短|简洁|详细|直接|温柔)"))return "用户偏好"+text.Trim('。','！','!');
            if(Regex.IsMatch(text,@"^(?:我|本人)(?:比较|有点|其实)(?:内向|外向|怕生|话少|容易焦虑|容易紧张)"))return "用户自述："+text.Trim('。','！','!');
            if(Regex.IsMatch(text,@"^(?:别催我|不要催我|说重点|别太啰嗦|解释详细点|说清楚一点)"))return "用户交流偏好："+text.Trim('。','！','!');
            return null;
        }
        internal static string Observe(string input) {
            string candidate=ExtractCandidate(input);
            if(candidate==null)return null;
            try { var rows=Read();if(rows.Contains(candidate))return null;
                string subject=PreferenceSubject(candidate);
                if(subject!=null)rows.RemoveAll(delegate(string row) { return PreferenceSubject(row)==subject; });
                if(rows.Count>=30)return null;
                rows.Add(candidate);Save(rows);return candidate;
            } catch {return null;}
        }
        private static string PreferenceSubject(string value) {
            Match m=Regex.Match(value??"", @"^用户(?:喜欢|不喜欢|讨厌|爱吃|不爱吃)(.+)$");
            return m.Success?m.Groups[1].Value:null;
        }
        private static void Save(List<string> rows) {
            if(Program.Smoke)return;
            Directory.CreateDirectory(Program.DataDir);string temp=PathName+".tmp";
            File.WriteAllBytes(temp,ProtectedData.Protect(Encoding.UTF8.GetBytes(String.Join("\n",rows.ToArray())),null,DataProtectionScope.CurrentUser));
            if(File.Exists(PathName))File.Replace(temp,PathName,null);else File.Move(temp,PathName);
        }
        internal static string Handle(string input) {
            string t=input.Trim();
            if(t!="查看记忆" && t!="清空记忆" && !t.StartsWith("记住：") && !t.StartsWith("记住:") && !t.StartsWith("忘记：") && !t.StartsWith("忘记:"))return null;
            try {
                var rows=Read();
                if(t=="查看记忆")return rows.Count==0?"还没有保存记忆。可以说：记住：请叫我小明。":"已保存的记忆：\n"+String.Join("\n",rows.ToArray())+"\n可用「忘记：完整内容」删除一条，或「清空记忆」。记忆保存在本机，聊天时会随请求发送给 GPT。";
                if(t=="清空记忆")rows.Clear();
                else { string value=t.Substring(3).Trim(); if(value.Length==0 || value.Length>200)return "每条记忆请输入 1～200 个字。";
                    if(t.StartsWith("忘记")) { if(!rows.Remove(value))return "没有找到这条记忆，请先查看记忆并复制完整内容。"; }
                    else { if(rows.Contains(value))return "这条已经记住了。"; if(rows.Count>=30)return "已保存30条，请先删除不需要的记忆。"; rows.Add(value); }
                }
                Save(rows);
                return t.StartsWith("记住")?"记住啦。已加密保存在本机，之后聊天会把这条记忆作为背景发送给 GPT；可随时查看或删除。":"记忆已删除。";
            }catch {return "记忆读取或保存失败，请检查本机配置目录。";}
        }
    }
}
