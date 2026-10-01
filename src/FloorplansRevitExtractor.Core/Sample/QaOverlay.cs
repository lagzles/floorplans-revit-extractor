using System;
using FloorplansRevitExtractor.Core.Png;
using FloorplansRevitExtractor.Core.Raster;

namespace FloorplansRevitExtractor.Core.Sample
{
    /// <summary>
    /// qa/NNNNNN.png: the image with the mask over it at 40% in the
    /// classes.json colors, what a person looks at to validate a sample.
    /// Background and ignore pixels show the image untouched.
    /// </summary>
    public static class QaOverlay
    {
        public const double Opacity = 0.4;

        public static RgbImage Compose(RgbImage image, SemanticMask mask)
        {
            if (image.Width != mask.Width || image.Height != mask.Height)
            {
                throw new ArgumentException($"Image is {image.Width}x{image.Height} but the mask is {mask.Width}x{mask.Height}.");
            }

            byte[] pixels = image.CopyPixels();
            byte[] classes = mask.Pixels;
            for (int i = 0; i < classes.Length; i++)
            {
                byte cls = classes[i];
                if (cls == (byte)StructureClass.Background || cls >= StructureClassPalette.Colors.Count)
                {
                    continue;
                }

                Rgb color = StructureClassPalette.Colors[cls];
                int o = i * 3;
                pixels[o] = Blend(pixels[o], color.R);
                pixels[o + 1] = Blend(pixels[o + 1], color.G);
                pixels[o + 2] = Blend(pixels[o + 2], color.B);
            }

            return new RgbImage(image.Width, image.Height, pixels);
        }

        private static byte Blend(byte under, byte over) =>
            (byte)Math.Round((under * (1 - Opacity)) + (over * Opacity), MidpointRounding.AwayFromZero);
    }
}
