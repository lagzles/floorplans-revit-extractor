using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using FloorplansRevitExtractor.Core.Geometry;
using FloorplansRevitExtractor.Core.Label;
using FloorplansRevitExtractor.Core.Model;
using FloorplansRevitExtractor.Core.Raster;
using FloorplansRevitExtractor.RevitInterop;

namespace FloorplansRevitExtractor.ViewExport
{
    /// <summary>What the doors and windows of a view contribute: mask openings and swings, label records, notes.</summary>
    internal sealed record OpeningReading(
        IReadOnlyList<GeomElement> Elements,
        IReadOnlyList<DoorRecord> Doors,
        IReadOnlyList<WindowRecord> Windows,
        IReadOnlyList<GeomWarning> Warnings,
        IReadOnlyList<string> Notes);

    /// <summary>
    /// Doors and windows cut by the view's cut plane. In the mask each one is
    /// the critical spec v2 rectangle: opening width x host wall thickness,
    /// centered on the wall's own center line (the insertion point sits on the
    /// wall's location line, which may be a face), never extended. Single and
    /// double doors also draw their swing, which gives the label its hinge and
    /// the room the leaf opens into. Rooms come from FromRoom/ToRoom in the
    /// view's phase; exterior means the host wall is an exterior wall.
    /// </summary>
    internal static class OpeningReader
    {
        /// <summary>How far past the wall face the swing side is probed for its room.</summary>
        private const double ProbePastFaceFeet = 1.0;

        /// <summary>Probe height above the opening's insertion point: inside the room's volume.</summary>
        private const double ProbeHeightFeet = 3.0;

        /// <summary>A rough opening wider than this many nominal widths is a stale family default.</summary>
        private const double MaxRoughToNominal = 1.5;

        internal static OpeningReading Read(Document doc, ViewSetup setup, IReadOnlyDictionary<long, int> roomIds)
        {
            var state = new State(doc.GetElement(setup.PhaseId) as Phase, roomIds);
            IEnumerable<FamilyInstance> openings = ViewElements.Collect(doc, setup, BuiltInCategory.OST_Doors, BuiltInCategory.OST_Windows);
            ElementGuard.Each(openings, state.Warnings, opening => ReadOne(opening, setup, state));
            return new OpeningReading(state.Elements, state.Doors, state.Windows, state.Warnings, state.Notes());
        }

        private static void ReadOne(FamilyInstance opening, ViewSetup setup, State state)
        {
            bool isDoor = opening.Category.Id.Value == (long)BuiltInCategory.OST_Doors;
            Wall host = CutHost(opening, setup);
            if (host == null || host.WallType.Kind == WallKind.Curtain)
            {
                state.CurtainHosted++;
                return;
            }

            if (!CutByPlane(opening, setup.CutPlaneZFeet))
            {
                state.NotCut++;
                return;
            }

            double? widthFeet = OpeningWidthFeet(opening);
            (XYZ Center, XYZ Along, XYZ Normal)? placement = Placement(opening, host, state.Warnings);
            if (widthFeet == null || placement == null)
            {
                state.Warnings.Add(new GeomWarning(opening.Id.Value, "vão sem largura ou sem posição na parede: fora da máscara"));
                return;
            }

            (XYZ center, XYZ along, XYZ normal) = placement.Value;
            Point2D centerM = setup.ToFrame(center);
            Vector2D alongView = setup.Frame.ToViewDirection(new Vector2D(along.X, along.Y));
            double widthM = UnitsConverter.ToMeters(widthFeet.Value);
            List<IReadOnlyList<Point2D>> rect = CutSection.Clip(
                new[] { OpeningRectangle.Build(centerM, alongView, widthM, UnitsConverter.ToMeters(host.Width)) },
                setup.ModelRegion);
            if (rect.Count == 0)
            {
                return;
            }

            state.Elements.Add(GeomElement.Filled(isDoor ? StructureClass.Door : StructureClass.Window, opening.Id.Value, rect));
            double rotation = OpeningKinds.RotationDegrees(alongView);
            (int? from, int? to) = Rooms(opening, state);
            if (!isDoor)
            {
                double? sillM = Length(opening, BuiltInParameter.INSTANCE_SILL_HEIGHT_PARAM);
                string windowKind = OpeningKinds.WindowKind(opening.Symbol.FamilyName, opening.Symbol.Name, sillM);
                state.Windows.Add(new WindowRecord(state.Windows.Count, to ?? from, windowKind, centerM, rotation, widthM));
                return;
            }

            (string kind, bool mapped) = OpeningKinds.DoorKind(opening.Symbol.FamilyName, opening.Symbol.Name);
            if (!mapped)
            {
                state.UnmappedFamilies.Add(opening.Symbol.FamilyName);
            }

            Swing swing = LabelVocabularies.HasHinge(kind) ? SwingReader.Read(opening, setup) : null;
            if (swing != null)
            {
                state.Elements.Add(swing.Element);
            }
            else if (LabelVocabularies.HasHinge(kind))
            {
                state.MissingSwing++;
            }

            AddDoor(opening, host, kind, from, to, center, normal, centerM, rotation, widthM, swing, setup, state);
        }

