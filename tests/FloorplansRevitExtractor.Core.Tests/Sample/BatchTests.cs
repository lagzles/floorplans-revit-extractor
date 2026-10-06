using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using FloorplansRevitExtractor.Core.Sample;
using FluentAssertions;
using Xunit;

namespace FloorplansRevitExtractor.Core.Tests.Sample
{
    public class BatchTests
    {
        [Fact]
        public void ViewVariant_DefaultResolutionKeepsTheBareViewId()
        {
            SampleResolution.ViewVariant(1350581, 48.0).Should().Be("1350581", "samples exported before resolutions keep their ids");
            SampleResolution.ViewVariant(1350581, 100.0).Should().Be("1350581@100");
            SampleResolution.ViewVariant(1350581, 22.5).Should().Be("1350581@22.5");
        }

        [Fact]
        public void ViewVariant_MakesEachResolutionItsOwnSample()
        {
            var typical = new SampleKey("doc", "level", SampleResolution.ViewVariant(7, 48.0));
            var dense = new SampleKey("doc", "level", SampleResolution.ViewVariant(7, 100.0));

            typical.Should().NotBe(dense);
        }

        [Fact]
        public void Bands_FollowTheSpecEvaluationRanges()
        {
            SampleResolution.Bands.Select(b => b.PxPerM).Should().Equal(22.0, 48.0, 100.0);
            SampleResolution.Bands.Should().ContainSingle(b => b.PxPerM == SampleResolution.DefaultPxPerM);
        }

        [Fact]
        public void BatchReport_CountsOutcomes_AndListsEveryView()
        {
            BatchEntry[] entries =
            {
                new BatchEntry(1, "L1", 48, BatchOutcome.Accepted, "900001", "", 12.34),
                new BatchEntry(1, "L1", 100, BatchOutcome.Rejected, "900003", "roundtrip", 20),
                new BatchEntry(2, "L2", 48, BatchOutcome.Failed, null, "boom", 1),
            };
            using var stream = new MemoryStream();

            BatchReport.Write(stream, "Snowdon", new DateTimeOffset(2026, 10, 5, 17, 0, 0, TimeSpan.Zero), entries);

            using JsonDocument json = JsonDocument.Parse(stream.ToArray());
            JsonElement total = json.RootElement.GetProperty("total");
            total.GetProperty("aceita").GetInt32().Should().Be(1);
            total.GetProperty("rejeitada").GetInt32().Should().Be(1);
            total.GetProperty("erro").GetInt32().Should().Be(1);
            total.GetProperty("pulada").GetInt32().Should().Be(0);
            JsonElement[] views = json.RootElement.GetProperty("vistas").EnumerateArray().ToArray();
            views.Should().HaveCount(3);
            views[2].GetProperty("amostra").ValueKind.Should().Be(JsonValueKind.Null);
            views[0].GetProperty("segundos").GetDouble().Should().Be(12.3);
        }
    }
}
