using System;
using System.Collections.Generic;
using System.Linq;
using FloorplansRevitExtractor.Core.Geometry;
using FloorplansRevitExtractor.Core.Label;
using FloorplansRevitExtractor.Core.Model;
using FloorplansRevitExtractor.Core.Png;
using FloorplansRevitExtractor.Core.Raster;
using FloorplansRevitExtractor.Core.Sample;
using FloorplansRevitExtractor.Core.Tests.Label;

namespace FloorplansRevitExtractor.Core.Tests.Sample
{
    /// <summary>
    /// A 20 x 10 m plan at 10 px/m: two rooms side by side inside 0.2 m walls,
    /// and a matching "Revit export" image: black poché on the walls, white paper.
    /// </summary>
    internal static class SampleTestData
    {
        internal static readonly PixelTransform Transform = new PixelTransform(0, 10, 10, 200, 100);

        internal static IReadOnlyList<Point2D> Rect(double x0, double y0, double x1, double y1) =>
            new[] { new Point2D(x0, y0), new Point2D(x1, y0), new Point2D(x1, y1), new Point2D(x0, y1) };

        internal static GeomDocument Geom(bool withWalls = true) => new GeomDocument
        {
            View = new GeomView(234567, "PLANTA TÉRREO", 100, 1.2),
            Transform = Transform,
            Elements = new[]
            {
                GeomElement.Filled(StructureClass.Room, 11, new[] { Rect(1.2, 1.2, 7.9, 8.8) }),
                GeomElement.Filled(StructureClass.Room, 12, new[] { Rect(8.1, 1.2, 14.8, 8.8) }),
            }.Concat(withWalls ? Walls() : Array.Empty<GeomElement>()).ToArray(),
            Warnings = Array.Empty<GeomWarning>(),
        };

        internal static IEnumerable<GeomElement> Walls() => new[]
        {
            GeomElement.Filled(StructureClass.Wall, 1, new[] { Rect(1, 1, 15, 1.2) }),
            GeomElement.Filled(StructureClass.Wall, 2, new[] { Rect(1, 8.8, 15, 9) }),
            GeomElement.Filled(StructureClass.Wall, 3, new[] { Rect(1, 1, 1.2, 9) }),
            GeomElement.Filled(StructureClass.Wall, 4, new[] { Rect(14.8, 1, 15, 9) }),
            GeomElement.Filled(StructureClass.Wall, 5, new[] { Rect(7.9, 1.2, 8.1, 8.8) }),
        };

        internal static IReadOnlyList<RoomPreviewRecord> Rooms() => new[]
        {
            LabelTestData.Room(0, "bedroom", 1.2, 1.2, 7.9, 8.8),
            LabelTestData.Room(1, "living_room", 8.1, 1.2, 14.8, 8.8),
        };

        /// <summary>The export as Revit would draw it: dark where the walls are, paper elsewhere.</summary>
        internal static byte[] ImagePng(GeomDocument geom, bool darkWalls = true, int? width = null)
        {
            SemanticMask mask = GeomRasterizer.PaintSemantic(geom);
            int w = width ?? mask.Width;
            var rgb = new byte[w * mask.Height * 3];
            for (int y = 0; y < mask.Height; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    bool wall = x < mask.Width && mask[x, y] == (byte)StructureClass.Wall;
                    byte value = wall == darkWalls ? (byte)20 : (byte)250;
                    int i = ((y * w) + x) * 3;
                    rgb[i] = rgb[i + 1] = rgb[i + 2] = value;
                }
            }

            return PngEncoder.EncodeRgb(w, mask.Height, rgb);
        }

        internal static SampleRequest Request(GeomDocument? geom = null, byte[]? image = null, string viewKey = "234567", IReadOnlyList<RoomPreviewRecord>? rooms = null)
        {
            GeomDocument g = geom ?? Geom();
            return new SampleRequest
            {
                Key = new SampleKey("doc-guid-1", "level", viewKey),
                ImagePng = image ?? ImagePng(g),
                Geom = g,
                Source = LabelTestData.Source(),
                Building = new BuildingMeasures { TotalArea = 112.0, WallExt = 0.2, WallInt = 0.2, Outline = Array.Empty<Point2D>() },
                Rooms = rooms ?? Rooms(),
                Doors = Array.Empty<DoorRecord>(),
                Windows = Array.Empty<WindowRecord>(),
                Furniture = Array.Empty<FurnitureRecord>(),
                RenderStyle = RenderStyle.Empty.With("scale_den", 100).With("lang", "pt"),
                ReviewNotes = Array.Empty<string>(),
            };
        }
    }
}
