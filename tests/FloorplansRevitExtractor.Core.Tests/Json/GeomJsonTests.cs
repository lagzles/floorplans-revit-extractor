using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using FloorplansRevitExtractor.Core.Geometry;
using FloorplansRevitExtractor.Core.Json;
using FloorplansRevitExtractor.Core.Model;
using FloorplansRevitExtractor.Core.Raster;
using FluentAssertions;
using Xunit;

namespace FloorplansRevitExtractor.Core.Tests.Json
{
    public class GeomJsonTests
    {
        internal static GeomDocument SpecExample() => new GeomDocument
        {
            View = new GeomView(123456, "TÉRREO", 100, 1.2),
            Transform = new PixelTransform(-2.35, 18.40, 48.0, 1520, 1210),
            Elements = new[]
            {
                GeomElement.Filled(StructureClass.Wall, 3301, new[] { Ring((0.0, 0.0), (5.2, 0.0), (5.2, 0.15), (0.0, 0.15)) }),
                GeomElement.Filled(
                    StructureClass.Room,
                    4410,
                    new[] { Ring((0.15, 0.15), (5.05, 0.15), (5.05, 4.0), (0.15, 4.0)) },
                    new[] { Ring((2.0, 2.0), (2.5, 2.0), (2.5, 2.5), (2.0, 2.5)) }),
                GeomElement.Stroked(StructureClass.Swing, 5502, new[] { Ring((1.0, 0.15), (1.4, 0.35), (1.8, 0.95)) }, 0.04),
                GeomElement.Filled(StructureClass.Door, 5502, new[] { Ring((1.0, 0.0), (1.8, 0.0), (1.8, 0.15), (1.0, 0.15)) }),
            },
            Warnings = new[] { new GeomWarning(7710, "parede com menos de 2 px") },
        };

        internal static IReadOnlyList<Point2D> Ring(params (double X, double Y)[] points) =>
            points.Select(p => new Point2D(p.X, p.Y)).ToArray();

        private static byte[] Write(GeomDocument document)
        {
            using var stream = new MemoryStream();
            GeomJsonWriter.Write(stream, document);
            return stream.ToArray();
        }

        private static GeomDocument Read(string json) =>
            GeomJsonReader.Read(new MemoryStream(Encoding.UTF8.GetBytes(json)));

        [Fact]
        public void WriteThenRead_ReproducesTheDocument()
        {
            GeomDocument original = SpecExample();

            GeomDocument back = GeomJsonReader.Read(new MemoryStream(Write(original)));

            back.View.Should().Be(original.View);
            back.Transform.Should().Be(original.Transform);
            back.Warnings.Should().Equal(original.Warnings);
            back.Elements.Should().HaveCount(4);
            for (int i = 0; i < original.Elements.Count; i++)
            {
                back.Elements[i].Class.Should().Be(original.Elements[i].Class);
                back.Elements[i].RevitId.Should().Be(original.Elements[i].RevitId);
                back.Elements[i].StrokeM.Should().Be(original.Elements[i].StrokeM);
                back.Elements[i].Polygons.Should().BeEquivalentTo(original.Elements[i].Polygons, o => o.WithStrictOrdering());
                back.Elements[i].Holes.Should().BeEquivalentTo(original.Elements[i].Holes, o => o.WithStrictOrdering());
                back.Elements[i].Polylines.Should().BeEquivalentTo(original.Elements[i].Polylines, o => o.WithStrictOrdering());
            }
        }

        [Fact]
        public void WriteThenRead_KeepsEveryBitOfEachCoordinate()
        {
            // geom/ is the Core's regression input: a mask painted from the file must
            // equal the one painted in memory, so coordinates are not rounded here.
            double[] awkward = { 0.1 + 0.2, 1.0 / 3.0, -2.3500000000000001, 1e-17, 12345.678901234567, Math.PI };
            var element = GeomElement.Filled(StructureClass.Wall, 1, new[] { awkward.Select(v => new Point2D(v, -v)).ToArray() });
            GeomDocument original = SpecExample() with { Elements = new[] { element } };

            GeomDocument back = GeomJsonReader.Read(new MemoryStream(Write(original)));

            IReadOnlyList<Point2D> ring = back.Elements[0].Polygons[0];
            ring.Select(p => p.X).Should().Equal(awkward);
            ring.Select(p => p.Y).Should().Equal(awkward.Select(v => -v));
        }

