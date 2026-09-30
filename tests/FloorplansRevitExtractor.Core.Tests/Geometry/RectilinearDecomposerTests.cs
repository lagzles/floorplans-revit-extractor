using System.Collections.Generic;
using FloorplansRevitExtractor.Core.Geometry;
using FluentAssertions;
using Xunit;

namespace FloorplansRevitExtractor.Core.Tests.Geometry
{
    public class RectilinearDecomposerTests
    {
        [Fact]
        public void TryDecompose_SingleRectangle_ReturnsOneRectMatchingBBox()
        {
            IReadOnlyList<Point2D> rectangle = new[]
            {
                new Point2D(1, 1),
                new Point2D(5, 1),
                new Point2D(5, 4),
                new Point2D(1, 4),
            };

            var rects = RectilinearDecomposer.TryDecompose(rectangle);

            rects.Should().ContainSingle();
            rects[0].Should().Be((1.0, 1.0, 4.0, 3.0));
        }

        [Fact]
        public void TryDecompose_LShape_ReturnsEmpty()
        {
            IReadOnlyList<Point2D> lShape = new[]
            {
                new Point2D(0, 0),
                new Point2D(5, 0),
                new Point2D(5, 2),
                new Point2D(2, 2),
                new Point2D(2, 4),
                new Point2D(0, 4),
            };

            RectilinearDecomposer.TryDecompose(lShape).Should().BeEmpty();
        }

        [Fact]
        public void TryDecompose_NonOrthogonalPolygon_ReturnsEmpty()
        {
            IReadOnlyList<Point2D> triangle = new[]
            {
                new Point2D(0, 0),
                new Point2D(4, 0),
                new Point2D(2, 3),
            };

            RectilinearDecomposer.TryDecompose(triangle).Should().BeEmpty();
        }

        [Fact]
        public void TryDecompose_NullLoop_ReturnsEmpty()
        {
            RectilinearDecomposer.TryDecompose(null).Should().BeEmpty();
        }
    }
}
