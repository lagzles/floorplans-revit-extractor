using System;
using System.Collections.Generic;
using System.Linq;
using FloorplansRevitExtractor.Core.Geometry;
using FloorplansRevitExtractor.Core.Label;
using FloorplansRevitExtractor.Core.Model;
using FluentAssertions;
using Xunit;
using static FloorplansRevitExtractor.Core.Tests.Label.LabelTestData;

namespace FloorplansRevitExtractor.Core.Tests.Label
{
    public class LabelAssemblerTests
    {
        [Fact]
        public void Build_Params_CountsWhatIsMeasuredAndLeavesTheGeneratorKnobsNull()
        {
            LabelInputs inputs = Inputs(rooms: new[]
            {
                Room(0, "bedroom", 1, 1, 3, 3),
                Room(1, "bedroom", 3, 1, 5, 3),
                Room(2, "bathroom", 5, 1, 7, 3),
                Room(3, "powder", 7, 1, 9, 3),
                Room(4, "parking", 9, 1, 13, 3),
            }, doors: Array.Empty<DoorRecord>(), windows: Array.Empty<WindowRecord>(), furniture: Array.Empty<FurnitureRecord>());

            ParamsFragment p = LabelAssembler.Build(inputs).Params;

            p.Seed.Should().BeNull();
            p.NBedrooms.Should().Be(2);
            p.NBathrooms.Should().Be(1, "powder rooms are not bathrooms");
            p.Garage.Should().BeTrue("parking counts as garage");
            p.TotalArea.Should().Be(64.0);
            p.ThinPartitions.Should().BeFalse();
            p.WallIntMin.Should().Be(0.1);
            p.Style.Should().BeNull();
            p.CornerCut.Should().BeNull();
            p.WallPillars.Should().BeNull();
            p.NotchCount.Should().BeNull();
        }

        [Fact]
        public void Build_ConnectivityGraph_HasEdgesOnlyForInteriorDoors()
        {
            SampleLabel label = LabelAssembler.Build(Inputs());

            label.ConnectivityGraph.Nodes.Select(n => n.Id).Should().Equal(0, 1, 2);
            label.ConnectivityGraph.Edges.Should().ContainSingle()
                .Which.Should().Be(new GraphEdge(0, 2, 0, "single"));
            label.ConnectivityGraph.ExternalDoors.Should().Equal(new[] { 1 }, "external means room_b is null, as in the generator");
        }

        [Fact]
        public void Build_Caption_UsesTheGeneratorTemplateWithoutStyleOrBuildingType()
        {
            SampleLabel label = LabelAssembler.Build(Inputs());

            label.Caption.Should().Be("Planta baixa com 1 quarto, 1 banheiro, 1 sala; área construída 60 m², um pavimento.");
        }

        [Fact]
        public void Build_Instances_CoverEveryRoomAndImageSizeIsTheTransformSize()
        {
            SampleLabel label = LabelAssembler.Build(Inputs());

            label.InstancesPx.Select(i => (i.InstanceId, i.RoomId)).Should().Equal((1, 0), (2, 1), (3, 2));
            label.ImageSize.Should().Be((200, 100));
            label.Envelope.Should().Be("rect");
        }

        [Fact]
        public void Build_UnmappedRoomType_IsKeptWithNullTypeAndLeftOutOfTheCounts()
        {
            LabelInputs inputs = Inputs(rooms: new[] { Room(0, null, 1, 1, 5, 5), Room(1, "bedroom", 5, 1, 9, 5) },
                doors: Array.Empty<DoorRecord>(), windows: Array.Empty<WindowRecord>(), furniture: Array.Empty<FurnitureRecord>());

            SampleLabel label = LabelAssembler.Build(inputs);

            label.Rooms[0].Type.Should().BeNull();
            label.Caption.Should().Be("Planta baixa com 1 quarto; área construída 60 m², um pavimento.");
        }

        [Fact]
        public void Build_DoorPointingAtAMissingRoom_Throws()
        {
            var doors = new[] { new DoorRecord(0, 0, 7, "single", new Point2D(5, 3), 90, 0.8, null, null, false) };

            Action act = () => LabelAssembler.Build(Inputs(doors: doors));

            act.Should().Throw<ArgumentException>().WithMessage("*room*7*");
        }

