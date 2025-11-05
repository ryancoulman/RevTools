using System;
using System.Windows;
using System.Windows.Controls;

namespace ValveGetter.UI
{
    public partial class DebugWindow : Window
    {
        public DebugWindow(string title, string content)
        {
            InitializeComponent();

            Title = title;
            OutputTextBox.Text = content;

            // Set window size based on screen
            Width = SystemParameters.WorkArea.Width * 0.6;
            Height = SystemParameters.WorkArea.Height * 0.7;

            // Center window
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        private void CopyButton_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(OutputTextBox.Text))
            {
                Clipboard.SetText(OutputTextBox.Text);
                CopyButton.Content = "Copied!";

                // Reset button text after 2 seconds
                var timer = new System.Windows.Threading.DispatcherTimer();
                timer.Interval = TimeSpan.FromSeconds(2);
                timer.Tick += (s, args) =>
                {
                    CopyButton.Content = "Copy All";
                    timer.Stop();
                };
                timer.Start();
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void SelectAllButton_Click(object sender, RoutedEventArgs e)
        {
            // Only select if not already fully selected
            if (OutputTextBox.SelectionLength != OutputTextBox.Text.Length)
            {
                OutputTextBox.SelectAll();
            }

            OutputTextBox.Focus();
        }

    }
}