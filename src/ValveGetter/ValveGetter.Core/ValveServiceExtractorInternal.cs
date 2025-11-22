using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.UI;
using KdTree;
using KdTree.Math;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime;
using System.Runtime.Remoting.Messaging;
using System.Text;
using ValveGetter.Settings;
using RevitAPIWrapper;

namespace ValveGetter.Core
{
    /// <summary>
    /// Internal implementation - not exposed to Python
    /// </summary>
    internal class ValveServiceExtractorInternal
    {
        private readonly Document _doc;
        private readonly ValveServiceSettings _settings;
        private readonly double _proximityTolerance;
        private readonly double _proximityToleranceSq;
        private readonly double _touchingDistSq;
        private const double MM_TO_FEET = 1.0 / 304.8;
        // --- store MEP element lookup to avoid storing for each mep connector --- 
        private readonly Element[] _mepElements;
        private readonly int[] _mepElementIds;
        // --- delegate methods ---
        private readonly Func<Element, Parameter> _getServiceParam;
        private readonly Func<Element, string> _getServiceProperty;
        private readonly Func<Element, string> _getMepService;

        StringBuilder _debugtext = new StringBuilder();

        public ValveServiceExtractorInternal(Document doc, ValveServiceSettings settings, Element[] mepElements)
        {
            _doc = doc;
            _settings = settings;
            _proximityTolerance = settings.ToleranceMm * MM_TO_FEET;
            _proximityToleranceSq = _proximityTolerance * _proximityTolerance;
            _touchingDistSq = (settings.TouchingDistMm * MM_TO_FEET) * (settings.TouchingDistMm * MM_TO_FEET);

            // Pre-cache MEP fabrication elements and ids 
            _mepElements = mepElements;
            if (_mepElements.Length == 0) throw new ArgumentException("No MEP Fabrication elements found in document.");
            _mepElementIds = Array.ConvertAll(_mepElements, e => e.Id.IntegerValue);

            // Determine input parameter type for faster access
            _getServiceParam = ParameterHandler.FactoryHandler(_doc, _settings.InputParameter);
            var getServiceProperty = FabPropertyFactory.FactoryHandler(_settings.InputParameter.ParameterName, _mepElements[0]);
            _getServiceProperty = getServiceProperty.Getter;
            // Choose best method for fetching the mep service based on validity of property getter
            _getMepService = getServiceProperty.IsValid ? GetServiceFromProperty : GetServiceFromParameter;
        }

        public List<ValveResult> Process(Dictionary<int, Element> valves)
        {
            // Get valves
            if (valves.Count == 0)
                return new List<ValveResult>();

            // Check MEP elements populated 
            if (_mepElements.Length == 0)
                return new List<ValveResult>();

            // Build caches
            Dictionary<int, ValveCacheData> valveCache = BuildValveConnectorCache(valves);

            // Build K-d tree for fast spatial queries
            KdTree<double, MEPConnectorData> mepConnectorKdTree = BuildMEPConnectorKdTree();

            // Process
            return ProcessAllValves(valveCache, mepConnectorKdTree);

        }


        private string GetServiceFromParameter(Element element)
        {
            Parameter param = _getServiceParam(element);
            if (param?.HasValue == true)
            {
                string value = param.AsValueString() ?? param.AsString();
                if (!string.IsNullOrEmpty(value))
                    return value;
            }

            // log no service or ensure later that method = no service 
            _debugtext.AppendLine($"Element {element.Name} has no service found.");
            return null;
        }

        private string GetServiceFromProperty(Element element)
        {
            string service = _getServiceProperty(element);

            if (!string.IsNullOrEmpty(service))
                return service;

            // Fallback to parameter if not fabrication part or property not found
            return GetServiceFromParameter(element);
        }

