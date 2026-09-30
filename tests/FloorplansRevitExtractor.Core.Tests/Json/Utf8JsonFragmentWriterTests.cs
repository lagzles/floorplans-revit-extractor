using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using FloorplansRevitExtractor.Core.Geometry;
using FloorplansRevitExtractor.Core.Json;
using FloorplansRevitExtractor.Core.Model;
using FluentAssertions;
using Xunit;

namespace FloorplansRevitExtractor.Core.Tests.Json
{
    public class Utf8JsonFragmentWriterTests
    {
        private static RoomsPreviewDocument BuildDocument(string roomName = "SALA", double x = 1.23456, double area = 12.3456789)
        {
            IReadOnlyList<Point2D> polygon = new[]
            {
                new Point2D(0, 0),
                new Point2D(x, 0),
                new Point2D(x, 3),
                new Point2D(0, 3),
            };

            var room = new RoomPreviewRecord
            {
                Id = 0,
                Type = "living_room",
                Name = roomName,
                Zone = "social",
                ClassId = 1,
                Polygon = polygon,
                Holes = Array.Empty<IReadOnlyList<Point2D>>(),
                AxisRects = Array.Empty<(double, double, double, double)>(),
                Area = area,
                Centroid = new Point2D(1, 1),
                BBoxM = (0, 0, x, 3),
                RevitAreaM2 = area,
            };

            return new RoomsPreviewDocument
            {
                Source = new SourceFragment
                {
                    DocTitle = "EDIF-A_ARQ.rvt",
                    DocGuid = "guid-123",
                    RevitVersion = "2024",
                    LevelId = 1,
                    LevelName = "PAV 01",
                    LevelElevationM = 0,
                    ExportedAt = "2026-01-01T00:00:00-03:00",
                    PluginVersion = "0.1.0",
                },
                Params = new ParamsFragment { TotalArea = area },
                Rooms = new[] { room },
                Debug = new DebugFragment
                {
                    DiscardedRooms = Array.Empty<DiscardedRoom>(),
                    UnmappedNames = Array.Empty<UnmappedRoomName>(),
                    AreaMismatchWarnings = Array.Empty<AreaMismatchWarning>(),
                },
            };
        }

        private static (byte[] Bytes, string Text) WriteToBytes(RoomsPreviewDocument document)
        {
            using var stream = new MemoryStream();
            Utf8JsonFragmentWriter.Write(stream, document);
            byte[] bytes = stream.ToArray();
            return (bytes, Encoding.UTF8.GetString(bytes));
        }

        [Fact]
        public void Write_OutputHasNoByteOrderMark()
        {
            (byte[] bytes, _) = WriteToBytes(BuildDocument());

            byte[] utf8Bom = Encoding.UTF8.GetPreamble();
            bytes.Take(utf8Bom.Length).Should().NotBeEquivalentTo(utf8Bom);
        }

        [Fact]
        public void Write_RoundsNumbersToThreeDecimals()
        {
            (_, string text) = WriteToBytes(BuildDocument(area: 12.3456789));

            text.Should().Contain("12.346");
            text.Should().NotContain("12.3456789");
        }

        [Fact]
        public void Write_PreservesSpecKeyOrder()
        {
            (_, string text) = WriteToBytes(BuildDocument());

            using JsonDocument parsed = JsonDocument.Parse(text);
            List<string> topLevelKeys = parsed.RootElement.EnumerateObject().Select(p => p.Name).ToList();
            topLevelKeys.Should().Equal("source", "params", "rooms", "debug");

            JsonElement room = parsed.RootElement.GetProperty("rooms")[0];
            List<string> roomKeys = room.EnumerateObject().Select(p => p.Name).ToList();
            roomKeys.Should().Equal(
                "id", "type", "name", "zone", "class_id", "open", "suite_of",
                "polygon", "holes", "axis_rects", "area", "centroid", "bbox_m");
        }

        [Fact]
        public void Write_KeepsAccentedCharactersAsLiteralUtf8_NotEscaped()
        {
            (_, string text) = WriteToBytes(BuildDocument(roomName: "LABORATÓRIO"));

            text.Should().Contain("LABORATÓRIO");
            text.Should().NotContain("\\u00");
        }

        [Fact]
        public void Write_UnmappedRoom_WritesNullTypeZoneClassId()
        {
            RoomsPreviewDocument document = BuildDocument();
            RoomPreviewRecord unmapped = document.Rooms[0] with { Type = null, Zone = null, ClassId = null };
            RoomsPreviewDocument withUnmapped = document with { Rooms = new[] { unmapped } };

            (_, string text) = WriteToBytes(withUnmapped);

            using JsonDocument parsed = JsonDocument.Parse(text);
            JsonElement room = parsed.RootElement.GetProperty("rooms")[0];
            room.GetProperty("type").ValueKind.Should().Be(JsonValueKind.Null);
            room.GetProperty("zone").ValueKind.Should().Be(JsonValueKind.Null);
            room.GetProperty("class_id").ValueKind.Should().Be(JsonValueKind.Null);
        }
    }
}
