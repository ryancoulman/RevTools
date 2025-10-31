using System.Windows;

namespace ValveGetter.UI
{
    public partial class NewProfileDialog : Window
    {
        public string ProfileName { get; private set; }
        public ProfileOption SettingsOption { get; private set; }

        public NewProfileDialog()
        {
            InitializeComponent();
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            ProfileName = ProfileNameTextBox.Text.Trim();
            SettingsOption = UseCurrentSettingsRadio.IsChecked == true
                         ? ProfileOption.UseCurrent
                         : ProfileOption.RestoreDefault;

            if (string.IsNullOrWhiteSpace(ProfileName))
            {
                MessageBox.Show("Please enter a profile name.", "Missing Name", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }

    public enum ProfileOption
    {
        UseCurrent,
        RestoreDefault
    }
}
