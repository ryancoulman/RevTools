using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using ValveGetter.Settings;

using static RevitAPIWrapper.Extensions.BuiltInEnumHandler;


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

            // Load all parameters
            try
            {
                _allParameters = CategoryParameterResolver.GetParametersForCategories(_doc, new List<long>());
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading parameters: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }

            RefreshParameterList();
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

        private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e) => RefreshParameterList();

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

        private void BtnOk_Click(object sender, RoutedEventArgs e) => AcceptSelection();

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

    }

    public class ParameterItem
    {
        public long ParameterBipId { get; set; }
        public string ParameterName { get; set; }
        public string ParameterGUID { get; set; }
        public bool IsCommon { get; set; }
        public string Category { get; set; }

    }

    public enum ParamSelectorMode
    {
        MepServiceInput,
        ValveOutput
    }
}

namespace ValveGetter.UI
{

    public static class CategoryParameterResolver
    {
        private static readonly List<BuiltInParameter> TypicalMepServiceBics =
        [
            // Common Fab Service parameters
            BuiltInParameter.FABRICATION_SERVICE_NAME,
            BuiltInParameter.FABRICATION_SERVICE_ABBREVIATION,
            BuiltInParameter.FABRICATION_SERVICE_PARAM,
            // Common MEP parameters
            BuiltInParameter.RBS_SYSTEM_NAME_PARAM,
            BuiltInParameter.RBS_DUCT_PIPE_SYSTEM_ABBREVIATION_PARAM,
            BuiltInParameter.RBS_SYSTEM_CLASSIFICATION_PARAM,
            BuiltInParameter.RBS_PIPING_SYSTEM_TYPE_PARAM,
        ];

        public static List<ParameterItem> GetParametersForCategories(Document doc, List<long> selectedBipIds)
        {
            var selectedBics = GetBicsFromIds(selectedBipIds);
            if (selectedBics == null || !selectedBics.Any())
                return [];

            // Get common filterable parameters
            var commonParams = GetCommonFilterableParameters(doc, selectedBics);

            var isTypicalParamFunc = IsTypicalParamFunc(ParamSelectorMode.MepServiceInput);
            List<ParameterItem> results = new(commonParams.Count);
            foreach (var (name, bipId, guid) in commonParams)
            {
                bool isCommon = isTypicalParamFunc(bipId);
                results.Add(new ParameterItem
                {
                    ParameterName = name,
                    ParameterBipId = bipId,
                    ParameterGUID = guid,
                    IsCommon = isCommon,
                    Category = isCommon ? "Typical Parmeters" : "Common Parameters",
                });
            }

            return results;
        }


        private static List<(string name, long bipId, string guid)> GetCommonFilterableParameters(
            Document doc, List<BuiltInCategory> bics, bool IsOnlyWritableParams = false)
        {
            var results = new List<(string, long, string)>(); // name, bipId, guid

            var sampleElem = GetSampleELemOfCats(doc, bics);

            var categoryIds = bics
                .Select(bic => new ElementId(bic))
                .ToList();

            var filterableParams = ParameterFilterUtilities
                .GetFilterableParametersInCommon(doc, categoryIds);

            var parameterBindings = doc.ParameterBindings;


            foreach (var paramId in filterableParams)
            {
                try
                {
                    // Case 1 — BuiltInParameter (no ParameterElement exists in the document)
                    if (Enum.IsDefined(typeof(BuiltInParameter), paramId.IntegerValue))
                    {
                        var bip = (BuiltInParameter)paramId.IntegerValue;

                        if (bip == BuiltInParameter.INVALID) continue;

                        if (IsBuiltInTypeParameter(sampleElem, bip)) continue;

                        string name = LabelUtils.GetLabelFor(bip);
                        if (string.IsNullOrEmpty(name)) continue;

                        if (IsOnlyWritableParams && !IsParamWritable(sampleElem, name, bip, null)) continue;

                        results.Add((name, (long)bip, ""));
                    }
                    // Case 2 — ParameterElement (Shared or Project parameter)
                    else if (doc.GetElement(paramId) is ParameterElement paramElem)
                    {
                        if (paramElem.GetDefinition() is not InternalDefinition def) continue;

                        if (IsParamElemTypeParameter(parameterBindings, def)) continue;

                        string name = def?.Name;
                        if (string.IsNullOrEmpty(name)) continue;

                        Guid? guidNullable = null;
                        // Shared parameter else no GUID
                        if (paramElem is SharedParameterElement sharedParemElem && sharedParemElem?.GuidValue is Guid guidValue)
                            guidNullable = guidValue;

                        if (IsOnlyWritableParams && !IsParamWritable(sampleElem, name, null, guidNullable)) continue;

                        results.Add((name, 0, guidNullable.HasValue ? guidNullable.Value.ToString() : ""));
                    }
                }
                catch { } // silent catch
            }

            return results;
        }

