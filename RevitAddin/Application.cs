using Autodesk.Revit.UI;
using RevitAddin;
using System;

namespace RevitAddin
{
    public class Application : IExternalApplication
    {
        public Result OnStartup(UIControlledApplication application)
        {
            try
            {
                // Create the ribbon
                RibbonBuilder ribbonBuilder = new RibbonBuilder();
                ribbonBuilder.CreateRibbon(application);

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                TaskDialog.Show("RevitTools Error", $"Failed to load RevitTools: {ex.Message}");
                return Result.Failed;
            }
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            return Result.Succeeded;
        }
    }
}