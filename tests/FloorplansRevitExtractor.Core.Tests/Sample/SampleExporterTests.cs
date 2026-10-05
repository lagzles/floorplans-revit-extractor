using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using FloorplansRevitExtractor.Core.Png;
using FloorplansRevitExtractor.Core.Raster;
using FloorplansRevitExtractor.Core.Sample;
using FloorplansRevitExtractor.Core.Tests.Label;
using FloorplansRevitExtractor.Core.Tests.Png;
using FluentAssertions;
using Xunit;
using static FloorplansRevitExtractor.Core.Tests.Sample.SampleTestData;

namespace FloorplansRevitExtractor.Core.Tests.Sample
{
    public sealed class SampleExporterTests : IDisposable
    {
        private readonly string _out = Path.Combine(Path.GetTempPath(), "fpx-tests-" + Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            if (Directory.Exists(_out))
            {
                Directory.Delete(_out, recursive: true);
            }
        }

        private string P(params string[] parts) => Path.Combine(new[] { _out }.Concat(parts).ToArray());

        private JsonElement Json(params string[] parts) => JsonDocument.Parse(File.ReadAllBytes(P(parts))).RootElement;

        [Fact]
        public void Export_AcceptedSample_WritesTheDatasetLayout()
        {
            ExportResult result = SampleExporter.Export(_out, Request());

            result.Accepted.Should().BeTrue(string.Join("; ", result.Gate.Rejections.Select(r => r.Message)));
            result.Id.Should().Be("900001");
            foreach (string dir in new[] { "images", "masks_semantic", "masks_instance", "labels", "geom", "qa", "reports" })
            {
                string ext = dir == "labels" || dir == "geom" || dir == "reports" ? ".json" : ".png";
                File.Exists(P(dir, "900001" + ext)).Should().BeTrue(dir);
            }

            Directory.Exists(P(".staging")).Should().BeFalse("staging is cleaned up");
            File.Exists(P("qa", "900001_anotacao.png")).Should().BeTrue("spec v3: a QA image of grid and dimension alone");
        }

        [Fact]
        public void Export_FilesHaveTheDatasetFormats()
        {
            SampleExporter.Export(_out, Request());

            PngTestDecoder image = PngTestDecoder.Decode(File.ReadAllBytes(P("images", "900001.png")));
            PngTestDecoder semantic = PngTestDecoder.Decode(File.ReadAllBytes(P("masks_semantic", "900001.png")));
            PngTestDecoder instance = PngTestDecoder.Decode(File.ReadAllBytes(P("masks_instance", "900001.png")));

            (image.ColorType, image.BitDepth).Should().Be((2, 8), "PNG RGB 8-bit");
            (semantic.ColorType, semantic.BitDepth).Should().Be((3, 8), "mode P");
            (instance.ColorType, instance.BitDepth).Should().Be((0, 16), "I;16");
            new[] { image.Width, semantic.Width, instance.Width }.Should().OnlyContain(w => w == 200);
            semantic.Palette.Skip(2 * 3).Take(3).Should().Equal(new byte[] { 230, 60, 60 }, "wall color from classes.json");
            semantic.Pixels.Should().Contain((byte)StructureClass.Wall);
        }

        [Fact]
        public void Export_RootFiles_DescribeTheBatch()
        {
            SampleExporter.Export(_out, Request());
            SampleExporter.Export(_out, Request(viewKey: "999"));

            JsonElement manifest = Json("manifest.json");
            manifest.GetProperty("num_classes").GetInt32().Should().Be(17);
            manifest.GetProperty("ignore_index").GetInt32().Should().Be(255);
            manifest.GetProperty("image_ext").GetString().Should().Be(".png");
            manifest.GetProperty("mask_mode").GetString().Should().Be("structure");
            manifest.GetProperty("n_samples").GetInt32().Should().Be(2);
            manifest.GetProperty("splits").GetProperty("test").EnumerateArray().Select(e => e.GetString()).Should().Equal("900001", "900002");
            manifest.GetProperty("splits").GetProperty("train").GetArrayLength().Should().Be(0);

            JsonElement classes = Json("classes.json");
            classes.GetArrayLength().Should().Be(17);
            classes[10].GetProperty("name").GetString().Should().Be("grid");
            classes[11].GetProperty("color").EnumerateArray().Select(c => c.GetInt32()).Should().Equal(255, 140, 0);
            classes[8].GetProperty("name").GetString().Should().Be("swing");
            classes[8].GetProperty("color").EnumerateArray().Select(c => c.GetInt32()).Should().Equal(235, 110, 175);
        }

