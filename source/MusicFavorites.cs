using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using System.Web.Script.Serialization;
namespace AnnaDesktopPet {
    internal sealed class FavoriteSong { public string Id,Title,Artist,Album; }
    internal static class MusicFavorites {
        private static string FileName {get{return Path.Combine(Program.DataDir,"music-favorites.json");}}
        internal static List<FavoriteSong> Load(){if(Program.Smoke || !File.Exists(FileName))return new List<FavoriteSong>();return new JavaScriptSerializer().Deserialize<List<FavoriteSong>>(File.ReadAllText(FileName,Encoding.UTF8))??new List<FavoriteSong>();}
        private static void Save(List<FavoriteSong> songs){if(Program.Smoke)return;Directory.CreateDirectory(Program.DataDir);string temp=FileName+".tmp";File.WriteAllText(temp,new JavaScriptSerializer().Serialize(songs),Encoding.UTF8);if(File.Exists(FileName))File.Replace(temp,FileName,null);else File.Move(temp,FileName);}
        internal static string Add(QqTrack track){var rows=Load();if(track==null)return "请先通过杏菜点播一首 QQ 音乐歌曲，再说「收藏这首」。";if(rows.Exists(x=>x.Id==track.Id))return "这首已经在收藏里啦。";if(rows.Count>=200)return "收藏已满200首，请先移除一些歌曲。";rows.Add(new FavoriteSong{Id=track.Id,Title=track.Title,Artist=track.Artist,Album=track.Album});Save(rows);return "已收藏「"+track.Title+"」— "+track.Artist+"。重启后仍会保留。";}
        internal static string Remove(int index){var rows=Load();if(index<1 || index>rows.Count)return "请按「查看收藏」里的编号删除。";string title=rows[index-1].Title;rows.RemoveAt(index-1);Save(rows);return "已移除收藏「"+title+"」。";}
        internal static string List(){var rows=Load();if(rows.Count==0)return "收藏还是空的。点歌后说「收藏这首」即可加入。";var b=new StringBuilder("我的收藏：");for(int i=0;i<rows.Count;i++)b.Append("\n").Append(i+1).Append(". ").Append(rows[i].Title).Append(" — ").Append(rows[i].Artist);return b+"\n说「播放收藏第1首」「随机来一首」，或「移除收藏第1首」。";}
        internal static QqTrack Track(FavoriteSong s){return new QqTrack{Id=s.Id,Title=s.Title,Artist=s.Artist,Album=s.Album,Type=0};}
    }
}
