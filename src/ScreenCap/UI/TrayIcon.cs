using System;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace ScreenCap.UI
{
    public sealed class TrayIcon : IDisposable
    {
        private readonly NotifyIcon _notifyIcon;
        private DateTime _lastStartClick = DateTime.MinValue;

        public TrayIcon(Action startRecording, Action checkUpdate, Action openHotkeySettings, Action openFolder, Action exit)
        {
            var menu = new ContextMenuStrip();
            menu.Items.Add("开始录制", null, (s, e) => startRecording());
            menu.Items.Add("检查更新", null, (s, e) => checkUpdate());
            menu.Items.Add("设置", null, (s, e) => openHotkeySettings());
            menu.Items.Add("打开录制文件夹", null, (s, e) => openFolder());
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("退出", null, (s, e) => exit());

            _notifyIcon = new NotifyIcon
            {
                Icon = LoadIcon(),
                Text = "ScreenCap · Alt+Shift+Q 录屏",
                Visible = true,
                ContextMenuStrip = menu
            };
            _notifyIcon.MouseClick += (s, e) =>
            {
                if (e.Button != MouseButtons.Left)
                {
                    return;
                }
                if ((DateTime.Now - _lastStartClick).TotalMilliseconds < 350)
                {
                    return;
                }
                _lastStartClick = DateTime.Now;
                startRecording();
            };

            Task.Delay(2000).ContinueWith(_ => PromoteTrayIcon());
        }

        private static void PromoteTrayIcon()
        {
            try
            {
                if (Environment.OSVersion.Version.Build < 22000)
                {
                    return;
                }

                string exe = Application.ExecutablePath;
                using (var root = Registry.CurrentUser.OpenSubKey(@"Control Panel\NotifyIconSettings", true))
                {
                    if (root == null)
                    {
                        return;
                    }

                    foreach (var name in root.GetSubKeyNames())
                    {
                        using (var key = root.OpenSubKey(name, true))
                        {
                            if (key == null)
                            {
                                continue;
                            }

                            string path = key.GetValue("ExecutablePath") as string;
                            if (!string.IsNullOrEmpty(path) &&
                                string.Equals(Path.GetFullPath(path), Path.GetFullPath(exe), StringComparison.OrdinalIgnoreCase))
                            {
                                key.SetValue("IsPromoted", 1, RegistryValueKind.DWord);
                            }
                        }
                    }
                }
            }
            catch
            {
            }
        }

        public void SetHotkey(string hotkey)
        {
            try
            {
                _notifyIcon.Text = "ScreenCap · " + hotkey + " 录屏";
            }
            catch
            {
            }
        }

        public void ShowMessage(string title, string text, ToolTipIcon icon = ToolTipIcon.Info)
        {
            try
            {
                _notifyIcon.BalloonTipTitle = title;
                _notifyIcon.BalloonTipText = text;
                _notifyIcon.BalloonTipIcon = icon;
                _notifyIcon.ShowBalloonTip(4000);
            }
            catch
            {
            }
        }

        private static Icon LoadIcon()
        {
            try
            {
                var icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                if (icon != null)
                {
                    return icon;
                }
            }
            catch
            {
            }
            return SystemIcons.Application;
        }

        public void Dispose()
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
        }
    }
}
