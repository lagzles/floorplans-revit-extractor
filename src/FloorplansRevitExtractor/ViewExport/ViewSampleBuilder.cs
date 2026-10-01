using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;
using FloorplansRevitExtractor.Core.Geometry;
using FloorplansRevitExtractor.Core.Label;
using FloorplansRevitExtractor.Core.Model;
using FloorplansRevitExtractor.Core.Sample;
using FloorplansRevitExtractor.RevitInterop;

namespace FloorplansRevitExtractor.ViewExport
{
    /// <summary>
    /// Reads one plan view into a <see cref="SampleRequest"/>: exports the
    /// image, measures its transform, and extracts rooms and cut walls (the
    /// first delivery of spec v2: room and wall; doors, windows, pillars,
    /// stairs, furniture, voids and swings come in later increments).
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
            WallReading walls = WallSectionReader.Read(doc, setup, image.Transform.PxPerM);
            ViewPlan view = setup.View;
            string docGuid = doc.ProjectInformation.UniqueId;

            var geom = new GeomDocument
            {
                View = new GeomView(view.Id.Value, view.Name, view.Scale, setup.CutPlaneM),
                Transform = image.Transform,
                Elements = rooms.Elements.Concat(walls.Elements).ToList(),
                Warnings = walls.Warnings,
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
                Doors = Array.Empty<DoorRecord>(),
                Windows = Array.Empty<WindowRecord>(),
                Furniture = Array.Empty<FurnitureRecord>(),
                RenderStyle = RenderStyle.Empty.With("scale_den", view.Scale).With("lang", "pt"),
                ReviewNotes = rooms.Notes.Concat(walls.Notes).Concat(LinkNotes(doc, view)).ToList(),
            };

            return (request, image.Diagnostics);
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
