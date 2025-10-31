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
            categoryList.Add(new CategoryItem
            {
                CategoryName = "──── Fabrication Categories ────",
                IsSeparator = true
            });

            var fabCategories = new[]
            {
                "MEP Fabrication Pipework",
                "MEP Fabrication Ductwork",
                "MEP Fabrication Containment",
                "MEP Fabrication Hangers"
            };

            // Add fabrication categories if they exist in the document
            foreach (var fabCatName in fabCategories)
            {
                try
                {
                    var category = _doc.Settings.Categories.Cast<Category>()
                        .FirstOrDefault(c => c.Name == fabCatName);

                    if (category != null)
                    {
                        categoryList.Add(new CategoryItem
                        {
                            CategoryId = category.Id.IntegerValue,
                            CategoryName = category.Name,
                            IsCommon = true
                        });
                    }
                }
                catch { }
            }

            // Non-fabrication MEP categories
            categoryList.Add(new CategoryItem
            {
                CategoryName = "──── Standard MEP Categories ────",
                IsSeparator = true
            });

            // Get all MEPCurve catagories in the document
            var mepCurveCategories = new FilteredElementCollector(_doc)
                .OfClass(typeof(MEPCurve))
                .WhereElementIsNotElementType()
                .Select(e => e.Category)
                .Where(c => c != null)
                .GroupBy(c => c.Id.IntegerValue)
                .Select(g => g.First())
                .OrderBy(c => c.Name)
                .ToList();

            // Add MEP curve categories
            try
            {
                foreach (var category in mepCurveCategories)
                {
                    categoryList.Add(new CategoryItem
                    {
                        CategoryId = category.Id.IntegerValue,
                        CategoryName = category.Name,
                        IsCommon = false
                    });
                }
            }
            catch { }
            //// Only set MEP curve categories
            //var standardMEPCategories = new[]
            //{
            //    BuiltInCategory.OST_PipeCurves,
            //    BuiltInCategory.OST_DuctCurves,
            //    BuiltInCategory.OST_CableTray,
            //    BuiltInCategory.OST_Conduit,
            //    BuiltInCategory.OST_FlexPipeCurves,
            //    BuiltInCategory.OST_FlexDuctCurves
            //};

            //// Add standard MEP categories
            //foreach (var builtInCat in standardMEPCategories)
            //{
            //    try
            //    {
            //        Category cat = Category.GetCategory(_doc, builtInCat);
            //        if (cat != null)
            //        {
            //            categoryList.Add(new CategoryItem
            //            {
            //                CategoryId = cat.Id.IntegerValue,
            //                CategoryName = cat.Name,
            //                IsCommon = false
            //            });
            //        }
            //    }
            //    catch { }
            //}
        }

        private void PopulateValveCategories(List<CategoryItem> categoryList)
        {
            // Common valve categories
            categoryList.Add(new CategoryItem
            {
                CategoryName = "──── Common Valve Categories ────",
                IsSeparator = true
            });

            var commonValveCategories = new[]
            {
                BuiltInCategory.OST_PipeAccessory,
                BuiltInCategory.OST_MechanicalEquipment,
                BuiltInCategory.OST_PlumbingFixtures,
                BuiltInCategory.OST_PipeFitting,
                BuiltInCategory.OST_GenericModel
            };

            // Add common valve categories
            foreach (var builtInCat in commonValveCategories)
            {
                try
                {
                    Category cat = Category.GetCategory(_doc, builtInCat);
                    if (cat != null)
                    {
                        categoryList.Add(new CategoryItem
                        {
                            CategoryId = cat.Id.IntegerValue,
                            CategoryName = cat.Name,
                            IsCommon = true
                        });
                    }
                }
                catch { }
            }

            // All other categories
            categoryList.Add(new CategoryItem
            {
                CategoryName = "──── Other Categories ────",
                IsSeparator = true
            });

            var allCategories = _doc.Settings.Categories
                .Cast<Category>()
                .Where(c => c.CategoryType == CategoryType.Model)
                .Where(c => !commonValveCategories.Any(bic => (int)bic == c.Id.IntegerValue))
                .OrderBy(c => c.Name)
                .Select(c => new CategoryItem
                {
                    CategoryId = c.Id.IntegerValue,
                    CategoryName = c.Name,
                    IsCommon = false
                });

            categoryList.AddRange(allCategories);
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

        private class CategoryItem
        {
            public int CategoryId { get; set; }
            public string CategoryName { get; set; }
            public bool IsCommon { get; set; }
            public bool IsSeparator { get; set; }
        }
    }
}