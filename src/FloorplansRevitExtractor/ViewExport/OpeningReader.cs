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
    /// Spec v4: a door with no leaf drawn is 12 passage (no swing); a window
    /// 3.0 m or wider, or a curtain kind, is 15 curtain_wall; a door in a
    /// curtain wall is kept, at least 2 px thick; a rectangular wall opening
    /// is a passage when it reaches the floor, else a window (mask only).
    /// </summary>
    internal static class OpeningReader
    {
        /// <summary>How far past the wall face the swing side is probed for its room.</summary>
        private const double ProbePastFaceFeet = 1.0;

        /// <summary>Probe height above the opening's insertion point: inside the room's volume.</summary>
        private const double ProbeHeightFeet = 3.0;

        /// <summary>A rough opening wider than this many nominal widths is a stale family default.</summary>
        private const double MaxRoughToNominal = 1.5;

        /// <summary>Spec v4: a wall opening whose sill is this low (m) reaches the floor: a passage.</summary>
        private const double PassageMaxSillM = 0.10;

        /// <summary>Thinner than this in the mask, an opening vanishes when rasterized.</summary>
        private const double MinOpeningPx = 2.0;

        internal static OpeningReading Read(Document doc, ViewSetup setup, IReadOnlyDictionary<long, int> roomIds, double pxPerM)
        {
            var state = new State(doc.GetElement(setup.PhaseId) as Phase, roomIds, pxPerM);
            IEnumerable<FamilyInstance> openings = ViewElements.Collect(doc, setup, BuiltInCategory.OST_Doors, BuiltInCategory.OST_Windows);
            ElementGuard.Each(openings, state.Warnings, opening => ReadOne(opening, setup, state));
            IEnumerable<Opening> wallOpenings = new FilteredElementCollector(doc, setup.View.Id)
                .OfClass(typeof(Opening))
                .Cast<Opening>()
                .Where(o => o.Host is Wall && o.IsRectBoundary)
                .OrderBy(o => o.Id.Value);
            ElementGuard.Each(wallOpenings, state.Warnings, opening => ReadWallOpening(opening, setup, state));
            return new OpeningReading(state.Elements, state.Doors, state.Windows, state.Warnings, state.Notes());
        }

        private static void ReadOne(FamilyInstance opening, ViewSetup setup, State state)
        {
            bool isDoor = opening.Category.Id.Value == (long)BuiltInCategory.OST_Doors;
            Wall host = CutHost(opening, setup);
            bool inCurtain = host != null && host.WallType.Kind == WallKind.Curtain;
            if (host == null || (inCurtain && !isDoor))
            {
                // A window in a curtain wall is one of its panels: the curtain wall (15) covers it.
                state.CurtainHosted++;
                return;
            }

            if (!CutByPlane(opening, setup.CutPlaneZFeet))
            {
                state.NotCut++;
                return;
            }

            double? widthFeet = OpeningWidthFeet(opening);
            (XYZ Center, XYZ Along, XYZ Normal)? placement = opening.Location is LocationPoint insertion
                ? Placement(insertion.Point, host, state.Warnings)
                : null;
            if (widthFeet == null || placement == null)
            {
                state.Warnings.Add(new GeomWarning(opening.Id.Value, "vão sem largura ou sem posição na parede: fora da máscara"));
                return;
            }

            (XYZ center, XYZ along, XYZ normal) = placement.Value;
            Point2D centerM = setup.ToFrame(center);
            Vector2D alongView = setup.Frame.ToViewDirection(new Vector2D(along.X, along.Y));
            double widthM = UnitsConverter.ToMeters(widthFeet.Value);
            double thicknessM = WallThicknessM(host, state);
            List<IReadOnlyList<Point2D>> rect = CutSection.Clip(
                new[] { OpeningRectangle.Build(centerM, alongView, widthM, thicknessM) },
                setup.ModelRegion);
            if (rect.Count == 0)
            {
                return;
            }

            double rotation = OpeningKinds.RotationDegrees(alongView);
            (int? from, int? to) = Rooms(opening, state);
            if (!isDoor)
            {
                double? sillM = Length(opening, BuiltInParameter.INSTANCE_SILL_HEIGHT_PARAM);
                string windowKind = OpeningKinds.WindowKind(opening.Symbol.FamilyName, opening.Symbol.Name, sillM);
                bool glassSkin = OpeningKinds.IsCurtainWall(windowKind, widthM);
                state.WideWindows += glassSkin ? 1 : 0;
                state.Elements.Add(GeomElement.Filled(glassSkin ? StructureClass.CurtainWall : StructureClass.Window, opening.Id.Value, rect));
                state.Windows.Add(new WindowRecord(state.Windows.Count, to ?? from, windowKind, centerM, rotation, widthM));
                return;
            }

            (string familyKind, bool mapped) = OpeningKinds.DoorKind(opening.Symbol.FamilyName, opening.Symbol.Name);
            if (!mapped)
            {
                state.UnmappedFamilies.Add(opening.Symbol.FamilyName);
            }

            Swing swing = LabelVocabularies.HasHinge(familyKind) ? SwingReader.Read(opening, setup) : null;
            string kind = OpeningKinds.DoorKindForMask(familyKind, swing != null);
            if (kind == "passage")
            {
                state.PassageFamilies.Add(opening.Symbol.FamilyName);
            }

            state.Elements.Add(GeomElement.Filled(kind == "passage" ? StructureClass.Passage : StructureClass.Door, opening.Id.Value, rect));
            if (swing != null)
            {
                state.Elements.Add(swing.Element);
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

        /// <summary>The host's thickness in the mask; a curtain wall's is widened to 2 px, as the curtain wall itself.</summary>
        private static double WallThicknessM(Wall host, State state)
        {
            double thicknessM = UnitsConverter.ToMeters(host.Width);
            return host.WallType.Kind == WallKind.Curtain ? Math.Max(thicknessM, MinOpeningPx / state.PxPerM) : thicknessM;
        }

        /// <summary>
        /// A rectangular wall opening (OST_SWallRectOpening) cut by the plane:
        /// reaching the floor, a passage; higher, a window, or glass skin when
        /// 3.0 m or wider. It has no rooms or family, so it enters the mask only.
        /// </summary>
        private static void ReadWallOpening(Opening opening, ViewSetup setup, State state)
        {
            IList<XYZ> corners = opening.BoundaryRect;
            Wall host = opening.Host as Wall;
            BoundingBoxXYZ hostBox = host?.get_BoundingBox(null);
            if (corners == null || corners.Count < 2 || hostBox == null)
            {
                return;
            }

            double bottom = Math.Min(corners[0].Z, corners[1].Z);
            double top = Math.Max(corners[0].Z, corners[1].Z);
            if (bottom > setup.CutPlaneZFeet || top < setup.CutPlaneZFeet)
            {
                state.NotCut++;
                return;
            }

            XYZ middle = (corners[0] + corners[1]) / 2;
            (XYZ Center, XYZ Along, XYZ Normal)? placement = Placement(middle, host, state.Warnings);
            if (placement == null)
            {
                return;
            }

            double widthM = UnitsConverter.ToMeters(new XYZ(corners[1].X - corners[0].X, corners[1].Y - corners[0].Y, 0).GetLength());
            double sillM = UnitsConverter.ToMeters(bottom - hostBox.Min.Z);
            StructureClass cls = sillM <= PassageMaxSillM ? StructureClass.Passage
                : OpeningKinds.IsCurtainWall("single", widthM) ? StructureClass.CurtainWall
                : StructureClass.Window;
            Vector2D alongView = setup.Frame.ToViewDirection(new Vector2D(placement.Value.Along.X, placement.Value.Along.Y));
            List<IReadOnlyList<Point2D>> rect = CutSection.Clip(
                new[] { OpeningRectangle.Build(setup.ToFrame(placement.Value.Center), alongView, widthM, WallThicknessM(host, state)) },
                setup.ModelRegion);
            if (rect.Count > 0)
            {
                state.Elements.Add(GeomElement.Filled(cls, opening.Id.Value, rect));
                state.WallOpenings++;
            }
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
        private static (XYZ Center, XYZ Along, XYZ Normal)? Placement(XYZ point, Wall host, List<GeomWarning> warnings)
        {
            if (!(host.Location is LocationCurve location))
            {
                return null;
            }

            IntersectionResult projected = location.Curve.Project(point);
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

            double offset = host.WallType.Kind == WallKind.Curtain ? 0 : CenterLineOffset(host, warnings);
            if (offset != 0 && !(location.Curve is Line))
            {
                warnings.Add(new GeomWarning(host.Id.Value, "parede curva com linha de localização fora do eixo: o vão pode sair deslocado"));
            }

            XYZ onCenter = projected.XYZPoint - (normal * offset);
            return (new XYZ(onCenter.X, onCenter.Y, point.Z), along, normal);
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
            internal State(Phase phase, IReadOnlyDictionary<long, int> roomIds, double pxPerM)
            {
                Phase = phase;
                RoomIds = roomIds;
                PxPerM = pxPerM;
            }

            internal double PxPerM { get; }

            internal SortedSet<string> PassageFamilies { get; } = new SortedSet<string>(StringComparer.Ordinal);

            internal int WideWindows { get; set; }

            internal int WallOpenings { get; set; }

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

            internal IReadOnlyList<string> Notes()
            {
                var notes = new List<string>();
                Add(notes, CurtainHosted, "janela(s) em parede-cortina (painel, coberto pela pele de vidro) ou vão(s) sem parede hospedeira ficaram fora");
                Add(notes, WideWindows, "janela(s) com 3,0 m ou mais, ou de tipo cortina, como pele de vidro (15)");
                Add(notes, WallOpenings, "abertura(s) retangular(es) de parede na máscara (passagem até o piso, janela acima), fora do label");
                if (PassageFamilies.Count > 0)
                {
                    notes.Add("Portas sem folha na vista, como passagem (12): " + string.Join("; ", PassageFamilies) + ".");
                }

                Add(notes, NotCut, "porta(s)/janela(s) visível(is) não cortada(s) pelo plano de corte ficaram fora");
                Add(notes, NoRoom, "porta(s) sem ambiente em nenhum dos lados: pintada(s) na máscara, fora do label");
                Add(notes, SameRoom, "porta(s) dentro de um único ambiente (FromRoom = ToRoom): pintada(s) na máscara, fora do label");
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
