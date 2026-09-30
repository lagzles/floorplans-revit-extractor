using System;
using System.Collections.Generic;
using System.Linq;

namespace FloorplansRevitExtractor.Core.Raster
{
    /// <summary>
    /// Paints the semantic mask in the spec v2 layer order, writing spans
    /// straight into the final buffer. Within a layer, painting is
    /// idempotent (and the stencil only ever turns room pixels into the
    /// layer), so the input order never changes the result. Swapping any
    /// two layers yields a wrong mask with no error, which is why the order
    /// lives here and nowhere else.
    /// </summary>
    public static class SemanticMaskPainter
    {
        private static readonly StructureClass[] PaintOrder =
        {
            StructureClass.Room,
            StructureClass.Stair,
            StructureClass.Void,
            StructureClass.Wall,
            StructureClass.Pillar, // after the wall, or embedded pillars disappear
            StructureClass.Furniture, // stencil: room pixels only
            StructureClass.Swing, // stencil: room pixels only, so furniture stays
            StructureClass.Window, // openings cut the wall
            StructureClass.Door,
        };

        /// <summary>
        /// Painting furniture or a swing over a wall would open a hole in it,
        /// teaching the model that cabinets create openings.
        /// </summary>
        private static readonly HashSet<StructureClass> RoomStencilled = new HashSet<StructureClass>
        {
            StructureClass.Furniture,
            StructureClass.Swing,
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
            if (!RoomStencilled.Contains(layer))
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

            return (row, first, last) =>
            {
                int rowStart = row * width;
                for (int x = first; x <= last; x++)
                {
                    if (pixels[rowStart + x] == (byte)StructureClass.Room)
                    {
                        pixels[rowStart + x] = value;
                    }
                }
            };
        }
    }
}
