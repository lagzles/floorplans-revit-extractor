using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using FloorplansRevitExtractor.Core.Json;
using FloorplansRevitExtractor.Core.Label;
using FloorplansRevitExtractor.Core.Tests.Label;
using FluentAssertions;
using Xunit;
using static FloorplansRevitExtractor.Core.Tests.Label.LabelTestData;

namespace FloorplansRevitExtractor.Core.Tests.Json
{
    public class LabelJsonWriterTests
    {
        private static byte[] Write(SampleLabel label)
        {
            using var stream = new MemoryStream();
            LabelJsonWriter.Write(stream, label);
            return stream.ToArray();
        }

        private static JsonElement Root() => JsonDocument.Parse(Write(LabelAssembler.Build(Inputs()))).RootElement;

        private static string[] Keys(JsonElement e) => e.EnumerateObject().Select(p => p.Name).ToArray();

        [Fact]
        public void Write_RootKeys_AreTheDatasetsInOrder_WithSourceRightAfterSeed()
        {
            string[] expected = new[] { "seed", "source" }.Concat(DatasetV4.RootKeys.Skip(1)).ToArray();

            Keys(Root()).Should().Equal(expected);
        }

        [Fact]
        public void Write_NestedObjects_UseTheDatasetsKeysInOrder()
        {
            JsonElement root = Root();

            Keys(root.GetProperty("params")).Should().Equal(DatasetV4.ParamsKeys);
            Keys(root.GetProperty("render_style")).Should().Equal(DatasetV4.RenderStyleKeys);
            Keys(root.GetProperty("rooms")[0]).Should().Equal(DatasetV4.RoomKeys);
            Keys(root.GetProperty("doors")[0]).Should().Equal(DatasetV4.DoorKeys);
            Keys(root.GetProperty("windows")[0]).Should().Equal(DatasetV4.WindowKeys);
            Keys(root.GetProperty("furniture")[0]).Should().Equal(DatasetV4.FurnitureKeys);
            Keys(root.GetProperty("instances_px")[0]).Should().Equal(DatasetV4.InstanceKeys);
            Keys(root.GetProperty("transform")).Should().Equal(DatasetV4.TransformKeys);
            Keys(root.GetProperty("connectivity_graph")).Should().Equal("nodes", "edges", "external_doors");
        }

        [Fact]
        public void Write_Source_AddsTheSpecV2Fields()
        {
            JsonElement source = Root().GetProperty("source");

            Keys(source).Should().Equal(
                "doc_title", "doc_guid", "revit_version", "mode", "level_id", "level_name", "level_elevation_m",
                "view_id", "view_name", "view_scale_den", "sheet_number", "exported_at", "plugin_version",
                "px_per_m_alvo", "alinhamento");
            source.GetProperty("alinhamento").GetString().Should().Be("direto");
        }

        [Fact]
        public void Write_Values_MatchTheDatasetConventions()
        {
            JsonElement root = Root();

            root.GetProperty("seed").ValueKind.Should().Be(JsonValueKind.Null);
            root.GetProperty("mask_mode").GetString().Should().Be("structure");
            root.GetProperty("file").GetString().Should().Be("900001.png");
            root.GetProperty("transform").GetProperty("rotated").GetBoolean().Should().BeFalse();
            root.GetProperty("image_size").EnumerateArray().Select(v => v.GetInt32()).Should().Equal(200, 100);
            root.GetProperty("doors")[1].GetProperty("room_b").ValueKind.Should().Be(JsonValueKind.Null);
            root.GetProperty("doors")[1].GetProperty("exterior").GetBoolean().Should().BeTrue();
            root.GetProperty("render_style").GetProperty("lang").GetString().Should().Be("pt");
            root.GetProperty("render_style").GetProperty("ink").ValueKind.Should().Be(JsonValueKind.Null);
            root.GetProperty("instances_px")[0].GetProperty("bbox_px").GetArrayLength().Should().Be(4);
        }

        [Fact]
        public void Write_Meters_AreRoundedToThreeDecimalsLikePython()
        {
            LabelInputs inputs = Inputs() with
            {
                Building = Inputs().Building with { Width = 14.20049, WallExt = 0.2004999999 },
            };

            JsonElement root = JsonDocument.Parse(Write(LabelAssembler.Build(inputs))).RootElement;

            root.GetProperty("width").GetDouble().Should().Be(14.2);
            root.GetProperty("wall_ext").GetDouble().Should().Be(0.2);
        }

        [Fact]
        public void Write_WholeNumberDoubles_StayFloatsAndCountsStayIntegers()
        {
            // Python reads 90 as int: np.array of such points is int64, and an in-place
            // float operation on it truncates silently. The dataset writes 90.0.
            JsonElement root = Root();
            JsonElement door = root.GetProperty("doors")[0];

            door.GetProperty("rotation").GetRawText().Should().Be("90.0");
            door.GetProperty("position")[0].GetRawText().Should().Be("5.0");
            root.GetProperty("params").GetProperty("total_area").GetRawText().Should().Be("64.0");
            root.GetProperty("transform").GetProperty("px_per_m").GetRawText().Should().Be("10.0");
            root.GetProperty("render_style").GetProperty("scale_den").GetRawText().Should().Be("100");
            root.GetProperty("params").GetProperty("n_bedrooms").GetRawText().Should().Be("1");
            door.GetProperty("id").GetRawText().Should().Be("0");
            root.GetProperty("instances_px")[0].GetProperty("area_px").GetRawText().Should().NotContain(".");
        }

        [Fact]
        public void Write_RoundedValues_UseTheShortDecimalText()
        {
            // On .NET Framework the default double text is 17 digits: 0.80000000000000004.
            JsonElement root = Root();

            root.GetProperty("doors")[0].GetProperty("width").GetRawText().Should().Be("0.8");
            root.GetProperty("width").GetRawText().Should().Be("14.2");
        }

        [Fact]
        public void Write_LargeWholeNumber_IsStillAFloat()
        {
            LabelInputs inputs = Inputs() with { Building = Inputs().Building with { BuiltArea = 1e15 } };

            JsonElement root = JsonDocument.Parse(Write(LabelAssembler.Build(inputs))).RootElement;

            root.GetProperty("built_area").GetRawText().Should().Be("1000000000000000.0");
        }

        [Fact]
        public void Write_LabelColor_IsAnRgbArray()
        {
            LabelInputs inputs = Inputs() with { RenderStyle = RenderStyle.Empty.With("label_color", new[] { 110, 40, 120 }) };

            JsonElement color = JsonDocument.Parse(Write(LabelAssembler.Build(inputs))).RootElement.GetProperty("render_style").GetProperty("label_color");

            color.EnumerateArray().Select(v => v.GetInt32()).Should().Equal(110, 40, 120);
        }

        [Fact]
        public void Write_IsUtf8WithoutBom_WithReadableAccents()
        {
            byte[] bytes = Write(LabelAssembler.Build(Inputs()));

            bytes.Take(3).Should().NotEqual(new byte[] { 0xEF, 0xBB, 0xBF });
            Encoding.UTF8.GetString(bytes).Should().Contain("\"TÉRREO\"").And.Contain("área construída");
        }
    }
}
