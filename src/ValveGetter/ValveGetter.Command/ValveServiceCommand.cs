using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime;
using System.Text;
using ValveGetter.Core;
using ValveGetter.Settings;
 using ValveGetter.UI;

namespace ValveGetter.Command
{
    /// <summary>
    /// Main business logic - can be called from pyRevit OR from the Revit command above
    /// This is what pyRevit will call directly
    /// </summary>
    public static class ValveServiceCommand
    {
        public static void Execute(UIDocument uidoc, Mode mode)
        {
            Document doc = uidoc.Document;
            ValveServiceSettings settings;

            try
            {
                if (mode == Mode.Advanced)
                {
                    // Show advanced settings form
                    var form = new AdvancedSettingsForm(doc);
                    bool? result = form.ShowDialog();

                    if (result == true && form.RunRequested)
                    {
                        // run with settings defined in form not neccessarily saved
                        settings = form.Settings;
                    }
                    else
                    {
                        // User cancelled
                        return;
                    }
                }
                else // mode == Mode.Default
                {
                    // Load default settings (creates one if doesn't exist)
                    settings = ValveServiceSettingsManager.LoadDefault();
                }

                // Run extraction if we have settings
                if (settings != null)
                {
                    // Run extraction
                    var results = ValveServiceExtractor.ExtractValveServices(uidoc, settings);

                    // Output results based on debug level
                    OutputResults(results, settings.DebugMode);
                }
            }
            catch (Exception ex)
            {
                TaskDialog.Show("Error", $"Failed to execute valve service extraction:\n\n{ex.Message}");
            }
        }

        private static void OutputResults(List<ValveServiceExtractor.ValveResult> results, DebugLevel debugMode)
        {
            if (debugMode == DebugLevel.None)
                return;

            var sb = new StringBuilder(results.Count * 200); // Pre-allocate capacity

            sb.AppendLine();
            sb.AppendLine(new string('=', 60));
            sb.AppendLine("RESULTS OVERVIEW");
            sb.AppendLine(new string('=', 60));
            sb.AppendLine();
            sb.AppendLine($"Total Valves: {results.Count}");

            // Count methods efficiently in single pass
            int connected = 0, proximityConn = 0, proximityCenter = 0, notFound = 0, noConnectors = 0;
            var notFoundIds = new List<string>();

            foreach (var r in results)
            {
                switch (r.Method)
                {
                    case "connected":
                        connected++;
                        break;
                    case "proximity_connector":
                        proximityConn++;
                        break;
                    case "proximity_centerline":
                        proximityCenter++;
                        break;
                    case "no_connectors":
                        noConnectors++;
                        break;
                    case "not_found":
                        notFound++;
                        notFoundIds.Add(r.ValveId.ToString());
                        break;
                }
            }

            sb.AppendLine($"  Connected: {connected}");
            sb.AppendLine($"  Proximity (Connector): {proximityConn}");
            sb.AppendLine($"  Proximity (Centerline): {proximityCenter}");
            sb.AppendLine($"  Unassigned Accessories with no Connectors: {noConnectors}");
            sb.AppendLine($"  Accessories with Connectors but no MEP Element Found: {notFound}");
            sb.AppendLine($"  Total Accessories in Categories: {connected + proximityConn + proximityCenter + noConnectors + notFound}");
            sb.AppendLine();

            if (notFoundIds.Count > 0)
            {
                sb.AppendLine("Accessory IDs of Connectors with no MEP Element Found:");
                sb.AppendLine($" {new string('-', 55)}");
                foreach (var id in notFoundIds)
                {
                    sb.AppendLine(id);
                }
                sb.AppendLine();
            }

            if (debugMode == DebugLevel.Concise)
            {
                // Group services summary
                var serviceGroups = results
                    .Where(r => !string.IsNullOrEmpty(r.Service))
                    .GroupBy(r => r.Service)
                    .OrderByDescending(g => g.Count())
                    .ToList();

                if (serviceGroups.Count > 0)
                {
                    sb.AppendLine(new string('=', 60));
                    sb.AppendLine("SERVICES SUMMARY");
                    sb.AppendLine(new string('=', 60));
                    sb.AppendLine();
                    foreach (var group in serviceGroups)
                    {
                        sb.AppendLine($"  • {group.Key}: {group.Count()} valves");
                    }
                }
            }
            else if (debugMode == DebugLevel.Full)
            {
                sb.AppendLine(new string('=', 60));
                sb.AppendLine("RESULTS DETAILED");
                sb.AppendLine(new string('=', 60));
                sb.AppendLine();

                foreach (var r in results)
                {
                    sb.AppendLine($"Valve: {r.ValveName} [ID: {r.ValveId}]");
                    sb.AppendLine($"  Connected MEP Element ID: {(r != null ? r.SourceElementId.ToString() : "N/A")}");
                    sb.AppendLine($"  Service: {r.Service ?? "UNASSIGNED"}");
                    sb.AppendLine($"  Method: {r.Method}");
                    sb.AppendLine($"  Valve Connector Origin: {r.ValveConnectorLocation ?? "N/A"}");
                    sb.AppendLine($"  MEP Connector Origin: {r.MEPConnectorLocation ?? "N/A"}");
                    sb.AppendLine($"  Distance: {r.DistanceMm:F3} mm");
                    sb.AppendLine();
                }
            }

            var debugWindow = new DebugWindow("Valve Service Extraction", sb.ToString());
            debugWindow.Show();
        }
    }

    public enum Mode
    {
        Default,
        Advanced
    }
}