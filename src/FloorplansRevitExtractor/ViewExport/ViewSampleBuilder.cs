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
            RgbImage rgb = PngDecoder.DecodeRgb(image.Png);
            RoomReading rooms = ViewRoomReader.Read(doc, setup, setup.ModelRegion, SlabReader.Read(doc, setup));
            WallReading walls = WallSectionReader.Read(doc, setup, image.Transform.PxPerM);
            PillarReading pillars = PillarReader.Read(doc, setup);
            if (!walls.Elements.Any(e => e.Class == StructureClass.Wall))
            {
                // Roof and legend views: the gate would reject them for having no wall; they are not floor plans.
                throw new ViewNotExportableException(
                    "Nenhuma parede atravessa o plano de corte desta vista (cobertura, legenda?): não é planta baixa. Nenhuma amostra foi gravada.");
            }
            (PixelTransform transform, string registration) = image.Search == null
                ? (image.Transform, null)
                : Register(setup, image, rgb, walls.Elements.Concat(pillars.Elements).ToList());
            var ink = new InkBoxes(rgb, transform);
            OpeningReading openings = OpeningReader.Read(doc, setup, rooms.RecordIdByRoom, transform.PxPerM);
            RailingReading railings = RailingReader.Read(doc, setup);
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
                Transform = transform,
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
                Key = new SampleKey(docGuid, Mode, SampleResolution.ViewVariant(view.Id.Value, targetPxPerM)),
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
                    Alignment = registration == null ? "direto" : "registrado",
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
                    .Concat(registration == null ? Enumerable.Empty<string>() : new[] { registration })
                    .Concat(setup.ShiftedLevelNames.Count == 0
                        ? Enumerable.Empty<string>()
                        : new[] { "Blocos em níveis próprios cortados à mesma altura acima do seu nível: " + string.Join(", ", setup.ShiftedLevelNames) + "." })
                    .ToList(),
            };

            return (request, image.Diagnostics + (registration == null ? string.Empty : Environment.NewLine + registration));
        }

        /// <summary>
        /// Spec v4 section 7, automatic registration: the image's scale is known
        /// (from the view's outline) but not where the crop sits in it. The cut
        /// walls and pillars are slid over the possible shifts, and the shift is
        /// taken only when one clearly wins. Otherwise the view is skipped, as
        /// before: never a mask that may be misaligned.
        /// </summary>
        private static (PixelTransform Transform, string Note) Register(ViewSetup setup, ExportedImage image, RgbImage rgb, IReadOnlyList<GeomElement> construction)
        {
            PixelTransform provisional = image.Transform;
            ((int, int) dxRange, (int, int) dyRange) = image.Search.Value;
            double k = provisional.PxPerM;
            var draft = new GeomDocument
            {
                View = new GeomView(setup.View.Id.Value, setup.View.Name, setup.View.Scale, setup.CutPlaneM),
                Transform = new PixelTransform(
                    provisional.X0,
                    provisional.Y1,
                    k,
                    (int)Math.Ceiling(image.Region.Width * k),
                    (int)Math.Ceiling(image.Region.Height * k)),
                Elements = construction,
                Warnings = Array.Empty<GeomWarning>(),
            };
            SemanticMask mask = GeomRasterizer.PaintSemantic(draft);
            RegistrationFit fit = ImageRegistration.Find(rgb, mask, new[] { StructureClass.Wall, StructureClass.Pillar }, dxRange, dyRange);
            if (fit == null || !fit.IsConfident)
            {
                string scores = fit == null
                    ? "sem paredes nem pilares para registrar"
                    : $"melhor ({fit.Dx}, {fit.Dy}) px com {fit.Score:P0} das bordas sobre traço, segundo melhor {fit.RunnerUpScore:P0}";
                throw new ViewNotExportableException(
                    "A imagem é maior que o recorte (anotação fora dele) e o registro não achou um encaixe seguro "
                    + $"({scores}). Nenhuma amostra foi gravada." + Environment.NewLine + image.Diagnostics);
            }

            var registered = new PixelTransform(provisional.X0 - (fit.Dx / k), provisional.Y1 + (fit.Dy / k), k, provisional.Width, provisional.Height);
            return (registered, $"Imagem registrada: recorte em ({fit.Dx}, {fit.Dy}) px a {k:F2} px/m; {fit.Score:P0} das bordas de parede/pilar sobre traço (segundo melhor encaixe {fit.RunnerUpScore:P0}).");
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
