using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using ValveGetter.Settings;

namespace ValveGetter.UI
{
    public partial class ParameterSelectorDialog : Window
    {
        private readonly Document _doc;
        private List<ParameterItem> _allParameters;

        public ParameterFilter SelectedParameter { get; private set; }

        public ParameterSelectorDialog(Document doc, ParamSelectorMode mode)
        {
            _doc = doc;
            InitializeComponent();

            // Update title based on mode
            this.Title = mode == ParamSelectorMode.MepServiceInput
                ? "Select MEP Service Parameter (Input)"
                : "Select Valve Parameter (Output)";

            LoadParameters(mode);
        }

        private void LoadParameters(ParamSelectorMode mode)
        {
            _allParameters = new List<ParameterItem>();

            try
            {
                if (mode == ParamSelectorMode.MepServiceInput)
                {
                    LoadMEPFabricationParameters();
                }
                else // Output
                {
                    LoadValveParameters();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading parameters: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }

            RefreshParameterList();
        }

        private void AddNewBipsToList(List<BuiltInParameter> bipList, Element sampleElem, bool isCommon)
        {

            foreach (BuiltInParameter bip in bipList)
            {
                try
                {
                    // Get the Category object from the document settings using the BuiltInCategory enum
                    Parameter parameter = sampleElem.get_Parameter(bip);

                    if (parameter == null) continue;

                    // Get the user-visible, localized name of the category
                    _allParameters.Add(new ParameterItem
                    {
                        ParameterName = parameter.Definition.Name,
                        ParameterBipId = (long)bip,
                        ParameterGUID = "",
                        IsCommon = isCommon,
                        Category = "Common Parameters",
                    });
                }
                catch { }
            }
        }

        private Element GetSampleElementOfCategory(List<BuiltInCategory> bicList)
        {
            foreach (BuiltInCategory bic in bicList)
            {
                Element sampleElem = new FilteredElementCollector(_doc)
                    .OfCategory(bic)
                    .WhereElementIsNotElementType()
                    .FirstElement();
                if (sampleElem != null)
                {
                    return sampleElem;
                }
            }

            MessageBox.Show("No sample element found for the specified categories.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            return null;
        }

        private void AddInstanceParametersToList(Element sampleElem, List<BuiltInParameter> commonBips = null, bool allowOnlyWritableParams = true)
        {
            var factoryMethod = allowOnlyWritableParams ? (Func<Parameter, bool>)IsWritableTextParameter : IsReadableTextParameter;
            // Get instance parameters
            foreach (Parameter param in sampleElem.Parameters)
            {
                try
                {
                    if (!factoryMethod(param)) continue;
                    var definition = param.Definition;
                    if (definition is InternalDefinition internalDef)
                    {
                        var builtInParam = internalDef.BuiltInParameter;
                        if (commonBips != null && commonBips.Contains(builtInParam)) continue;
                        if (builtInParam != BuiltInParameter.INVALID)
                        {
                            _allParameters.Add(new ParameterItem
                            {
                                ParameterName = definition.Name,
                                ParameterBipId = (long)builtInParam,
                                ParameterGUID = "",
                                IsCommon = false,
                                Category = "Parameters",
                            });
                        }
                        else if (param.IsShared)
                        {
                            // User-defined shared project parameter 
                            _allParameters.Add(new ParameterItem
                            {
                                ParameterName = definition.Name,
                                ParameterBipId = 0L,
                                ParameterGUID = param?.GUID.ToString(),
                                IsCommon = false,
                                Category = "Parameters",
                            });
                        }
                    }
                }
                catch 
                {
                    // Log and continue
                }
            }
        }

        private void LoadMEPFabricationParameters()
        {
            List<BuiltInParameter> commonServiceParams =
            [
                BuiltInParameter.FABRICATION_SERVICE_NAME,
                BuiltInParameter.FABRICATION_SERVICE_ABBREVIATION,
                BuiltInParameter.FABRICATION_SERVICE_PARAM,
            ];

            // Clean up and get the common service categories from other form 
            var sampleFabPart = GetSampleElementOfCategory(new List<BuiltInCategory>
            {
                BuiltInCategory.OST_FabricationPipework,
                BuiltInCategory.OST_FabricationDuctwork,
                BuiltInCategory.OST_PipeCurves,
                BuiltInCategory.OST_DuctCurves,
            });

            // Add common service parameters first
            AddNewBipsToList(commonServiceParams, sampleFabPart, true);

            // Get sample FabricationPart to check available parameters
            AddInstanceParametersToList(sampleFabPart, commonServiceParams, false);
        }

        private void LoadValveParameters()
        {

            Element sampleValve = GetSampleElementOfCategory(new List<BuiltInCategory>
            {
                BuiltInCategory.OST_PipeAccessory,
                BuiltInCategory.OST_MechanicalEquipment,
                BuiltInCategory.OST_GenericModel,
            });

            // Get sample valve element to check available parameters
            AddInstanceParametersToList(sampleValve, null, false);
        }

        private bool IsWritableTextParameter(Parameter param)
        {
            try
            {
                if (param.IsReadOnly)
                    return false;

                // Only string parameters
                if (param.StorageType != StorageType.String)
                    return false;

                // Exclude system parameters that shouldn't be written to
                if (param.Definition is InternalDefinition internalDef)
                {
                    // Allow built-in parameters that are commonly used
                    var builtInParam = internalDef?.BuiltInParameter;
                    if (builtInParam == BuiltInParameter.INVALID || builtInParam == null)
                    {
                        return false;
                    }
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        private bool IsReadableTextParameter(Parameter param)
        {
            try
            {
                // For input (reading), we don't care if it's read-only
                // We just need string parameters
                if (param.StorageType != StorageType.String)
                    return false;

                return true;
            }
            catch
            {
                return false;
            }
        }

        private void RefreshParameterList()
        {
            lstParameters.Items.Clear();

            var filteredParams = string.IsNullOrWhiteSpace(txtSearch.Text)
                ? _allParameters
                : _allParameters.Where(p => p.ParameterName.IndexOf(txtSearch.Text, StringComparison.OrdinalIgnoreCase) >= 0).ToList();

            // Sort alphabetically
            filteredParams = filteredParams.OrderBy(p => p.ParameterName).ToList();
            // Group by category
            var grouped = filteredParams.GroupBy(p => p.Category).OrderBy(g =>
                g.Key.Contains("Common") ? 0 : 1);

            foreach (var group in grouped)
            {
                // Add category header
                var header = new ListBoxItem
                {
                    Content = $"── {group.Key} ──",
                    IsEnabled = false,
                    FontWeight = System.Windows.FontWeights.Bold,
                    Foreground = System.Windows.Media.Brushes.Gray
                };
                lstParameters.Items.Add(header);

                // Add parameters
                foreach (var param in group)
                {
                    var item = new ListBoxItem
                    {
                        Content = param.ParameterName,
                        Tag = param,
                        FontWeight = param.IsCommon ? System.Windows.FontWeights.SemiBold : System.Windows.FontWeights.Normal
                    };
                    lstParameters.Items.Add(item);
                }
            }

            // Auto-select first selectable item if nothing selected
            if (lstParameters.SelectedItem == null)
            {
                var firstSelectableItem = lstParameters.Items
                    .Cast<ListBoxItem>()
                    .FirstOrDefault(item => item.IsEnabled && item.Tag is ParameterItem);

                if (firstSelectableItem != null)
                {
                    lstParameters.SelectedItem = firstSelectableItem;
                }
            }
        }

        private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            RefreshParameterList();
        }

        private void LstParameters_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            btnOk.IsEnabled = lstParameters.SelectedItem is ListBoxItem item &&
                              item.IsEnabled &&
                              item.Tag is ParameterItem;
        }

        private void LstParameters_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (lstParameters.SelectedItem is ListBoxItem item &&
                item.IsEnabled &&
                item.Tag is ParameterItem)
            {
                AcceptSelection();
            }
        }

        private void BtnOk_Click(object sender, RoutedEventArgs e)
        {
            AcceptSelection();
        }

        private void AcceptSelection()
        {
            if (lstParameters.SelectedItem is ListBoxItem selectedItem &&
                selectedItem.Tag is ParameterItem param)
            {
                SelectedParameter = new ParameterFilter
                {
                    ParameterBipId = param.ParameterBipId,
                    ParameterGUID = param.ParameterGUID,
                    ParameterName = param.ParameterName,
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
        private class ParameterItem
        {
            public long ParameterBipId { get; set; }
            public string ParameterName { get; set; }
            public string ParameterGUID { get; set; }
            public bool IsCommon { get; set; }
            public string Category { get; set; }
            public bool IsProperty { get; set; } 

        }
    }

    public enum ParamSelectorMode
    {
        MepServiceInput,
        ValveOutput
    }
}


namespace RevitPlugin.Parameters
{
    public class ParameterInfo
    {
        public string ParameterName { get; set; }
        public long ParameterBipId { get; set; }
        public string ParameterGUID { get; set; }
        public bool IsCommon { get; set; }
        public int CategoryCount { get; set; } // How many categories have this param

        public ParameterInfo(string name, long bipId, string guid, bool isCommon, int catCount)
        {
            ParameterName = name;
            ParameterBipId = bipId;
            ParameterGUID = guid;
            IsCommon = isCommon;
            CategoryCount = catCount;
        }
    }

    public static class CategoryParameterResolver
    {
        public static List<ParameterInfo> GetParametersForCategories(
            Document doc,
            ICollection<BuiltInCategory> selectedCategories)
        {
            if (selectedCategories == null || !selectedCategories.Any())
                return new List<ParameterInfo>();

            var categoryIds = selectedCategories
                .Select(bic => new ElementId(bic))
                .ToList();

            // Get common filterable parameters
            var commonParams = GetCommonFilterableParameters(doc, categoryIds);

            // Get all parameters across categories for non-common ones
            var allParams = GetAllParametersAcrossCategories(doc, categoryIds);

            // Merge results
            var results = new Dictionary<string, ParameterInfo>();

            // Add common params first
            foreach (var param in commonParams)
            {
                var key = GetParameterKey(param.bipId, param.guid);
                if (!results.ContainsKey(key))
                {
                    results[key] = new ParameterInfo(
                        param.name,
                        param.bipId,
                        param.guid,
                        true,
                        categoryIds.Count
                    );
                }
            }

            // Add non-common params
            foreach (var param in allParams)
            {
                var key = GetParameterKey(param.bipId, param.guid);
                if (!results.ContainsKey(key))
                {
                    results[key] = new ParameterInfo(
                        param.name,
                        param.bipId,
                        param.guid,
                        false,
                        param.categoryCount
                    );
                }
            }

            return results.Values
                .OrderByDescending(p => p.IsCommon)
                .ThenByDescending(p => p.CategoryCount)
                .ThenBy(p => p.ParameterName)
                .ToList();
        }

        private static List<(string name, long bipId, string guid)> GetCommonFilterableParameters(
            Document doc,
            List<ElementId> categoryIds)
        {
            var results = new List<(string, long, string)>();

            try
            {
                var filterableParams = ParameterFilterUtilities
                    .GetFilterableParametersInCommon(doc, categoryIds);

                var parameterBindings = doc.ParameterBindings;

                foreach (var paramId in filterableParams)
                {

                    // Case 1 — BuiltInParameter (no ParameterElement exists in the document)
                    if (Enum.IsDefined(typeof(BuiltInParameter), paramId.IntegerValue))
                    {
                        var bip = (BuiltInParameter)paramId.IntegerValue;

                        // Skip INVALID
                        if (bip == BuiltInParameter.INVALID)
                            continue;

                        string name = LabelUtils.GetLabelFor(bip);
                        results.Add((name, (long)bip, ""));
                        continue;
                    }

                    // Case 2 — ParameterElement (Shared or Project parameter)
                    if (doc.GetElement(paramId) is ParameterElement paramElem)
                    {
                        var def = paramElem.GetDefinition();

                        var binding = parameterBindings.get_Item(def);
                        if (binding != null && binding is TypeBinding) continue;

                        string name = def?.Name;
                        if (string.IsNullOrEmpty(name)) continue;

                        // Shared parameter
                        if (paramElem is SharedParameterElement sharedParemElem)
                        {
                            results.Add((name, 0, sharedParemElem.GuidValue.ToString()));
                        }
                        // Project parameter (no GUID)
                        else
                        {
                            results.Add((name, 0, ""));
                        }
                    }
                }
            }
            catch (Exception)
            {
                // Silently handle if method fails
            }

            return results;
        }

        private static List<(string name, long bipId, string guid, int categoryCount)>
            GetAllParametersAcrossCategories(Document doc, List<ElementId> categoryIds)
        {
            var parameterOccurrences = new Dictionary<string, (string name, long bipId, string guid, int count)>();

            foreach (var categoryId in categoryIds)
            {
                var category = Category.GetCategory(doc, categoryId);
                if (category == null) continue;

                var paramSet = GetCategoryParameters(doc, category);

                foreach (var param in paramSet)
                {
                    var key = GetParameterKey(param.bipId, param.guid);

                    if (parameterOccurrences.ContainsKey(key))
                    {
                        var existing = parameterOccurrences[key];
                        parameterOccurrences[key] = (existing.name, existing.bipId, existing.guid, existing.count + 1);
                    }
                    else
                    {
                        parameterOccurrences[key] = (param.name, param.bipId, param.guid, 1);
                    }
                }
            }

            return parameterOccurrences.Values
                .Select(p => (p.name, p.bipId, p.guid, p.count))
                .ToList();
        }

        private static HashSet<(string name, long bipId, string guid)> GetCategoryParameters(
            Document doc,
            Category category)
        {
            var parameters = new HashSet<(string, long, string)>();

            // Get instance binding parameters
            var bindingMap = doc.ParameterBindings;
            var iterator = bindingMap.ForwardIterator();

            while (iterator.MoveNext())
            {
                if (iterator.Key is InternalDefinition definition && iterator.Current is ElementBinding binding)
                {
                    if (binding.Categories.Contains(category))
                    {
                        var bipId = definition.BuiltInParameter != BuiltInParameter.INVALID
                            ? (long)definition.BuiltInParameter
                            : 0;

                        //var guid = definition is SharedParameterElement sharedDef
                        //    ? sharedDef.GuidValue.ToString()
                        //    : "";

                        //parameters.Add((definition.Name, bipId, guid));
                    }
                }
            }

            // Add built-in parameters for the category
            foreach (BuiltInParameter bip in Enum.GetValues(typeof(BuiltInParameter)))
            {
                if (bip == BuiltInParameter.INVALID) continue;

                try
                {
                    var name = LabelUtils.GetLabelFor(bip);
                    if (!string.IsNullOrEmpty(name))
                    {
                        parameters.Add((name, (long)bip, ""));
                    }
                }
                catch
                {
                    // Parameter not applicable to this category
                }
            }

            return parameters;
        }

        private static string GetParameterKey(long bipId, string guid)
        {
            return bipId != -1 ? $"BIP_{bipId}" : $"GUID_{guid}";
        }
    }
}

