using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ValveGetter.Command;

namespace RevitAddin.Commands
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ValveGetterCommandMain : IExternalCommand
    {
        // This is the method Revit calls when you click the smartbutton
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            return ExcecuteHelper.Execute(commandData, ref message, Mode.Default);
        }
    }

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ValveGetterCommandAdvanced : IExternalCommand
    {
        // This is the method Revit calls when you click the drop-down button
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            return ExcecuteHelper.Execute(commandData, ref message, Mode.Advanced);
        }
    }

    internal static class ExcecuteHelper
    {
        public static Result Execute(ExternalCommandData commandData, ref string message, Mode mode)
        {
            try
            {
                UIDocument uidoc = commandData.Application.ActiveUIDocument;

                ValveServiceCommand.Execute(uidoc, mode);

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return Result.Failed;
            }
        }
    }
}
