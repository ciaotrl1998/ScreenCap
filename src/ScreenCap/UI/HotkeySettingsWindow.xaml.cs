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
        private bool _gifUpdating;

        private static readonly (int Colors, bool Dither, int Width)[] GifPresets =
        {
            (32, false, 320),
            (64, false, 480),
            (64, true, 480),
            (96, true, 640),
            (128, true, 640),
            (128, true, 800),
            (192, true, 960),
            (256, true, 960),
            (256, true, 1280),
            (256, true, 1920)
        };

        public HotkeySettingsWindow(AppSettings settings, Func<string, string> apply)
        {
            InitializeComponent();
            _settings = settings;
            _combo = settings.Hotkey;
            _apply = apply;
            ComboText.Text = _combo;
            QualitySlider.Value = Math.Max(10, Math.Min(100, settings.Quality));
            GifFpsSlider.Value = Math.Max(5, Math.Min(30, settings.GifFramerate));
            QualityText.Text = ((int)Math.Round(QualitySlider.Value)).ToString();
            GifFpsText.Text = ((int)Math.Round(GifFpsSlider.Value)).ToString();
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

        private void GifLevel_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_gifUpdating || GifColorsSlider == null)
            {
                return;
            }

            int level = (int)Math.Round(e.NewValue);
            if (level < 1 || level > 10)
            {
                return;
            }

            var preset = GifPresets[level - 1];
            _settings.GifQualityLevel = level;
            _gifUpdating = true;
            GifColorsSlider.Value = preset.Colors;
            GifDitherCheck.IsChecked = preset.Dither;
            GifWidthSlider.Value = preset.Width;
            _gifUpdating = false;
            UpdateGifQualityText();
        }

        private void GifColors_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (GifColorsText != null)
            {
                GifColorsText.Text = ((int)Math.Round(e.NewValue)).ToString();
            }
            MarkGifCustom();
        }

        private void GifWidth_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (GifWidthText != null)
            {
                GifWidthText.Text = ((int)Math.Round(e.NewValue)).ToString();
            }
            MarkGifCustom();
        }

        private void GifDither_Changed(object sender, RoutedEventArgs e)
        {
            MarkGifCustom();
        }

        private void MarkGifCustom()
        {
            if (_gifUpdating || _settings == null)
            {
                return;
            }
            _settings.GifQualityLevel = 0;
            UpdateGifQualityText();
        }

        private void UpdateGifQualityText()
        {
            int level = (int)Math.Round(GifLevelSlider.Value);
            int colors = (int)Math.Round(GifColorsSlider.Value);
            bool dither = GifDitherCheck.IsChecked == true;
            int width = (int)Math.Round(GifWidthSlider.Value);
            GifLevelText.Text = _settings.GifQualityLevel == 0 ? "自定义" : level.ToString();
            GifPresetHint.Text = colors + " 色 · " + (dither ? "抖动" : "无抖动") + " · " + width + " px";
        }

        private void GifExpand_Click(object sender, RoutedEventArgs e)
        {
            bool show = GifCustomPanel.Visibility != Visibility.Visible;
            GifCustomPanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            GifExpandButton.Content = show ? "▴" : "▾";
        }

        private void GifFps_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (GifFpsText != null)
            {
                GifFpsText.Text = ((int)Math.Round(e.NewValue)).ToString();
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
                _settings.GifFramerate = (int)Math.Round(GifFpsSlider.Value);
                _settings.GifMaxColors = (int)Math.Round(GifColorsSlider.Value);
                _settings.GifDither = GifDitherCheck.IsChecked == true;
                _settings.GifMaxWidth = (int)Math.Round(GifWidthSlider.Value);
                int level = (int)Math.Round(GifLevelSlider.Value);
                _settings.GifQualityLevel = GifLevelText.Text == "自定义" ? 0 : level;
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
