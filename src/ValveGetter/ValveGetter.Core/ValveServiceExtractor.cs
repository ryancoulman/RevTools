// ValveServiceExtractor.cs - C# DLL for pyRevit
// Build as Class Library (.NET Framework 4.8) targeting Revit 2024+

using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime;
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
            ValveServiceSettings settings = null)
        {
            // For testing 
            settings ??= ValveServiceSettingsManager.CreateDefault();

            Document doc = uidoc.Document;
            // Get Valves
            Stopwatch swCollect = Stopwatch.StartNew();
            Dictionary<int, Element> valves = FilteredValveCollector.GetValves(uidoc, doc, settings);
            swCollect.Stop();
            TaskDialog.Show("timeywimey", $"Took {swCollect.ElapsedMilliseconds} ms to collect {valves.Count} valves");
            // Run Extractor 
            var extractor = new ValveServiceExtractorInternal(doc, settings);
            var results =  extractor.Process(valves);
            // Set Valve Service parameters
            if (settings.WriteToParameters)
                SetValveService.SetValveServiceParameters(doc, settings, results, valves);

            return results;
        }
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


    internal static class Bicywicy
    {
        /// <summary>
        /// Converts a stored BIC long value to a BuiltInCategory, with validation
        /// </summary>
        /// <returns>The BuiltInCategory if valid and exists in current version, otherwise null</returns>
        public static BuiltInCategory? GetBuiltInCategory(long bicValue)
        {
#if REVIT2024_OR_GREATER
            // In 2024+, BuiltInCategory is a long enum
            if (bicValue > 0)
                return null;

            if (!Enum.IsDefined(typeof(BuiltInCategory), bicValue))
                return null;

            return (BuiltInCategory)bicValue;
#else
            // Pre-2024, BuiltInCategory is an int enum
            if (bicValue > 0 || bicValue < int.MinValue || bicValue > int.MaxValue)
                return null;

            int bicInt = (int)bicValue;

            if (!Enum.IsDefined(typeof(BuiltInCategory), bicInt))
                return null;

            return (BuiltInCategory)bicInt;
#endif
        }
    }

}