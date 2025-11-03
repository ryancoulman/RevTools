using IRevitAPI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Autodesk.Revit.DB;

namespace RevitAPI2023
{
    public partial class RevitApi2023 : IConnectorsApi
    {
        public XYZ GetConnectorOrigin(Connector connector) => connector.Origin;
        public bool IsPhysicalConnector(Connector connector) =>
            connector.ConnectorType == ConnectorType.End ||
            connector.ConnectorType == ConnectorType.Curve ||
            connector.ConnectorType == ConnectorType.Physical;

    }

    public partial class RevitApi2023 : IXYZApi
    {
        public double GetX(XYZ point) => point.X;
        public double GetY(XYZ point) => point.Y;
        public double GetZ(XYZ point) => point.Z;
}
}
