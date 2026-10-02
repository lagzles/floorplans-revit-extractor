using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using FloorplansRevitExtractor.Core.Model;

namespace FloorplansRevitExtractor.ViewExport
{
    /// <summary>
    /// Runs a reader's per-element work so that one odd element (an unbound
    /// curve, a failed boolean, a family the API cannot read) becomes a
    /// warning on that element instead of aborting the whole view's export.
    /// </summary>
    internal static class ElementGuard
    {
        internal static void Each<T>(IEnumerable<T> elements, List<GeomWarning> warnings, Action<T> read)
            where T : Element
        {
            foreach (T element in elements)
            {
                try
                {
                    read(element);
                }
                catch (Exception ex) when (!(ex is OutOfMemoryException) && !(ex is Autodesk.Revit.Exceptions.OperationCanceledException))
                {
                    warnings.Add(new GeomWarning(element.Id.Value, $"elemento ignorado ({ex.GetType().Name}): {ex.Message}"));
                }
            }
        }
    }
}
