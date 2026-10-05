using System;
using System.Collections.Generic;
using System.Linq;

namespace FloorplansRevitExtractor.Core.Raster
{
    /// <summary>
    /// Paints the semantic mask in the spec v4 layer order (section 5), writing spans
    /// straight into the final buffer. Within a layer, painting is
    /// idempotent (and the stencil only ever turns room pixels into the
    /// layer), so the input order never changes the result. Swapping any
    /// two layers yields a wrong mask with no error, which is why the order
    /// lives here and nowhere else.
    /// </summary>
    public static class SemanticMaskPainter
    {
        /// <remarks>
        /// The generator paints a window 3.0 m or wider as curtain_wall in the
        /// window step; here every curtain_wall is painted with the walls. The
        /// two differ only where a pillar overlaps a wide window.
        /// </remarks>
        private static readonly StructureClass[] PaintOrder =
        {
            StructureClass.Room,
            StructureClass.Stair,
            StructureClass.Void,
            StructureClass.Wall,
            StructureClass.CurtainWall, // same step as the wall
            StructureClass.LowWall, // stencil: background or room only, so the wall it meets stays
            StructureClass.Pillar, // after the wall, or embedded pillars disappear
            StructureClass.Furniture, // stencil: room pixels only
            StructureClass.Fixture, // stencil: room pixels before furniture, so a sink on a counter is fixture
            StructureClass.Swing, // stencil: room pixels only, so furniture and fixtures stay
            StructureClass.Window, // openings cut the wall
            StructureClass.Door,
            StructureClass.Passage,
            StructureClass.Grid, // annotation last, each over background or room only:
            StructureClass.Dimension, // where a dimension crosses a grid, the grid stays
            StructureClass.Text, // and where text touches a dimension, the dimension stays
        };

        private static readonly StructureClass[] FloorOrBackground = { StructureClass.Background, StructureClass.Room };

        /// <summary>
        /// Layers that may only paint over some classes. Furniture or a swing
        /// over a wall would open a hole in it, teaching the model that cabinets
        /// create openings; a grid, dimension or text over a wall would teach it
        /// to open the wall under every grid line. Construction always wins over
        /// annotation, so under an annotation pixel there is only background or
        /// room, which keeps the 10-class fold exact. Fixture may also take a
        /// furniture pixel: furniture only ever paints room, so that pixel was
        /// room when the furniture step began, as the generator's stencil reads.
        /// </summary>
        private static readonly Dictionary<StructureClass, StructureClass[]> PaintsOnlyOver = new Dictionary<StructureClass, StructureClass[]>
        {
            [StructureClass.LowWall] = FloorOrBackground,
            [StructureClass.Furniture] = new[] { StructureClass.Room },
            [StructureClass.Fixture] = new[] { StructureClass.Room, StructureClass.Furniture },
            [StructureClass.Swing] = new[] { StructureClass.Room },
            [StructureClass.Grid] = FloorOrBackground,
            [StructureClass.Dimension] = FloorOrBackground,
            [StructureClass.Text] = FloorOrBackground,
        };

        public static SemanticMask Paint(int width, int height, IReadOnlyList<MaskElement> elements)
        {
            MaskElement? unpaintable = elements.FirstOrDefault(e => !PaintOrder.Contains(e.Class));
            if (unpaintable != null)
            {
                throw new ArgumentException(
                    $"Class {unpaintable.Class} is not a paint layer; background is what is left unpainted.",
                    nameof(elements));
            }

            var pixels = new byte[PixelSpans.RequireArea(width, height)];
            foreach (StructureClass layer in PaintOrder)
            {
                SpanWriter write = LayerWriter(pixels, width, layer);
                foreach (MaskElement element in elements.Where(e => e.Class == layer))
                {
                    foreach (PolygonShape polygon in element.Polygons)
                    {
                        ScanlineFiller.FillSpans(width, height, polygon, write);
                    }

                    if (element.Polylines.Count > 0)
                    {
                        StrokeRasterizer.StrokeSpans(width, height, element.Polylines, element.StrokeWidthPixels, write);
                    }
                }
            }

            return new SemanticMask(width, height, pixels);
        }

        private static SpanWriter LayerWriter(byte[] pixels, int width, StructureClass layer)
        {
            var value = (byte)layer;
            if (!PaintsOnlyOver.TryGetValue(layer, out StructureClass[] under))
            {
                return (row, first, last) =>
                {
                    int rowStart = row * width;
                    for (int x = first; x <= last; x++)
                    {
                        pixels[rowStart + x] = value;
                    }
                };
            }

            var allowed = new bool[256];
            foreach (StructureClass cls in under)
            {
                allowed[(byte)cls] = true;
            }

            return (row, first, last) =>
            {
                int rowStart = row * width;
                for (int x = first; x <= last; x++)
                {
                    if (allowed[pixels[rowStart + x]])
                    {
                        pixels[rowStart + x] = value;
                    }
                }
            };
        }
    }
}
