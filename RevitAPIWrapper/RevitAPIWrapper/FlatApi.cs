using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Autodesk.Revit.DB;
using IRevitAPIWrapper;

namespace RevitAPIWrapper
{
    public partial class RevitApi : IConnectorsApi
    {
        public XYZ GetConnectorOrigin(Connector connector) => connector.Origin;
        public bool IsPhysicalConnector(Connector connector) =>
            connector.ConnectorType == ConnectorType.End ||
            connector.ConnectorType == ConnectorType.Curve ||
            connector.ConnectorType == ConnectorType.Physical;

    }

    public partial class RevitApi : IXYZApi
    {
        public double GetX(XYZ point) => point.X;
        public double GetY(XYZ point) => point.Y;
        public double GetZ(XYZ point) => point.Z;
    }
}
