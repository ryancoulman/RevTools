using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using ValveGetter.Settings;

namespace ValveGetter.UI
{
    public partial class AdvancedSettingsForm : Window
    {
        private readonly Document _doc;
        private ValveServiceSettings _settings;
        private bool _isLoadingProfile = false;
        private const string DEFAULT_PROFILE_NAME = "<Default>";

        public ValveServiceSettings Settings => _settings;
        public bool RunRequested { get; private set; }

        public AdvancedSettingsForm(Document doc)
        {
            _doc = doc;
            InitializeComponent();
            LoadSettings();
            LoadProfileList();
            PopulateUI();
        }

        private void LoadSettings()
        {
            try
            {
                _settings = ValveServiceSettingsManager.LoadDefault();
            }
            catch
            {
                _settings = ValveServiceSettingsManager.CreateDefault();
            }
        }

        private void LoadProfileList()
        {
            _isLoadingProfile = true;

            cmbProfiles.Items.Clear();

            // Add default profile
            cmbProfiles.Items.Add(DEFAULT_PROFILE_NAME);

            // Add saved profiles
            var profiles = ValveServiceSettingsManager.GetAvailableProfiles();
            foreach (var profile in profiles)
            {
                cmbProfiles.Items.Add(profile);
            }

            // Select default
            cmbProfiles.SelectedIndex = 0;

            _isLoadingProfile = false;
        }

        private void PopulateUI()
        {
            // Tolerance settings
            txtTolerance.Text = _settings.ToleranceMm.ToString();
            txtTouchingDistance.Text = _settings.TouchingDistMm.ToString();

            // Debug mode
            switch (_settings.DebugMode)
            {
                case DebugLevel.None:
                    rbDebugNone.IsChecked = true;
                    break;
                case DebugLevel.Concise:
                    rbDebugConcise.IsChecked = true;
                    break;
                case DebugLevel.Full:
                    rbDebugFull.IsChecked = true;
                    break;
            }

            // Scope
            switch (_settings.CollectionScope)
            {
                case ValveCollectionScope.EntireProject:
                    rbScopeProject.IsChecked = true;
                    break;
                case ValveCollectionScope.ActiveView:
                    rbScopeActive.IsChecked = true;
                    break;
            }
            chkEnableSelectionOverride.IsChecked = _settings.AllowSelectionOverrides;

            // Parameters
            chkWriteToParameters.IsChecked = _settings.WriteToParameters;
            txtInputParameter.Text = _settings.InputParameter.ParameterName;
            txtOutputParameter.Text = _settings.OutputParameter.ParameterName;

            // Category filters
            RefreshValveCategoryList();
            RefreshMEPCategoryList();
        }

        private void RefreshValveCategoryList()
        {
            lstValveCategories.Items.Clear();

            foreach (var filter in _settings.ValveCategoryFilters)
            {
                string displayText = GetCategoryDisplayName(filter);
                var item = new ListBoxItem { Content = displayText, Tag = filter };
                lstValveCategories.Items.Add(item);
            }
        }

        private void RefreshMEPCategoryList()
        {
            lstMEPCategories.Items.Clear();

            foreach (var filter in _settings.MEPCategoryFilters)
            {
                var item = new ListBoxItem { Content = filter.CategoryName, Tag = filter };
                lstMEPCategories.Items.Add(item);
            }
        }


        private void OnSettingChanged(object sender, RoutedEventArgs e)
        {
            // Called when any setting is changed - hook for future features
        }

        private string GetCategoryDisplayName(CategoryFilter filter)
        {
            string catName = filter.CategoryName;
            string condition = "";

            if (!string.IsNullOrEmpty(filter.NameCondition))
            {
                string target = filter.ConditionTarget == FilterTarget.FamilyName ? "Family" : "Type";
                condition = $" | {target} contains '{filter.NameCondition}'";
            }

            return $"{catName}{condition}";
        }

        // ==================== PROFILE MANAGEMENT ====================

        private void CmbProfiles_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoadingProfile) return;
            if (cmbProfiles.SelectedItem == null) return;

            string selectedProfile = cmbProfiles.SelectedItem.ToString();

            // Enable/disable delete based on selection
            bool isDefault = selectedProfile == DEFAULT_PROFILE_NAME;
            btnDeleteProfile.IsEnabled = !isDefault;

