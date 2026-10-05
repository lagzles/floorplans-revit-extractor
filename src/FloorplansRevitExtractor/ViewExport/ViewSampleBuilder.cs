using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;
using FloorplansRevitExtractor.Core.Geometry;
using FloorplansRevitExtractor.Core.Label;
using FloorplansRevitExtractor.Core.Model;
using FloorplansRevitExtractor.Core.Png;
using FloorplansRevitExtractor.Core.Raster;
using FloorplansRevitExtractor.Core.Sample;
using FloorplansRevitExtractor.RevitInterop;

namespace FloorplansRevitExtractor.ViewExport
{
    /// <summary>
    /// Reads one plan view into a <see cref="SampleRequest"/>: exports the
    /// image, measures its transform, and extracts every mask class of spec
    /// v3: rooms, stairs, voids, cut walls, pillars, furniture, doors, windows,
    /// door swings, grids and dimensions.
    /// </summary>
    internal static class ViewSampleBuilder
    {
        internal const string PluginVersion = "0.2.0";

        /// <summary>v1: each plan view is one sample of one level.</summary>
        private const string Mode = "level";

        internal static (SampleRequest Request, string Diagnostics) Build(
            Document doc,
            ViewSetup setup,
            double targetPxPerM,
            string revitVersion,
            string debugFolder)
        {
            ExportedImage image = ViewImageExporter.Export(doc, setup, targetPxPerM, debugFolder);
            RoomReading rooms = ViewRoomReader.Read(doc, setup, setup.ModelRegion);
            var ink = new InkBoxes(PngDecoder.DecodeRgb(image.Png), image.Transform);
            WallReading walls = WallSectionReader.Read(doc, setup, image.Transform.PxPerM);
            OpeningReading openings = OpeningReader.Read(doc, setup, rooms.RecordIdByRoom, image.Transform.PxPerM);
            RailingReading railings = RailingReader.Read(doc, setup);
            PillarReading pillars = PillarReader.Read(doc, setup);
            StairReading stairs = StairReader.Read(doc, setup);
            VoidReading voids = VoidReader.Read(doc, setup);
            FloorReading floors = FloorReader.Read(doc, setup, rooms.Records.Where(r => r.Type == "patio").Select(r => r.Polygon).ToList());
            GridReading grids = GridReader.Read(doc, setup, ink);
            DimensionReading dimensions = DimensionReader.Read(doc, setup, ink);
            TextReading texts = TextReader.Read(doc, setup, ink);
            FurnitureReading furniture = FurnitureReader.Read(doc, setup, rooms.RecordIdByRoom);
            IReadOnlyList<GeomElement> elements = floors.Elements
                .Concat(rooms.Elements)
                .Concat(stairs.Elements)
                .Concat(voids.Elements)
                .Concat(walls.Elements)
                .Concat(railings.Elements)
                .Concat(pillars.Elements)
                .Concat(furniture.Elements)
                .Concat(openings.Elements)
                .Concat(grids.Elements)
                .Concat(dimensions.Elements)
                .Concat(texts.Elements)
                .ToList();
            HeightDiagnostics.Write(doc, setup, debugFolder, walls.Uncut.Concat(pillars.Uncut));
            ViewPlan view = setup.View;
            string docGuid = doc.ProjectInformation.UniqueId;

            var geom = new GeomDocument
            {
                View = new GeomView(view.Id.Value, view.Name, view.Scale, setup.CutPlaneM),
                Transform = image.Transform,
                Elements = elements,
                Warnings = walls.Warnings
                    .Concat(openings.Warnings)
                    .Concat(pillars.Warnings)
                    .Concat(stairs.Warnings)
                    .Concat(voids.Warnings)
                    .Concat(furniture.Warnings)
                    .Concat(floors.Warnings)
                    .Concat(grids.Warnings)
                    .Concat(dimensions.Warnings)
                    .Concat(railings.Warnings)
                    .Concat(texts.Warnings)
                    .ToList(),
            };

            var request = new SampleRequest
            {
                Key = new SampleKey(docGuid, Mode, view.Id.Value.ToString(CultureInfo.InvariantCulture)),
                ImagePng = image.Png,
                Geom = geom,
                Source = new SourceFragment
                {
                    DocTitle = doc.Title,
                    DocGuid = docGuid,
                    RevitVersion = revitVersion,
                    Mode = Mode,
                    LevelId = setup.Level.Id.Value,
                    LevelName = setup.Level.Name,
                    LevelElevationM = UnitsConverter.ToMeters(setup.Level.Elevation),
                    ViewId = view.Id.Value,
                    ViewName = view.Name,
                    ViewScaleDen = view.Scale,
                    ExportedAt = DateTimeOffset.Now.ToString("o", CultureInfo.InvariantCulture),
                    PluginVersion = PluginVersion,
                    PxPerMTarget = targetPxPerM,
                    Alignment = "direto",
                },
                Building = new BuildingMeasures
                {
                    // Gross floor area is not measured yet: the rooms' net area stands in.
                    TotalArea = rooms.Records.Sum(r => r.Area),
                    WallExt = walls.WallExtM,
                    WallInt = walls.WallIntM,
                    WallIntMin = walls.WallIntMinM,
                    ThinPartitions = walls.ThinPartitions,
                    Outline = Array.Empty<Point2D>(),
                },
                Rooms = rooms.Records,
                Doors = openings.Doors,
                Windows = openings.Windows,
                Furniture = furniture.Records,
                RenderStyle = RenderStyle.Empty.With("scale_den", view.Scale).With("lang", "pt"),
                ReviewNotes = rooms.Notes
                    .Concat(floors.Notes)
                    .Concat(walls.Notes)
                    .Concat(openings.Notes)
                    .Concat(pillars.Notes)
                    .Concat(stairs.Notes)
                    .Concat(voids.Notes)
                    .Concat(furniture.Notes)
                    .Concat(grids.Notes)
                    .Concat(dimensions.Notes)
                    .Concat(railings.Notes)
                    .Concat(texts.Notes)
                    .Concat(GridLookalikeNotes(doc, view))
                    .Concat(LinkNotes(doc, view))
                    .Concat(setup.ShiftedLevelNames.Count == 0
                        ? Enumerable.Empty<string>()
                        : new[] { "Blocos em níveis próprios cortados à mesma altura acima do seu nível: " + string.Join(", ", setup.ShiftedLevelNames) + "." })
                    .ToList(),
            };

            return (request, image.Diagnostics);
        }

