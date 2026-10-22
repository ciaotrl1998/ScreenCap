using System;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using ScreenCap.Core;

namespace ScreenCap.Services
{
    public static class UpdateService
    {
        private const string DefaultVersionUrl = "https://raw.githubusercontent.com/ciaotrl1998/ScreenCap/main/version.json";

        private static string VersionUrl
        {
            get
            {
                string overrideUrl = Environment.GetEnvironmentVariable("SCREENCAP_UPDATE_VERSION_URL");
                return string.IsNullOrWhiteSpace(overrideUrl) ? DefaultVersionUrl : overrideUrl;
            }
        }

        public static async Task<UpdateInfo> CheckAsync(CancellationToken token)
        {
            try
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                Log.Info("更新检查: " + VersionUrl);
                string json = await DownloadStringAsync(VersionUrl, token).ConfigureAwait(false);
                json = json.TrimStart('\uFEFF');
                Log.Info("更新检查响应长度: " + (json == null ? 0 : json.Length));
                var info = JsonConvert.DeserializeObject<UpdateInfo>(json);
                if (info == null ||
                    string.IsNullOrWhiteSpace(info.Version) ||
                    string.IsNullOrWhiteSpace(info.Url))
                {
                    return null;
                }
                return IsNewer(info.Version) ? info : null;
            }
            catch (Exception ex)
            {
                Log.Error("更新检查失败", ex);
                return null;
            }
        }

        public static bool IsNewer(string version)
        {
            try
            {
                return Version.Parse(version) > Version.Parse(VersionInfo.Current);
            }
            catch
            {
                return false;
            }
        }

        public static async Task<string> DownloadAsync(UpdateInfo info, IProgress<double> progress, CancellationToken token)
        {
            string directory = Path.Combine(Path.GetTempPath(), "ScreenCapUpdate");
            Directory.CreateDirectory(directory);
            string target = Path.Combine(directory, "ScreenCap_" + info.Version + ".exe");
            Log.Info("开始下载更新: " + info.Url + " -> " + target);
            if (File.Exists(target))
            {
                File.Delete(target);
            }

            using (var client = new WebClient())
            {
                client.Headers.Add("User-Agent", "ScreenCap-Updater");
                client.DownloadProgressChanged += (s, e) =>
                {
                    if (e.TotalBytesToReceive > 0)
                    {
                        progress?.Report(e.BytesReceived / (double)e.TotalBytesToReceive);
                    }
                };

                var completion = new TaskCompletionSource<bool>();
                client.DownloadFileCompleted += (s, e) =>
                {
                    if (e.Error != null)
                    {
                        completion.TrySetException(e.Error);
                    }
                    else if (e.Cancelled)
                    {
                        completion.TrySetCanceled();
                    }
                    else
                    {
                        completion.TrySetResult(true);
                    }
                };

                using (token.Register(() =>
                {
                    try
                    {
                        client.CancelAsync();
                    }
                    catch
                    {
                    }
                }))
                {
                    client.DownloadFileAsync(new Uri(info.Url), target);
                    await completion.Task.ConfigureAwait(false);
                }
            }

            if (!string.IsNullOrWhiteSpace(info.Sha256))
            {
                string actual = ComputeSha256(target);
                if (!string.Equals(actual, info.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        File.Delete(target);
                    }
                    catch
                    {
                    }
                    throw new InvalidOperationException("更新包校验失败，已取消更新。");
                }
            }

            return target;
        }

        public static void Apply(string newExePath, string currentExePath, int currentPid)
        {
            string appDirectory = Path.GetDirectoryName(currentExePath);
            if (string.IsNullOrEmpty(appDirectory))
            {
                throw new InvalidOperationException("无法定位程序目录。");
            }

            string probe = Path.Combine(appDirectory, ".update_write_test");
            using (File.Create(probe))
            {
            }
            File.Delete(probe);

            string workDirectory = Path.Combine(Path.GetTempPath(), "ScreenCapUpdate");
            Directory.CreateDirectory(workDirectory);
            string scriptPath = Path.Combine(workDirectory, "apply_" + Guid.NewGuid().ToString("N") + ".ps1");
            string backupPath = currentExePath + ".old";

            string script =
                "$ErrorActionPreference = 'SilentlyContinue'\r\n" +
                "while (Get-Process -Id " + currentPid + " -ErrorAction SilentlyContinue) { Start-Sleep -Milliseconds 400 }\r\n" +
                "Start-Sleep -Milliseconds 600\r\n" +
                "Stop-Process -Name ScreenCap -Force -ErrorAction SilentlyContinue\r\n" +
                "Move-Item -LiteralPath " + Quote(currentExePath) + " -Destination " + Quote(backupPath) + " -Force\r\n" +
                "Copy-Item -LiteralPath " + Quote(newExePath) + " -Destination " + Quote(currentExePath) + " -Force\r\n" +
                "Start-Process -FilePath " + Quote(currentExePath) + "\r\n" +
                "Remove-Item -LiteralPath " + Quote(backupPath) + " -Force\r\n" +
                "Remove-Item -LiteralPath $PSCommandPath -Force\r\n";

            File.WriteAllText(scriptPath, script, new UTF8Encoding(true));

            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"" + scriptPath + "\"",
                CreateNoWindow = true,
                UseShellExecute = false,
                WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden,
                WorkingDirectory = workDirectory
            };
            Log.Info("启动更新器: " + scriptPath);
            System.Diagnostics.Process.Start(startInfo);
        }

        private static string Quote(string value)
        {
            return "'" + value.Replace("'", "''") + "'";
        }

        private static string ComputeSha256(string path)
        {
            using (var sha = SHA256.Create())
            using (var stream = File.OpenRead(path))
            {
                byte[] hash = sha.ComputeHash(stream);
                var builder = new StringBuilder(hash.Length * 2);
                foreach (byte value in hash)
                {
                    builder.Append(value.ToString("x2"));
                }
                return builder.ToString();
            }
        }

        private static async Task<string> DownloadStringAsync(string url, CancellationToken token)
        {
            using (var client = new WebClient())
            {
                client.Encoding = Encoding.UTF8;
                client.Headers.Add("User-Agent", "ScreenCap-Updater");
                var completion = new TaskCompletionSource<string>();
                client.DownloadStringCompleted += (s, e) =>
                {
                    if (e.Error != null)
                    {
                        completion.TrySetException(e.Error);
                    }
                    else if (e.Cancelled)
                    {
                        completion.TrySetCanceled();
                    }
                    else
                    {
                        completion.TrySetResult(e.Result);
                    }
                };

                using (token.Register(() =>
                {
                    try
                    {
                        client.CancelAsync();
                    }
                    catch
                    {
                    }
                }))
                {
                    client.DownloadStringAsync(new Uri(url));
                    return await completion.Task.ConfigureAwait(false);
                }
            }
        }
    }
}