        [Theory]
        [InlineData("window")]
        [InlineData("furniture")]
        [InlineData("swing_into")]
        [InlineData("suite_of")]
        public void Build_AnyDanglingRoomReference_Throws(string where)
        {
            LabelInputs inputs = where switch
            {
                "window" => Inputs(windows: new[] { new WindowRecord(0, 9, "single", new Point2D(3, 1), 0, 1.2) }),
                "furniture" => Inputs(furniture: new[] { new FurnitureRecord(0, "Bed", 9, new Point2D(3, 3), 0, 1.4, 1.9) }),
                "swing_into" => Inputs(doors: new[] { new DoorRecord(0, 9, null, "single", new Point2D(5, 3), 90, 0.8, null, 9, false) }),
                _ => Inputs(rooms: new[] { Room(0, "bedroom", 1, 1, 5, 5), Room(1, "bathroom", 5, 1, 8, 5) with { SuiteOf = 9 } }),
            };

            Action act = () => LabelAssembler.Build(inputs);

            act.Should().Throw<ArgumentException>();
        }

        [Fact]
        public void Build_RoomIdsNotSequentialFromZero_Throws()
        {
            Action act = () => LabelAssembler.Build(Inputs(rooms: new[] { Room(0, "bedroom", 1, 1, 5, 5), Room(2, "bathroom", 5, 1, 8, 5) },
                doors: Array.Empty<DoorRecord>(), windows: Array.Empty<WindowRecord>(), furniture: Array.Empty<FurnitureRecord>()));

            act.Should().Throw<ArgumentException>().WithMessage("*sequential*");
        }

        [Fact]
        public void Build_ClassIdThatDisagreesWithTheType_Throws()
        {
            RoomPreviewRecord wrong = Room(0, "bedroom", 1, 1, 5, 5) with { ClassId = 1 };

            Action act = () => LabelAssembler.Build(Inputs(rooms: new[] { wrong },
                doors: Array.Empty<DoorRecord>(), windows: Array.Empty<WindowRecord>(), furniture: Array.Empty<FurnitureRecord>()));

            act.Should().Throw<ArgumentException>().WithMessage("*class_id*");
        }

        [Fact]
        public void Build_DuplicateDoorIds_Throws()
        {
            var doors = new[]
            {
                new DoorRecord(0, 0, 1, "single", new Point2D(5, 3), 90, 0.8, null, null, false),
                new DoorRecord(0, 1, 2, "single", new Point2D(8, 3), 90, 0.8, null, null, false),
            };

            Action act = () => LabelAssembler.Build(Inputs(doors: doors));

            act.Should().Throw<ArgumentException>();
        }

        [Fact]
        public void Build_NullListsOrNames_AreRejectedExplicitly()
        {
            var actions = new Action[]
            {
                () => LabelAssembler.Build(Inputs() with { Doors = null! }),
                () => LabelAssembler.Build(Inputs() with { Windows = null! }),
                () => LabelAssembler.Build(Inputs() with { Furniture = null! }),
                () => LabelAssembler.Build(Inputs() with { Building = Inputs().Building with { Outline = null! } }),
                () => LabelAssembler.Build(Inputs(rooms: new[] { Room(0, "bedroom", 1, 1, 5, 5) with { Name = null! } },
                    doors: Array.Empty<DoorRecord>(), windows: Array.Empty<WindowRecord>(), furniture: Array.Empty<FurnitureRecord>())),
            };

            foreach (Action act in actions)
            {
                act.Should().Throw<ArgumentException>();
            }
        }

        [Fact]
        public void Build_RoomHiddenUnderAnother_IsReportedAsHavingNoPixels()
        {
            // Room 1 lies entirely under room 2 (higher id wins), so it has no instance.
            LabelInputs inputs = Inputs(rooms: new[]
            {
                Room(0, "bedroom", 1, 1, 5, 5),
                Room(1, "closet", 9, 2, 10, 3),
                Room(2, "living_room", 8, 1, 15, 5),
            }, doors: Array.Empty<DoorRecord>(), windows: Array.Empty<WindowRecord>(), furniture: Array.Empty<FurnitureRecord>());

            SampleLabel label = LabelAssembler.Build(inputs);

            label.InstancesPx.Select(i => i.RoomId).Should().Equal(0, 2);
            label.RoomsWithoutPixels.Should().Equal(1);
        }

        [Theory]
        [InlineData("1.png")]
        [InlineData("900001.jpg")]
        [InlineData("")]
        public void Build_FileNotSixDigitsPng_Throws(string file)
        {
            Action act = () => LabelAssembler.Build(Inputs() with { File = file });

            act.Should().Throw<ArgumentException>();
        }
    }

