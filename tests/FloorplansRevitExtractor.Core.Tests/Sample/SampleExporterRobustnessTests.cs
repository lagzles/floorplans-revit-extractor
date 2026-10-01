using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using FloorplansRevitExtractor.Core.Sample;
using FluentAssertions;
using Xunit;
using static FloorplansRevitExtractor.Core.Tests.Sample.SampleTestData;

namespace FloorplansRevitExtractor.Core.Tests.Sample
{
    /// <summary>What happens to the batch when an export fails half way.</summary>
    public sealed class SampleExporterRobustnessTests : IDisposable
    {
        private readonly string _out = Path.Combine(Path.GetTempPath(), "fpx-robust-" + Guid.NewGuid().ToString("N"));

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
        public void Export_IdIsReservedInTheRegistryBeforeAnythingCanFail()
        {
            // A failure after the files land must not let the next view take the same id.
            Action broken = () => SampleExporter.Export(_out, Request(image: new byte[] { 1, 2, 3 }));
            broken.Should().Throw<InvalidDataException>();

            ExportResult next = SampleExporter.Export(_out, Request(viewKey: "another view"));

            next.Id.Should().Be("900002");
        }

        [Fact]
        public void Export_FailedReExport_LeavesThePreviousSampleWholeAndListed()
        {
            SampleExporter.Export(_out, Request());

            Action broken = () => SampleExporter.Export(_out, Request(image: new byte[] { 1, 2, 3 }));
            broken.Should().Throw<InvalidDataException>();

            foreach (string dir in new[] { "images", "masks_semantic", "masks_instance" })
            {
                File.Exists(P(dir, "900001.png")).Should().BeTrue(dir);
            }

            File.Exists(P("labels", "900001.json")).Should().BeTrue();
            Json("manifest.json").GetProperty("n_samples").GetInt32().Should().Be(1);
            Directory.Exists(P(".staging")).Should().BeFalse();
        }

        [Fact]
        public void Export_LeftoversOfACrashedExport_AreCleanedUp()
        {
            Directory.CreateDirectory(P(".staging", "900777", "labels"));
            File.WriteAllText(P(".staging", "900777", "labels", "900777.json"), "{}");

            SampleExporter.Export(_out, Request());

            Directory.Exists(P(".staging")).Should().BeFalse();
        }

        [Fact]
        public void Export_PartialSampleFromACrash_IsNeverListedInTheManifest()
        {
            // A crash mid-promotion: image and masks landed, the label did not.
            SampleExporter.Export(_out, Request());
            File.Delete(P("labels", "900001.json"));

            SampleExporter.Export(_out, Request(viewKey: "another view"));

            Json("manifest.json").GetProperty("splits").GetProperty("test").EnumerateArray()
                .Select(e => e.GetString()).Should().Equal("900002");
        }

        [Fact]
        public void Export_RejectedReExport_TakesTheOldAcceptedSampleOutOfTheBatch()
        {
            SampleExporter.Export(_out, Request());

            ExportResult rejected = SampleExporter.Export(_out, Request(geom: Geom(withWalls: false), image: ImagePng(Geom())));

            rejected.Accepted.Should().BeFalse();
            File.Exists(P("labels", "900001.json")).Should().BeFalse();
            File.Exists(P("images", "900001.png")).Should().BeFalse();
            Json("manifest.json").GetProperty("n_samples").GetInt32().Should().Be(0);
        }

        [Fact]
        public void Export_CorruptReportOfAnotherSample_DoesNotBlockTheBatch()
        {
            SampleExporter.Export(_out, Request());
            File.WriteAllText(P("reports", "900001.json"), "{ corrupt");

            ExportResult next = SampleExporter.Export(_out, Request(viewKey: "another view"));

            next.Accepted.Should().BeTrue();
            Json("review.json").GetRawText().Should().Contain("900001").And.Contain("relatório");
        }

        [Fact]
        public void Export_BlankImage_IsRejectedAsHavingNoInk()
        {
            byte[] blank = Core.Png.PngEncoder.EncodeRgb(200, 100, Enumerable.Repeat((byte)255, 200 * 100 * 3).ToArray());

            ExportResult result = SampleExporter.Export(_out, Request(image: blank));

            result.Gate.Rejections.Should().ContainSingle(f => f.Check == "tinta")
                .Which.Message.Should().Contain("sem tinta");
        }

        [Fact]
        public void Export_BatchJsonFiles_LeaveNoTemporaryFilesBehind()
        {
            SampleExporter.Export(_out, Request());

            Directory.GetFiles(_out).Select(Path.GetFileName).Should().BeEquivalentTo(
                "manifest.json", "classes.json", "registry.json", "review.json");
        }
    }
}
