using System;
using System.Linq;
using FloorplansRevitExtractor.Core.Png;
using FloorplansRevitExtractor.Core.Raster;
using FluentAssertions;
using Xunit;

namespace FloorplansRevitExtractor.Core.Tests.Raster
{
    public class StructureClassPaletteTests
    {
        [Fact]
        public void Colors_MatchTheTrainingDatasetClassesJson()
        {
            // Copied from struct7k_v4/classes.json; the spec forbids redefining colors.
            StructureClassPalette.Colors.Should().Equal(
                new Rgb(0, 0, 0),
                new Rgb(120, 200, 120),
                new Rgb(230, 60, 60),
                new Rgb(60, 120, 255),
                new Rgb(255, 200, 40),
                new Rgb(160, 90, 210),
                new Rgb(0, 160, 160),
                new Rgb(200, 120, 60),
                new Rgb(235, 110, 175),
                new Rgb(120, 120, 175));
        }

        [Fact]
        public void Colors_HaveOneEntryPerClassInIdOrder()
        {
            StructureClassPalette.Colors.Count.Should().Be(Enum.GetValues(typeof(StructureClass)).Length);
            StructureClassPalette.ColorOf(StructureClass.Swing).Should().Be(new Rgb(235, 110, 175));
        }
    }
}