    public class CaptionBuilderTests
    {
        [Fact]
        public void Build_FollowsTheGeneratorsTypeOrderAndPlurals()
        {
            string[] types = { "kitchen", "bedroom", "bedroom", "garage", "bathroom", "powder", "corridor", "office", "office" };

            CaptionBuilder.Build(types, 120.0).Should().Be(
                "Planta baixa com 2 quartos, 1 banheiro, 1 cozinha, 1 garagem, 2 escritórios, 1 lavabo; área construída 120 m², um pavimento.");
        }

        [Theory]
        [InlineData(120.5, "120")] // Python f"{x:.0f}" rounds half to even
        [InlineData(121.5, "122")]
        [InlineData(995.6, "996")]
        public void Build_AreaIsRoundedLikePythonFormatting(double area, string expected)
        {
            CaptionBuilder.Build(new[] { "bedroom" }, area).Should().Contain($"área construída {expected} m²");
        }

        [Fact]
        public void Build_NothingCountedAndNoArea_StillReadsAsASentence()
        {
            CaptionBuilder.Build(new[] { "corridor" }, null).Should().Be("Planta baixa; um pavimento.");
        }
    }

    public class EnvelopeClassifierTests
    {
        private static IReadOnlyList<Point2D> Ring(params (double X, double Y)[] p) => p.Select(q => new Point2D(q.X, q.Y)).ToArray();

        // The generator (cad.py) names the envelope by corner count alone:
        // 4 rect, 6 L, 8 T, anything else U. The label follows the data it joins.
        [Theory]
        [InlineData("rect", "rect")]
        [InlineData("rotated rect", "rect")]
        [InlineData("L", "L")]
        [InlineData("T", "T")]
        [InlineData("U with 8 corners", "T")]
        [InlineData("Z with 8 corners", "T")]
        [InlineData("10 corners", "U")]
        public void Classify_NamesTheShapeByCornerCountLikeTheGenerator(string shape, string expected)
        {
            IReadOnlyList<Point2D> ring = shape switch
            {
                "rect" => Ring((0, 0), (10, 0), (10, 5), (0, 5)),
                "rotated rect" => Ring((0, 0), (8, 6), (5, 10), (-3, 4)),
                "L" => Ring((0, 0), (10, 0), (10, 3), (4, 3), (4, 8), (0, 8)),
                "T" => Ring((4, 0), (8, 0), (8, 6), (12, 6), (12, 9), (0, 9), (0, 6), (4, 6)),
                "U with 8 corners" => Ring((0, 0), (12, 0), (12, 8), (8, 8), (8, 4), (4, 4), (4, 8), (0, 8)),
                "Z with 8 corners" => Ring((0, 0), (4, 0), (4, 2), (6, 2), (6, 4), (2, 4), (2, 2), (0, 2)),
                _ => Ring((0, 0), (12, 0), (12, 8), (9, 8), (9, 4), (6, 4), (6, 6), (3, 6), (3, 8), (0, 8)),
            };

            EnvelopeClassifier.Classify(ring).Should().Be(expected);
            EnvelopeClassifier.Classify(ring.Reverse().ToArray()).Should().Be(expected, "winding does not matter");
        }

        [Fact]
        public void Classify_IgnoresCollinearAndDuplicatePoints()
        {
            EnvelopeClassifier.Classify(Ring((0, 0), (5, 0), (10, 0), (10, 3), (4, 3), (4, 3), (4, 8), (0, 8), (0, 0))).Should().Be("L");
        }

        [Fact]
        public void Classify_ModellingNoise_DoesNotAddCorners()
        {
            // A 2 mm jog and a 0.01 degree bend are modelling noise, not corners.
            IReadOnlyList<Point2D> noisy = Ring((0, 0), (5, 0), (5, 0.002), (10, 0.002), (10, 5), (5, 5.0009), (0, 5));

            EnvelopeClassifier.Classify(noisy).Should().Be("rect");
        }

        [Fact]
        public void Classify_NoOutline_IsNull()
        {
            EnvelopeClassifier.Classify(Array.Empty<Point2D>()).Should().BeNull("an outline that was not measured is not guessed");
        }
    }

    public class LabelRecordTests
    {
        [Theory]
        [InlineData("revolving")]
        [InlineData("Single")]
        public void DoorRecord_KindOutsideTheVocabulary_Throws(string kind)
        {
            Action act = () => _ = new DoorRecord(0, 0, null, kind, new Point2D(0, 0), 0, 0.8, null, null, false);

            act.Should().Throw<ArgumentException>();
        }

        [Theory]
        [InlineData("passage")]
        [InlineData("sliding")]
        [InlineData("gate")]
        public void DoorRecord_HingeOnAnythingButSingleOrDouble_Throws(string kind)
        {
            // The generator only gives single and double doors a hinge.
            Action act = () => _ = new DoorRecord(0, 0, null, kind, new Point2D(0, 0), 0, 0.8, new Point2D(0, 0.4), null, false);

            act.Should().Throw<ArgumentException>();
        }

