using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
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

            // REMOVE THIS AND JUST CHECK IF BIP EXISTS IN THE LIST GIVEN FROM CategoryParameterResolver (and set iscommon to true)
            // Acc all revit logic should be removed from this class anyway
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
        public static List<ParameterItem> GetParametersForCategories(
            Document doc,
            List<BuiltInCategory> selectedCategories)
        {
            if (selectedCategories == null || !selectedCategories.Any())
                return [];

            // Get common filterable parameters
            var commonParams = GetCommonFilterableParameters(doc, selectedCategories);

            List<ParameterItem> results = new(commonParams.Count);
            foreach (var (name, bipId, guid) in commonParams)
            {
                results.Add(new ParameterItem
                {
                    ParameterName = name,
                    ParameterBipId = bipId,
                    ParameterGUID = guid,
                    IsCommon = false,
                    Category = "Common Parameters",
                });
            }

            return results;
        }

        private static List<(string name, long bipId, string guid)> GetCommonFilterableParameters(
            Document doc,
            List<BuiltInCategory> bics)
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

                        results.Add((name, (long)bip, ""));
                    }
                    // Case 2 — ParameterElement (Shared or Project parameter)
                    else if (doc.GetElement(paramId) is ParameterElement paramElem)
                    {
                        if (paramElem.GetDefinition() is not InternalDefinition def) continue;

                        if (IsParamElemTypeParameter(parameterBindings, def)) continue;

                        string name = def?.Name;
                        if (string.IsNullOrEmpty(name)) continue;

                        var guid = "";
                        // Shared parameter else no GUID
                        if (paramElem is SharedParameterElement sharedParemElem && sharedParemElem?.GuidValue is Guid guidValue)
                            guid = guidValue.ToString();

                        results.Add((name, 0, guid));
                    }
                }
                catch { } // silent catch
            }

            return results;
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

        private static bool IsParamWritable(Element sampleElem, ValueTuple<string, long, string> paramInfo)
        {
            // expand paramInfo so paramInfo = (name, bipId, guid)
            if (sampleElem == null) return true;
            (string name, long bipId, string guid) = paramInfo;

            try
            {
                if (bipId < -1L)
                {
                    var bip = (BuiltInParameter)bipId;
                    var parameter = sampleElem.get_Parameter(bip);
                    if (parameter == null) return false;
                    return !parameter.IsReadOnly;
                }
                else if (!string.IsNullOrEmpty(guid))
                {
                    var param = sampleElem.get_Parameter(new Guid(guid));
                    if (param == null) return false;
                    return !param.IsReadOnly;
                }
                if (!string.IsNullOrEmpty(name))
                {
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