        private ConnectorManager GetConnectorManager(Element element)
        {
            try
            {
                // For MEP elements
                if (element is FabricationPart fabPart)
                    return fabPart.ConnectorManager;

                // For Valves etc 
                if (element is FamilyInstance fi)
                    return fi?.MEPModel.ConnectorManager;

                // For standard MEP elements
                if (element is MEPCurve mepCurve)
                    return mepCurve.ConnectorManager;

                // Use reflection as a fallback 
                _debugtext.AppendLine($"Using reflection to get ConnectorManager for element {element.Name}");
                var connectorMgr = element.GetType().GetProperty("ConnectorManager")?.GetValue(element) as ConnectorManager;
                return connectorMgr;
            }
            catch { }
            return null;
        }

        /// <summary>
        /// Build K-d tree for fast spatial queries of MEP connectors
        /// Reduces search complexity from O(N*M) to O(N*log(M))
        /// </summary>
        private KdTree<double, MEPConnectorData> BuildMEPConnectorKdTree()
        {
            var tree = new KdTree<double, MEPConnectorData>(3, new DoubleMath());
            // Pre-extract all points to avoid repeated Revit API calls during tree building
            var pointsToAdd = new List<(double[] point, MEPConnectorData data)>(_mepElements.Length*2); // most mep elems have 2 connectors

            for (int i = 0; i < _mepElements.Length; i++)
            {
                Element elem = _mepElements[i];
                if (elem == null) continue;

                ConnectorManager connMgr = GetConnectorManager(elem);
                if (connMgr == null) continue;

                foreach (Connector connector in connMgr.Connectors)
                {
                    try
                    {
                        if (connector.ConnectorType == ConnectorType.End ||
                            connector.ConnectorType == ConnectorType.Physical)
                        {
                            XYZ origin = connector.Origin;
                            pointsToAdd.Add((
                                ToDoubleArray(origin),
                                new MEPConnectorData { ElementHash = i, Connector = connector }
                            ));
                        }
                    }
                    catch { }
                }
            }

            // Build the tree without any Revit API calls
            foreach (var (point, data) in pointsToAdd)
            {
                tree.Add(point, data);
            }

            return tree;
        }


        private Dictionary<int, ValveCacheData> BuildValveConnectorCache(Dictionary<int, Element> valves)
        {
            var cache = new Dictionary<int, ValveCacheData>(valves.Count);

            foreach (var kvp in valves)
            {
                Element valve = kvp.Value;
                ConnectorManager connMgr = GetConnectorManager(valve);

                // Initialize ValveCacheData with defaults
                var valveCacheData = new ValveCacheData
                {
                    ValveElement = valve,
                    ValveName = valve.Name,
                    Connectors = new List<ValveConnectorData>()
                };

                if (connMgr != null)
                {
                    foreach (Connector connector in connMgr.Connectors)
                    {
                        try
                        {
                            // Only physical connectors
                            if (connector.ConnectorType == ConnectorType.End ||
                                connector.ConnectorType == ConnectorType.Curve ||
                                connector.ConnectorType == ConnectorType.Physical)
                            {
                                XYZ origin = connector.Origin;
                                valveCacheData.Connectors.Add(new ValveConnectorData
                                {
                                    Connector = connector,
                                    Origin = ToDoubleArray(origin),
                                    IsConnected = connector.IsConnected,
                                });
                            }
                        }
                        catch
                        {
                            // optionally log connector failure
                        }
                    }
                }

                cache[kvp.Key] = valveCacheData;
            }

            return cache;
        }


        /// <summary>
        ///  Fist check for directly connected MEP elements. If connected on either side return that service   
        /// </summary>
        private ValveResult GetConnectedService(int valveId, List<ValveConnectorData> valveConnectors)
        {
            foreach (ValveConnectorData valveConnInfo in valveConnectors)
            {
                // Skip unconnected
                if (!valveConnInfo.IsConnected) continue;

                foreach (Connector refConnector in valveConnInfo.Connector.AllRefs)
                {
                    Element refElem = refConnector.Owner; // Element of connected MEP 
                    int refElemId = refElem.Id.IntegerValue;
                    // Skip self-references
                    if (refElemId != valveId)
                    {
                        string service = _getMepService(refElem);
                        if (!string.IsNullOrEmpty(service))
                        {
                            // Return immediately on first found connected service
                            return new ValveResult
                            {
                                Service = service,
                                Method = ConnectionMethod.Connected,
                                DistanceMm = 0.0,
                                SourceElementId = refElemId,
                                ValveConnectorLocation = FormatCoordinates(valveConnInfo.Origin),
                                MEPConnectorLocation = FormatXYZ(refConnector.Origin)
                            };
                        }
                    }
                }
            }
            return null;
        }

