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

        private void LoadMEPFabricationParameters()
        {
            // Common service-related properties/parameters for MEP Fabrication Parts
            var commonServiceParams = new[]
            {
                "ServiceName",
                "ServiceAbbreviation",// Fuck it remove these options and in getservice if param nname == Service Name (check onnce not each loop) say then first try elem.ServiceName and see if not null else lookup param. 
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