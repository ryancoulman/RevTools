using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using IRevitAPI;
using Autodesk.Revit.DB;

namespace RevitAPI2023
{
    public partial class RevitApi2023 : IElementApi
    {
        public ElementId GetElementId(Element e) => e.Id;
    }
}