        /// <summary>
        ///  If no connected MEP found, find nearest by connector proximity using K-d tree
        ///  K-d tree reduces search from O(N*M) to O(N*log(M)) - runs roughly as quickly as GetConnectedService
        /// </summary>
        private ValveResult FindNearestByConnectors(
            List<ValveConnectorData> valveConnectors,
            KdTree<double, MEPConnectorData> mepConnectorKdTree)
        {
            int bestMEPConnHash = -1; // negative indicates none found used to check bestMEPConn data assigned 
            double[] bestMepOrigin = null;
            ValveConnectorData bestValveConn = null;
            double minDistanceSq = double.PositiveInfinity;

            // Loop through each valve connector and find overall closest MEP connector
            foreach (ValveConnectorData valveConnInfo in valveConnectors)
            {
                // Get nearest neighbor within tolerance radius using K-d tree
                var nearest = mepConnectorKdTree.GetNearestNeighbours(valveConnInfo.Origin, 1);

                if (nearest != null && nearest.Length > 0)
                {
                    MEPConnectorData mepConnInfo = nearest[0].Value; // GetNearestNeighbours returns array of KdTreeNode
                    double[] mepConnOrigin = nearest[0].Point;

                    double distanceSq = SqDistance(valveConnInfo.Origin, mepConnOrigin);

                    if (distanceSq < minDistanceSq)
                    {
                        // Early exit if touching
                        if (distanceSq < _touchingDistSq)
                        {
                            string service = _getMepService(_mepElements[mepConnInfo.ElementHash]);
                            if (!string.IsNullOrEmpty(service))
                            {
                                return new ValveResult
                                {
                                    Service = service,
                                    Method = ConnectionMethod.Nearest,
                                    DistanceMm = Math.Sqrt(distanceSq) / MM_TO_FEET,
                                    SourceElementId = _mepElementIds[mepConnInfo.ElementHash],
                                    ValveConnectorLocation = FormatCoordinates(valveConnInfo.Origin),
                                    MEPConnectorLocation = FormatCoordinates(mepConnOrigin)
                                };
                            }
                        }
                        // Track closest after touching check
                        minDistanceSq = distanceSq;
                        bestMEPConnHash = mepConnInfo.ElementHash;
                        bestMepOrigin = mepConnOrigin;
                        bestValveConn = valveConnInfo;
                    }
                }
            }

            // Only compute actual distance if within tolerance
            if (minDistanceSq < _proximityToleranceSq && bestMEPConnHash >= 0)
            {
                string service = _getMepService(_mepElements[bestMEPConnHash]);
                if (!string.IsNullOrEmpty(service))
                {
                    return new ValveResult
                    {
                        Service = service,
                        Method = ConnectionMethod.Nearest,
                        DistanceMm = Math.Sqrt(minDistanceSq) / MM_TO_FEET,
                        SourceElementId = _mepElementIds[bestMEPConnHash],
                        ValveConnectorLocation = FormatCoordinates(bestValveConn.Origin),
                        MEPConnectorLocation = FormatCoordinates(bestMepOrigin)
                    };
                }
            }

            return null;
        }


