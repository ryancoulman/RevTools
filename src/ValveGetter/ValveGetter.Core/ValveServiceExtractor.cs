// ValveServiceExtractor.cs - C# DLL for pyRevit
// Build as Class Library (.NET Framework 4.8) targeting Revit 2024+

using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.UI;
using KdTree;
using KdTree.Math;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ValveGetter.Settings;

namespace ValveGetter.Core
{
    /// <summary>
    /// Main public API for valve service extraction - callable from Python
    /// </summary>
    public static class ValveServiceExtractor
    {
        /// <summary>
        /// Extract MEP services for valves
        /// </summary>
        /// <returns>List of valve results</returns>
        public static List<ValveResult> ExtractValveServices(
            UIDocument uidoc, // Must pass reference to UIDocument for selection
            ValveServiceSettings settings)
        {
            Document doc = uidoc.Document;
            // Get Valves
            Dictionary<int, Element> valves = FilteredValveCollector.GetValves(uidoc, doc, settings);
            // Run Extractor 
            var extractor = new ValveServiceExtractorInternal(doc, settings);
            return extractor.Process(valves);
        }

        /// <summary>
        /// Result data for a single valve - must be public for Python interop
        /// </summary>
        public class ValveResult
        {
            public int ValveId { get; set; }
            public string ValveName { get; set; }
            public string Service { get; set; }
            public string Method { get; set; }
            public double DistanceMm { get; set; }
            public int SourceElementId { get; set; } // Id of connected MEP element 
            public string ValveConnectorLocation { get; set; }
            public string MEPConnectorLocation { get; set; }
        }
    }

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
        // --- input parameter type flags - used for faster checking ---
        private readonly bool _isServiceName;
        private readonly bool _isServiceAbbreviation;
        // --- store MEP element lookup to avoid storing for each mep connector --- 
        private readonly Element[] _mepElements;
        private readonly int[] _mepElementIds;

        public ValveServiceExtractorInternal(Document doc, ValveServiceSettings settings)
        {
            _doc = doc;
            _settings = settings;
            _proximityTolerance = settings.ToleranceMm * MM_TO_FEET;
            _proximityToleranceSq = _proximityTolerance * _proximityTolerance;
            _touchingDistSq = (settings.TouchingDistMm * MM_TO_FEET) * (settings.TouchingDistMm * MM_TO_FEET);

            // Pre-cache MEP fabrication elements and ids 
            _mepElements = GetMEPFabricationElements();
            _mepElementIds = Array.ConvertAll(_mepElements, e => e.Id.IntegerValue);

            // If input paramater is also a property we can optimise checks
            _isServiceName = _settings.InputParameterName == "ServiceName";
            _isServiceAbbreviation = _settings.InputParameterName == "ServiceAbbreviation";
        }

        public List<ValveServiceExtractor.ValveResult> Process(Dictionary<int, Element> valves)
        {
            // Get valves
            if (valves.Count == 0)
                return new List<ValveServiceExtractor.ValveResult>();

            // Check MEP elements populated 
            if (_mepElements.Length == 0)
                return new List<ValveServiceExtractor.ValveResult>();

            // Build caches
            Dictionary<int, ValveCacheData> valveCache = BuildValveConnectorCache(valves);

            // Build K-d tree for fast spatial queries
            KdTree<double, MEPConnectorData> mepConnectorKdTree = BuildMEPConnectorKdTree();

            // Process
            List<ValveServiceExtractor.ValveResult> results = ProcessAllValves(valveCache, mepConnectorKdTree);

            // Set Valve Service parameters
            if (_settings.WriteToParameters)
                SetValveServiceParameters(results, valveCache);

            return results;
        }