        /// <summary>
        /// Spec v3: a detail line drawn to look like a grid (dash-dot style) is
        /// not a grid; it stays out of the mask and is only counted, to learn
        /// how common it is.
        /// </summary>
        private static IEnumerable<string> GridLookalikeNotes(Document doc, View view)
        {
            string[] gridLike = { "CENTER", "EIXO", "DASH DOT", "DASHDOT", "TRACO PONTO", "TRAÇO PONTO", "GRID" };
            int count = new FilteredElementCollector(doc, view.Id)
                .OfCategory(BuiltInCategory.OST_Lines)
                .OfClass(typeof(CurveElement))
                .Cast<CurveElement>()
                .Count(c => c.LineStyle is GraphicsStyle style && gridLike.Any(k => style.Name.ToUpperInvariant().Contains(k)));
            if (count > 0)
            {
                yield return $"{count} linha(s) de detalhe com estilo de eixo (traço-ponto) ficaram fora da máscara (spec v3: a classe vem da categoria).";
            }
        }

        /// <summary>v2 first delivery: active document only; a visible link is flagged, never half-exported in silence.</summary>
        private static IEnumerable<string> LinkNotes(Document doc, View view)
        {
            int links = new FilteredElementCollector(doc, view.Id).OfClass(typeof(RevitLinkInstance)).GetElementCount();
            if (links > 0)
            {
                yield return $"{links} modelo(s) vinculado(s) visível(is) na vista: os elementos deles ficaram fora da máscara.";
            }
        }
    }
}
