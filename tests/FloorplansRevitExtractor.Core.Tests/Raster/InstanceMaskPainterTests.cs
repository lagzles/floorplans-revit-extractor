using System;
using FloorplansRevitExtractor.Core.Raster;
using FluentAssertions;
using Xunit;
using static FloorplansRevitExtractor.Core.Tests.Raster.RasterTestShapes;

namespace FloorplansRevitExtractor.Core.Tests.Raster
{
    public class InstanceMaskPainterTests
    {
        [Fact]
        public void Paint_EachRoom_GetsRoomIdPlusOne_OutsideIsZero()
        {
            InstanceMask mask = InstanceMaskPainter.Paint(10, 4, new[]
            {
                new RoomInstance(0, new[] { Rect(0, 0, 4, 4) }),
                new RoomInstance(1, new[] { Rect(4, 0, 8, 4) }),
            });

            mask[1, 1].Should().Be(1);
            mask[5, 1].Should().Be(2);
            mask[9, 1].Should().Be(0);
            mask.CountPixels(1).Should().Be(16);
            mask.CountPixels(2).Should().Be(16);
        }

        [Fact]
        public void Paint_IdsAbove255_AreKeptIn16Bits()
        {
            InstanceMask mask = InstanceMaskPainter.Paint(4, 4, new[] { new RoomInstance(299, new[] { Rect(0, 0, 2, 2) }) });

            mask[0, 0].Should().Be(300);
        }

        [Fact]
        public void Paint_OverlappingRooms_HigherIdWinsRegardlessOfInputOrder()
        {
            var low = new RoomInstance(0, new[] { Rect(0, 0, 6, 4) });
            var high = new RoomInstance(1, new[] { Rect(4, 0, 10, 4) });

            InstanceMask a = InstanceMaskPainter.Paint(10, 4, new[] { low, high });
            InstanceMask b = InstanceMaskPainter.Paint(10, 4, new[] { high, low });

            a[5, 1].Should().Be(2);
            b.CopyPixels().Should().Equal(a.CopyPixels());
        }

        [Fact]
        public void Paint_RoomWithSeveralPolygons_UsesOneIdForAll()
        {
            InstanceMask mask = InstanceMaskPainter.Paint(10, 4, new[]
            {
                new RoomInstance(4, new[] { Rect(0, 0, 3, 4), Rect(6, 0, 9, 4) }),
            });

            mask.CountPixels(5).Should().Be(24);
            mask[4, 1].Should().Be(0);
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(65535)]
        public void Paint_RoomIdOutOfUInt16Range_Throws(int roomId)
        {
            Action act = () => InstanceMaskPainter.Paint(4, 4, new[] { new RoomInstance(roomId, new[] { Rect(0, 0, 2, 2) }) });

            act.Should().Throw<ArgumentOutOfRangeException>();
        }
    }
}
