using Autodesk.Revit.UI;
using System;
using System.Reflection;
using System.Windows.Media.Imaging;

namespace RevitAddin
{
    public class RibbonBuilder
    {
        // Configuration: Set to false to use Add-Ins tab, true for custom tab
        private const bool USE_CUSTOM_TAB = false;
        private const string CUSTOM_TAB_NAME = "My MEP Tools";
        private const string PANEL_NAME = "Data Extractors";

        public void CreateRibbon(UIControlledApplication app)
        {
            RibbonPanel panel;

            if (USE_CUSTOM_TAB)
            {
                // Create custom tab (future)
                panel = CreateCustomTab(app);
            }
            else
            {
                // Add to existing Add-Ins tab (current)
                panel = CreatePanelOnAddInsTab(app);
            }

            // Add all tool buttons to the panel
            AddValveGetterButton(panel);

            // Future tools:
            // AddPipeAnalyzerButton(panel);
            // AddEquipmentTrackerButton(panel);
        }

        /// <summary>
        /// Creates a panel on Revit's built-in Add-Ins tab
        /// </summary>
        private RibbonPanel CreatePanelOnAddInsTab(UIControlledApplication app)
        {
            // The Add-Ins tab already exists in Revit, just add a panel to it
            RibbonPanel panel = app.CreateRibbonPanel(PANEL_NAME);
            return panel;
        }

        /// <summary>
        /// Creates a custom tab with a panel (for future use)
        /// </summary>
        private RibbonPanel CreateCustomTab(UIControlledApplication app)
        {
            try
            {
                // Try to create the tab (might fail if it already exists)
                app.CreateRibbonTab(CUSTOM_TAB_NAME);
            }
            catch (Exception)
            {
                // Tab already exists, that's fine
            }

            // Create panel on the custom tab
            RibbonPanel panel = app.CreateRibbonPanel(CUSTOM_TAB_NAME, PANEL_NAME);
            return panel;
        }

        /// <summary>
        /// Adds the ValveGetter button to the panel
        /// </summary>
        private void AddValveGetterButton(RibbonPanel panel)
        {
            string assemblyPath = Assembly.GetExecutingAssembly().Location;

            PushButtonData buttonData = new PushButtonData(
                "ValveGetterButton",           // Internal name
                "Valve\nGetter",               // Display text (use \n for two lines)
                assemblyPath,                  // DLL path
                "RevitAddin.Commands.ValveGetterCommand"  // Full class name
            );

            PushButton button = panel.AddItem(buttonData) as PushButton;

            // Set tooltip
            button.ToolTip = "Extract valve service information from Revit model";
            button.LongDescription =
                "Scans the active Revit model for valves and extracts service " +
                "information including pipe system, size, and location data.";

            // Optional: Add icon (uncomment when you have an icon file)
             button.LargeImage = GetEmbeddedImage("RevitAddin.Resources.ValveGetterIcon_32x32.png");
        }


        /// <summary>
        /// Helper method to load embedded image resources (for icons)
        /// </summary>
        private BitmapImage GetEmbeddedImage(string resourceName)
        {
            try
            {
                Assembly assembly = Assembly.GetExecutingAssembly();
                System.IO.Stream stream = assembly.GetManifestResourceStream(resourceName);

                if (stream == null)
                    return null;

                BitmapImage image = new BitmapImage();
                image.BeginInit();
                image.StreamSource = stream;
                image.EndInit();

                return image;
            }
            catch
            {
                return null;
            }
        }
    }
}