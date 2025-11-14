using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Autodesk.Revit.DB;
using ValveGetter.Settings;

namespace ValveGetter.UI
{
    public partial class CategorySelectorDialog : Window
    {
        private readonly Document _doc;
        private readonly Categories _categories;
        private readonly List<CategoryFilter> _existingFilters;
        private readonly string _mode; // "valve" or "mep"
        private readonly CategoryFilter _editingFilter;

        public CategoryFilter SelectedFilter { get; private set; }

        public CategorySelectorDialog(
            Document doc,
            List<CategoryFilter> existingFilters,
            string mode = "valve",
            CategoryFilter editFilter = null)
        {
            _doc = doc;
            _categories = _doc.Settings.Categories;
            _existingFilters = existingFilters;
            _mode = mode.ToLower();
            _editingFilter = editFilter;

            InitializeComponent();

            // Update UI based on mode
            if (_mode == "mep")
            {
                this.Title = "Select MEP Category";
                grpNameFilter.Visibility = System.Windows.Visibility.Collapsed;
                txtMEPWarning.Visibility = System.Windows.Visibility.Visible;
            }
            else
            {
                this.Title = "Select Valve Category & Filter";
                grpNameFilter.Visibility = System.Windows.Visibility.Visible;
                txtMEPWarning.Visibility = System.Windows.Visibility.Collapsed;
            }

            PopulateCategories();

            if (_editingFilter != null)
            {
                LoadExistingFilter();
            }
        }

        private void PopulateCategories()
        {
            var categoryList = new List<CategoryItem>();

            if (_mode == "mep")
            {
                PopulateMEPCategories(categoryList);
            }
            else // valve mode
            {
                PopulateValveCategories(categoryList);
            }

            lstCategories.ItemsSource = categoryList;
        }

        private void PopulateMEPCategories(List<CategoryItem> categoryList)
        {
            // MEP Fabrication categories
            categoryList.Add(CreateSeperator("Fabrication Categories"));
            // List of relevant BuiltInCategories for MEP Fabrication
            List<BuiltInCategory> fabCategories =
            [
                BuiltInCategory.OST_FabricationDuctwork,
                BuiltInCategory.OST_FabricationPipework,
                BuiltInCategory.OST_FabricationContainment,
                BuiltInCategory.OST_FabricationHangers,
            ];

            AddNewBicsToList(fabCategories, categoryList, true);

            // Non-fabrication MEP categories
            categoryList.Add(CreateSeperator("Standard MEP Categories"));

            // Get all MEPCurve catagories in the document
            List<BuiltInCategory> mepCurveCategories =
            [
                BuiltInCategory.OST_PipeCurves,
                BuiltInCategory.OST_DuctCurves,
                BuiltInCategory.OST_CableTray,
                BuiltInCategory.OST_Conduit,
                BuiltInCategory.OST_FlexPipeCurves,
                BuiltInCategory.OST_FlexDuctCurves
            ];

            // Add MEP curve categories
            AddNewBicsToList(mepCurveCategories, categoryList, false);
        }

        private void PopulateValveCategories(List<CategoryItem> categoryList)
        {
            // Common valve categories
            categoryList.Add(CreateSeperator("Common Valve Categories"));

            List<BuiltInCategory> commonValveCategories =
            [
                BuiltInCategory.OST_PipeAccessory,
                BuiltInCategory.OST_MechanicalEquipment,
                BuiltInCategory.OST_PlumbingFixtures,
                BuiltInCategory.OST_PipeFitting,
                BuiltInCategory.OST_GenericModel,
            ];

            // Add common valve categories
            AddNewBicsToList(commonValveCategories, categoryList, true);

            // All other categories
            categoryList.Add(CreateSeperator("Other Categories"));

            // Get all model categories in the document excluding common valve categories
            List<CategoryItem> tempList = [];
            foreach (var cat in _categories)
            {
                if (cat is Category category && category.CategoryType == CategoryType.Model)
                {
                    BuiltInCategory bic = category.BuiltInCategory;
                    if (bic == BuiltInCategory.INVALID | commonValveCategories.Contains(bic)) continue;
                    tempList.Add(new CategoryItem
                    {
                        CategoryId = (long)bic,
                        CategoryName = category.Name,
                        IsCommon = false,
                    });
                }
            }

            categoryList.AddRange(tempList.OrderBy(c => c.CategoryName));
        }

        private void LoadExistingFilter()
        {
            var categoryItem = lstCategories.Items.Cast<CategoryItem>()
                .FirstOrDefault(c => c.CategoryId == _editingFilter.CategoryId);

            if (categoryItem != null)
            {
                lstCategories.SelectedItem = categoryItem;
            }

            if (_mode == "valve")
            {
                txtNameCondition.Text = _editingFilter.NameCondition ?? "";

                if (_editingFilter.ConditionTarget == FilterTarget.FamilyName)
                    rbFamilyName.IsChecked = true;
                else
                    rbTypeName.IsChecked = true;
            }
        }

        private void LstCategories_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            btnOk.IsEnabled = lstCategories.SelectedItem is CategoryItem item && !item.IsSeparator;
        }

        private void BtnOk_Click(object sender, RoutedEventArgs e)
        {
            if (lstCategories.SelectedItem is CategoryItem selectedItem && !selectedItem.IsSeparator)
            {
                // Check for duplicates (unless editing)
                if (_editingFilter == null)
                {
                    string nameCondition = _mode == "valve" ? txtNameCondition.Text.Trim() : "";
                    FilterTarget target = _mode == "valve" && rbTypeName.IsChecked == true 
                        ? FilterTarget.TypeName 
                        : FilterTarget.FamilyName;

                    bool isDuplicate = _existingFilters.Any(f =>
                        f.CategoryId == selectedItem.CategoryId &&
                        f.NameCondition == nameCondition &&
                        f.ConditionTarget == target);

                    if (isDuplicate)
                    {
                        MessageBox.Show("This category filter already exists.", "Duplicate Filter",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                }

                // Create the filter
                SelectedFilter = new CategoryFilter
                {
                    CategoryId = selectedItem.CategoryId,
                    CategoryName = selectedItem.CategoryName,
                    NameCondition = _mode == "valve" && !string.IsNullOrWhiteSpace(txtNameCondition.Text) 
                        ? txtNameCondition.Text.Trim() 
                        : "",
                    ConditionTarget = _mode == "valve" && rbTypeName.IsChecked == true 
                        ? FilterTarget.TypeName 
                        : FilterTarget.FamilyName
                };

                DialogResult = true;
                Close();
            }
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private static CategoryItem CreateSeperator(string text) => new()
        {
            CategoryName = $"──── {text} ────",
            IsSeparator = true
        };

        private void AddNewBicsToList(List<BuiltInCategory> bicList, List<CategoryItem> categoryList, bool isCommon)
        {

            foreach (BuiltInCategory bic in bicList)
            {
                try
                {
                    // Get the Category object from the document settings using the BuiltInCategory enum
                    Category category = _categories.get_Item(bic);

                    if (category == null) continue;

                    // Get the user-visible, localized name of the category
                    categoryList.Add(new CategoryItem
                    {
                        CategoryId = (long)bic,
                        CategoryName = category.Name,
                        IsCommon = isCommon,
                    });
                }
                catch { }
            }
        }


        private class CategoryItem
        {
            public string CategoryName { get; set; }
            public long CategoryId { get; set; }
            public bool IsCommon { get; set; }
            public bool IsSeparator { get; set; }
        }


    }
}