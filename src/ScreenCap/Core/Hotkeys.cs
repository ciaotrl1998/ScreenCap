using System;
using System.Windows.Interop;
using ScreenCap.Core;

namespace ScreenCap.Core
{
    public sealed class HotkeyWindow : IDisposable
    {
        private const int HotkeyId = 0x53C0;
        private readonly HwndSource _source;
        private bool _registered;

        public event Action Pressed;

        public HotkeyWindow()
        {
            var parameters = new HwndSourceParameters("ScreenCapHotkey")
            {
                Width = 0,
                Height = 0,
                PositionX = 0,
                PositionY = 0,
                WindowStyle = 0,
                ParentWindow = Native.HWND_MESSAGE
            };
            _source = new HwndSource(parameters);
            _source.AddHook(WndProc);
        }

        public bool Register(string hotkeyText)
        {
            if (!HotkeyParser.TryParse(hotkeyText, out uint modifiers, out uint key))
            {
                return false;
            }
            return Register(modifiers, key);
        }

        public bool Register(uint modifiers, uint key)
        {
            Unregister();
            if (Native.TryRegisterHotkey(_source.Handle, HotkeyId, modifiers, key))
            {
                _registered = true;
                return true;
            }
            return false;
        }

        public void Unregister()
        {
            if (_registered)
            {
                Native.UnregisterHotkey(_source.Handle, HotkeyId);
                _registered = false;
            }
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == Native.WM_HOTKEY && wParam.ToInt32() == HotkeyId)
            {
                handled = true;
                Pressed?.Invoke();
            }
            return IntPtr.Zero;
        }

        public void Dispose()
        {
            Unregister();
            _source.RemoveHook(WndProc);
            _source.Dispose();
        }
    }

    public static class HotkeyParser
    {
        public static bool TryParse(string text, out uint modifiers, out uint key)
        {
            modifiers = Native.MOD_NOREPEAT;
            key = 0;
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            foreach (var part in text.Split(new[] { '+' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string token = part.Trim();
                if (token.Length == 0)
                {
                    continue;
                }

                switch (token.ToLowerInvariant())
                {
                    case "alt":
                        modifiers |= Native.MOD_ALT;
                        break;
                    case "ctrl":
                    case "control":
                        modifiers |= Native.MOD_CONTROL;
                        break;
                    case "shift":
                        modifiers |= Native.MOD_SHIFT;
                        break;
                    case "win":
                    case "windows":
                        modifiers |= Native.MOD_WIN;
                        break;
                    default:
                        if (key != 0 || !TryParseKey(token, out key))
                        {
                            return false;
                        }
                        break;
                }
            }

            return key != 0 && (modifiers & ~Native.MOD_NOREPEAT) != 0;
        }

        private static bool TryParseKey(string token, out uint key)
        {
            key = 0;
            if (token.Length == 1)
            {
                char c = char.ToUpperInvariant(token[0]);
                if ((c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9'))
                {
                    key = c;
                    return true;
                }
            }

            if (Enum.TryParse(token, true, out System.Windows.Forms.Keys parsed))
            {
                uint value = (uint)parsed;
                if (value > 0 && value <= 0xFF)
                {
                    key = value;
                    return true;
                }
            }
            return false;
        }
    }
}
