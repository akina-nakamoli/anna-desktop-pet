using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace AnnaDesktopPet
{
    // GPT-SoVITS api_v2 /tts: the reference is a local path, never an upload.
    internal sealed class GptSovitsClient
    {
        internal const int MaximumWaveBytes = 32 * 1024 * 1024;
        private readonly HttpMessageHandler testHandler;
        internal GptSovitsClient() { }
        internal GptSovitsClient(HttpMessageHandler handler) { testHandler = handler; }

        internal static Uri TtsUri(string endpoint)
        {
            Uri value;
            if (!Uri.TryCreate((endpoint ?? "").Trim(), UriKind.Absolute, out value) ||
                (value.Scheme != Uri.UriSchemeHttp && value.Scheme != Uri.UriSchemeHttps) ||
                value.UserInfo.Length != 0 || value.Query.Length != 0 || value.Fragment.Length != 0)
                throw new InvalidOperationException("请填写本地 GPT-SoVITS 地址，例如 http://127.0.0.1:9880。");
            IPAddress address;
            bool loopback = value.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
                (IPAddress.TryParse(value.Host.Trim('[', ']'), out address) && IPAddress.IsLoopback(address));
            if (!loopback) throw new InvalidOperationException("参考音色只连接本机地址（127.0.0.1、localhost 或 ::1）。");
            if (value.AbsolutePath != "/" && value.AbsolutePath.TrimEnd('/') != "/tts")
                throw new InvalidOperationException("请填写本地服务根地址或 /tts 地址。");
            UriBuilder builder = new UriBuilder(value); builder.Path = "/tts";
            return builder.Uri;
        }

        internal static void ValidateConfiguration(string endpoint, string referenceAudio, string referenceText)
        {
            TtsUri(endpoint);
            if (String.IsNullOrWhiteSpace(referenceAudio) || !Path.IsPathRooted(referenceAudio) ||
                !Path.GetExtension(referenceAudio).Equals(".wav", StringComparison.OrdinalIgnoreCase) || !File.Exists(referenceAudio))
                throw new InvalidOperationException("请选择本机可读取的 WAV 参考样音。");
            if (String.IsNullOrWhiteSpace(referenceText))
                throw new InvalidOperationException("请填写与参考样音逐字一致的日语台词。");
            if (referenceText.Length > 1500)
                throw new InvalidOperationException("参考台词过长，请使用一段简短日语台词。");
        }

        internal static string CreateRequest(string text, string referenceAudio, string referenceText, int rate)
        {
            Dictionary<string, object> body = new Dictionary<string, object>();
            body["text"] = text; body["text_lang"] = "ja";
            body["ref_audio_path"] = referenceAudio; body["prompt_text"] = referenceText;
            body["prompt_lang"] = "ja"; body["media_type"] = "wav";
            body["streaming_mode"] = false; body["text_split_method"] = "cut5";
            body["batch_size"] = 1;
            body["speed_factor"] = 1.0 + Math.Max(-5, Math.Min(5, rate)) * 0.05;
            return new JavaScriptSerializer().Serialize(body);
        }

        internal async Task<byte[]> GenerateAsync(string endpoint, string referenceAudio, string referenceText,
            string text, int rate, CancellationToken cancellation)
        {
            ValidateConfiguration(endpoint, referenceAudio, referenceText);
            HttpMessageHandler handler = testHandler ?? new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false };
            using (HttpClient client = new HttpClient(handler, testHandler == null))
            using (CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation))
            using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, TtsUri(endpoint)))
            {
                client.Timeout = Timeout.InfiniteTimeSpan;
                timeout.CancelAfter(TimeSpan.FromMinutes(3));
                request.Content = new StringContent(CreateRequest(text, referenceAudio, referenceText, rate), Encoding.UTF8, "application/json");
                try
                {
                    using (HttpResponseMessage response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false))
                    using (timeout.Token.Register(delegate { response.Dispose(); }))
                    {
                        if (!response.IsSuccessStatusCode)
                            throw new InvalidOperationException("本地音色服务返回错误（HTTP " + (int)response.StatusCode + "）。请检查服务窗口、参考台词和模型是否已加载。");
                        if (response.Content == null || (response.Content.Headers.ContentLength.HasValue && response.Content.Headers.ContentLength.Value > MaximumWaveBytes))
                            throw new InvalidOperationException("本地音色服务返回的音频为空或过大，请缩短配音文字。");
                        using (Stream source = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                        using (MemoryStream result = new MemoryStream())
                        {
                            byte[] buffer = new byte[16384]; int read;
                            while ((read = await source.ReadAsync(buffer, 0, buffer.Length, timeout.Token).ConfigureAwait(false)) != 0)
                            {
                                if (result.Length + read > MaximumWaveBytes)
                                    throw new InvalidOperationException("本地音色服务返回的音频过大，请缩短配音文字。");
                                result.Write(buffer, 0, read);
                            }
                            byte[] wave = result.ToArray();
                            ValidateWave(wave);
                            cancellation.ThrowIfCancellationRequested();
                            return wave;
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    if (cancellation.IsCancellationRequested) throw;
                    throw new InvalidOperationException("本地音色生成超过 3 分钟，请检查模型、显卡状态或缩短文字后重试。");
                }
                catch (HttpRequestException)
                {
                    throw new InvalidOperationException("无法连接本地 GPT-SoVITS。请先启动音色服务，再点击试听；不会切换到系统声音。");
                }
                catch (IOException)
                {
                    if (cancellation.IsCancellationRequested) throw new OperationCanceledException(cancellation);
                    if (timeout.IsCancellationRequested) throw new InvalidOperationException("本地音色生成超过 3 分钟，请检查服务或缩短文字后重试。");
                    throw new InvalidOperationException("本地音色服务的连接中断，请检查服务窗口后重试。");
                }
                catch (ObjectDisposedException)
                {
                    if (cancellation.IsCancellationRequested) throw new OperationCanceledException(cancellation);
                    if (timeout.IsCancellationRequested) throw new InvalidOperationException("本地音色生成超过 3 分钟，请检查服务或缩短文字后重试。");
                    throw new InvalidOperationException("本地音色服务的连接中断，请检查服务窗口后重试。");
                }
            }
        }

        internal static void ValidateWave(byte[] wave)
        {
            if (wave == null || wave.Length < 44 || wave.Length > MaximumWaveBytes ||
                Encoding.ASCII.GetString(wave, 0, 4) != "RIFF" || Encoding.ASCII.GetString(wave, 8, 4) != "WAVE")
                throw new InvalidOperationException("本地音色服务没有返回有效 WAV 音频。");
            bool format = false, data = false;
            using (BinaryReader reader = new BinaryReader(new MemoryStream(wave, false)))
            {
                reader.BaseStream.Position = 12;
                while (reader.BaseStream.Position + 8 <= wave.Length)
                {
                    string kind = Encoding.ASCII.GetString(reader.ReadBytes(4)); uint length = reader.ReadUInt32();
                    long next = reader.BaseStream.Position + length + (length & 1);
                    if (next > wave.Length) throw new InvalidOperationException("本地音色服务返回的 WAV 不完整。");
                    if (kind == "fmt " && length >= 16)
                    {
                        int encoding = reader.ReadUInt16(), channels = reader.ReadUInt16();
                        int samples = reader.ReadInt32(); reader.ReadInt32(); reader.ReadUInt16(); int bits = reader.ReadUInt16();
                        format = encoding == 1 && channels >= 1 && channels <= 2 && samples >= 8000 && samples <= 192000 && bits == 16;
                    }
                    if (kind == "data" && length >= 2) data = true;
                    reader.BaseStream.Position = next;
                }
            }
            if (!format || !data) throw new InvalidOperationException("本地音色服务须返回含声音数据的 16 位 PCM WAV。");
        }
    }
}