        private Element[] GetMEPFabricationElements()
        {
            if (_settings.MEPCategoryFilters == null || !_settings.MEPCategoryFilters.Any())
            {
                // Fallback get all fabrication parts
                return new FilteredElementCollector(_doc)
                    .OfClass(typeof(FabricationPart))
                    .WhereElementIsNotElementType()
                    .Cast<FabricationPart>()
                    .ToArray();
            }

            // Separate fabrication and non-fabrication category filters
            var fabFilters = _settings.MEPCategoryFilters
                .Where(f => f.CategoryName.StartsWith("MEP Fabrication"))
                .Select(f => f.CategoryName)
                .ToHashSet(); 

            var standardCategoryIds = _settings.MEPCategoryFilters
                .Where(f => !f.CategoryName.StartsWith("MEP Fabrication"))
                .Select(f => new ElementId(f.CategoryId))
                .ToList();

            // Build a single combined filter
            if (fabFilters.Any() && standardCategoryIds.Any())
            {
                List<Element> combinedResults = new List<Element>();
                // Need both fabrication parts AND standard MEP elements
                var fabParts = new FilteredElementCollector(_doc)
                    .OfClass(typeof(FabricationPart))
                    .WhereElementIsNotElementType()
                    .Cast<FabricationPart>()
                    .Where(f => fabFilters.Contains(f.Category?.Name))
                    .ToList();

                var standardElements = new FilteredElementCollector(_doc)
                    .OfClass(typeof(MEPCurve))
                    .WherePasses(new ElementMulticategoryFilter(standardCategoryIds))
                    .WhereElementIsNotElementType()
                    .Cast<MEPCurve>()
                    .ToList();

                combinedResults.AddRange(fabParts);
                return combinedResults.ToArray();
            }
            else if (fabFilters.Any())
            {
                // Only fabrication parts
                return new FilteredElementCollector(_doc)
                    .OfClass(typeof(FabricationPart))
                    .WhereElementIsNotElementType()
                    .Cast<FabricationPart>()
                    .Where(f => fabFilters.Contains(f.Category?.Name))
                    .ToArray();
            }
            else if (standardCategoryIds.Any())
            {
                // Only standard MEP elements
                var standardElements = new FilteredElementCollector(_doc)
                    .OfClass(typeof(MEPCurve))
                    .WherePasses(new ElementMulticategoryFilter(standardCategoryIds))
                    .WhereElementIsNotElementType()
                    .Cast<MEPCurve>()
                    .ToList();
            }

            return Array.Empty<Element>();
        }

        /// <summary>
        /// Returns service of MEP element in priority order
        /// </summary>
        private string GetServiceFromElement(Element element)
        {
            if (_isServiceName)
            {
                // Direct property access for speed
                if (element is FabricationPart fabPart)
                {
                    if (!string.IsNullOrEmpty(fabPart.ServiceName))
                        return fabPart.ServiceName;
                }
            }
            else if (_isServiceAbbreviation)
            {
                // Direct property access for speed
                if (element is FabricationPart fabPart)
                {
                    if (!string.IsNullOrEmpty(fabPart.ServiceAbbreviation))
                        return fabPart.ServiceAbbreviation;
                }
            }

            Parameter param = element.LookupParameter(_settings.InputParameterName);
            if (param?.HasValue == true)
            {
                string value = param.AsValueString() ?? param.AsString();
                if (!string.IsNullOrEmpty(value))
                    return value;
            }

            // log no service or ensure later that method = no service 
            return null;
        }

