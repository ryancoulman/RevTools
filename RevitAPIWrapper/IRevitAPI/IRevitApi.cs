using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

// Use revit api iheritance levels to split up irevit api

namespace IRevitAPI
{
    public interface IBaseAPI 
    {
    }

    public interface IRevitApi :
    IBaseAPI,
    IConnectorsApi,
    IXYZApi
    // Add more as needed: IDocumentApi, IElementApi, etc.
    { }
}