        /// <summary>
        /// Fallback method to find nearest MEP element by projecting valve connector onto MEP element centerline
        /// If the projected point is within the pipe radius, return that service immediately
        /// Use k-d tree as approximation to reduce number of MEP elements to check -> massive speedup - runs 0.5x speed of FindNearestByConnectors
        /// </summary>
        private ValveResult FindNearestByCenterline(
        List<ValveConnectorData> valveConnectors,
        KdTree<double, MEPConnectorData> mepConnectorKdTree)
        {
            int bestMEPConnHash = -1; // negative indicates none found used to check bestMEPConn data assigned 
            ValveConnectorData bestValveConn = null;
            double[] bestPoint = null;
            double minDistanceSq = double.PositiveInfinity;

            // Query once per valve connector with reasonable k
            int k = Math.Min(100, mepConnectorKdTree.Count / 10);

            foreach (var valveConnInfo in valveConnectors)
            {
                // Track processed element IDs to avoid duplicates
                HashSet<int> visitedElemIds = new HashSet<int>();
                double[] ValveConnOrigin = valveConnInfo.Origin;

                // Single query of k nearest connectors in order of distance
                var nearestNeighbours = mepConnectorKdTree.GetNearestNeighbours(ValveConnOrigin, k);
                if (nearestNeighbours == null)
                    continue;

                foreach (var neighbour in nearestNeighbours)
                {
                    MEPConnectorData mepConnInfo = neighbour.Value;
                    if (mepConnInfo == null)
                        continue;

                    int elemId = _mepElementIds[mepConnInfo.ElementHash];
                    if (!visitedElemIds.Add(elemId))
                        continue;

                    var mepElem = _mepElements[mepConnInfo.ElementHash];
                    if (!(mepElem.Location is LocationCurve locCurve))
                        continue;

                    Curve curve = locCurve.Curve;
                    if (curve == null)
                        continue;

                    try
                    {
                        // fetch radius of MEP pipe
                        double radius = GetPipeRadiusInFeet(mepConnInfo);

                        // Project valve connector onto curve
                        IntersectionResult result = curve.Project(ToXYZ(ValveConnOrigin));
                        if (result == null)
                            continue;

                        // Get distance of closest projected point to valve connector
                        double[] closestPoint = ToDoubleArray(result.XYZPoint);
                        double distanceSq = SqDistance(ValveConnOrigin, closestPoint);

                        // If valve connector is within radius of mep elem return immediately
                        // Slight innaccuracy at endpoints of pipe as volume carved out by radius will be > tolerance if radius > tolerance
                        if (radius > 0 && distanceSq < radius * radius)
                        {
                            string service = _getMepService(mepElem);
                            if (!string.IsNullOrEmpty(service))
                            {
                                return new ValveResult
                                {
                                    Service = service,
                                    Method = ConnectionMethod.Intersecting,
                                    DistanceMm = Math.Sqrt(distanceSq) / MM_TO_FEET,
                                    SourceElementId = elemId,
                                    ValveConnectorLocation = FormatCoordinates(ValveConnOrigin),
                                    MEPConnectorLocation = FormatCoordinates(closestPoint)
                                };
                            }
                        }
                        // Fallback to tracking closest if within proximity tolerance
                        else if (distanceSq < minDistanceSq)
                        {
                            minDistanceSq = distanceSq;
                            bestValveConn = valveConnInfo;
                            bestMEPConnHash = mepConnInfo.ElementHash;
                            bestPoint = closestPoint;
                        }
                    }
                    catch { }
                }
            }

            // Fallback if nothing found within touching tolerance
            if (minDistanceSq < _proximityToleranceSq && bestMEPConnHash >= 0)
            {
                string service = _getMepService(_mepElements[bestMEPConnHash]);
                if (!string.IsNullOrEmpty(service))
                {
                    return new ValveResult
                    {
                        Service = service,
                        Method = ConnectionMethod.Intersecting,
                        DistanceMm = Math.Sqrt(minDistanceSq) / MM_TO_FEET,
                        SourceElementId = _mepElementIds[bestMEPConnHash],
                        ValveConnectorLocation = FormatCoordinates(bestValveConn.Origin),
                        MEPConnectorLocation = FormatCoordinates(bestPoint)
                    };
                }
            }

            return null;
        }


