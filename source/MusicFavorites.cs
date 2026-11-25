using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace AnnaDesktopPet
{
    internal sealed class FavoriteSong { public string Id, Title, Artist, Album; }

    internal static class MusicFavorites
    {
        private static readonly object Gate = new object();
        private static string FileName { get { return Path.Combine(Program.DataDir, "music-favorites.json"); } }

        internal static List<FavoriteSong> Load()
        {
            lock (Gate)
            {
                if (Program.Smoke || !File.Exists(FileName)) return new List<FavoriteSong>();
                return new JavaScriptSerializer().Deserialize<List<FavoriteSong>>(File.ReadAllText(FileName, Encoding.UTF8)) ?? new List<FavoriteSong>();
            }
        }

        private static void Save(List<FavoriteSong> songs)
        {
            if (Program.Smoke) return;
            Directory.CreateDirectory(Program.DataDir);
            string temp = FileName + ".tmp";
            File.WriteAllText(temp, new JavaScriptSerializer().Serialize(songs), Encoding.UTF8);
            if (File.Exists(FileName)) File.Replace(temp, FileName, null);
            else File.Move(temp, FileName);
        }

        internal static FavoriteSong FindPlaying(IList<FavoriteSong> rows, string title, string artist, string album, QqTrack known)
        {
            FavoriteSong match = null;
            foreach (var entry in rows)
            {
                if (entry == null || String.IsNullOrEmpty(entry.Id)) continue;
                if (known != null && !String.IsNullOrEmpty(known.Id))
                {
                    if (known.Id == entry.Id) return entry;
                    continue;
                }
                if (!QqTrackIdentity.Matches(title, artist, album, entry.Title, entry.Artist, entry.Album)) continue;
                if (match != null && match.Id != entry.Id) return null;
                match = entry;
            }
            return match;
        }

        internal static string Add(QqTrack track)
        {
            if (track == null || String.IsNullOrEmpty(track.Id)) return "请先通过杏菜点播一首 QQ 音乐歌曲，再说「收藏这首」。";
            lock (Gate)
            {
                var rows = Load();
                if (rows.Exists(entry => entry.Id == track.Id)) return "这首已经在收藏里啦。";
                if (rows.Count >= 200) return "收藏已满200首，请先移除一些歌曲。";
                rows.Add(new FavoriteSong { Id = track.Id, Title = track.Title, Artist = track.Artist, Album = track.Album });
                Save(rows);
                return "已收藏「" + track.Title + "」— " + track.Artist + "。重启后仍会保留。";
            }
        }

        private static string RemoveAt(List<FavoriteSong> rows, int index)
        {
            string title = rows[index].Title;
            rows.RemoveAt(index);
            Save(rows);
            return "已移除收藏「" + title + "」。";
        }

        internal static string Remove(int index)
        {
            lock (Gate)
            {
                var rows = Load();
                if (index < 1 || index > rows.Count) return "请按「查看收藏」里的编号删除。";
                return RemoveAt(rows, index - 1);
            }
        }

        internal static string RemoveById(string id)
        {
            lock (Gate)
            {
                var rows = Load();
                int index = rows.FindIndex(entry => entry.Id == id && !String.IsNullOrEmpty(id));
                return index < 0 ? "这首歌已经不在收藏里了。" : RemoveAt(rows, index);
            }
        }

        internal static string List()
        {
            var rows = Load();
            if (rows.Count == 0) return "收藏还是空的。点歌后说「收藏这首」即可加入。";
            var message = new StringBuilder("我的收藏：");
            for (int i = 0; i < rows.Count; i++) message.Append("\n").Append(i + 1).Append(". ").Append(rows[i].Title).Append(" — ").Append(rows[i].Artist);
            return message + "\n说「播放收藏第1首」「随机来一首」，或「移除收藏第1首」。";
        }

        internal static QqTrack Track(FavoriteSong song)
        {
            return new QqTrack { Id = song.Id, Title = song.Title, Artist = song.Artist, Album = song.Album, Type = 0 };
        }
    }
}
