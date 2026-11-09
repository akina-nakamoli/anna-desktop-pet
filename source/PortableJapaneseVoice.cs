using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AnnaDesktopPet
{
    internal static class PortableJapaneseVoice
    {
        private static string DirectoryPath { get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ja-tts"); } }
        internal static bool IsBundled { get { return File.Exists(Path.Combine(DirectoryPath, "tts.py")); } }
        internal static bool Available { get { return IsBundled && File.Exists(Path.Combine(DirectoryPath, "python.exe")) && File.Exists(Path.Combine(DirectoryPath, "pyopenjtalk", "dictionary", "sys.dic")); } }

        internal static async Task<byte[]> GenerateAsync(string text, int rate, CancellationToken token)
        {
            if (!Available) throw new InvalidOperationException("离线日语组件缺失，请完整解压新版 ZIP。");
            string speed = (1.0 + rate * 0.10).ToString(CultureInfo.InvariantCulture);
            var info = new ProcessStartInfo(Path.Combine(DirectoryPath, "python.exe"), "-I -B tts.py " + speed)
            {
                WorkingDirectory = DirectoryPath,
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(token))
            using (var process = new Process { StartInfo = info })
            using (var wave = new MemoryStream())
            {
                token.ThrowIfCancellationRequested();
                process.Start();
                timeout.CancelAfter(TimeSpan.FromSeconds(25));
                using (timeout.Token.Register(delegate { try { if (!process.HasExited) process.Kill(); } catch (InvalidOperationException) { } catch (System.ComponentModel.Win32Exception) { } }))
                {
                    Task output = process.StandardOutput.BaseStream.CopyToAsync(wave);
                    Task<string> error = process.StandardError.ReadToEndAsync();
                    byte[] input = Encoding.UTF8.GetBytes(text);
                    try
                    {
                        await process.StandardInput.BaseStream.WriteAsync(input, 0, input.Length, timeout.Token).ConfigureAwait(false);
                        process.StandardInput.Close();
                        await Task.WhenAll(output, error).ConfigureAwait(false);
                        process.WaitForExit();
                    }
                    catch (IOException)
                    {
                        bool expired = timeout.IsCancellationRequested;
                        timeout.Cancel();
                        token.ThrowIfCancellationRequested();
                        if (expired) throw new InvalidOperationException("离线日语合成超时，请重试。");
                        throw;
                    }
                    catch (OperationCanceledException)
                    {
                        token.ThrowIfCancellationRequested();
                        throw new InvalidOperationException("离线日语合成超时，请重试。");
                    }
                    token.ThrowIfCancellationRequested();
                    if (timeout.IsCancellationRequested) throw new InvalidOperationException("离线日语合成超时，请重试。");
                    if (process.ExitCode != 0)
                        throw new InvalidOperationException("离线日语合成失败，请确认 ZIP 已完整解压。");
                    byte[] result = wave.ToArray();
                    GptSovitsClient.ValidateWave(result);
                    return result;
                }
            }
        }
    }
}
