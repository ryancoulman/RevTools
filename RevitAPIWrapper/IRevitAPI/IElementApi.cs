using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Autodesk.Revit.DB;

namespace IRevitAPI
{
    internal interface IElementApi
    {
        ElementId GetElementId(Element elem);
        XYZ GetConnectorOrigin(Connector connector);
        bool IsPhysicalConnector(Connector connector);
    }
}
