using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Autodesk.Revit.DB;

namespace ValveGetter.UI
{
    public partial class ParameterSelectorDialog : Window
    {
        private readonly Document _doc;
        private readonly string _mode; // "Input" or "Output"
        private List<ParameterItem> _allParameters;

        public string SelectedParameterName { get; private set; }

        public ParameterSelectorDialog(Document doc, string mode = "Output")
        {
            _doc = doc;
            _mode = mode;
            InitializeComponent();

            // Update title based on mode
            this.Title = _mode == "Input"
                ? "Select MEP Service Parameter (Input)"
                : "Select Valve Parameter (Output)";

            LoadParameters();
        }

        private void LoadParameters()
        {
            _allParameters = new List<ParameterItem>();

            if (_mode == "Input")
            {
                LoadMEPFabricationParameters();
            }
            else // Output
            {
                LoadValveParameters();
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
                        ParameterId = (long)bip,
                        ParameterName = parameter.Definition.Name,
                        IsCommon = isCommon,
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

            throw new ArgumentNullException("No sample elements found");
        }

        private void AddInstanceParametersToList(List<BuiltInParameter> commonBips, Element sampleElem)
        {
            // Get instance parameters
            foreach (Parameter param in sampleElem.Parameters)
            {
                try
                {
                    if (!IsReadableTextParameter(param)) continue;
                    var definition = param.Definition;
                    if (definition is InternalDefinition internalDef)
                    {
                        var builtInParam = internalDef.BuiltInParameter;
                        if (builtInParam != BuiltInParameter.INVALID && !commonBips.Contains(builtInParam))
                        {
                            _allParameters.Add(new ParameterItem
                            {
                                ParameterName = definition.Name,
                                ParameterId = (long)builtInParam,
                                IsCommon = false,
                                Category = "Fabrication Part Parameters",
                                IsProperty = false
                            });
                        }
                        else
                        {
                            // User-defined non-shared project parameter or family parameter
                            _allParameters.Add(new ParameterItem
                            {
                                ParameterName = definition.Name,
                                ParameterId = 0L,
                                ParamaterGUID = "",
                                IsCommon = false,
                                Category = "Fabrication Part Parameters",
                                IsProperty = false
                            });
                        }
                    }
                    else if (definition is ExternalDefinition externalDef)
                    {
                        // User-defined shared project parameter 
                        _allParameters.Add(new ParameterItem
                        {
                            ParameterName = definition.Name,
                            ParameterId = 0L,
                            ParameterGUID = externalDef.GUID,
                            IsCommon = false,
                            Category = "Fabrication Part Parameters",
                            IsProperty = false
                        });
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
            AddInstanceParametersToList(commonServiceParams, sampleFabPart);
        }

        private void LoadValveParameters()
        {
            // Common writable text parameters (high priority)
            List<BuiltInParameter> commonValveParams =
            [
                BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS,
                BuiltInParameter.ALL_MODEL_MARK,
            ];

            Element sampleValve = GetSampleElementOfCategory(new List<BuiltInCategory>
            {
                BuiltInCategory.OST_PipeAccessory,
                BuiltInCategory.OST_MechanicalEquipment,
                BuiltInCategory.OST_GenericModel,
            });

            // Add common parameters first
            AddNewBipsToList(commonValveParams, sampleValve, true);

            // Get sample valve element to check available parameters
            AddInstanceParametersToList(commonValveParams, sampleValve);
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
                    var displayName = param.IsProperty
                        ? $"{param.Name} (Property)"
                        : param.Name;

                    var item = new ListBoxItem
                    {
                        Content = displayName,
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
                SelectedParameterName = param.Name;
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
            public long ParameterId { get; set; }
            public string ParameterName { get; set; }
            public bool IsCommon { get; set; }
            public string Category { get; set; }
        }
    }
}



//// OLD 



namespace ValveGetter.UI
{
    public partial class ParameterSelectorDialogw : Window
    {
        private readonly Document _doc;
        private readonly string _mode; // "Input" or "Output"
        private List<ParameterItem> _allParameters;

        public string SelectedParameterName { get; private set; }

        public ParameterSelectorDialog(Document doc, string mode = "Output")
        {
            _doc = doc;
            _mode = mode;
            InitializeComponent();

            // Update title based on mode
            this.Title = _mode == "Input"
                ? "Select MEP Service Parameter (Input)"
                : "Select Valve Parameter (Output)";

            LoadParameters();
        }

        private void LoadParameters()
        {
            _allParameters = new List<ParameterItem>();

            if (_mode == "Input")
            {
                LoadMEPFabricationParameters();
            }
            else // Output
            {
                LoadValveParameters();
            }

            RefreshParameterList();
        }

        private void LoadMEPFabricationParameters()
        {
            // Common service-related properties/parameters for MEP Fabrication Parts
            var commonServiceParams = new[]
            {
                "Fabrication Service",
                "Fabrication Service Name",
                "Fabrication Service Abbreviation",
                "Service",
                "Service Name",
                "Service Abbreviation"
            };

            // Add common service parameters first
            foreach (var paramName in commonServiceParams)
            {
                _allParameters.Add(new ParameterItem
                {
                    Name = paramName,
                    IsCommon = true,
                    Category = "Common Service Parameters",
                    IsProperty = paramName == "ServiceName" || paramName == "ServiceAbbreviation"
                });
            }

            // Get sample FabricationPart to check available parameters
            try
            {
                var sampleFabPart = new FilteredElementCollector(_doc)
                    .OfClass(typeof(FabricationPart))
                    .WhereElementIsNotElementType()
                    .Cast<FabricationPart>()
                    .FirstOrDefault(f => f.Category?.Name == "MEP Fabrication Pipework");

                if (sampleFabPart != null)
                {
                    var instanceParams = new HashSet<string>();

                    // Get instance parameters
                    foreach (Parameter param in sampleFabPart.Parameters)
                    {
                        if (IsReadableTextParameter(param))
                        {
                            string paramName = param.Definition.Name;
                            if (!commonServiceParams.Contains(paramName))
                            {
                                instanceParams.Add(paramName);
                            }
                        }
                    }

                    // Add instance parameters (sorted)
                    foreach (var paramName in instanceParams.OrderBy(p => p))
                    {
                        _allParameters.Add(new ParameterItem
                        {
                            Name = paramName,
                            IsCommon = false,
                            Category = "Fabrication Part Parameters",
                            IsProperty = false
                        });
                    }

                    // Get type parameters
                    var fabPartType = _doc.GetElement(sampleFabPart.GetTypeId());
                    if (fabPartType != null)
                    {
                        foreach (Parameter param in fabPartType.Parameters)
                        {
                            if (IsReadableTextParameter(param))
                            {
                                string paramName = param.Definition.Name;
                                if (!commonServiceParams.Contains(paramName) && !instanceParams.Contains(paramName))
                                {
                                    _allParameters.Add(new ParameterItem
                                    {
                                        Name = paramName,
                                        IsCommon = false,
                                        Category = "Fabrication Part Type Parameters",
                                        IsProperty = false
                                    });
                                }
                            }
                        }
                    }
                }
            }
            catch
            {
                // If we can't find a sample element, just show common parameters
            }
        }

        private void LoadValveParameters()
        {
            // Common writable text parameters (high priority)
            var commonParams = new[]
            {
                "Comments",
                "Mark",
                "Description",
                "Valve Service",
                "Service",
                "System Name",
                "System Type",
                "Type Comments"
            };

            // Add common parameters first
            foreach (var paramName in commonParams)
            {
                _allParameters.Add(new ParameterItem
                {
                    Name = paramName,
                    IsCommon = true,
                    Category = "Common",
                    IsProperty = false
                });
            }

            // Get sample valve element to check available parameters
            try
            {
                var sampleValve = new FilteredElementCollector(_doc)
                    .OfCategory(BuiltInCategory.OST_PipeAccessory)
                    .WhereElementIsNotElementType()
                    .FirstElement();

                if (sampleValve != null)
                {
                    var instanceParams = new HashSet<string>();

                    // Get instance parameters
                    foreach (Parameter param in sampleValve.Parameters)
                    {
                        if (IsWritableTextParameter(param))
                        {
                            string paramName = param.Definition.Name;
                            if (!commonParams.Contains(paramName))
                            {
                                instanceParams.Add(paramName);
                            }
                        }
                    }

                    // Add instance parameters (sorted)
                    foreach (var paramName in instanceParams.OrderBy(p => p))
                    {
                        _allParameters.Add(new ParameterItem
                        {
                            Name = paramName,
                            IsCommon = false,
                            Category = "Instance Parameters",
                            IsProperty = false
                        });
                    }

                    // Get type parameters
                    if (sampleValve is FamilyInstance fi && fi.Symbol != null)
                    {
                        foreach (Parameter param in fi.Symbol.Parameters)
                        {
                            if (IsWritableTextParameter(param))
                            {
                                string paramName = param.Definition.Name;
                                if (!commonParams.Contains(paramName) && !instanceParams.Contains(paramName))
                                {
                                    _allParameters.Add(new ParameterItem
                                    {
                                        Name = paramName,
                                        IsCommon = false,
                                        Category = "Type Parameters",
                                        IsProperty = false
                                    });
                                }
                            }
                        }
                    }
                }
            }
            catch
            {
                // If we can't find a sample element, just show common parameters
            }
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
                    var builtInParam = internalDef.BuiltInParameter;
                    if (builtInParam != BuiltInParameter.INVALID &&
                        builtInParam != BuiltInParameter.ALL_MODEL_MARK &&
                        builtInParam != BuiltInParameter.ALL_MODEL_DESCRIPTION &&
                        builtInParam != BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS &&
                        builtInParam != BuiltInParameter.ALL_MODEL_TYPE_COMMENTS)
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
                : _allParameters.Where(p => p.Name.IndexOf(txtSearch.Text, StringComparison.OrdinalIgnoreCase) >= 0).ToList();

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
                    var displayName = param.IsProperty
                        ? $"{param.Name} (Property)"
                        : param.Name;

                    var item = new ListBoxItem
                    {
                        Content = displayName,
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
                SelectedParameterName = param.Name;
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
            public string Name { get; set; }
            public bool IsCommon { get; set; }
            public string Category { get; set; }
            public bool IsProperty { get; set; } // True for FabricationPart properties like ServiceName
        }
    }
}