        private ConnectorManager GetConnectorManager(Element element)
        {
            try
            {
                // For MEP elements
                if (element is FabricationPart fabPart)
                    return fabPart.ConnectorManager;

                // For Valves etc 
                if (element is FamilyInstance fi && fi.MEPModel != null)
                    return fi.MEPModel.ConnectorManager;

                // For standard MEP elements
                if (element is MEPCurve mepCurve)
                    return mepCurve.ConnectorManager;

                // Use reflection as a fallback 
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
            // K-d tree with 3 dimensions (X, Y, Z) using Euclidean distance metric
            var tree = new KdTree<double, MEPConnectorData>(3, new DoubleMath());

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
                            // Convert to double array for K-d tree
                            XYZ origin = connector.Origin;
                            // Convert XYZ to double array for K-d tree
                            tree.Add(ToDoubleArray(origin), 
                                new MEPConnectorData { ElementHash = i, Connector = connector });
                        }
                    }
                    catch { }
                }
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
        private ValveServiceExtractor.ValveResult GetConnectedService(int valveId, List<ValveConnectorData> valveConnectors)
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
                        string service = GetServiceFromElement(refElem);
                        if (!string.IsNullOrEmpty(service))
                        {
                            // Return immediately on first found connected service
                            return new ValveServiceExtractor.ValveResult
                            {
                                Service = service,
                                Method = "connected",
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
        private ValveServiceExtractor.ValveResult FindNearestByConnectors(
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
                            string service = GetServiceFromElement(_mepElements[mepConnInfo.ElementHash]);
                            if (!string.IsNullOrEmpty(service))
                            {
                                return new ValveServiceExtractor.ValveResult
                                {
                                    Service = service,
                                    Method = "proximity_connector",
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
                string service = GetServiceFromElement(_mepElements[bestMEPConnHash]);
                if (!string.IsNullOrEmpty(service))
                {
                    return new ValveServiceExtractor.ValveResult
                    {
                        Service = service,
                        Method = "proximity_connector",
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
        private ValveServiceExtractor.ValveResult FindNearestByCenterline(
        List<ValveConnectorData> valveConnectors,
        KdTree<double, MEPConnectorData> mepConnectorKdTree)
        {
            int bestMEPConnHash = -1; // negative indicates none found used to check bestMEPConn data assigned 
            ValveConnectorData bestValveConn = null;
            double[] bestPoint = null;
            double minDistanceSq = double.PositiveInfinity;

            // Query once per valve connector with reasonable k
            int k = Math.Min(100, Math.Max(mepConnectorKdTree.Count / 100, 20));

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
                            string service = GetServiceFromElement(mepElem);
                            if (!string.IsNullOrEmpty(service))
                            {
                                return new ValveServiceExtractor.ValveResult
                                {
                                    Service = service,
                                    Method = "proximity_centerline",
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
                string service = GetServiceFromElement(_mepElements[bestMEPConnHash]);
                if (!string.IsNullOrEmpty(service))
                {
                    return new ValveServiceExtractor.ValveResult
                    {
                        Service = service,
                        Method = "proximity_centerline",
                        DistanceMm = Math.Sqrt(minDistanceSq) / MM_TO_FEET,
                        SourceElementId = _mepElementIds[bestMEPConnHash],
                        ValveConnectorLocation = FormatCoordinates(bestValveConn.Origin),
                        MEPConnectorLocation = FormatCoordinates(bestPoint)
                    };
                }
            }

            return null;
        }


        private List<ValveServiceExtractor.ValveResult> ProcessAllValves(
            Dictionary<int, ValveCacheData> valveCache,
            KdTree<double, MEPConnectorData> mepConnectorKdTree)
        {
            List<ValveServiceExtractor.ValveResult> results = new List<ValveServiceExtractor.ValveResult>(valveCache.Count);

            foreach (var kvp in valveCache)
            {
                int valveId = kvp.Key;
                string ValveName = kvp.Value.ValveName;

                ValveServiceExtractor.ValveResult result = new ValveServiceExtractor.ValveResult
                {
                    ValveId = valveId,
                    ValveName = ValveName,
                    Method = "no_connectors"
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
                ValveServiceExtractor.ValveResult connected = GetConnectedService(valveId, valveConnectors);
                if (connected != null)
                {
                    connected.ValveId = valveId;
                    connected.ValveName = ValveName;
                    results.Add(connected);
                    continue;
                }

                // Check connector proximity
                ValveServiceExtractor.ValveResult nearest = FindNearestByConnectors(valveConnectors, mepConnectorKdTree);
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
                    result.Method = "not_found";
                    results.Add(result);
                }
            }

            return results;
        }

        /// <summary>
        /// Sets the "Valve Service" parameter of valves from your ValveResult list
        /// </summary>
        private void SetValveServiceParameters(List<ValveServiceExtractor.ValveResult> results, Dictionary<int, ValveCacheData> valveCache)
        {
            if (results == null || results.Count == 0) return;

            string paramName = _settings.OutputParameterName;

            // Cache the "Comments" parameters for each valve first
            var paramCache = new Dictionary<int, Parameter>(valveCache.Count);
            foreach (var kvp in valveCache)
            {
                var valve = kvp.Value.ValveElement;
                if (valve == null) continue;

                Parameter param = valve.LookupParameter(paramName);
                if (param != null && !param.IsReadOnly)
                {
                    paramCache[kvp.Key] = param; // key is valveId
                }
                // ELSE SHOULD LOG MISSING PARAMETER
            }

            // Only start transaction if we have something to write
            if (paramCache.Count == 0) return;

            using (Transaction t = new Transaction(_doc, "Set Valve Services"))
            {
                t.Start();

                foreach (var result in results)
                {
                    // Skip if no service found
                    if (string.IsNullOrEmpty(result.Service)) continue;

                    // Get cached parameter
                    if (!paramCache.TryGetValue(result.ValveId, out var param)) continue;

                    // Only set if different to avoid unnecessary writes
                    string currentValue = param.AsString();
                    if (currentValue != result.Service)
                    {
                        param.Set(result.Service);
                    }
                }

                t.Commit();
            }
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
            return new double[] { xyz.X, xyz.Y, xyz.Z };
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

        // Data classes
        private class MEPConnectorData
        {
            public int ElementHash { get; set; } // Index of the MEP element, Id in the _mepElements, _mepElementIds lists
            public Connector Connector { get; set; }
        }
        private class ValveConnectorData
        {
            public Connector Connector { get; set; }
            public double[] Origin { get; set; }
            public bool IsConnected { get; set; }
        }

        private class ValveCacheData
        {
            public Element ValveElement { get; set; }
            public string ValveName { get; set; }
            public List<ValveConnectorData> Connectors { get; set; }
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


    /// <summary>
    /// Collect valves based on selection and settings
    /// </summary>
    internal static class FilteredValveCollector
    {
        public static Dictionary<int, Element> GetValves(UIDocument uidoc, Document doc, ValveServiceSettings settings)
        {
            ICollection<ElementId> selectedIds = uidoc.Selection.GetElementIds();
            if ((selectedIds.Count != 0 || selectedIds != null) && settings.AllowSelectionOverrides)
            {
                // Priority 1: Check for element selection (overrides all settings)
                var selectedElements = GetSelectedElements(doc, selectedIds);
                if (selectedElements != null && selectedElements.Count > 0)
                    return selectedElements;

                // Priority 2: Check for view selection in project browser (overrides default settings)
                var selectedViews = GetSelectedViewsFromProjectBrowser(doc, selectedIds);
                if (selectedViews != null && selectedViews.Length > 0)
                    return CollectValvesFromViews(doc, settings, selectedViews);
            }

            // Priority 3: Use scope setting
            switch (settings.CollectionScope)
            {
                case ValveCollectionScope.ActiveView:
                    var view = uidoc.ActiveView;
                    if (view is ViewSheet sheet)
                    {
                        var sheetViews = GetViewsOnSheet(doc, sheet).ToArray();
                        return CollectValvesFromViews(doc, settings, sheetViews);
                    }   
                    return CollectValvesFromViews(doc, settings, new[] { view });

                case ValveCollectionScope.EntireProject:
                    return CollectValvesFromDocument(doc, settings);

                default:
                    return new Dictionary<int, Element>();
            }
        }

        /// <summary>
        /// Gets selected elements if they are valid family instances
        /// </summary>
        private static Dictionary<int, Element> GetSelectedElements(Document doc, ICollection<ElementId> selectedIds)
        {
            var dict = new Dictionary<int, Element>(selectedIds.Count);
            foreach (var id in selectedIds)
            {
                Element elem = doc.GetElement(id);
                if (elem != null && elem is FamilyInstance)
                    dict[id.IntegerValue] = elem;
            }

            return dict.Count > 0 ? dict : null;
        }

        /// <summary>
        /// Gets views selected in the project browser
        /// </summary>
        private static View[] GetSelectedViewsFromProjectBrowser(Document doc, ICollection<ElementId> selectedIds)
        {
            var validViews = selectedIds
                .Select(id => doc.GetElement(id))
                .OfType<View>()
                .Where(v => !v.IsTemplate &&
                            v.ViewType != ViewType.Schedule &&
                            v.ViewType != ViewType.Legend &&
                            v.ViewType != ViewType.DrawingSheet);

            var viewList = new List<View>();

            foreach (var view in validViews)
            {
                // If it's a sheet, get all views on the sheet
                if (view is ViewSheet sheet)
                {
                    var sheetViews = GetViewsOnSheet(doc, sheet);
                    viewList.AddRange(sheetViews);
                }
                else
                {
                    viewList.Add(view);
                }
            }

            return viewList.Count > 0 ? viewList.ToArray() : null;
        }

        /// <summary>
        /// Helper class for GetSelectedViewsFromProjectBrowser() - gets views placed on a sheet
        /// </summary>
        private static IEnumerable<View> GetViewsOnSheet(Document doc, ViewSheet sheet)
        {
            foreach (ElementId viewportId in sheet.GetAllViewports())
            {
                if (doc.GetElement(viewportId) is Viewport viewport)
                {
                    if (doc.GetElement(viewport.ViewId) is View view &&
                        !view.IsTemplate &&
                        view.ViewType != ViewType.Schedule &&
                        view.ViewType != ViewType.Legend)
                    {
                        yield return view;
                    }
                }
            }
        }

        /// <summary>
        /// Collects valves from specified views using filters
        /// </summary>
        private static Dictionary<int, Element> CollectValvesFromViews(Document doc, ValveServiceSettings settings, View[] views)
        {
            if (views == null || views.Length == 0)
                return new Dictionary<int, Element>();

            // Use Dictionairy for efficient deduplication across multiple views
            var valveCache = new Dictionary<int, Element>();

            foreach (var view in views)
            {
                foreach (var filter in settings.ValveCategoryFilters)
                {
                    try
                    {
                        IEnumerable<Element> elements = new FilteredElementCollector(doc, view.Id)
                            .OfCategoryId(new ElementId(filter.CategoryId))
                            .WhereElementIsNotElementType().ToElements();

                        if (!string.IsNullOrEmpty(filter.NameCondition))
                        {
                            elements = ApplyNameFilter(elements, filter);
                        }

                        foreach (var element in elements)
                        {
                            valveCache[element.Id.IntegerValue] = element; // Overwrites duplicate
                        }
                    }
                    catch { /* Silently skip invalid categories/views */ }
                }
            }

            // Single materialization at the end
            return valveCache;
        }

        /// <summary>
        /// Collects valves from entire document using filters
        /// </summary>
        private static Dictionary<int, Element> CollectValvesFromDocument(Document doc, ValveServiceSettings settings)
        {
            // Use Dictionary to cache elements while ensuring uniqueness by ID
            var valveCache = new Dictionary<int, Element>();

            foreach (var filter in settings.ValveCategoryFilters)
            {
                try
                {
                    IEnumerable<Element> elements = new FilteredElementCollector(doc)
                        .OfCategoryId(new ElementId(filter.CategoryId))
                        .WhereElementIsNotElementType().ToElements();

                    if (!string.IsNullOrEmpty(filter.NameCondition))
                    {
                        elements = ApplyNameFilter(elements, filter);
                    }

                    foreach (var element in elements)
                    {
                        valveCache[element.Id.IntegerValue] = element; // Overwrites duplicate
                    }
                }
                catch { /* Silently skip invalid categories */ }
            }

            return valveCache;
        }

        /// <summary>
        /// Applies name condition filter to elements
        /// </summary>
        private static IEnumerable<Element> ApplyNameFilter(IEnumerable<Element> elements, CategoryFilter filter)
        {
            string condition = filter.NameCondition.ToLower();
            // Determine which parameter to check
            BuiltInParameter param = filter.ConditionTarget == FilterTarget.FamilyName
                ? BuiltInParameter.ELEM_FAMILY_PARAM
                : BuiltInParameter.ELEM_TYPE_PARAM;

            foreach (var element in elements)
            {
                string paramValue = element.get_Parameter(param)?.AsValueString();
                if (paramValue != null && paramValue.ToLower().Contains(condition))
                {
                    yield return element;
                }
            }
        }
    }
}