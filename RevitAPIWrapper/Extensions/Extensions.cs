using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Linq;
using System.Text;
using Autodesk.Revit.DB;

// Extensions is for more complex shared api code that goes beyond simple method calls 
namespace RevitAPIWrapper.Extensions;


public static class BuiltInEnumHandler
{
    /// <summary>
    /// Converts a stored BIC long value to a BuiltInCategory, with validation
    /// </summary>
    /// <returns>The BuiltInCategory if valid and exists in current version, otherwise null</returns>
    public static T? GetBuiltInEnum<T>(long value) where T : struct, Enum
    {
#if REVIT2024_OR_GREATER
    // In 2024+, built-in enums are long
    if (value >= 0)
        return null;
    if (!Enum.IsDefined(typeof(T), value))
        return null;
    return (T)Enum.ToObject(typeof(T), value);
#else
        // Pre-2024, built-in enums are int
        if (value >= 0 || value < int.MinValue || value > int.MaxValue)
            return null;
        int intValue = (int)value;
        if (!Enum.IsDefined(typeof(T), intValue))
            return null;
        return (T)Enum.ToObject(typeof(T), intValue);
#endif
    }
}