        private static void AddDoor(
            FamilyInstance door,
            Wall host,
            string kind,
            int? from,
            int? to,
            XYZ center,
            XYZ normal,
            Point2D centerM,
            double rotation,
            double widthM,
            Swing swing,
            ViewSetup setup,
            State state)
        {
            if (from.HasValue && from == to)
            {
                state.SameRoom++;
                return;
            }

            // The dataset's room_a is always a room; room_b is null when the door opens to the outside.
            int? roomA = from ?? to;
            int? roomB = from.HasValue ? to : null;
            if (roomA == null)
            {
                state.NoRoom++;
                return;
            }

            Point2D? hinge = swing == null ? (Point2D?)null : setup.ToFrame(swing.Hinge);
            int? swingInto = swing == null ? null : SwingRoom(door, host, center, normal, swing, state, roomA.Value, roomB);
            bool exterior = host.WallType.Function == WallFunction.Exterior;
            state.Doors.Add(new DoorRecord(state.Doors.Count, roomA.Value, roomB, kind, centerM, rotation, widthM, hinge, swingInto, exterior));
        }

        /// <summary>
        /// The room on the side of the wall the swing arc is drawn on, probed
        /// just past the wall face at mid height. Neither FacingOrientation nor
        /// From/To (which users can flip) says which side the leaf opens to.
        /// </summary>
        private static int? SwingRoom(FamilyInstance door, Wall host, XYZ center, XYZ normal, Swing swing, State state, int roomA, int? roomB)
        {
            XYZ toArc = swing.ArcMiddle - center;
            double side = Math.Sign(new XYZ(toArc.X, toArc.Y, 0).DotProduct(normal));
            if (side == 0)
            {
                return null;
            }

            XYZ probe = center + (normal * side * ((host.Width / 2) + ProbePastFaceFeet)) + new XYZ(0, 0, ProbeHeightFeet);
            Room[] rooms = { RoomOf(door, state.Phase, from: true), RoomOf(door, state.Phase, from: false) };
            foreach (Room room in rooms.Where(r => r != null))
            {
                if (state.RoomIds.TryGetValue(room.Id.Value, out int id) && (id == roomA || id == roomB) && room.IsPointInRoom(probe))
                {
                    return id;
                }
            }

            return null;
        }

        private static (int? From, int? To) Rooms(FamilyInstance opening, State state)
        {
            int? Map(Room room) => room != null && state.RoomIds.TryGetValue(room.Id.Value, out int id) ? id : (int?)null;
            return (Map(RoomOf(opening, state.Phase, from: true)), Map(RoomOf(opening, state.Phase, from: false)));
        }

