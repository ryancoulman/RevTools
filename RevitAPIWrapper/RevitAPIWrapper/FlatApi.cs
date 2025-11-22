using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Autodesk.Revit.DB;
using IRevitAPIWrapper;

namespace RevitAPIWrapper
{
    public static partial class RevitApi
    {
        public static XYZ GetConnectorOrigin(Connector connector) => connector.Origin;
        public static bool IsPhysicalConnector(Connector connector) =>
            connector.ConnectorType == ConnectorType.End ||
            connector.ConnectorType == ConnectorType.Curve ||
            connector.ConnectorType == ConnectorType.Physical;

    }

    public static partial class RevitApi 
    {
        public static double GetX(XYZ point) => point.X;
        public static double GetY(XYZ point) => point.Y;
        public static double GetZ(XYZ point) => point.Z;
    }
}
