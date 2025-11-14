using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.UI;
using KdTree;
using KdTree.Math;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using ValveGetter.Settings;

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
        // --- input parameter type flags - used for faster checking ---
        private readonly bool _isServiceName;
        private readonly bool _isServiceAbbreviation;
        // --- store MEP element lookup to avoid storing for each mep connector --- 
        private readonly Element[] _mepElements;
        private readonly int[] _mepElementIds;

        StringBuilder _debugtext = new StringBuilder();

        public ValveServiceExtractorInternal(Document doc, ValveServiceSettings settings)
        {
            _doc = doc;
            _settings = settings;
            _proximityTolerance = settings.ToleranceMm * MM_TO_FEET;
            _proximityToleranceSq = _proximityTolerance * _proximityTolerance;
            _touchingDistSq = (settings.TouchingDistMm * MM_TO_FEET) * (settings.TouchingDistMm * MM_TO_FEET);

            // Pre-cache MEP fabrication elements and ids 
            Stopwatch sw = Stopwatch.StartNew();
            _mepElements = GetMEPFabricationElements();
            sw.Stop();
            TaskDialog.Show("timeywimey", $"Took {sw.ElapsedMilliseconds} ms to collect {_mepElements.Length} mep elems");
            _mepElementIds = Array.ConvertAll(_mepElements, e => e.Id.IntegerValue);

            // If input paramater is also a property we can optimise checks
            _isServiceName = _settings.InputParameterName == "Service Name" || _settings.InputParameterName == "ServiceName";
            _isServiceAbbreviation = _settings.InputParameterName == "Service Abbreviation" || _settings.InputParameterName == "ServiceAbbreviation";
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
            Stopwatch sw = Stopwatch.StartNew();
            Dictionary<int, ValveCacheData> valveCache = BuildValveConnectorCache(valves);
            sw.Stop();
            TaskDialog.Show("timeywimey", $"Took {sw.ElapsedMilliseconds} ms to build valve connector cache for {valves.Count} valves");

            // Build K-d tree for fast spatial queries
            KdTree<double, MEPConnectorData> mepConnectorKdTree = BuildMEPConnectorKdTree();

            // Process
            Stopwatch stopwatch = Stopwatch.StartNew();
            var res = ProcessAllValves(valveCache, mepConnectorKdTree);
            stopwatch.Stop();
            TaskDialog.Show("timeywimey", $"Took {stopwatch.ElapsedMilliseconds} ms to process {valves.Count} valves");
            return res;

        }


        private Element[] GetMEPFabricationElements()
        {
            if (_settings.MEPCategoryFilters == null || !_settings.MEPCategoryFilters.Any())
            {
                // Raise exception 
                return Array.Empty<Element>();
            }

            var fabBics = new List<BuiltInCategory>();
            var standardBics = new List<BuiltInCategory>();

            foreach (var filter in _settings.MEPCategoryFilters)
            {
                var bic = Bicywicy.GetBuiltInCategory(filter.CategoryId) ?? throw new ArgumentNullException(nameof(filter.CategoryName));
                if (IsFabricationCategory(bic))
                    fabBics.Add(bic);
                else
                    standardBics.Add(bic);
            }

            // Combine both filters into one collector using LogicalOrFilter

            // Build a single combined filter
            if (fabBics.Any() && standardBics.Any())
            {
                List<Element> combinedResults = [];
                // Need both fabrication parts AND standard MEP elements
                var fabParts = new FilteredElementCollector(_doc)
                    .WhereElementIsNotElementType()
                    .WherePasses(new ElementMulticategoryFilter(fabBics))
                    .OfClass(typeof(FabricationPart))
                    .Cast<FabricationPart>()
                    .ToList();

                var standardElements = new FilteredElementCollector(_doc)
                    .WhereElementIsNotElementType()
                    .WherePasses(new ElementMulticategoryFilter(standardBics))
                    .OfClass(typeof(MEPCurve))
                    .Cast<MEPCurve>()
                    .ToList();

                combinedResults.AddRange(fabParts);
                combinedResults.AddRange(standardElements);
                return combinedResults.ToArray();
            }
            else if (fabBics.Any())
            {
                // Only fabrication parts
                return new FilteredElementCollector(_doc)
                    .WhereElementIsNotElementType()
                    .WherePasses(new ElementMulticategoryFilter(fabBics))
                    .OfClass(typeof(FabricationPart))
                    .Cast<FabricationPart>()
                    .ToArray();
            }
            else if (standardBics.Any())
            {
                // Only standard MEP elements
                return new FilteredElementCollector(_doc)
                    .WhereElementIsNotElementType()
                    .WherePasses(new ElementMulticategoryFilter(standardBics))
                    .OfClass(typeof(MEPCurve))
                    .Cast<MEPCurve>()
                    .ToArray();
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
            _debugtext.AppendLine($"Element ID {element.Name} has no service found.");
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
        private KdTree<double, MEPConnectorData> BuildMEPConnectorKdTreeee()
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

        private KdTree<double, MEPConnectorData> BuildMEPConnectorKdTree2()
        {
            var tree = new KdTree<double, MEPConnectorData>(3, new DoubleMath());

            // Pre-extract all points to avoid repeated Revit API calls during tree building
            var pointsToAdd = new List<(double[] point, MEPConnectorData data)>(_mepElements.Length*2); // most mep elems have 2 connectors

            Stopwatch sw = Stopwatch.StartNew();

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

            sw.Stop();
            TaskDialog.Show("timeywimey", $"Took {sw.ElapsedMilliseconds} ms to extract {_mepElements.Length} mep elems connectors for kd-tree");
            _debugtext.AppendLine($"Extracted {pointsToAdd.Count} connectors from {_mepElements.Length} MEP elements.");

            Stopwatch swBuild = Stopwatch.StartNew();
            // Now build the tree without any Revit API calls
            foreach (var (point, data) in pointsToAdd)
            {
                tree.Add(point, data);
            }
            swBuild.Stop();
            TaskDialog.Show("timeywimey", $"Took {swBuild.ElapsedMilliseconds} ms to build kd-tree with {tree.Count} connectors");
            _debugtext.AppendLine($"Kd-tree built with {tree.Count} connectors from {_mepElements.Length} MEP elements.");

            return tree;
        }

        private KdTree<double, MEPConnectorData> BuildMEPConnectorKdTree()
        {
            var tree = new KdTree<double, MEPConnectorData>(3, new DoubleMath());
            var pointsToAdd = new List<(double[] point, MEPConnectorData data)>(_mepElements.Length * 2);

            Stopwatch swTotal = Stopwatch.StartNew();
            Stopwatch swGetManager = new Stopwatch();
            Stopwatch swIterateConnectors = new Stopwatch();
            Stopwatch swGetConnectorType = new Stopwatch();
            Stopwatch swGetOrigin = new Stopwatch();
            Stopwatch swToDoubleArray = new Stopwatch();
            Stopwatch swListAdd = new Stopwatch();

            int managerCallCount = 0;
            int connectorIterationCount = 0;
            int typeCheckCount = 0;
            int originGetCount = 0;
            int nullManagerCount = 0;
            int exceptionCount = 0;

            for (int i = 0; i < _mepElements.Length; i++)
            {
                Element elem = _mepElements[i];
                if (elem == null) continue;

                swGetManager.Start();
                ConnectorManager connMgr = GetConnectorManager(elem);
                swGetManager.Stop();
                managerCallCount++;

                if (connMgr == null)
                {
                    nullManagerCount++;
                    continue;
                }

                swIterateConnectors.Start();
                ConnectorSet connectors = connMgr.Connectors;
                swIterateConnectors.Stop();

                foreach (Connector connector in connectors)
                {
                    connectorIterationCount++;
                    try
                    {
                        swGetConnectorType.Start();
                        ConnectorType connType = connector.ConnectorType;
                        swGetConnectorType.Stop();
                        typeCheckCount++;

                        if (connType == ConnectorType.End || connType == ConnectorType.Physical)
                        {
                            swGetOrigin.Start();
                            XYZ origin = connector.Origin;
                            swGetOrigin.Stop();
                            originGetCount++;

                            swToDoubleArray.Start();
                            double[] pointArray = ToDoubleArray(origin);
                            swToDoubleArray.Stop();

                            swListAdd.Start();
                            pointsToAdd.Add((
                                pointArray,
                                new MEPConnectorData { ElementHash = i, Connector = connector }
                            ));
                            swListAdd.Stop();
                        }
                    }
                    catch
                    {
                        exceptionCount++;
                    }
                }
            }

            swTotal.Stop();

            var sb = new StringBuilder();
            sb.AppendLine($"=== TIMING BREAKDOWN ===");
            sb.AppendLine($"Total extraction time: {swTotal.ElapsedMilliseconds} ms");
            sb.AppendLine($"");
            sb.AppendLine($"GetConnectorManager: {swGetManager.ElapsedMilliseconds} ms ({managerCallCount} calls, avg {(managerCallCount > 0 ? swGetManager.ElapsedMilliseconds / (double)managerCallCount : 0):F2} ms/call)");
            sb.AppendLine($"Null managers: {nullManagerCount}");
            sb.AppendLine($"");
            sb.AppendLine($"Get ConnectorSet: {swIterateConnectors.ElapsedMilliseconds} ms");
            sb.AppendLine($"Get ConnectorType: {swGetConnectorType.ElapsedMilliseconds} ms ({typeCheckCount} checks, avg {(typeCheckCount > 0 ? swGetConnectorType.ElapsedMilliseconds / (double)typeCheckCount : 0):F2} ms/check)");
            sb.AppendLine($"Get Origin: {swGetOrigin.ElapsedMilliseconds} ms ({originGetCount} gets, avg {(originGetCount > 0 ? swGetOrigin.ElapsedMilliseconds / (double)originGetCount : 0):F2} ms/get)");
            sb.AppendLine($"ToDoubleArray: {swToDoubleArray.ElapsedMilliseconds} ms");
            sb.AppendLine($"List.Add: {swListAdd.ElapsedMilliseconds} ms");
            sb.AppendLine($"");
            sb.AppendLine($"Total connectors iterated: {connectorIterationCount}");
            sb.AppendLine($"Connectors added: {pointsToAdd.Count}");
            sb.AppendLine($"Exceptions caught: {exceptionCount}");

            TaskDialog.Show("Timing Breakdown", sb.ToString());

            _debugtext.AppendLine($"Extracted {pointsToAdd.Count} connectors from {_mepElements.Length} MEP elements.");

            Stopwatch swBuild = Stopwatch.StartNew();
            foreach (var (point, data) in pointsToAdd)
            {
                tree.Add(point, data);
            }
            swBuild.Stop();

            TaskDialog.Show("KD-Tree Build", $"Took {swBuild.ElapsedMilliseconds} ms to build kd-tree with {tree.Count} connectors");
            _debugtext.AppendLine($"Kd-tree built with {tree.Count} connectors from {_mepElements.Length} MEP elements.");

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
                        string service = GetServiceFromElement(refElem);
                        if (!string.IsNullOrEmpty(service))
                        {
                            // Return immediately on first found connected service
                            return new ValveResult
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
                            string service = GetServiceFromElement(_mepElements[mepConnInfo.ElementHash]);
                            if (!string.IsNullOrEmpty(service))
                            {
                                return new ValveResult
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
                    return new ValveResult
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
                            string service = GetServiceFromElement(mepElem);
                            if (!string.IsNullOrEmpty(service))
                            {
                                return new ValveResult
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
                    return new ValveResult
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
                    result.Method = "not_found";
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

        /// <summary>
        /// Checks if a BuiltInCategory is a fabrication category
        /// </summary>
        private static bool IsFabricationCategory(BuiltInCategory bic)
        {
            return bic == BuiltInCategory.OST_FabricationPipework ||
                bic == BuiltInCategory.OST_FabricationDuctwork ||
                bic == BuiltInCategory.OST_FabricationContainment ||
                bic == BuiltInCategory.OST_FabricationHangers;
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

}
