using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using ScreenCap.Capture;
using ScreenCap.Core;
using ScreenCap.Services;
using ScreenCap.UI;
using Forms = System.Windows.Forms;

namespace ScreenCap
{
    public partial class App : Application
    {
        private Mutex _mutex;
        private bool _ownsMutex;
        private EventWaitHandle _activateEvent;
        private Thread _activateThread;
        private volatile bool _exiting;
        private bool _updateChecking;
        private AppSettings _settings;
        private TrayIcon _tray;
        private HotkeyWindow _hotkey;
        private CaptureController _controller;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            if (!CreateSingleInstance())
            {
                Shutdown();
                return;
            }

            DispatcherUnhandledException += OnDispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += (s, args) =>
                Log.Error("未处理异常", args.ExceptionObject as Exception);

            _settings = AppSettings.Load();
            Log.Info("ScreenCap 启动");

            ClipboardService.CleanupOldFiles(TimeSpan.FromDays(1));
            Log.Info("启动时清理缓存");
            CacheManager.Cleanup(_settings.CacheFolder);

            _controller = new CaptureController(_settings);
            _tray = new TrayIcon(
                () => Dispatcher.BeginInvoke(new Action(() => _controller.Toggle())),
                () => Dispatcher.BeginInvoke(new Action(() => CheckForUpdates(true))),
                () => Dispatcher.BeginInvoke(new Action(OpenSettings)),
                OpenRecordingsFolder,
                () => Dispatcher.BeginInvoke(new Action(ExitApplication)));

            _hotkey = new HotkeyWindow();
            _hotkey.Pressed += () => Dispatcher.BeginInvoke(new Action(() => _controller.Toggle()));
            if (_hotkey.Register(_settings.Hotkey))
            {
                Log.Info("热键注册成功: " + _settings.Hotkey);
                _tray.ShowMessage("ScreenCap 已启动", $"{_settings.Hotkey} 框选区域开始录制");
            }
            else
            {
                Log.Error("热键注册失败: " + _settings.Hotkey);
                _tray.ShowMessage(
                    "热键被占用",
                    $"{_settings.Hotkey} 注册失败，可能被其它程序占用。可编辑 %LOCALAPPDATA%\\ScreenCap\\settings.json 更换组合键。",
                    Forms.ToolTipIcon.Warning);
            }

            StartActivationListener();

            OpenEditorFromArguments(e.Args);

            Dispatcher.BeginInvoke(new Action(async () =>
            {
                await Task.Delay(2500);
                CheckForUpdates(false);
            }));
        }

        private async void CheckForUpdates(bool manual)
        {
            if (_updateChecking || _exiting)
            {
                return;
            }

            _updateChecking = true;
            try
            {
                UpdateInfo info;
                using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
                {
                    info = await Task.Run(() => UpdateService.CheckAsync(cts.Token));
                }

                Log.Info("更新检查结果: " + (info == null ? "无新版本" : "发现 " + info.Version));
                if (info == null)
                {
                    if (manual)
                    {
                        MessageBox.Show(
                            "当前已是最新版本（" + VersionInfo.Current + "），或暂时无法连接更新服务器。",
                            "ScreenCap 更新",
                            MessageBoxButton.OK,
                            MessageBoxImage.Information);
                    }
                    return;
                }

                string notes = string.IsNullOrWhiteSpace(info.Notes) ? "" : info.Notes.Trim() + "\n\n";
                var result = MessageBox.Show(
                    "发现新版本 " + info.Version + "（当前 " + VersionInfo.Current + "）。\n\n" +
                    notes +
                    "是否立即更新？程序会自动下载并重启，无需重新安装。",
                    "ScreenCap 更新",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Information);
                if (result != MessageBoxResult.Yes)
                {
                    return;
                }

                string currentExe = Process.GetCurrentProcess().MainModule.FileName;
                _tray?.ShowMessage("正在下载更新", "ScreenCap " + info.Version + " 正在下载，完成后会自动重启。");
                string newExe = await UpdateService.DownloadAsync(info, null, CancellationToken.None);
                UpdateService.Apply(newExe, currentExe, Process.GetCurrentProcess().Id);
                _exiting = true;
                Shutdown();
            }
            catch (Exception ex)
            {
                Log.Error("自动更新失败", ex);
                MessageBox.Show(
                    "更新失败：\n" + ex.Message + "\n\n请稍后重试。",
                    "ScreenCap 更新",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                _updateChecking = false;
            }
        }

        private void OpenSettings()
        {
            try
            {
                var window = new HotkeySettingsWindow(_settings, ApplyHotkey);
                window.ShowDialog();
            }
            catch (Exception ex)
            {
                Log.Error("打开设置失败", ex);
            }
        }

        private string ApplyHotkey(string text)
        {
            if (!HotkeyParser.TryParse(text, out uint modifiers, out uint key))
            {
                return "无法识别的组合键，请重新按下。";
            }

            if (_hotkey.Register(modifiers, key))
            {
                _settings.Hotkey = text;
                _settings.Save();
                _tray?.SetHotkey(text);
                _tray?.ShowMessage("快捷键已更新", text);
                Log.Info("快捷键已更新: " + text);
                return null;
            }

            if (HotkeyParser.TryParse(_settings.Hotkey, out uint oldModifiers, out uint oldKey))
            {
                _hotkey.Register(oldModifiers, oldKey);
            }
            return "注册失败，可能被其它程序占用，请换一个组合键。";
        }

        private void OpenEditorFromArguments(string[] args)
        {
            if (args == null)
            {
                return;
            }

            for (int i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], "--hotkeys", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(args[i], "--settings", StringComparison.OrdinalIgnoreCase))
                {
                    Dispatcher.BeginInvoke(new Action(OpenSettings));
                    return;
                }

                if (string.Equals(args[i], "--edit", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    string path = args[i + 1];
                    if (File.Exists(path))
                    {
                        new EditorWindow(path, TimeSpan.FromSeconds(10), _settings, false).Show();
                    }
                    return;
                }
            }
        }

