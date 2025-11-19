using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ValveGetter.Settings;

namespace ValveGetter.Core
{
    internal class FilteredMepCollector
    {
        internal static Element[] GetMepElements(Document doc, ValveServiceSettings settings)
        {
            if (settings.MEPCategoryFilters == null || !settings.MEPCategoryFilters.Any())
            {
                // Raise exception 
                return Array.Empty<Element>();
            }

            var fabBics = new List<BuiltInCategory>();
            var standardBics = new List<BuiltInCategory>();

            foreach (var filter in settings.MEPCategoryFilters)
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
                var fabParts = new FilteredElementCollector(doc)
                    .WhereElementIsNotElementType()
                    .WherePasses(new ElementMulticategoryFilter(fabBics))
                    .OfClass(typeof(FabricationPart))
                    .Cast<FabricationPart>()
                    .ToList();

                var standardElements = new FilteredElementCollector(doc)
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
                return new FilteredElementCollector(doc)
                    .WhereElementIsNotElementType()
                    .WherePasses(new ElementMulticategoryFilter(fabBics))
                    .OfClass(typeof(FabricationPart))
                    .Cast<FabricationPart>()
                    .ToArray();
            }
            else if (standardBics.Any())
            {
                // Only standard MEP elements
                return new FilteredElementCollector(doc)
                    .WhereElementIsNotElementType()
                    .WherePasses(new ElementMulticategoryFilter(standardBics))
                    .OfClass(typeof(MEPCurve))
                    .Cast<MEPCurve>()
                    .ToArray();
            }

            return Array.Empty<Element>();
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
    }
}
