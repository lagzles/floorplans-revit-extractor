using FloorplansRevitExtractor.Core.Geometry;
using FloorplansRevitExtractor.Core.Label;
using FluentAssertions;
using Xunit;

namespace FloorplansRevitExtractor.Core.Tests.Label
{
    public class OpeningKindsTests
    {
        [Theory]
        [InlineData("Door-Single-Flush", "36\" x 84\"", "single")]
        [InlineData("Door-Double-Flush_Panel", "72\" x 84\"", "double")]
        [InlineData("Porta de Giro 2 Folhas", "160x210", "double")]
        [InlineData("Door-Interior-Sliding-Single", "36\" x 84\"", "sliding")]
        [InlineData("Porta de Correr", "80x210", "sliding")]
        [InlineData("Door-Opening", "36\" x 84\"", "passage")]
        [InlineData("Vão de Passagem", "90x210", "passage")]
        [InlineData("Door-Overhead-Sectional", "16' x 8'", "gate")]
        [InlineData("Portão Basculante", "300x240", "gate")]
        [InlineData("Door-Exterior-Revolving", "Standard", "single")] // unmapped: falls back to single, logged
        public void DoorKind_FromFamilyAndTypeNames(string family, string type, string expected)
        {
            OpeningKinds.DoorKind(family, type).Kind.Should().Be(expected);
        }

        [Fact]
        public void DoorKind_Unmapped_IsSingleAndSaysSo()
        {
            (string kind, bool mapped) = OpeningKinds.DoorKind("Door-Exterior-Revolving", "Standard");

            kind.Should().Be("single");
            mapped.Should().BeFalse("v1: an unmapped family falls back to single and is listed for review");
            OpeningKinds.DoorKind("Door-Single-Flush", "x").Mapped.Should().BeTrue();
        }

        [Theory]
        [InlineData("Window-Fixed", "1.20", 0.9, "single")]
        [InlineData("Window-Double-Hung", "36x48", 0.9, "double")]
        [InlineData("Janela de Correr 2 Folhas", "120x100", 1.1, "sliding")]
        [InlineData("Window-Casement-Double", "x", 0.9, "double")]
        [InlineData("Janela Basculante", "60x60", 1.6, "high")] // v1: sill at 1.50 m or more is high
        [InlineData("Window-Sliding", "x", 1.5, "high")]
        public void WindowKind_SillHeightFirstThenFamily(string family, string type, double sillM, string expected)
        {
            OpeningKinds.WindowKind(family, type, sillM).Should().Be(expected);
        }

        [Theory]
        [InlineData(1, 0, 0.0)]
        [InlineData(0, 1, 90.0)]
        [InlineData(-1, 0, 180.0)]
        [InlineData(0, -1, 270.0)]
        [InlineData(1, 1, 45.0)]
        public void RotationDegrees_IsTheDirectionAngleIn0To360(double x, double y, double expected)
        {
            OpeningKinds.RotationDegrees(new Vector2D(x, y)).Should().BeApproximately(expected, 1e-9);
        }
    }
}