        private List<ValveResult> ProcessAllValves(
            Dictionary<int, ValveCacheData> valveCache,
            KdTree<double, MEPConnectorData> mepConnectorKdTree)
        {
            List<ValveResult> results = new List<ValveResult>(valveCache.Count);

            foreach (var kvp in valveCache)
            {
                int valveId = kvp.Key;
                string ValveName = kvp.Value.ValveName;

                ValveResult result = new()
                {
                    ValveId = valveId,
                    ValveName = ValveName,
                    Method = ConnectionMethod.NoConnectors
                };

                // Get valve connectors from cache
                if (!valveCache.TryGetValue(valveId, out var valveData) || valveData.Connectors == null || valveData.Connectors.Count == 0)
                {
                    // If no connectors found, skip
                    results.Add(result);
                    continue;
                }
                var valveConnectors = valveData.Connectors;

                // Check connected
                ValveResult connected = GetConnectedService(valveId, valveConnectors);
                if (connected != null)
                {
                    connected.ValveId = valveId;
                    connected.ValveName = ValveName;
                    results.Add(connected);
                    continue;
                }

                // Check connector proximity
                ValveResult nearest = FindNearestByConnectors(valveConnectors, mepConnectorKdTree);
                if (nearest != null)
                {
                    nearest.ValveId = valveId;
                    nearest.ValveName = ValveName;
                    results.Add(nearest);
                    continue;
                }

                // Check centerline proximity
                nearest = FindNearestByCenterline(valveConnectors, mepConnectorKdTree);
                if (nearest != null)
                {
                    nearest.ValveId = valveId;
                    nearest.ValveName = ValveName;
                    results.Add(nearest);
                }
                // No MEP Service found 
                else
                {
                    result.Method = ConnectionMethod.NotFound;
                    results.Add(result);
                }
            }
            TaskDialog.Show("debug", _debugtext.ToString());

            return results;
        }


        private double SqDistance(double[] pt1, double[] pt2)
        {
            double dx = pt1[0] - pt2[0];
            if (Math.Abs(dx) > _proximityTolerance)
                return double.PositiveInfinity;

            double dy = pt1[1] - pt2[1];
            if (Math.Abs(dy) > _proximityTolerance)
                return double.PositiveInfinity;

            double dz = pt1[2] - pt2[2];
            if (Math.Abs(dz) > _proximityTolerance)
                return double.PositiveInfinity;

            return dx * dx + dy * dy + dz * dz;
        }

        // / Utility conversion methods
        private static double[] ToDoubleArray(XYZ xyz)
        {
            return [xyz.X, xyz.Y, xyz.Z];
        }

        private static XYZ ToXYZ(double[] arr)
        {
            return new XYZ(arr[0], arr[1], arr[2]);
        }

        private string FormatCoordinates(double[] point)
        {
            return $"({point[0]:F2}, {point[1]:F2}, {point[2]:F2})";
        }

        private string FormatXYZ(XYZ point)
        {
            return $"({point.X:F2}, {point.Y:F2}, {point.Z:F2})";
        }


        // Beefy helper methods