            try
            {
                if (isDefault)
                {
                    _settings = ValveServiceSettingsManager.LoadDefault();
                }
                else
                {
                    _settings = ValveServiceSettingsManager.LoadProfile(selectedProfile);
                }

                PopulateUI();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to load profile: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnNewProfile_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new NewProfileDialog();
            if (dialog.ShowDialog() == true)
            {
                try
                {
                    ValveServiceSettings newSettings;

                    if (dialog.SettingsOption == ProfileOption.UseCurrent)
                    {
                        // Update settings from UI first
                        if (!UpdateSettingsFromUI()) return;
                        newSettings = _settings;
                    }
                    else // Restore default settings
                    {
                        newSettings = ValveServiceSettingsManager.CreateDefault();
                    }

                    // Save as new profile
                    ValveServiceSettingsManager.SaveAsProfile(newSettings, dialog.ProfileName);

                    // Reload profile list and select new profile
                    LoadProfileList();
                    cmbProfiles.SelectedItem = dialog.ProfileName;

                    MessageBox.Show($"Profile '{dialog.ProfileName}' created successfully.", "Success",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to create profile: {ex.Message}", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void BtnSaveProfile_Click(object sender, RoutedEventArgs e)
        {
            if (cmbProfiles.SelectedItem == null) return;

            string selectedProfile = cmbProfiles.SelectedItem.ToString();

            // Update settings from UI
            if (!UpdateSettingsFromUI()) return;

            var result = MessageBox.Show(
                $"Update profile '{selectedProfile}' with current settings?",
                "Save Profile",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    if (selectedProfile == DEFAULT_PROFILE_NAME)
                    {
                        ValveServiceSettingsManager.SaveAsDefault(_settings);
                    }
                    else
                    {
                        ValveServiceSettingsManager.SaveAsProfile(_settings, selectedProfile);
                    }

                    MessageBox.Show($"Profile '{selectedProfile}' saved successfully.", "Success",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to save profile: {ex.Message}", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void BtnDeleteProfile_Click(object sender, RoutedEventArgs e)
        {
            if (cmbProfiles.SelectedItem == null) return;

            string selectedProfile = cmbProfiles.SelectedItem.ToString();

            if (selectedProfile == DEFAULT_PROFILE_NAME)
            {
                MessageBox.Show("Cannot delete the default profile.", "Cannot Delete",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var result = MessageBox.Show(
                $"Are you sure you want to delete profile '{selectedProfile}'?\n\nThis action cannot be undone.",
                "Delete Profile",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    if (ValveServiceSettingsManager.DeleteProfile(selectedProfile))
                    {
                        // Reload profile list and select default
                        LoadProfileList();
                        cmbProfiles.SelectedIndex = 0;

                        MessageBox.Show($"Profile '{selectedProfile}' deleted.", "Success",
                            MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    else
                    {
                        MessageBox.Show("Failed to delete profile.", "Error",
                            MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to delete profile: {ex.Message}", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        // ==================== CATEGORY HANDLERS ====================

        private void BtnAddValveCategory_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new CategorySelectorDialog(_doc, _settings.ValveCategoryFilters, "valve");
            if (dialog.ShowDialog() == true && dialog.SelectedFilter != null)
            {
                _settings.ValveCategoryFilters.Add(dialog.SelectedFilter);
                RefreshValveCategoryList();
            }
        }

        private void BtnEditValveCategory_Click(object sender, RoutedEventArgs e)
        {
            if (lstValveCategories.SelectedItem is ListBoxItem selectedItem &&
                selectedItem.Tag is CategoryFilter filter)
            {
                var dialog = new CategorySelectorDialog(_doc, _settings.ValveCategoryFilters, "valve", filter);
                if (dialog.ShowDialog() == true && dialog.SelectedFilter != null)
                {
                    int index = _settings.ValveCategoryFilters.IndexOf(filter);
                    _settings.ValveCategoryFilters[index] = dialog.SelectedFilter;
                    RefreshValveCategoryList();
                }
            }
            else
            {
                MessageBox.Show("Please select a category to edit.", "No Selection",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void BtnRemoveValveCategory_Click(object sender, RoutedEventArgs e)
        {
            if (lstValveCategories.SelectedItem is ListBoxItem selectedItem &&
                selectedItem.Tag is CategoryFilter filter)
            {
                _settings.ValveCategoryFilters.Remove(filter);
                RefreshValveCategoryList();
            }
        }

        private void BtnAddMEPCategory_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new CategorySelectorDialog(_doc, _settings.MEPCategoryFilters, "mep");
            if (dialog.ShowDialog() == true && dialog.SelectedFilter != null)
            {
                _settings.MEPCategoryFilters.Add(dialog.SelectedFilter);
                RefreshMEPCategoryList();
            }
        }

        private void BtnRemoveMEPCategory_Click(object sender, RoutedEventArgs e)
        {
            if (lstMEPCategories.SelectedItem is ListBoxItem selectedItem &&
                selectedItem.Tag is CategoryFilter filter)
            {
                _settings.MEPCategoryFilters.Remove(filter);
                RefreshMEPCategoryList();
            }
        }

        // ==================== PARAMETER HANDLERS ====================

        private void BtnSelectInputParameter_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new ParameterSelectorDialog(_doc, "Input");
            if (dialog.ShowDialog() == true && !string.IsNullOrEmpty(dialog.SelectedParameter.ParameterName))
            {
                txtInputParameter.Text = dialog.SelectedParameter.ParameterName;
                _settings.InputParameter = dialog.SelectedParameter;
            }
        }

        private void BtnSelectOutputParameter_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new ParameterSelectorDialog(_doc, "Output");
            if (dialog.ShowDialog() == true && !string.IsNullOrEmpty(dialog.SelectedParameter.ParameterName))
            {
                txtOutputParameter.Text = dialog.SelectedParameter.ParameterName;
                _settings.OutputParameter = dialog.SelectedParameter;
            }
        }

        // ==================== ACTION HANDLERS ====================

        private void BtnSetAsDefault_Click(object sender, RoutedEventArgs e)
        {
            if (!UpdateSettingsFromUI()) return;

            var result = MessageBox.Show(
                "Save current settings as the default configuration?",
                "Set as Default",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    ValveServiceSettingsManager.SaveAsDefault(_settings);
                    MessageBox.Show("Current settings saved as default.", "Success",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to save default: {ex.Message}", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void BtnRestoreDefaults_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(
                "Restore all settings to factory defaults?",
                "Restore Defaults",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                _settings = ValveServiceSettingsManager.CreateDefault();
                PopulateUI();
            }
        }

        private void BtnRun_Click(object sender, RoutedEventArgs e)
        {
            if (!UpdateSettingsFromUI())
                return;

            RunRequested = true;
            DialogResult = true;
            Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            RunRequested = false;
            DialogResult = false;
            Close();
        }

        // ==================== VALIDATION ====================

        private bool UpdateSettingsFromUI()
        {
            // Validate tolerance
            if (!double.TryParse(txtTolerance.Text, out double tolerance) || tolerance <= 0)
            {
                MessageBox.Show("Please enter a valid tolerance value (mm).", "Invalid Input",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            _settings.ToleranceMm = tolerance;

            // Validate touching distance
            if (!double.TryParse(txtTouchingDistance.Text, out double touching) || touching <= 0)
            {
                MessageBox.Show("Please enter a valid touching distance (mm).", "Invalid Input",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            _settings.TouchingDistMm = touching;

            // Debug mode
            if (rbDebugNone.IsChecked == true)
                _settings.DebugMode = DebugLevel.None;
            else if (rbDebugConcise.IsChecked == true)
                _settings.DebugMode = DebugLevel.Concise;
            else if (rbDebugFull.IsChecked == true)
                _settings.DebugMode = DebugLevel.Full;

            // Scope Radiobuttons
            if (rbScopeProject.IsChecked == true)
                _settings.CollectionScope = ValveCollectionScope.EntireProject;
            else if (rbScopeActive.IsChecked == true)
                _settings.CollectionScope = ValveCollectionScope.ActiveView;
            // Scope Selection Override 
            _settings.AllowSelectionOverrides = chkEnableSelectionOverride.IsChecked == true;

            // Parameters
            _settings.WriteToParameters = chkWriteToParameters.IsChecked == true;

            // Validate valve categories
            if (_settings.ValveCategoryFilters.Count == 0)
            {
                MessageBox.Show("Please add at least one valve category.", "No Categories",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            // Validate mep categories
            if (_settings.MEPCategoryFilters.Count == 0)
            {
                MessageBox.Show("Please add at least one MEP category.", "No Categories",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            return true;
        }
    }
}