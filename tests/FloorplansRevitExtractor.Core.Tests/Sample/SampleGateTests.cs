using System.Collections.Generic;
using System.IO;
using System.Linq;
using FloorplansRevitExtractor.Core.Geometry;
using FloorplansRevitExtractor.Core.Model;
using FloorplansRevitExtractor.Core.Png;
using FloorplansRevitExtractor.Core.Raster;
using FloorplansRevitExtractor.Core.Sample;
using FloorplansRevitExtractor.Core.Tests.Label;
using FluentAssertions;
using Xunit;
using static FloorplansRevitExtractor.Core.Tests.Sample.SampleTestData;

namespace FloorplansRevitExtractor.Core.Tests.Sample
{
    public sealed class SampleGateTests : System.IDisposable
    {
        private readonly string _out = Path.Combine(Path.GetTempPath(), "fpx-gate-" + System.Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            if (Directory.Exists(_out))
            {
                Directory.Delete(_out, recursive: true);
            }
        }

        private GateResult Gate(SampleRequest request) => SampleExporter.Export(_out, request).Gate;

        private static IEnumerable<string> Checks(IEnumerable<GateFinding> findings) => findings.Select(f => f.Check);

        [Fact]
        public void Check_WellFormedSample_PassesWithMetrics()
        {
            GateResult gate = Gate(Request());

            gate.Rejections.Should().BeEmpty();
            gate.Metrics.WallFraction.Should().BeInRange(0.05, 0.08);
            gate.Metrics.WallInk.Should().Be(1.0);
            gate.Metrics.RoomInk.Should().Be(0.0);
        }

        [Fact]
        public void Check_WallLighterThanRoom_IsRejectedAsSwappedNumbering()
        {
            GateResult gate = Gate(Request(image: ImagePng(Geom(), darkWalls: false)));

            Checks(gate.Rejections).Should().Contain("tinta");
        }

        [Fact]
        public void Check_OutlineOnlyWalls_AreFlaggedNotRejected()
        {
            // Walls drawn as two thin lines: mostly paper under the wall class, still darker than rooms.
            GeomDocument geom = Geom();
            SemanticMask mask = GeomRasterizer.PaintSemantic(geom);
            var rgb = Enumerable.Repeat((byte)250, 200 * 100 * 3).ToArray();
            for (int y = 0; y < 100; y++)
            {
                for (int x = 0; x < 200; x++)
                {
                    bool wall = mask[x, y] == (byte)StructureClass.Wall;
                    bool edge = wall && (x == 0 || y == 0 || mask[x - 1, y] != mask[x, y] || mask[x, y - 1] != mask[x, y]);
                    if (edge)
                    {
                        int i = ((y * 200) + x) * 3;
                        rgb[i] = rgb[i + 1] = rgb[i + 2] = 20;
                    }
                }
            }

            GateResult gate = Gate(Request(image: PngEncoder.EncodeRgb(200, 100, rgb)));

            gate.Rejections.Should().BeEmpty();
            Checks(gate.Review).Should().Contain("tinta");
        }

        [Fact]
        public void Check_RoomAreaOffByMoreThanTwoPercent_IsRejected()
        {
            var rooms = Rooms().Select((r, i) => i == 0 ? r with { RevitAreaM2 = r.Area * 1.05 } : r).ToArray();

            GateResult gate = Gate(Request(rooms: rooms));

            gate.Rejections.Should().ContainSingle(f => f.Check == "area").Which.Message.Should().Contain("0");
        }

        [Fact]
        public void Check_RoomPolygonShiftedFromTheMask_FailsTheRoundtrip()
        {
            // The label says the room is 0.5 m (5 px) to the right of where it was painted.
            var rooms = Rooms().Select((r, i) => i == 0 ? r with { Centroid = new Point2D(r.Centroid.X + 0.5, r.Centroid.Y) } : r).ToArray();

            GateResult gate = Gate(Request(rooms: rooms));

            Checks(gate.Rejections).Should().Contain("roundtrip");
        }