        /// <summary>
        /// Safely attempts to extract the radius (in feet) from an MEP element.
        /// Returns null if radius cannot be determined.
        /// </summary>
        private double GetPipeRadiusInFeet(MEPConnectorData mepConnInfo)
        {
            // Primary search via connector profile
            Connector connector = mepConnInfo.Connector;
            if (connector != null)
            {
                ConnectorProfileType shape = connector.Shape;

                if (shape == ConnectorProfileType.Round)
                {
                    double radius = connector.Radius; // in feet
                    if (radius > 0)
                        return radius;
                }
                if (shape == ConnectorProfileType.Rectangular)
                {
                    // Need to handle 
                    return -1;
                }
                if (shape == ConnectorProfileType.Oval)
                {
                    // Need to handle 
                    return -1;
                }
            }

            // Fallback methods via MEP element properties and parameters
            Element mepElem = _mepElements[mepConnInfo.ElementHash];
            try
            {
                // Priority 1: Try FabricationPart properties (fastest, no parameter lookup)
                if (mepElem is FabricationPart fabPart)
                {
                    try
                    {
                        // Try Size property first (e.g., "160ø")
                        string sizeStr = fabPart.Size;
                        if (!string.IsNullOrEmpty(sizeStr))
                        {
                            double? radius = ParseSizeStringToRadius(sizeStr);
                            if (radius.HasValue)
                                return radius.Value;
                        }
                    }
                    catch { /* Continue to next attempt */ }

                    try
                    {
                        // Try ProductSizeDescription as fallback
                        string productSize = fabPart.ProductSizeDescription;
                        if (!string.IsNullOrEmpty(productSize))
                        {
                            double? radius = ParseSizeStringToRadius(productSize);
                            if (radius.HasValue)
                                return radius.Value;
                        }
                    }
                    catch { /* Continue to next attempt */ }

                    try
                    {
                        // Try FreeSize as last fabrication property
                        string freeSize = fabPart.FreeSize;
                        if (!string.IsNullOrEmpty(freeSize))
                        {
                            double? radius = ParseSizeStringToRadius(freeSize);
                            if (radius.HasValue)
                                return radius.Value;
                        }
                    }
                    catch { /* Continue to parameters */ }
                }

                // Priority 2: Try standard Pipe properties (for non-fabrication pipes)
                if (mepElem is Pipe pipe)
                {
                    try
                    {
                        double diameter = pipe.Diameter; // Already in feet
                        if (diameter > 0)
                            return diameter / 2.0;
                    }
                    catch { /* Continue to parameters */ }
                }

                // Priority 3: Fall back to parameters (slower, requires lookup)
                // Try "Size" parameter first
                Parameter sizeParam = mepElem.LookupParameter("Size");
                if (sizeParam != null && sizeParam.HasValue && sizeParam.StorageType == StorageType.String)
                {
                    string sizeVal = sizeParam.AsString();
                    if (!string.IsNullOrEmpty(sizeVal))
                    {
                        double? radius = ParseSizeStringToRadius(sizeVal);
                        if (radius.HasValue)
                            return radius.Value;
                    }
                }

                // Try "Product Size Description" parameter
                Parameter productSizeParam = mepElem.LookupParameter("Product Size Description");
                if (productSizeParam != null && productSizeParam.HasValue && productSizeParam.StorageType == StorageType.String)
                {
                    string productSizeVal = productSizeParam.AsString();
                    if (!string.IsNullOrEmpty(productSizeVal))
                    {
                        double? radius = ParseSizeStringToRadius(productSizeVal);
                        if (radius.HasValue)
                            return radius.Value;
                    }
                }
            }
            catch
            {
                // Swallow all exceptions 
            }

            // If no radius found return negative value to indicate failure
            return -1;
        }

        /// <summary>
        /// Parses size strings like "160ø", "160", "6\"", etc. and returns radius in mm.
        /// Returns null if parsing fails.
        /// </summary>
        private double? ParseSizeStringToRadius(string sizeStr)
        {
            if (string.IsNullOrWhiteSpace(sizeStr))
                return null;

            try
            {
                // HashSet of common symbol characters to remove
                HashSet<char> symbolsToRemove = new HashSet<char>
                {
                    'ø', 'Ø', '"', '\'', '°', '´', '`',
                    'm', 'M', // for "mm" or "MM"
                    ' ', '\t', '\n', '\r' // whitespace
                };

                // Build cleaned string keeping only digits, decimal point, and minus sign
                StringBuilder cleaned = new StringBuilder();
                foreach (char c in sizeStr)
                {
                    // Skip symbols
                    if (symbolsToRemove.Contains(c))
                        continue;

                    // Keep digits, decimal point
                    int asciiValue = (int)c;
                    if ((asciiValue >= 48 && asciiValue <= 57) || // '0'-'9'
                        c == '.' ||
                        c == '-' ||
                        c == ',') // Handle comma as decimal separator
                    {
                        // Normalize comma to period for decimal parsing
                        cleaned.Append(c == ',' ? '.' : c);
                    }
                    // Skip any other character
                }

                string cleanedStr = cleaned.ToString();
                if (string.IsNullOrEmpty(cleanedStr))
                    return null;

                // Try to parse as double (diameter in mm)
                if (double.TryParse(cleanedStr, out double diameterMm))
                {
                    if (diameterMm <= 0)
                        return null;

                    return diameterMm / 2.0; // Return radius in mm
                }
            }
            catch
            {
                // Parsing failed
            }

            return null;
        }
    }