        [Fact]
        public void WriteThenRead_KeepsEveryBitOfRandomCoordinates()
        {
            var random = new Random(20260930);
            double[] values = Enumerable.Range(0, 2000)
                .Select(_ => (random.NextDouble() - 0.5) * Math.Pow(10, random.Next(-6, 6)))
                .ToArray();
            var element = GeomElement.Filled(StructureClass.Wall, 1, new[] { values.Select(v => new Point2D(v, v / 7)).ToArray() });
            GeomDocument original = SpecExample() with { Elements = new[] { element } };

            GeomDocument back = GeomJsonReader.Read(new MemoryStream(Write(original)));

            back.Elements[0].Polygons[0].Should().Equal(element.Polygons[0]);
        }

        [Fact]
        public void WriteThenRead_KeepsTheClosedFlagsOfAnnotationStrokes()
        {
            GeomElement grid = GeomElement.Stroked(
                StructureClass.Grid,
                6601,
                new[] { Ring((0.0, -1.2), (0.0, 9.8)), Ring((0.4, 10.2), (0.0, 10.6), (-0.4, 10.2), (0.0, 9.8)) },
                0.04,
                new[] { false, true });

            GeomDocument back = GeomJsonReader.Read(new MemoryStream(Write(SpecExample() with { Elements = new[] { grid } })));

            back.Elements[0].Class.Should().Be(StructureClass.Grid);
            back.Elements[0].Closed.Should().Equal(false, true);
        }

        [Fact]
        public void Write_AnnotationStrokes_CarryTheClosedArray()
        {
            GeomElement dimension = GeomElement.Stroked(StructureClass.Dimension, 7701, new[] { Ring((0, -1.9), (5.2, -1.9)) }, 0.04);

            using JsonDocument json = JsonDocument.Parse(Write(SpecExample() with { Elements = new[] { dimension } }));
            JsonElement element = json.RootElement.GetProperty("elements")[0];

            element.EnumerateObject().Select(p => p.Name).Should().Equal("cls", "revit_id", "polylines", "closed", "stroke_m");
            element.GetProperty("closed").EnumerateArray().Select(e => e.GetBoolean()).Should().Equal(false);
        }

        [Fact]
        public void KindAndText_RoundTrip_AfterTheGeometry()
        {
            GeomElement text = GeomElement.Filled(StructureClass.Text, 7720, new[] { (IReadOnlyList<Point2D>)new[] { new Point2D(1.1, 2.0), new Point2D(2.4, 2.0), new Point2D(2.4, 2.2) } })
                .WithLabel(null, "SALA");
            GeomElement fixture = GeomElement.Filled(StructureClass.Fixture, 9902, new[] { (IReadOnlyList<Point2D>)new[] { new Point2D(0.3, 0.3), new Point2D(0.7, 0.3), new Point2D(0.7, 0.9) } })
                .WithLabel("Toilet", null);
            GeomDocument document = SpecExample() with { Elements = new[] { text, fixture } };

            byte[] bytes = Write(document);
            using JsonDocument json = JsonDocument.Parse(bytes);
            JsonElement[] elements = json.RootElement.GetProperty("elements").EnumerateArray().ToArray();
            elements[0].EnumerateObject().Select(p => p.Name).Should().Equal("cls", "revit_id", "polygons", "text");
            elements[1].EnumerateObject().Select(p => p.Name).Should().Equal("cls", "revit_id", "polygons", "kind");

            GeomDocument back = GeomJsonReader.Read(new MemoryStream(bytes));
            back.Elements[0].Text.Should().Be("SALA");
            back.Elements[0].Kind.Should().BeNull();
            back.Elements[1].Kind.Should().Be("Toilet");
        }

        [Fact]
        public void Read_ClosedArrayOfTheWrongLength_IsInvalid()
        {
            Action act = () => Read("{" + ValidView + ", " + ValidTransform + @", ""elements"": [{""cls"": ""grid"", ""revit_id"": 1, ""polylines"": [[[0,0],[1,0]]], ""closed"": [true, false], ""stroke_m"": 0.04}], ""warnings"": []}");

            act.Should().ThrowExactly<InvalidDataException>();
        }

