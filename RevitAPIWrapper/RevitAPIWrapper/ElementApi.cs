using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Linq;
using System.Text;
using Autodesk.Revit.DB;

namespace RevitAPIWrapper
{
    public static partial class RevitApi
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ElementId GetElementId(Element e) => e.Id;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static long GetElementIdAsValue(Element e) =>
#if REVIT2024_OR_GREATER
            e.Id.Value;
#else
            e.Id.IntegerValue;
#endif

        public static Parameter LookupParameter(Element e, string paramName) => e.LookupParameter(paramName);
        public static string LookupParameterAsString(Element e, string paramName)
        {
            Parameter param = LookupParameter(e, paramName);
            if (param?.HasValue != true)
                return null;
            string value = param.AsValueString() ?? param.AsString();
            return !string.IsNullOrEmpty(value) ? value : null;
        }

        public static FilteredElementCollector FilteredElemCollectorOfClass(Document doc, Type classType) => 
            new FilteredElementCollector(doc).OfClass(classType).WhereElementIsNotElementType();
        public static FilteredElementCollector FilteredElemCollectorOfClass(Document doc, ElementId viewId, Type classType) => 
            new FilteredElementCollector(doc, viewId).OfClass(classType).WhereElementIsNotElementType();
        public static FilteredElementCollector FilteredElemCollectorOfClassWherePasses(Document doc, Type classType, List<ElementId> categoryIds) =>
            new FilteredElementCollector(doc).OfClass(classType)
            .WhereElementIsNotElementType()
            .WherePasses(new ElementMulticategoryFilter(categoryIds));



    }
}
