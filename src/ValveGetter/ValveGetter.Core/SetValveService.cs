using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Linq;
using ValveGetter.Settings;


namespace ValveGetter.Core
{
    internal class SetValveService
    {
        /// <summary>
        /// Sets the "Valve Service" parameter of valves from your ValveResult list
        /// </summary>
        internal static void SetValveServiceParameters(Document doc, ValveServiceSettings settings, List<ValveResult> results, Dictionary<int, Element> valves)
        {
            if (results == null || results.Count == 0) return;

            // Get parameter getter factory method once 
            var getParam = ParameterHandler.FactoryHandler(doc, settings.OutputParameter);

            // Cache the parameters for each valve first
            var paramCache = new Dictionary<int, Parameter>(valves.Count);
            foreach (var kvp in valves)
            {
                var valve = kvp.Value;
                if (valve == null) continue;

                Parameter param = getParam(valve);
                if (param == null) continue;
                // Log $"Could not find {settings.OutputParameter.Name} parameter on valve with Id {valve.Name}. This has been skipped"
                if (param.IsReadOnly) continue;
                // Log $"Parameter {settings.OutputParameter.Name} on valve with Id {valve.Name} is read-only"
                paramCache[kvp.Key] = param; // key is valveId
            }

            // Only start transaction if we have something to write
            if (paramCache.Count == 0) return;

            using (Transaction t = new Transaction(doc, "Set Valve Services"))
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
    }
}