        [Fact]
        public void Write_FollowsTheContractKeyOrderAndNames()
        {
            using JsonDocument json = JsonDocument.Parse(Write(SpecExample()));
            JsonElement root = json.RootElement;

            root.EnumerateObject().Select(p => p.Name).Should().Equal("view", "transform", "elements", "warnings");
            root.GetProperty("view").EnumerateObject().Select(p => p.Name).Should().Equal("id", "name", "scale", "cut_plane_m");
            root.GetProperty("transform").EnumerateObject().Select(p => p.Name).Should().Equal("x0", "y1", "px_per_m", "width", "height");

            JsonElement[] elements = root.GetProperty("elements").EnumerateArray().ToArray();
            elements[0].EnumerateObject().Select(p => p.Name).Should().Equal("cls", "revit_id", "polygons");
            elements[1].EnumerateObject().Select(p => p.Name).Should().Equal("cls", "revit_id", "polygons", "holes");
            elements[2].EnumerateObject().Select(p => p.Name).Should().Equal("cls", "revit_id", "polylines", "stroke_m");
            elements[2].GetProperty("cls").GetString().Should().Be("swing");
            root.GetProperty("warnings")[0].GetProperty("msg").GetString().Should().Be("parede com menos de 2 px");
        }

        [Fact]
        public void Write_IsUtf8WithoutBom_WithReadableAccents()
        {
            byte[] bytes = Write(SpecExample());

            bytes.Take(3).Should().NotEqual(new byte[] { 0xEF, 0xBB, 0xBF });
            Encoding.UTF8.GetString(bytes).Should().Contain("\"TÉRREO\"");
        }

        [Fact]
        public void Read_SpecContractExample_Parses()
        {
            const string json = @"{""view"": {""id"": 123456, ""name"": ""TÉRREO"", ""scale"": 100, ""cut_plane_m"": 1.2},
 ""transform"": {""x0"": -2.35, ""y1"": 18.40, ""px_per_m"": 48.0, ""width"": 1520, ""height"": 1210},
 ""elements"": [
   {""cls"": ""wall"",   ""revit_id"": 3301, ""polygons"": [[[0.0, 0.0], [5.2, 0.0], [5.2, 0.15], [0.0, 0.15]]]},
   {""cls"": ""room"",   ""revit_id"": 4410, ""polygons"": [[[0.15, 0.15], [5.05, 0.15], [5.05, 4.0], [0.15, 4.0]]], ""holes"": []},
   {""cls"": ""swing"",  ""revit_id"": 5502, ""polylines"": [[[1.0, 0.15], [1.4, 0.35], [1.8, 0.95]]], ""stroke_m"": 0.04},
   {""cls"": ""door"",   ""revit_id"": 5502, ""polygons"": [[[1.0, 0.0], [1.8, 0.0], [1.8, 0.15], [1.0, 0.15]]]}
 ],
 ""warnings"": [{""revit_id"": 7710, ""msg"": ""parede com menos de 2 px""}]}";

            GeomDocument doc = Read(json);

            doc.View.Name.Should().Be("TÉRREO");
            doc.Transform.PxPerM.Should().Be(48.0);
            doc.Elements.Select(e => e.Class).Should().Equal(StructureClass.Wall, StructureClass.Room, StructureClass.Swing, StructureClass.Door);
            doc.Elements[2].StrokeM.Should().Be(0.04);
            doc.Elements[1].Holes.Should().BeEmpty();
        }

        [Theory]
        [InlineData(@"{""cls"": ""sofa"", ""revit_id"": 1, ""polygons"": [[[0,0],[1,0],[1,1]]]}", "unknown class")]
        [InlineData(@"{""cls"": ""background"", ""revit_id"": 1, ""polygons"": [[[0,0],[1,0],[1,1]]]}", "background is never painted")]
        [InlineData(@"{""cls"": ""swing"", ""revit_id"": 1, ""polylines"": [[[0,0],[1,0]]]}", "polylines without stroke_m")]
        [InlineData(@"{""cls"": ""wall"", ""revit_id"": 1, ""polygons"": [[[0,0],[1,0]]]}", "ring with fewer than 3 points")]
        [InlineData(@"{""cls"": ""wall"", ""revit_id"": 1, ""polygons"": [[[0,0,0],[1,0],[1,1]]]}", "point that is not [x, y]")]
        [InlineData(@"{""cls"": ""wall"", ""polygons"": [[[0,0],[1,0],[1,1]]]}", "missing revit_id")]
        [InlineData(@"{""cls"": ""wall"", ""revit_id"": 1}", "no geometry at all")]
        public void Read_InvalidElement_ThrowsInvalidData(string element, string because)
        {
            string json = @"{""view"": {""id"": 1, ""name"": ""V"", ""scale"": 100, ""cut_plane_m"": 1.2},
 ""transform"": {""x0"": 0, ""y1"": 10, ""px_per_m"": 48, ""width"": 100, ""height"": 100},
 ""elements"": [" + element + @"], ""warnings"": []}";

            Action act = () => Read(json);

