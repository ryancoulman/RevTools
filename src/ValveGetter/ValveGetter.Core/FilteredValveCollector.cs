using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using ValveGetter.Settings;

namespace ValveGetter.Core
{
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
                        BuiltInCategory bic = BuiltInEnumHandler.GetBuiltInEnum<BuiltInCategory>(filter.CategoryId) ?? throw new ArgumentNullException(nameof(filter.CategoryName));
                        IEnumerable<Element> elements = new FilteredElementCollector(doc, view.Id)
                            .OfCategory(bic)
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
                    BuiltInCategory bic = BuiltInEnumHandler.GetBuiltInEnum<BuiltInCategory>(filter.CategoryId) ?? throw new ArgumentNullException(nameof(filter.CategoryName));
                    IEnumerable<Element> elements = new FilteredElementCollector(doc)
                        .OfCategory(bic)
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