        private static Room RoomOf(FamilyInstance opening, Phase phase, bool from) =>
            phase == null
                ? (from ? opening.FromRoom : opening.ToRoom)
                : (from ? opening.get_FromRoom(phase) : opening.get_ToRoom(phase));

        /// <summary>
        /// The wall the opening really cuts: for a stacked wall, the member at
        /// the cut height, whose type has the layers and the thickness.
        /// </summary>
        private static Wall CutHost(FamilyInstance opening, ViewSetup setup)
        {
            if (!(opening.Host is Wall host) || !host.IsStackedWall)
            {
                return opening.Host as Wall;
            }

            return host.GetStackedWallMemberIds()
                .Select(id => opening.Document.GetElement(id) as Wall)
                .FirstOrDefault(member =>
                {
                    BoundingBoxXYZ box = member?.get_BoundingBox(null);
                    return box != null && box.Min.Z <= setup.CutPlaneZFeet && setup.CutPlaneZFeet <= box.Max.Z;
                });
        }

        /// <summary>
        /// The opening's center on the wall's center line, the wall direction
        /// there, and the normal toward the exterior. The insertion point is
        /// projected onto the location line, then moved by the location line's
        /// offset from the wall center (CompoundStructure.GetOffsetForLocationLine);
        /// that offset's sign is not documented, so it is read off the exterior
        /// face, whose side is known.
        /// </summary>
        private static (XYZ Center, XYZ Along, XYZ Normal)? Placement(FamilyInstance opening, Wall host, List<GeomWarning> warnings)
        {
            if (!(opening.Location is LocationPoint point) || !(host.Location is LocationCurve location))
            {
                return null;
            }

            IntersectionResult projected = location.Curve.Project(point.Point);
            if (projected == null)
            {
                return null;
            }

            XYZ along = location.Curve.ComputeDerivatives(projected.Parameter, false).BasisX.Normalize();
            XYZ normal = XYZ.BasisZ.CrossProduct(along).Normalize();
            if (normal.DotProduct(host.Orientation) < 0)
            {
                normal = normal.Negate();
            }

            double offset = CenterLineOffset(host, warnings);
            if (offset != 0 && !(location.Curve is Line))
            {
                warnings.Add(new GeomWarning(host.Id.Value, "parede curva com linha de localização fora do eixo: o vão pode sair deslocado"));
            }

            XYZ onCenter = projected.XYZPoint - (normal * offset);
            return (new XYZ(onCenter.X, onCenter.Y, point.Point.Z), along, normal);
        }

        /// <summary>Signed distance from the wall center to its location line, positive toward the exterior.</summary>
        private static double CenterLineOffset(Wall host, List<GeomWarning> warnings)
        {
            CompoundStructure structure = host.WallType.GetCompoundStructure();
            Parameter keyRef = host.get_Parameter(BuiltInParameter.WALL_KEY_REF_PARAM);
            if (structure == null || keyRef == null)
            {
                warnings.Add(new GeomWarning(host.Id.Value, "parede sem estrutura de camadas: vãos centrados na linha de localização"));
                return 0;
            }

            try
            {
                double exterior = structure.GetOffsetForLocationLine(WallLocationLine.FinishFaceExterior);
                double offset = structure.GetOffsetForLocationLine((WallLocationLine)keyRef.AsInteger());
                return exterior < 0 ? -offset : offset;
            }
            catch (Autodesk.Revit.Exceptions.InvalidOperationException)
            {
                warnings.Add(new GeomWarning(host.Id.Value, "parede não homogênea na vertical: vãos centrados na linha de localização"));
                return 0;
            }
        }

        private static bool CutByPlane(FamilyInstance opening, double cutZFeet)
        {
            BoundingBoxXYZ box = opening.get_BoundingBox(null);
            return box != null && box.Min.Z <= cutZFeet && cutZFeet <= box.Max.Z;
        }

