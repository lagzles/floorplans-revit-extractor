using System;
using System.Collections.Generic;
using System.Linq;
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

        /// <summary>How much of the drawing's darkness the annotation QA keeps, in percent.</summary>
        private const int LightenKeep = 35;

        private static readonly Rgb GridQaColor = new Rgb(0, 90, 255);
        private static readonly Rgb DimensionQaColor = new Rgb(255, 140, 0);
        private static readonly Rgb TextQaColor = new Rgb(128, 128, 0);

        private static readonly IReadOnlyDictionary<byte, Rgb> AnnotationColors = new Dictionary<byte, Rgb>
        {
            [(byte)StructureClass.Grid] = GridQaColor,
            [(byte)StructureClass.Dimension] = DimensionQaColor,
            [(byte)StructureClass.Text] = TextQaColor,
        };

        /// <summary>Spec v4: the classes whose mapping depends on the family, in their classes.json colors.</summary>
        private static readonly IReadOnlyDictionary<byte, Rgb> NewClassColors = new[]
        {
            StructureClass.Passage, StructureClass.LowWall, StructureClass.CurtainWall, StructureClass.Fixture,
        }.ToDictionary(c => (byte)c, StructureClassPalette.ColorOf);

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

        /// <summary>
        /// Spec v4: the image lightened, with only grid in blue, dimension in
        /// orange and text in olive at full strength. In the main overlay a
        /// white grid over a light room cannot be seen.
        /// </summary>
        public static RgbImage ComposeAnnotation(RgbImage image, SemanticMask mask) => ComposeOnly(image, mask, AnnotationColors);

        /// <summary>Spec v4: the image lightened, with only passage, low_wall, curtain_wall and fixture.</summary>
        public static RgbImage ComposeNewClasses(RgbImage image, SemanticMask mask) => ComposeOnly(image, mask, NewClassColors);

        private static RgbImage ComposeOnly(RgbImage image, SemanticMask mask, IReadOnlyDictionary<byte, Rgb> colors)
        {
            if (image.Width != mask.Width || image.Height != mask.Height)
            {
                throw new ArgumentException($"Image is {image.Width}x{image.Height} but the mask is {mask.Width}x{mask.Height}.");
            }

            byte[] pixels = image.CopyPixels();
            byte[] classes = mask.Pixels;
            for (int i = 0; i < classes.Length; i++)
            {
                int o = i * 3;
                Rgb? color = colors.TryGetValue(classes[i], out Rgb c0) ? c0 : (Rgb?)null;
                for (int c = 0; c < 3; c++)
                {
                    pixels[o + c] = color.HasValue
                        ? (c == 0 ? color.Value.R : c == 1 ? color.Value.G : color.Value.B)
                        : (byte)(255 - ((255 - pixels[o + c]) * LightenKeep / 100));
                }
            }

            return new RgbImage(image.Width, image.Height, pixels);
        }

        private static byte Blend(byte under, byte over) =>
            (byte)Math.Round((under * (1 - Opacity)) + (over * Opacity), MidpointRounding.AwayFromZero);
    }
}