        [Fact]
        public void Check_WallShareOutsideTheSyntheticRange_IsFlaggedForReview()
        {
            // Rooms only and one hair-thin wall: under 0.8% of the image is wall.
            GeomDocument thin = Geom(withWalls: false) with
            {
                Elements = Geom(withWalls: false).Elements.Concat(new[] { GeomElement.Filled(StructureClass.Wall, 9, new[] { Rect(7.9, 1.2, 8.1, 3) }) }).ToArray(),
            };

            GateResult gate = Gate(Request(geom: thin, image: ImagePng(thin)));

            gate.Rejections.Should().BeEmpty();
            Checks(gate.Review).Should().Contain("distribuicao");
        }

        [Fact]
        public void Check_UnmappedRoomName_IsFlaggedForReview()
        {
            var rooms = new[] { LabelTestData.Room(0, null, 1.2, 1.2, 7.9, 8.8, "DEPÓSITO"), LabelTestData.Room(1, "living_room", 8.1, 1.2, 14.8, 8.8) };

            GateResult gate = Gate(Request(rooms: rooms));

            gate.Review.Should().Contain(f => f.Check == "vocabulario" && f.Message.Contains("DEPÓSITO"));
        }
    }

    public sealed class DatasetRegistryTests : System.IDisposable
    {
        private readonly string _file = Path.Combine(Path.GetTempPath(), "fpx-registry-" + System.Guid.NewGuid().ToString("N") + ".json");

        public void Dispose()
        {
            if (File.Exists(_file))
            {
                File.Delete(_file);
            }
        }

        [Fact]
        public void Resolve_NewKeysGetConsecutiveIdsFrom900001_KnownKeysKeepTheirs()
        {
            DatasetRegistry registry = DatasetRegistry.Load(_file);

            (DatasetRegistry r1, int a) = registry.Resolve(new SampleKey("g", "level", "1"));
            (DatasetRegistry r2, int b) = r1.Resolve(new SampleKey("g", "level", "2"));
            (DatasetRegistry r3, int again) = r2.Resolve(new SampleKey("g", "level", "1"));

            (a, b, again).Should().Be((900001, 900002, 900001));
            r3.Should().BeSameAs(r2, "a known key changes nothing");
            registry.Count.Should().Be(0, "resolving never mutates");
        }

        [Fact]
        public void SaveThenLoad_KeepsTheAssignments()
        {
            (DatasetRegistry registry, _) = DatasetRegistry.Load(_file).Resolve(new SampleKey("g", "level", "1"));
            registry.Save(_file);

            (_, int id) = DatasetRegistry.Load(_file).Resolve(new SampleKey("g", "level", "1"));

            id.Should().Be(900001);
        }

        [Fact]
        public void Load_CorruptedFile_ThrowsInsteadOfStartingOver()
        {
            File.WriteAllText(_file, "{ not json");

            System.Action act = () => DatasetRegistry.Load(_file);

            act.Should().Throw<InvalidDataException>();
        }
    }

    public class QaOverlayTests
    {
        [Fact]
        public void Compose_TintsClassPixelsAt40Percent_AndLeavesBackgroundAlone()
        {
            RgbImage image = PngDecoder.DecodeRgb(PngEncoder.EncodeRgb(2, 1, new byte[] { 255, 255, 255, 255, 255, 255 }));
            SemanticMask mask = SemanticMaskPainter.Paint(2, 1, new[]
            {
                MaskElement.Filled(StructureClass.Wall, new[] { new PolygonShape(new[] { new Point2D(0, 0), new Point2D(1, 0), new Point2D(1, 1), new Point2D(0, 1) }, System.Array.Empty<IReadOnlyList<Point2D>>()) }),
            });

            RgbImage qa = QaOverlay.Compose(image, mask);

            // 255 * 0.6 + (230, 60, 60) * 0.4
            qa.CopyPixels().Should().Equal(245, 177, 177, 255, 255, 255);
        }
    }
}