        /// <summary>
        /// Spec v2: the rough opening width when the family keeps it, else the
        /// nominal width. Many families carry an unassociated Rough Width with
        /// a stale default, so it is trusted only between 1x and 1.5x the
        /// nominal width. Instance values come before type values.
        /// </summary>
        private static double? OpeningWidthFeet(FamilyInstance opening)
        {
            double? nominal = FirstLength(opening, BuiltInParameter.DOOR_WIDTH, BuiltInParameter.WINDOW_WIDTH, BuiltInParameter.FAMILY_WIDTH_PARAM);
            double? rough = FirstLength(opening, BuiltInParameter.FAMILY_ROUGH_WIDTH_PARAM);
            if (nominal == null)
            {
                return rough;
            }

            bool roughPlausible = rough >= nominal && rough <= nominal * MaxRoughToNominal;
            return roughPlausible ? rough : nominal;
        }

        private static double? FirstLength(FamilyInstance opening, params BuiltInParameter[] candidates)
        {
            foreach (Element source in new Element[] { opening, opening.Symbol })
            {
                foreach (BuiltInParameter candidate in candidates)
                {
                    Parameter p = source?.get_Parameter(candidate);
                    if (p != null && p.HasValue && p.StorageType == StorageType.Double && p.AsDouble() > 0)
                    {
                        return p.AsDouble();
                    }
                }
            }

            return null;
        }

        private static double? Length(Element element, BuiltInParameter parameter)
        {
            Parameter p = element.get_Parameter(parameter);
            return p != null && p.HasValue && p.StorageType == StorageType.Double ? UnitsConverter.ToMeters(p.AsDouble()) : (double?)null;
        }

        /// <summary>What one view's openings accumulate while being read.</summary>
        private sealed class State
        {
            internal State(Phase phase, IReadOnlyDictionary<long, int> roomIds)
            {
                Phase = phase;
                RoomIds = roomIds;
            }

            internal Phase Phase { get; }

            internal IReadOnlyDictionary<long, int> RoomIds { get; }

            internal List<GeomElement> Elements { get; } = new List<GeomElement>();

            internal List<DoorRecord> Doors { get; } = new List<DoorRecord>();

            internal List<WindowRecord> Windows { get; } = new List<WindowRecord>();

            internal List<GeomWarning> Warnings { get; } = new List<GeomWarning>();

            internal SortedSet<string> UnmappedFamilies { get; } = new SortedSet<string>(StringComparer.Ordinal);

            internal int CurtainHosted { get; set; }

            internal int NotCut { get; set; }

            internal int NoRoom { get; set; }

            internal int SameRoom { get; set; }

            internal int MissingSwing { get; set; }

            internal IReadOnlyList<string> Notes()
            {
                var notes = new List<string>();
                Add(notes, CurtainHosted, "porta(s)/janela(s) em parede-cortina ou sem parede hospedeira ficaram fora");
                Add(notes, NotCut, "porta(s)/janela(s) visível(is) não cortada(s) pelo plano de corte ficaram fora");
                Add(notes, NoRoom, "porta(s) sem ambiente em nenhum dos lados: pintada(s) na máscara, fora do label");
                Add(notes, SameRoom, "porta(s) dentro de um único ambiente (FromRoom = ToRoom): pintada(s) na máscara, fora do label");
                Add(notes, MissingSwing, "porta(s) de giro sem arco na vista: sem swing na máscara e sem dobradiça no label");
                if (UnmappedFamilies.Count > 0)
                {
                    notes.Add("Famílias de porta não mapeadas (viraram \"single\"):" + string.Join("; ", UnmappedFamilies) + ".");
                }

                return notes;
            }

            private static void Add(List<string> notes, int count, string text)
            {
                if (count > 0)
                {
                    notes.Add($"{count} {text}.");
                }
            }
        }
    }
}
