using System.Collections.Generic;
using Autodesk.Revit.DB;
using FloorplansRevitExtractor.Core.Geometry;
using FloorplansRevitExtractor.Core.Model;
using FloorplansRevitExtractor.Core.Raster;

namespace FloorplansRevitExtractor.ViewExport
{
    /// <summary>What the columns of a view contribute to the mask.</summary>
    /// <param name="Uncut">Visible columns the cut plane misses, for the height diagnostics.</param>
    internal sealed record PillarReading(IReadOnlyList<GeomElement> Elements, IReadOnlyList<GeomWarning> Warnings, IReadOnlyList<string> Notes, IReadOnlyList<Element> Uncut);

    /// <summary>
    /// Architectural and structural columns by their real section at the cut
    /// plane (spec v2: L, T and round shapes kept, never the bounding box).
    /// The painter puts them over the wall they are embedded in, which is the
    /// point of the class: the model must learn to tell a pillar from a wall.
    /// </summary>
    internal static class PillarReader
    {
        internal static PillarReading Read(Document doc, ViewSetup setup)
        {
            var elements = new List<GeomElement>();
            var warnings = new List<GeomWarning>();
            var uncut = new List<Element>();

            IEnumerable<FamilyInstance> columns = ViewElements.Collect(doc, setup, BuiltInCategory.OST_Columns, BuiltInCategory.OST_StructuralColumns);
            ElementGuard.Each(columns, warnings, column =>
            {
                (List<IReadOnlyList<Point2D>> outers, List<IReadOnlyList<Point2D>> holes) = CutSection.Of(column, setup, warnings);
                if (outers.Count == 0)
                {
                    uncut.Add(column);
                    return;
                }

                elements.Add(GeomElement.Filled(StructureClass.Pillar, column.Id.Value, outers, holes));
            });

            IReadOnlyList<string> notes = uncut.Count == 0
                ? new string[0]
                : new[] { $"{uncut.Count} pilar(es) visível(is) não cortado(s) pelo plano de corte ficaram fora da máscara." };
            return new PillarReading(elements, warnings, notes, uncut);
        }
    }
}
