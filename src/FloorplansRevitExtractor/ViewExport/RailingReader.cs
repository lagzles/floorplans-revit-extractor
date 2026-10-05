using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using FloorplansRevitExtractor.Core.Geometry;
using FloorplansRevitExtractor.Core.Model;
using FloorplansRevitExtractor.Core.Raster;
using FloorplansRevitExtractor.Core.Vocabulary;

namespace FloorplansRevitExtractor.ViewExport
{
    /// <summary>What the railings of a view contribute to the mask.</summary>
    internal sealed record RailingReading(IReadOnlyList<GeomElement> Elements, IReadOnlyList<GeomWarning> Warnings, IReadOnlyList<string> Notes);

    /// <summary>
    /// Spec v4 section 4.3: a guardrail on a terrace or balcony is 14
    /// low_wall, a fixed 10 cm stroke along its path (GUARDRAIL_ROOM_TYPES,
    /// LOW_WALL_PEN_M), whatever its drawing. Stair and ramp railings stay out
    /// (the stair stays), and so does a covered gallery's edge ("GALERIA").
    /// The terrace is found by probing both sides of the path's middle.
    /// </summary>
    internal static class RailingReader
    {
        /// <summary>The generator's LOW_WALL_PEN_M.</summary>
        internal const double StrokeM = 0.10;

        /// <summary>How far to each side of the path the room is probed (feet, ~30 cm), above the floor.</summary>
        private const double ProbeSideFeet = 1.0;
        private const double ProbeUpFeet = 1.0;

        internal static RailingReading Read(Document doc, ViewSetup setup)
        {
            var elements = new List<GeomElement>();
            var warnings = new List<GeomWarning>();
            int onStairs = 0;
            int elsewhere = 0;
            Phase phase = doc.GetElement(setup.PhaseId) as Phase;

            IEnumerable<Railing> railings = new FilteredElementCollector(doc, setup.View.Id)
                .OfClass(typeof(Railing))
                .Cast<Railing>()
                .Where(r => Shown(r, setup))
                .OrderBy(r => r.Id.Value);

            ElementGuard.Each(railings, warnings, railing =>
            {
                Element host = railing.HostId == ElementId.InvalidElementId ? null : doc.GetElement(railing.HostId);
                if (host is Stairs || host is StairsRun || host is StairsLanding || host?.Category?.Id.Value == (long)BuiltInCategory.OST_Ramps)
                {
                    onStairs++;
                    return;
                }

                List<Curve> path = railing.GetPath().Where(c => c.IsBound).ToList();
                if (path.Count == 0 || !OnTerrace(doc, phase, path, railing))
                {
                    elsewhere++;
                    return;
                }

                var lines = path.Select(c => (IReadOnlyList<Point2D>)c.Tessellate().Select(setup.ToFrame).ToArray()).ToList();
                elements.Add(GeomElement.Stroked(StructureClass.LowWall, railing.Id.Value, lines, StrokeM, lines.Select(_ => false).ToList()));
            });

            var notes = new List<string>();
            if (elements.Count > 0)
            {
                notes.Add($"{elements.Count} guarda-corpo(s) de terraço/varanda como mureta (14), faixa de 10 cm.");
            }

            if (onStairs + elsewhere > 0)
            {
                notes.Add($"Guarda-corpos fora da máscara: {onStairs} de escada/rampa, {elsewhere} fora de terraço/varanda (ou em galeria).");
            }

            return new RailingReading(elements, warnings, notes);
        }

        private static bool Shown(Element railing, ViewSetup setup)
        {
            BoundingBoxXYZ box = railing.get_BoundingBox(null);
            return box != null && box.Min.Z <= setup.CutPlaneZFeet && box.Max.Z >= setup.BottomZFeet;
        }

        /// <summary>A terrace room on either side of the middle of the path, and no gallery.</summary>
        private static bool OnTerrace(Document doc, Phase phase, List<Curve> path, Railing railing)
        {
            Curve middle = path[path.Count / 2];
            XYZ point = middle.Evaluate(0.5, true);
            XYZ along = middle.ComputeDerivatives(0.5, true).BasisX;
            XYZ side = XYZ.BasisZ.CrossProduct(new XYZ(along.X, along.Y, 0)).Normalize();
            double z = (railing.get_BoundingBox(null)?.Min.Z ?? point.Z) + ProbeUpFeet;

            foreach (double sign in new[] { 1.0, -1.0 })
            {
                XYZ probe = new XYZ(point.X, point.Y, z) + (side * sign * ProbeSideFeet);
                Room room = phase == null ? doc.GetRoomAtPoint(probe) : doc.GetRoomAtPoint(probe, phase);
                if (room == null)
                {
                    continue;
                }

                string name = RoomTypeVocabulary.Normalize(room.Name);
                if (name.Contains("GALERIA") || name.Contains("GALLERY"))
                {
                    return false;
                }

                if (RoomTypeVocabulary.TryMapType(name, out string type) && type == "terrace")
                {
                    return true;
                }
            }

            return false;
        }
    }
}
