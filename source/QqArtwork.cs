using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace AnnaDesktopPet
{
    // Windows media metadata identifies the playing track. Use the catalog only
    // to find the missing image, never to infer what the user is listening to.
    internal static class QqArtwork
    {
        private static bool SameField(string a, string b, bool albumVerified)
        {
            a = QqTrackIdentity.NormalizeField(a); b = QqTrackIdentity.NormalizeField(b);
            if (a.Length == 0 || b.Length == 0) return false;
            if (a == b) return true;
            // QQ may append a translated name to the title or artist in SMTC.
            // Relax that label only when the exact album also matches.
            return albumVerified && (QqTrackIdentity.WithoutAlias(a) == b || a == QqTrackIdentity.WithoutAlias(b));
        }

        internal static string MatchCoverUrl(IEnumerable<QqTrack> rows, string title, string artist, string album)
        {
            bool hasAlbum = !String.IsNullOrWhiteSpace(album);
            string match = null;
            foreach (var row in rows)
            {
                if (hasAlbum && !SameField(row.Album, album, false)) continue;
                if (!SameField(row.Title, title, hasAlbum) || !SameField(row.Artist, artist, hasAlbum)) continue;
                string url = row.AlbumCoverUrl;
                if (url == null) continue;
                if (match != null && !String.Equals(match, url, StringComparison.Ordinal)) return null;
                match = url;
            }
            return match;
        }

        internal static async Task<byte[]> ResolveAsync(string title, string artist, string album, CancellationToken cancellation)
        {
            if (String.IsNullOrWhiteSpace(title) || String.IsNullOrWhiteSpace(artist)) return null;
            string query = QqTrackIdentity.WithoutAlias(QqTrackIdentity.NormalizeField(title)) + " " + QqTrackIdentity.WithoutAlias(QqTrackIdentity.NormalizeField(artist));
            var rows = await QqCatalog.SearchPageAsync(query, 1, cancellation).ConfigureAwait(false);
            string url = MatchCoverUrl(rows.Tracks, title, artist, album);
            if (url == null) return null;
            return await Task.Run(delegate { return ReadImage(url, cancellation); }, cancellation).ConfigureAwait(false);
        }

        private static byte[] ReadImage(string url, CancellationToken cancellation)
        {
            const int maxSize = 2 * 1024 * 1024;
            var request = (HttpWebRequest)WebRequest.Create(url);
            request.AllowAutoRedirect = false;
            request.Timeout = request.ReadWriteTimeout = 6500;
            request.Referer = "https://y.qq.com/";
            request.UserAgent = "AnnaDesktopPet";
            using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation))
            {
                deadline.CancelAfter(9000);
                using (deadline.Token.Register(request.Abort))
                {
                    try
                    {
                        using (var response = (HttpWebResponse)request.GetResponse())
                        using (var stream = response.GetResponseStream())
                        using (var data = new MemoryStream())
                        {
                            if (response.StatusCode != HttpStatusCode.OK || response.ContentLength > maxSize ||
                                !response.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)) return null;
                            byte[] buffer = new byte[8192];
                            int size;
                            while ((size = stream.Read(buffer, 0, buffer.Length)) != 0)
                            {
                                deadline.Token.ThrowIfCancellationRequested();
                                if (size > maxSize - data.Length) return null;
                                data.Write(buffer, 0, size);
                            }
                            return data.Length == 0 ? null : data.ToArray();
                        }
                    }
                    catch (WebException)
                    {
                        cancellation.ThrowIfCancellationRequested();
                        return null;
                    }
                }
            }
        }
    }
}
