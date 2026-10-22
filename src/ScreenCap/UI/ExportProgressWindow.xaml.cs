using System;
using System.Windows;

namespace ScreenCap.UI
{
    public partial class ExportProgressWindow : Window
    {
        public event Action CancelClicked;

        public ExportProgressWindow()
        {
            InitializeComponent();
        }

        public void SetTitle(string title)
        {
            TitleText.Text = title;
        }

        public void SetProgress(double ratio)
        {
            double value = Math.Max(0, Math.Min(1, ratio)) * 100;
            Progress.Value = value;
            PercentText.Text = value.ToString("0") + "%";
        }

        public void SetCancelEnabled(bool enabled)
        {
            CancelButton.IsEnabled = enabled;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            CancelClicked?.Invoke();
        }
    }
}