        [Fact]
        public void DoorRecord_SameRoomOnBothSides_Throws()
        {
            Action act = () => _ = new DoorRecord(0, 2, 2, "single", new Point2D(0, 0), 0, 0.8, null, null, false);

            act.Should().Throw<ArgumentException>();
        }

        [Fact]
        public void DoorRecord_SwingIntoARoomTheDoorDoesNotConnect_Throws()
        {
            Action act = () => _ = new DoorRecord(0, 0, 1, "single", new Point2D(0, 0), 0, 0.8, null, 2, false);

            act.Should().Throw<ArgumentException>();
        }

        [Fact]
        public void DoorRecord_Exterior_IsTheWallItSitsIn_NotWhetherARoomIsMissing()
        {
            // Dataset: "exterior" means an exterior (thicker) wall; a door to a patio
            // room has both rooms and exterior true.
            new DoorRecord(0, 0, 1, "single", new Point2D(0, 0), 0, 0.8, null, null, true).Exterior.Should().BeTrue();
            new DoorRecord(0, 0, null, "single", new Point2D(0, 0), 0, 0.8, null, null, false).Exterior.Should().BeFalse();
        }

        [Theory]
        [InlineData(-1e-17, 0)]
        [InlineData(-1e-14, 0)]
        [InlineData(-90, 270)]
        [InlineData(360, 0)]
        [InlineData(450.5, 90.5)]
        public void Rotation_IsNormalizedTo0To360(double rotation, double expected)
        {
            new WindowRecord(0, null, "high", new Point2D(0, 0), rotation, 1).Rotation.Should().Be(expected);
        }

        [Fact]
        public void FurnitureRecord_KindOutsideTheClosedVocabulary_Throws()
        {
            Action act = () => _ = new FurnitureRecord(0, "Piano", null, new Point2D(0, 0), 0, 1, 1);

            act.Should().Throw<ArgumentException>();
        }

        [Fact]
        public void FurnitureKinds_AreExactlyTheSpecs24()
        {
            LabelVocabularies.FurnitureKinds.Should().HaveCount(24).And.Contain("TV").And.Contain("Washer");
        }
    }

    public class RenderStyleTests
    {
        [Fact]
        public void Empty_HasEveryDatasetKeyInOrder_AllNull()
        {
            RenderStyle.Empty.Keys.Should().Equal(DatasetV4.RenderStyleKeys);
            RenderStyle.Empty.Keys.Select(k => RenderStyle.Empty[k]).Should().OnlyContain(v => v == null);
        }

        [Fact]
        public void With_ReturnsANewStyleAndLeavesTheOriginalUntouched()
        {
            RenderStyle styled = RenderStyle.Empty.With("show_dims", true);

            styled["show_dims"].Should().Be(true);
            RenderStyle.Empty["show_dims"].Should().BeNull();
        }

        [Fact]
        public void With_UnknownKey_Throws()
        {
            Action act = () => RenderStyle.Empty.With("show_everything", true);

            act.Should().Throw<ArgumentException>();
        }

        [Fact]
        public void With_IntOnAFloatKey_IsStoredAsAFloat()
        {
            RenderStyle.Empty.With("hatch_angle", 45)["hatch_angle"].Should().Be(45.0);
        }

        [Theory]
        [InlineData("scale_den", 100.0)]
        [InlineData("show_dims", "yes")]
        [InlineData("lang", 1)]
        public void With_ValueOfTheWrongTypeForTheKey_Throws(string key, object value)
        {
            Action act = () => RenderStyle.Empty.With(key, value);

            act.Should().Throw<ArgumentException>();
        }

        [Fact]
        public void With_LabelColor_AcceptsAnRgbTripleOnly()
        {
            RenderStyle.Empty.With("label_color", new[] { 110, 40, 120 })["label_color"].Should().BeEquivalentTo(new[] { 110, 40, 120 });

            Action twoValues = () => RenderStyle.Empty.With("label_color", new[] { 1, 2 });
            Action outOfRange = () => RenderStyle.Empty.With("label_color", new[] { 1, 2, 300 });
            twoValues.Should().Throw<ArgumentException>();
            outOfRange.Should().Throw<ArgumentException>();
        }

        [Fact]
        public void With_UnsupportedValueType_Throws()
        {
            Action act = () => RenderStyle.Empty.With("scale_den", new object());

            act.Should().Throw<ArgumentException>();
        }
    }
}