        private static Func<long, bool> IsTypicalParamFunc(ParamSelectorMode mode)
        {
            if (mode == ParamSelectorMode.MepServiceInput)
            {
                return bipId => bipId < 0 && TypicalMepServiceBics.Contains((BuiltInParameter)bipId);
            }
            else
            {
                return bipId => false; // No typical params for Valve output
            }
        }

        private static Element GetSampleELemOfCats(Document doc, List<BuiltInCategory> bicList) 
        {
            foreach (BuiltInCategory bic in bicList)
            {
                Element sampleElem = new FilteredElementCollector(doc)
                    .OfCategory(bic)
                    .WhereElementIsNotElementType()
                    .FirstElement();
                if (sampleElem != null)
                {
                    return sampleElem;
                }
            }

            return null;
        }

        private static bool IsBuiltInTypeParameter(Element elem, BuiltInParameter bip)
        {
            if (elem == null) return true;

            return elem?.get_Parameter(bip) == null; // if type param then will not exist on instance
        }
        private static bool IsParamElemTypeParameter(BindingMap paramBindings, InternalDefinition def)
        {
            try
            {
                var binding = paramBindings.get_Item(def);
                return binding == null || binding is TypeBinding;
            }
            catch
            {
                return true; // will skip on error
            }
        }

        private static bool IsParamWritable(Element sampleElem, string nameNullable, BuiltInParameter? bipNullable, Guid? guidNullable)
        {
            // expand paramInfo so paramInfo = (name, bipId, guid)
            if (sampleElem == null) return true;

            try
            {
                if (bipNullable.HasValue)
                {
                    var bip = bipNullable.Value;
                    var parameter = sampleElem.get_Parameter(bip);
                    if (parameter == null) return false;
                    return !parameter.IsReadOnly;
                }
                else if (guidNullable.HasValue)
                {
                    var guid = guidNullable.Value;
                    var param = sampleElem.get_Parameter(guid);
                    if (param == null) return false;
                    return !param.IsReadOnly;
                }
                if (!string.IsNullOrEmpty(nameNullable))
                {
                    var name = nameNullable; // for clarity
                    var param = sampleElem.LookupParameter(name);
                    if (param == null) return false;
                    return !param.IsReadOnly;
                }
                return false;

            }
            catch
            {
                return false;
            }
        }

        private static List<BuiltInCategory> GetBicsFromIds(List<long> bipIds)
        {
            var list = new List<BuiltInCategory>();
            if (bipIds == null || !bipIds.Any()) return list;

            foreach (var bipId in bipIds)
            {
                var bicNullable = GetBuiltInEnum<BuiltInCategory>(bipId);
                if (bicNullable.HasValue)
                {
                    list.Add(bicNullable.Value);
                }
            }
            return list;
        }


    }

    public static class AllParametersFetcher
    {
        public static HashSet<string> GetAllParameterNames(Document doc)
        {
            var names = new HashSet<string>();

            // 1. Built-in parameters
            foreach (BuiltInParameter bip in Enum.GetValues(typeof(BuiltInParameter)))
            {
                if (bip == BuiltInParameter.INVALID) continue;

                try
                {
                    string name = LabelUtils.GetLabelFor(bip);
                    if (!string.IsNullOrWhiteSpace(name))
                        names.Add(name);
                }
                catch { }
            }

            // 2. Shared parameters + Project parameters
            var collector = new FilteredElementCollector(doc)
                .OfClass(typeof(ParameterElement));

            foreach (ParameterElement pe in collector)
            {
                Definition def = pe.GetDefinition();
                if (def == null) continue;

                string name = def.Name;
                if (!string.IsNullOrWhiteSpace(name))
                    names.Add(name);
            }

            // sort alphabetically
            return names;
        }

    }
}