        [Fact]
        public void Export_SameViewAgain_ReusesItsIdAndOverwrites()
        {
            SampleExporter.Export(_out, Request());

            ExportResult again = SampleExporter.Export(_out, Request());

            again.Id.Should().Be("900001");
            Directory.GetFiles(P("labels")).Should().ContainSingle();
            Json("registry.json").GetProperty("samples").GetArrayLength().Should().Be(1);
        }

        [Fact]
        public void Export_LabelFileAndImageSize_PointAtTheSample()
        {
            SampleExporter.Export(_out, Request());

            JsonElement label = Json("labels", "900001.json");
            label.GetProperty("file").GetString().Should().Be("900001.png");
            label.GetProperty("image_size").EnumerateArray().Select(v => v.GetInt32()).Should().Equal(200, 100);
            label.GetProperty("instances_px").GetArrayLength().Should().Be(2);
        }

        [Fact]
        public void Export_RejectedSample_GoesToRejectedWithItsReason_AndLeavesTheBatchAlone()
        {
            ExportResult result = SampleExporter.Export(_out, Request(geom: Geom(withWalls: false), image: ImagePng(Geom())));

            result.Accepted.Should().BeFalse();
            result.Gate.Rejections.Select(r => r.Check).Should().Contain("estrutura");
            File.Exists(P("rejected", "900001", "report.json")).Should().BeTrue();
            Directory.Exists(P("labels")).Should().BeFalse();
            Json("manifest.json").GetProperty("n_samples").GetInt32().Should().Be(0);
            Json("rejected", "900001", "report.json").GetProperty("status").GetString().Should().Be("rejected");
        }

        [Fact]
        public void Export_AcceptedAfterRejected_CleansTheRejectedCopy()
        {
            SampleExporter.Export(_out, Request(geom: Geom(withWalls: false), image: ImagePng(Geom())));

            SampleExporter.Export(_out, Request());

            Directory.Exists(P("rejected", "900001")).Should().BeFalse();
            File.Exists(P("labels", "900001.json")).Should().BeTrue();
        }

        [Fact]
        public void Export_ImageOfAnotherSize_IsRejectedNotResized()
        {
            ExportResult result = SampleExporter.Export(_out, Request(image: ImagePng(Geom(), width: 210)));

            result.Accepted.Should().BeFalse();
            result.Gate.Rejections.Select(r => r.Check).Should().Contain("arquivos");
        }

        [Fact]
        public void Export_ReviewFlags_AreCollectedInReviewJson()
        {
            var rooms = new[] { LabelTestData.Room(0, null, 1.2, 1.2, 7.9, 8.8, "DEPÓSITO"), LabelTestData.Room(1, "living_room", 8.1, 1.2, 14.8, 8.8) };
            SampleRequest request = Request(rooms: rooms) with { ReviewNotes = new[] { "3 elementos em vínculo ignorados" } };

            ExportResult result = SampleExporter.Export(_out, request);

            result.Accepted.Should().BeTrue();
            JsonElement entry = Json("review.json").GetProperty("samples")[0];
            entry.GetProperty("id").GetString().Should().Be("900001");
            string flags = entry.GetProperty("review").GetRawText();
            flags.Should().Contain("DEPÓSITO").And.Contain("vínculo");
        }
    }
}
