using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using IRevitAPIWrapper;
using Autodesk.Revit.DB;

namespace RevitAPIWrapper
{
    public partial class RevitApi : IElementApi
    {
        public ElementId GetElementId(Element e) => e.Id;

        public Parameter LookupParameter(Element e, string paramName) => e.LookupParameter(paramName);
        public string LookupParameterAsString(Element e, string paramName)
        {
            Parameter param = LookupParameter(e, paramName);
            if (param?.HasValue != true)
                return null;
            string value = param.AsValueString() ?? param.AsString();
            return !string.IsNullOrEmpty(value) ? value : null;
        }

        public FilteredElementCollector FilteredElemCollectorOfClass(Document doc, Type classType) => 
            new FilteredElementCollector(doc).OfClass(classType).WhereElementIsNotElementType();
        public FilteredElementCollector FilteredElemCollectorOfClass(Document doc, ElementId viewId, Type classType) => 
            new FilteredElementCollector(doc, viewId).OfClass(classType).WhereElementIsNotElementType();
        public FilteredElementCollector FilteredElemCollectorOfClassWherePasses(Document doc, Type classType, List<ElementId> categoryIds) =>
            new FilteredElementCollector(doc).OfClass(classType)
            .WhereElementIsNotElementType()
            .WherePasses(new ElementMulticategoryFilter(categoryIds));



    }
}
