using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace FloorplansRevitExtractor.ViewExport
{
    /// <param name="Group">The Project Browser group the view sits in: its kind and its view type, "Plantas de piso (Floor Plan)".</param>
    /// <param name="Sheets">Numbers of the sheets the view is placed on; empty when on none.</param>
    /// <param name="Blocked">Why the view cannot become a sample; null when it can.</param>
    internal sealed record ViewEntry(ViewPlan View, string Group, int GroupOrder, IReadOnlyList<string> Sheets, string Blocked)
    {
        internal bool OnSheet => Sheets.Count > 0;

        internal bool Exportable => Blocked == null;
    }

    /// <summary>
    /// The plan views of the open model, grouped as the Project Browser groups
    /// them by family and type (kind of plan, then the view type's name), each
    /// with its sheets and, when it cannot be exported, the reason. Ceiling plans
    /// are listed but blocked: they look up, not down at a cut plane.
    /// </summary>
    internal static class ViewCatalog
    {
        internal static IReadOnlyList<ViewEntry> Collect(Document doc)
        {
            ILookup<long, string> sheetsByView = new FilteredElementCollector(doc)
                .OfClass(typeof(Viewport))
                .Cast<Viewport>()
                .Select(vp => (View: vp.ViewId.Value, Sheet: (doc.GetElement(vp.SheetId) as ViewSheet)?.SheetNumber))
                .Where(p => p.Sheet != null)
                .ToLookup(p => p.View, p => p.Sheet);

            return new FilteredElementCollector(doc)
                .OfClass(typeof(ViewPlan))
                .Cast<ViewPlan>()
                .Where(v => !v.IsTemplate && Group(v) != null)
                .Select(v => new ViewEntry(
                    v,
                    $"{Group(v).Value.Name} ({doc.GetElement(v.GetTypeId())?.Name ?? "-"})",
                    Group(v).Value.Order,
                    sheetsByView[v.Id.Value].OrderBy(s => s).ToList(),
                    BlockedReason(v)))
                .OrderBy(e => e.GroupOrder)
                .ThenBy(e => e.Group, System.StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(e => e.View.Name, System.StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }

        private static (string Name, int Order)? Group(ViewPlan view)
        {
            switch (view.ViewType)
            {
                case ViewType.FloorPlan:
                    return ("Plantas de piso", 0);
                case ViewType.EngineeringPlan:
                    return ("Plantas estruturais", 1);
                case ViewType.AreaPlan:
                    return ("Plantas de área", 2);
                case ViewType.CeilingPlan:
                    return ("Plantas de forro", 3);
                default:
                    return null;
            }
        }

        private static string BlockedReason(ViewPlan view)
        {
            try
            {
                ViewSetup.Read(view);
                return null;
            }
            catch (ViewNotExportableException ex)
            {
                return ex.Message;
            }
        }
    }
}
