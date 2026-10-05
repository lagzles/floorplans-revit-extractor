using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using FloorplansRevitExtractor.Core.Geometry;
using FloorplansRevitExtractor.RevitInterop;

namespace FloorplansRevitExtractor.ViewExport
{
    /// <summary>
    /// debug/view_ID_altura.txt: every element the view shows but the cut
    /// plane misses (walls, columns, and rooms inside the crop), with its level
    /// and its base and top relative to the cut plane, plus the plan regions
    /// of the view. A plan region has its own view range, which the API does
    /// not expose: this file is how a block drawn cut but left out is found.
    /// </summary>
    internal static class HeightDiagnostics
    {
        internal static void Write(Document doc, ViewSetup setup, string debugFolder, IEnumerable<Element> uncut)
        {
            var text = new StringBuilder();
            View view = setup.View;
            text.AppendLine($"Vista {view.Name} ({view.Id.Value}), nível {setup.Level.Name} a {Meters(setup.Level.Elevation)} m");
            text.AppendLine($"Plano de corte: {Meters(setup.CutPlaneZFeet)} m absoluto; fundo da vista: {Meters(setup.BottomZFeet)} m absoluto");

            // A plan region is drawn only when selected: looked up by owner view, not by visibility.
            List<Element> regions = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_PlanRegion).WhereElementIsNotElementType()
                .Where(r => r.OwnerViewId == view.Id).ToList();
            text.AppendLine($"Regiões de planta (Plan Region) na vista: {regions.Count}" + (regions.Count > 0 ? " -> ids " + string.Join(", ", regions.Select(r => r.Id.Value)) : ""));
            text.AppendLine($"Níveis do mesmo pavimento cortados à altura própria (até {Meters(setup.SameFloorLimitZFeet)} m): "
                + (setup.ShiftedLevelNames.Count == 0 ? "nenhum" : string.Join(", ", setup.ShiftedLevelNames)));
            text.AppendLine();
            text.AppendLine("id | categoria | tipo | nível | base - corte (m) | topo - corte (m)");

            foreach (Element element in uncut.Concat(RoomsMissed(doc, setup)).OrderBy(e => e.Category?.Name).ThenBy(e => e.Id.Value))
            {
                text.AppendLine(Line(doc, element, setup));
            }

            Directory.CreateDirectory(debugFolder);
            File.WriteAllText(Path.Combine(debugFolder, $"view_{view.Id.Value}_altura.txt"), text.ToString(), Encoding.UTF8);
        }

        /// <summary>Placed rooms whose point is inside the crop but whose volume the cut plane misses.</summary>
        private static IEnumerable<Element> RoomsMissed(Document doc, ViewSetup setup)
        {
            ViewRegion crop = setup.ModelRegion;
            return new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Rooms)
                .WhereElementIsNotElementType()
                .Cast<Room>()
                .Where(r => r.Location is LocationPoint point && Inside(setup.ToFrame(point.Point), crop))
                .Where(r =>
                {
                    BoundingBoxXYZ box = r.get_BoundingBox(null);
                    return box == null || box.Min.Z > setup.CutZFor(r) || box.Max.Z < setup.CutZFor(r);
                });
        }

        private static bool Inside(Point2D p, ViewRegion region) =>
            p.X >= region.MinX && p.X <= region.MaxX && p.Y >= region.MinY && p.Y <= region.MaxY;

        private static string Line(Document doc, Element element, ViewSetup setup)
        {
            BoundingBoxXYZ box = element.get_BoundingBox(null);
            string level = element.LevelId != ElementId.InvalidElementId ? doc.GetElement(element.LevelId)?.Name : "-";
            string type = element is Room room ? room.Name : doc.GetElement(element.GetTypeId())?.Name ?? "-";
            string bottom = box == null ? "sem caixa" : Meters(box.Min.Z - setup.CutZFor(element));
            string top = box == null ? "sem caixa" : Meters(box.Max.Z - setup.CutZFor(element));
            return $"{element.Id.Value} | {element.Category?.Name} | {type} | {level} | {bottom} | {top}";
        }

        private static string Meters(double feet) => UnitsConverter.ToMeters(feet).ToString("F3", CultureInfo.InvariantCulture);
    }
}
