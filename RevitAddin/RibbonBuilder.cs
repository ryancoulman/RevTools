using Autodesk.Revit.UI;
using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Media;
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
            AddValveGetterSplitButton(panel);

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
        private void AddValveGetterSplitButton(RibbonPanel panel)
        {
            string assemblyPath = Assembly.GetExecutingAssembly().Location;

            // --- Define main (smart) button ---
            PushButtonData mainButtonData = new PushButtonData(
                "ValveGetterMain",
                "Valve Getter",
                assemblyPath,
                "RevitAddin.Commands.ValveGetterCommandMain" // ← primary command class
            );

            // --- Define secondary button(s) ---
            PushButtonData secondaryButtonData = new PushButtonData(
                "ValveGetterAdvanced",
                "Valve Getter Advanced",
                assemblyPath,
                "RevitAddin.Commands.ValveGetterCommandAdvanced" // ← secondary command class
            );

            // --- Create the SplitPushButtonData ---
            SplitButtonData splitButtonData = new SplitButtonData(
                "ValveGetterSplit",
                "Valve Getter Advanced" // This is the dropdown title shown on ribbon
            );

            // --- Add SplitPushButton to the panel ---
            SplitButton splitButton = panel.AddItem(splitButtonData) as SplitButton;

            // 3. Set IsSynchronizedWithCurrentItem to false for static behavior
            if (splitButton != null)
            {
                splitButton.IsSynchronizedWithCurrentItem = false;
            }

            // --- Add both PushButtons ---
            PushButton mainButton = splitButton.AddPushButton(mainButtonData);
            PushButton secondaryButton = splitButton.AddPushButton(secondaryButtonData);

            // --- Set icons and tooltips for each ---
            mainButton.LargeImage = GetEmbeddedImage("RevitAddin.Resources.valveGetterIcon96.png", 32);
            mainButton.Image = GetEmbeddedImage("RevitAddin.Resources.valveGetterIcon96.png", 16);
            mainButton.ToolTip = "Extract valve service information from Revit model.";

            secondaryButton.LargeImage = GetEmbeddedImage("RevitAddin.Resources.valveGetterFormIcon96.png", 32);
            secondaryButton.Image = GetEmbeddedImage("RevitAddin.Resources.valveGetterFormIcon96.png", 16);
            secondaryButton.ToolTip = "Advanced settings for full BIM extraction.";

            // --- Set the default 'smart' button (what happens when you click the top half) ---
            //splitButton.CurrentButton = mainButton; 
        }


        // Helper function to load an image stream. To solve scaling issue convert to image to ico and back to png online
        private ImageSource GetEmbeddedImage(string resourceName, int targetSize = 32)
        {
            var assembly = Assembly.GetExecutingAssembly();

            using (Stream stream = assembly.GetManifestResourceStream(resourceName))
            {
                if (stream == null)
                    throw new InvalidOperationException(
                        $"Resource '{resourceName}' not found. Ensure Build Action is 'Embedded Resource'."
                    );

                var decoder = new PngBitmapDecoder(stream,
                                                   BitmapCreateOptions.PreservePixelFormat,
                                                   BitmapCacheOption.OnLoad);
                BitmapSource bitmapSource = decoder.Frames[0];

                // If image isn't already the right size, rescale it
                if (bitmapSource.PixelWidth != targetSize || bitmapSource.PixelHeight != targetSize)
                {
                    double scaleX = (double)targetSize / bitmapSource.PixelWidth;
                    double scaleY = (double)targetSize / bitmapSource.PixelHeight;

                    var scaled = new TransformedBitmap(bitmapSource,
                        new ScaleTransform(scaleX, scaleY));

                    scaled.Freeze();
                    return scaled;
                }
                else
                {
                    bitmapSource.Freeze();
                    return bitmapSource;
                }
            }
        }

    }
}