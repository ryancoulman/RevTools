using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IRevitAPIWrapper
{
    // For any flat api methods that have no parent or child classes
    public interface IConnectorsApi : IBaseAPI
    {
        XYZ GetConnectorOrigin(Connector connector);
        bool IsPhysicalConnector(Connector connector);
    }

    public interface IXYZApi : IBaseAPI
    {
        double GetX(XYZ point);
        double GetY(XYZ point);
        double GetZ(XYZ point);

    }
}