        private bool CreateSingleInstance()
        {
            _mutex = new Mutex(true, @"Local\ScreenCap.SingleInstance", out bool created);
            if (created)
            {
                _ownsMutex = true;
                return true;
            }

            try
            {
                EventWaitHandle.OpenExisting(@"Local\ScreenCap.Activate").Set();
            }
            catch
            {
            }

            _mutex.Dispose();
            _mutex = null;
            return false;
        }

        private void StartActivationListener()
        {
            _activateEvent = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\ScreenCap.Activate");
            _activateThread = new Thread(() =>
            {
                while (!_exiting)
                {
                    try
                    {
                        if (!_activateEvent.WaitOne(500))
                        {
                            continue;
                        }
                    }
                    catch
                    {
                        break;
                    }

                    if (_exiting)
                    {
                        break;
                    }

                    Dispatcher.BeginInvoke(new Action(() => _controller.Toggle()));
                }
            })
            {
                IsBackground = true,
                Name = "ScreenCap.ActivateListener"
            };
            _activateThread.Start();
        }

        private void OpenRecordingsFolder()
        {
            try
            {
                string folder = _settings.ResolvedRecordingsFolder;
                Directory.CreateDirectory(folder);
                Process.Start("explorer.exe", "\"" + folder + "\"");
            }
            catch (Exception ex)
            {
                Log.Error("打开录制文件夹失败", ex);
            }
        }

        private void ExitApplication()
        {
            if (_controller != null && _controller.IsBusy)
            {
                var result = MessageBox.Show("当前正在框选或录制中，确定要退出吗？", "ScreenCap", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (result != MessageBoxResult.Yes)
                {
                    return;
                }
            }
            Shutdown();
        }

        private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            Log.Error("UI 线程异常", e.Exception);
            MessageBox.Show("发生错误：\n" + e.Exception.Message + "\n\n详细信息已写入：\n" + Log.LogFilePath,
                "ScreenCap", MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true;
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _exiting = true;

            try
            {
                _controller?.Dispose();
            }
            catch
            {
            }

            try
            {
                _hotkey?.Dispose();
            }
            catch
            {
            }

            try
            {
                _tray?.Dispose();
            }
            catch
            {
            }

            try
            {
                _settings?.Save();
            }
            catch
            {
            }

            try
            {
                if (_settings != null)
                {
                    Log.Info("退出时清理缓存");
                    CacheManager.Cleanup(_settings.CacheFolder);
                }
            }
            catch
            {
            }

            try
            {
                _activateEvent?.Set();
                _activateThread?.Join(300);
                _activateEvent?.Dispose();
            }
            catch
            {
            }

            try
            {
                if (_ownsMutex)
                {
                    _mutex?.ReleaseMutex();
                }
                _mutex?.Dispose();
            }
            catch
            {
            }

            Log.Info("ScreenCap 退出");
            base.OnExit(e);
        }
    }
}
