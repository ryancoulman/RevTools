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
    }
}
