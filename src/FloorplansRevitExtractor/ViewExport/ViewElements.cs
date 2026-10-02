using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace FloorplansRevitExtractor.ViewExport
{
    /// <summary>Family instances of some categories visible in the view, in its phase (new or existing, never demolished).</summary>
    internal static class ViewElements
    {
        internal static IEnumerable<FamilyInstance> Collect(Document doc, ViewSetup setup, params BuiltInCategory[] categories)
        {
            FilteredElementCollector collector = new FilteredElementCollector(doc, setup.View.Id)
                .OfClass(typeof(FamilyInstance))
                .WherePasses(new ElementMulticategoryFilter(categories.ToList()));
            if (setup.PhaseId != ElementId.InvalidElementId)
            {
                collector = collector.WherePasses(new ElementPhaseStatusFilter(
                    setup.PhaseId,
                    new List<ElementOnPhaseStatus> { ElementOnPhaseStatus.New, ElementOnPhaseStatus.Existing }));
            }

            return collector.Cast<FamilyInstance>().OrderBy(f => f.Id.Value);
        }
    }
}
