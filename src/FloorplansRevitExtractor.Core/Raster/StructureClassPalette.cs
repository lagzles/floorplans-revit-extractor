using System.Collections.Generic;
using FloorplansRevitExtractor.Core.Png;

namespace FloorplansRevitExtractor.Core.Raster
{
    /// <summary>
    /// Class colors, copied from the training dataset's classes.json
    /// (struct7k_v4) plus the spec v3 and v4 classes, from the generator's
    /// STRUCTURE_CLASSES (synthgen/core/schema.py). The spec forbids redefining them:
    /// classes.json and the mode-P mask palette are written from this list.
    /// </summary>
    public static class StructureClassPalette
    {
        public static IReadOnlyList<Rgb> Colors { get; } = new[]
        {
            new Rgb(0, 0, 0), // background
            new Rgb(120, 200, 120), // room
            new Rgb(230, 60, 60), // wall
            new Rgb(60, 120, 255), // door
            new Rgb(255, 200, 40), // window
            new Rgb(160, 90, 210), // pillar
            new Rgb(0, 160, 160), // stair
            new Rgb(200, 120, 60), // furniture
            new Rgb(235, 110, 175), // swing
            new Rgb(120, 120, 175), // void
            new Rgb(250, 250, 250), // grid
            new Rgb(255, 140, 0), // dimension
            new Rgb(150, 225, 255), // passage
            new Rgb(128, 128, 0), // text
            new Rgb(150, 0, 40), // low_wall
            new Rgb(40, 230, 200), // curtain_wall
            new Rgb(110, 50, 20), // fixture
        };

        public static Rgb ColorOf(StructureClass cls) => Colors[(int)cls];
    }
}
