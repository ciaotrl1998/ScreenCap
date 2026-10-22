using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using ScreenCap.Core;

namespace ScreenCap.UI
{
    public partial class HotkeySettingsWindow : Window
    {
        private readonly Func<string, string> _apply;
        private readonly AppSettings _settings;
        private string _combo;

        public HotkeySettingsWindow(AppSettings settings, Func<string, string> apply)
        {
            InitializeComponent();
            _settings = settings;
            _combo = settings.Hotkey;
            _apply = apply;
            ComboText.Text = _combo;
            QualitySlider.Value = Math.Max(10, Math.Min(100, settings.Quality));
            QualityText.Text = ((int)Math.Round(QualitySlider.Value)).ToString();
            SoundCheck.IsChecked = settings.RecordSystemAudio;
            PreviewKeyDown += OnPreviewKeyDown;
            Loaded += (s, e) =>
            {
                Activate();
                Keyboard.Focus(CaptureBox);
            };
        }

        private void Quality_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (QualityText != null)
            {
                QualityText.Text = ((int)Math.Round(e.NewValue)).ToString();
            }
        }

        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            e.Handled = true;

            if (e.Key == Key.Escape)
            {
                Close();
                return;
            }

            Key key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (IsModifier(key))
            {
                return;
            }

            var modifiers = Keyboard.Modifiers;
            if (modifiers == ModifierKeys.None)
            {
                ShowError("组合键至少需要 Ctrl / Alt / Shift / Win 中的一个。");
                return;
            }

            string keyName = GetKeyName(key);
            if (string.IsNullOrEmpty(keyName))
            {
                return;
            }

            _combo = Compose(modifiers, keyName);
            ComboText.Text = _combo;
            ErrorText.Visibility = Visibility.Collapsed;
        }

        private static bool IsModifier(Key key)
        {
            switch (key)
            {
                case Key.LeftCtrl:
                case Key.RightCtrl:
                case Key.LeftShift:
                case Key.RightShift:
                case Key.LeftAlt:
                case Key.RightAlt:
                case Key.LWin:
                case Key.RWin:
                    return true;
                default:
                    return false;
            }
        }

        private static string GetKeyName(Key key)
        {
            if (key >= Key.A && key <= Key.Z)
            {
                return key.ToString();
            }
            if (key >= Key.D0 && key <= Key.D9)
            {
                return ((char)('0' + (key - Key.D0))).ToString();
            }
            if (key >= Key.NumPad0 && key <= Key.NumPad9)
            {
                return "NumPad" + (key - Key.NumPad0);
            }
            if (key >= Key.F1 && key <= Key.F24)
            {
                return key.ToString();
            }

            switch (key)
            {
                case Key.Space:
                    return "Space";
                case Key.Tab:
                    return "Tab";
                case Key.Enter:
                    return "Enter";
                case Key.Back:
                    return "Back";
                case Key.Insert:
                    return "Insert";
                case Key.Delete:
                    return "Delete";
                case Key.Home:
                    return "Home";
                case Key.End:
                    return "End";
                case Key.PageUp:
                    return "PageUp";
                case Key.PageDown:
                    return "PageDown";
                case Key.Up:
                    return "Up";
                case Key.Down:
                    return "Down";
                case Key.Left:
                    return "Left";
                case Key.Right:
                    return "Right";
                default:
                    return key.ToString();
            }
        }

        private static string Compose(ModifierKeys modifiers, string keyName)
        {
            var parts = new List<string>();
            if (modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
            if (modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
            if (modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
            if (modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
            parts.Add(keyName);
            return string.Join("+", parts);
        }

        private void ShowError(string message)
        {
            ErrorText.Text = message;
            ErrorText.Visibility = Visibility.Visible;
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            string error = _apply(_combo);
            if (error == null)
            {
                _settings.Quality = (int)Math.Round(QualitySlider.Value);
                _settings.RecordSystemAudio = SoundCheck.IsChecked == true;
                _settings.Save();
                DialogResult = true;
                Close();
                return;
            }
            ShowError(error);
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                try
                {
                    DragMove();
                }
                catch
                {
                }
            }
        }

        private void CaptureBox_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            Activate();
            Keyboard.Focus(CaptureBox);
        }
    }
}
