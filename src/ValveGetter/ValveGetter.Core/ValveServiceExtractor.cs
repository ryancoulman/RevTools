// ValveServiceExtractor.cs - C# DLL for pyRevit
// Build as Class Library (.NET Framework 4.8) targeting Revit 2024+

using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime;
using System.Xml.Linq;
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

            // Get MEP Elements 
            Stopwatch swMEP = Stopwatch.StartNew();
            Element[] mepElements = FilteredMepCollector.GetMepElements(doc, settings);
            swMEP.Stop();
            TaskDialog.Show("timeywimey", $"Took {swMEP.ElapsedMilliseconds} ms to collect {mepElements.Length} MEP elements");

            // Run Extractor 
            var extractor = new ValveServiceExtractorInternal(doc, settings, mepElements);
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


    internal static class BuiltInEnumHandler
    {
        /// <summary>
        /// Converts a stored BIC long value to a BuiltInCategory, with validation
        /// </summary>
        /// <returns>The BuiltInCategory if valid and exists in current version, otherwise null</returns>
        public static T? GetBuiltInEnum<T>(long value) where T : struct, Enum
        {
#if REVIT2024_OR_GREATER
    // In 2024+, built-in enums are long
    if (value >= 0)
        return null;
    if (!Enum.IsDefined(typeof(T), value))
        return null;
    return (T)Enum.ToObject(typeof(T), value);
#else
            // Pre-2024, built-in enums are int
            if (value >= 0 || value < int.MinValue || value > int.MaxValue)
                return null;
            int intValue = (int)value;
            if (!Enum.IsDefined(typeof(T), intValue))
                return null;
            return (T)Enum.ToObject(typeof(T), intValue);
#endif
        }
    }

    internal static class ParameterHandler
    {
        internal static Func<Element, Parameter> FactoryHandler(Document doc, ParameterFilter parameterFilter)
        {
            // Pre-parse and validate all identifiers
            BuiltInParameter? bip = null;
            Guid? guid = null;
            string name = null;

            if (parameterFilter.ParameterBipId != 0)
            {
                // Fetch and validate BIP
                bip = BuiltInEnumHandler.GetBuiltInEnum<BuiltInParameter>(parameterFilter.ParameterBipId);
            }

            if (!string.IsNullOrEmpty(parameterFilter.ParameterGUID))
            {
                if (Guid.TryParse(parameterFilter.ParameterGUID, out Guid parsedGuid))
                {
                    // check the shared param exists within the document
                    if (SharedParameterElement.Lookup(doc, parsedGuid) != null)
                        guid = parsedGuid;
                    else
                        guid = null; // Redundant - add error message instead 
                }
            }
            if (!string.IsNullOrEmpty(parameterFilter.ParameterName))
            {
                name = parameterFilter.ParameterName;
            }
            else
            {
                name = ""; // Stops null reference in fallback lookups
                // Add error message  
            }

            if (bip.HasValue)
            {
                // Defensively capture non-nullable value to avoid closure issues
                BuiltInParameter nonNullableBip = bip.Value;
                // Return function with name based fallback logic
                return element => element.get_Parameter(nonNullableBip) ?? element.LookupParameter(name);
            }
            if (guid.HasValue)
            {
                Guid nonNullableGuid = guid.Value;
                return element => element.get_Parameter(nonNullableGuid) ?? element.LookupParameter(name);
            }
            if (!string.IsNullOrEmpty(name))
            {
                // Warning message if both bip and guid are null/invalid. Tell user to update settings.
                string nonEmptyOrNullableName = name;
                return element => element.LookupParameter(nonEmptyOrNullableName);
            }

            throw new ArgumentException("ParameterFilter must have at least one identifier set.");
        }
    }

}