    // Data classes
    internal class MEPConnectorData
    {
        public int ElementHash { get; set; } // Index of the MEP element, Id in the _mepElements, _mepElementIds lists
        public Connector Connector { get; set; }
    }
    internal class ValveConnectorData
    {
        public Connector Connector { get; set; }
        public double[] Origin { get; set; }
        public bool IsConnected { get; set; }
    }

    internal class ValveCacheData
    {
        public Element ValveElement { get; set; }
        public string ValveName { get; set; }
        public List<ValveConnectorData> Connectors { get; set; }
    }


    internal class FabPropertyFactory
    {

        /// <summary>
        /// Create a fast accessor for the requested parameter. Optimised for many repeated calls:
        /// - If a fabrication property is known, returns a delegated that does a single type-check and direct property read.
        /// - Otherwise precomputes best access strategy (BuiltInParameter / GUID / name) and returns a small hot delegate.
        /// </summary>
        internal static PropertyGetter FactoryHandler(string paramName, Element sampleElem)
        {
            if (string.IsNullOrEmpty(paramName))
                return new PropertyGetter(false, _ => null);
            if (!IsParamMappedToProperty(paramName))
                return new PropertyGetter(false, _ => null);
            if (!IsPropertyAccessible(sampleElem, paramName))
                return new PropertyGetter(false, _ => null);

            var fabAccessor = GetFabAccessor(paramName);
            return new PropertyGetter(true, element =>
            {
                if (element is FabricationPart fabPart)
                {
                    return fabAccessor(fabPart);
                }

                // handle non-fab element here
                return null;
            });
        }

        // Static map reused across calls to avoid re-allocating the dictionary each time.
        private static readonly Dictionary<string, Func<FabricationPart, string>> s_fabricationPropertyMap =
            new(StringComparer.OrdinalIgnoreCase)
            {
                { "Fabrication Service Name", fp => fp.ServiceName },
                { "Service Name", fp => fp.ServiceName },
                { "ServiceName", fp => fp.ServiceName },
                { "Fabrication Service Abbreviation", fp => fp.ServiceAbbreviation },
                { "Service Abbreviation", fp => fp.ServiceAbbreviation },
                { "ServiceAbbreviation", fp => fp.ServiceAbbreviation }
            };

        internal readonly struct PropertyGetter(bool isValid, Func<Element, string> getter)
        {
            public bool IsValid { get; } = isValid;
            public Func<Element, string> Getter { get; } = getter;
        }



        private static bool IsParamMappedToProperty(string paramName) => s_fabricationPropertyMap.ContainsKey(paramName);
        private static Func<FabricationPart, string> GetFabAccessor(string paramName) => s_fabricationPropertyMap[paramName];

        private static bool IsPropertyAccessible(Element sampleElem, string paramName)
        {
            var fabAccessor = GetFabAccessor(paramName);
            if (sampleElem is FabricationPart fabPart)
            {
                try
                {
                    string propertyValue = fabAccessor(fabPart);
                    return !string.IsNullOrEmpty(propertyValue);
                }
                catch
                {
                    // Property access failed
                    return false;
                }
            }
            // Not a FabricationPart
            return false;
        }

    }


}