            act.Should().Throw<InvalidDataException>(because);
        }

        private const string ValidView = @"""view"": {""id"": 1, ""name"": ""V"", ""scale"": 100, ""cut_plane_m"": 1.2}";
        private const string ValidTransform = @"""transform"": {""x0"": 0, ""y1"": 10, ""px_per_m"": 48, ""width"": 100, ""height"": 100}";
        private const string ValidWall = @"{""cls"": ""wall"", ""revit_id"": 1, ""polygons"": [[[0,0],[1,0],[1,1]]]}";

        [Theory]
        [InlineData(@"{""cls"": ""wall"", ""revit_id"": ""abc"", ""polygons"": [[[0,0],[1,0],[1,1]]]}")]
        [InlineData(@"{""cls"": ""wall"", ""revit_id"": 1.5, ""polygons"": [[[0,0],[1,0],[1,1]]]}")]
        [InlineData(@"{""cls"": ""wall"", ""revit_id"": 1, ""polygons"": [[[0,0],[1,""a""],[1,1]]]}")]
        [InlineData(@"{""cls"": ""wall"", ""revit_id"": 1, ""polygons"": [[[0,0],[1,1e400],[1,1]]]}")]
        [InlineData(@"{""cls"": ""wall"", ""revit_id"": 1, ""polygons"": {}}")]
        [InlineData(@"{""cls"": ""wall"", ""revit_id"": 1, ""polygons"": null}")]
        [InlineData(@"{""cls"": 5, ""revit_id"": 1, ""polygons"": [[[0,0],[1,0],[1,1]]]}")]
        [InlineData(@"{""cls"": ""swing"", ""revit_id"": 1, ""polylines"": [[[0,0],[1,0]]], ""stroke_m"": ""0.04""}")]
        [InlineData(@"{""cls"": ""swing"", ""revit_id"": 1, ""polylines"": [[[0,0],[1,0]]], ""stroke_m"": 0.04, ""holes"": [[[0,0],[1,0],[1,1]]]}")]
        [InlineData(@"{""cls"": ""wall"", ""revit_id"": 1, ""polygons"": [[[0,0],[1,0],[1,1]]], ""stroke_m"": 0.04}")]
        [InlineData(@"{""cls"": ""wall"", ""revit_id"": 1, ""polgons"": [[[0,0],[1,0],[1,1]]]}")]
        [InlineData(@"{""cls"": ""wall"", ""revit_id"": 1, ""polygons"": [[[0,0],[1,0],[1,1]]], ""extra"": 1}")]
        [InlineData(@"{""cls"": ""swing"", ""revit_id"": 1, ""polygons"": [], ""polylines"": [[[0,0],[1,0]]], ""stroke_m"": 0.04}")]
        [InlineData(@"5")]
        public void Read_ElementOutsideTheContract_ThrowsInvalidDataOnly(string element)
        {
            Action act = () => Read("{" + ValidView + ", " + ValidTransform + @", ""elements"": [" + element + @"], ""warnings"": []}");

            act.Should().ThrowExactly<InvalidDataException>();
        }

        [Theory]
        [InlineData(@"""view"": null")]
        [InlineData(@"""view"": {""id"": 1, ""name"": null, ""scale"": 100, ""cut_plane_m"": 1.2}")]
        [InlineData(@"""view"": {""id"": 1, ""name"": """", ""scale"": 100, ""cut_plane_m"": 1.2}")]
        [InlineData(@"""view"": {""id"": 1, ""name"": ""V"", ""scale"": -5, ""cut_plane_m"": 1.2}")]
        [InlineData(@"""view"": {""id"": 1, ""name"": ""V"", ""scale"": 100, ""cut_plane_m"": 1e400}")]
        [InlineData(@"""view"": {""id"": 1, ""name"": ""V"", ""scale"": 100, ""cut_plane_m"": 1.2, ""level"": 2}")]
        public void Read_InvalidView_ThrowsInvalidDataOnly(string view)
        {
            Action act = () => Read("{" + view + ", " + ValidTransform + @", ""elements"": [], ""warnings"": []}");

            act.Should().ThrowExactly<InvalidDataException>();
        }

        [Theory]
        [InlineData(@"""transform"": {""x0"": ""1"", ""y1"": 10, ""px_per_m"": 48, ""width"": 100, ""height"": 100}")]
        [InlineData(@"""transform"": {""x0"": 0, ""y1"": 10, ""px_per_m"": 48, ""width"": 1.5, ""height"": 100}")]
        [InlineData(@"""transform"": {""x0"": 0, ""y1"": 10, ""px_per_m"": 0, ""width"": 100, ""height"": 100}")]
        [InlineData(@"""transform"": {""x0"": 0, ""y1"": 10, ""px_per_m"": 48, ""width"": 100}")]
        public void Read_InvalidTransform_ThrowsInvalidDataOnly(string transform)
        {
            Action act = () => Read("{" + ValidView + ", " + transform + @", ""elements"": [], ""warnings"": []}");

            act.Should().ThrowExactly<InvalidDataException>();
        }

        [Theory]
        [InlineData(@"""elements"": {}, ""warnings"": []")]
        [InlineData(@"""elements"": [], ""warnings"": [{""revit_id"": ""abc"", ""msg"": ""x""}]")]
        [InlineData(@"""elements"": [], ""warnings"": [{""revit_id"": 1, ""msg"": 5}]")]
        [InlineData(@"""elements"": [], ""warnings"": [{""revit_id"": 1, ""msg"": """"}]")]
        [InlineData(@"""elements"": [], ""warnings"": [], ""extra"": 1")]
        public void Read_InvalidListsOrWarnings_ThrowInvalidDataOnly(string lists)
        {
            Action act = () => Read("{" + ValidView + ", " + ValidTransform + ", " + lists + "}");

            act.Should().ThrowExactly<InvalidDataException>();
        }

        [Theory]
        [InlineData("[]")]
        [InlineData("{not json")]
        public void Read_RootThatIsNotTheContractObject_ThrowsInvalidDataOnly(string json)
        {
            Action act = () => Read(json);

            act.Should().ThrowExactly<InvalidDataException>();
        }

        [Fact]
        public void Read_ErrorMessage_NamesTheJsonPath()
        {
            Action act = () => Read("{" + ValidView + ", " + ValidTransform + @", ""elements"": [" + ValidWall + @", {""cls"": ""wall"", ""revit_id"": ""x"", ""polygons"": [[[0,0],[1,0],[1,1]]]}], ""warnings"": []}");

            act.Should().Throw<InvalidDataException>().WithMessage("*$.elements[1].revit_id*");
        }

        [Fact]
        public void Read_WarningWithoutRevitId_IsAViewLevelWarning()
        {
            GeomDocument doc = Read("{" + ValidView + ", " + ValidTransform + @", ""elements"": [], ""warnings"": [{""revit_id"": null, ""msg"": ""vista sem recorte""}]}");

            doc.Warnings.Should().Equal(new GeomWarning(null, "vista sem recorte"));
        }

        [Fact]
        public void Factories_RejectElementsTheReaderWouldReject()
        {
            // Anything constructible in memory must survive a write and read back.
            var triangle = new[] { Ring((0, 0), (1, 0), (1, 1)) };
            var actions = new Action[]
            {
                () => GeomElement.Filled(StructureClass.Background, 1, triangle),
                () => GeomElement.Filled(StructureClass.Wall, 1, Array.Empty<IReadOnlyList<Point2D>>()),
                () => GeomElement.Filled(StructureClass.Wall, 1, new[] { Ring((0, 0), (1, 0)) }),
                () => GeomElement.Filled(StructureClass.Wall, 1, triangle, new[] { Ring((0, 0), (1, 0)) }),
                () => GeomElement.Filled(StructureClass.Wall, 1, new[] { Ring((0, 0), (double.NaN, 0), (1, 1)) }),
                () => GeomElement.Stroked(StructureClass.Swing, 1, new[] { Ring((0, 0), (1, 0)) }, 0),
                () => GeomElement.Stroked(StructureClass.Swing, 1, Array.Empty<IReadOnlyList<Point2D>>(), 0.04),
                () => GeomElement.Stroked(StructureClass.Swing, 1, new[] { Ring((0, 0), (1, double.PositiveInfinity)) }, 0.04),
                () => _ = new GeomView(1, null!, 100, 1.2),
                () => _ = new GeomView(1, " ", 100, 1.2),
                () => _ = new GeomView(1, "V", 0, 1.2),
                () => _ = new GeomView(1, "V", 100, double.NaN),
                () => _ = new GeomWarning(1, ""),
            };

            foreach (Action act in actions)
            {
                act.Should().Throw<ArgumentException>();
            }
        }

        [Fact]
        public void Filled_CopiesTheCallersRings()
        {
            var points = new[] { new Point2D(0, 0), new Point2D(1, 0), new Point2D(1, 1) };
            var element = GeomElement.Filled(StructureClass.Wall, 1, new IReadOnlyList<Point2D>[] { points });

            points[0] = new Point2D(99, 99);

            element.Polygons[0][0].Should().Be(new Point2D(0, 0));
        }
    }
}
