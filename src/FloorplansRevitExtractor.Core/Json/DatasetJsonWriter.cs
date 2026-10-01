using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using FloorplansRevitExtractor.Core.Model;
using FloorplansRevitExtractor.Core.Png;
using FloorplansRevitExtractor.Core.Raster;
using FloorplansRevitExtractor.Core.Sample;

namespace FloorplansRevitExtractor.Core.Json
{
    /// <summary>The batch-level JSON files: manifest, classes, per-sample report and review list.</summary>
    public static class DatasetJsonWriter
    {
        /// <summary>
        /// manifest.json in the training dataset's shape. In the evaluation phase
        /// every sample goes to test; the training team splits a volume batch,
        /// grouped by project.
        /// </summary>
        public static void WriteManifest(Stream output, IReadOnlyList<string> ids)
        {
            using var writer = new Utf8JsonWriter(output, JsonFields.Options);
            writer.WriteStartObject();
            writer.WriteNumber("num_classes", StructureClassPalette.Colors.Count);
            writer.WriteNumber("ignore_index", SemanticMask.IgnoreIndex);
            writer.WriteString("image_dir", SampleFiles.Images);
            writer.WriteString("image_ext", SampleFiles.ImageExtension);
            writer.WriteString("semantic_dir", SampleFiles.SemanticMasks);
            writer.WriteString("instance_dir", SampleFiles.InstanceMasks);
            writer.WriteString("labels_dir", SampleFiles.Labels);
            writer.WriteNumber("n_samples", ids.Count);
            writer.WriteString("mask_mode", "structure");
            writer.WriteStartObject("splits");
            WriteStrings(writer, "train", new string[0]);
            WriteStrings(writer, "val", new string[0]);
            WriteStrings(writer, "test", ids);
            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        /// <summary>classes.json: [{id, name, color}], the training dataset's colors.</summary>
        public static void WriteClasses(Stream output)
        {
            using var writer = new Utf8JsonWriter(output, JsonFields.Options);
            writer.WriteStartArray();
            for (int id = 0; id < StructureClassPalette.Colors.Count; id++)
            {
                Rgb color = StructureClassPalette.Colors[id];
                writer.WriteStartObject();
                writer.WriteNumber("id", id);
                writer.WriteString("name", StructureClassNames.ToName((StructureClass)id));
                writer.WriteStartArray("color");
                writer.WriteNumberValue(color.R);
                writer.WriteNumberValue(color.G);
                writer.WriteNumberValue(color.B);
                writer.WriteEndArray();
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
        }

        /// <summary>reports/NNNNNN.json (spec v1 section 9): verdict, reasons and gate measurements.</summary>
        public static void WriteReport(Stream output, string id, GateResult gate, SourceFragment source)
        {
            using var writer = new Utf8JsonWriter(output, JsonFields.Options);
            writer.WriteStartObject();
            writer.WriteString("id", id);
            writer.WriteString("status", gate.Accepted ? "accepted" : "rejected");
            WriteFindings(writer, "rejections", gate.Rejections);
            WriteFindings(writer, "review", gate.Review);
            writer.WriteStartObject("metrics");
            JsonFields.WriteFloat(writer, "wall_fraction", gate.Metrics.WallFraction);
            JsonFields.WriteFloat(writer, "opening_fraction", gate.Metrics.OpeningFraction);
            WriteNullableFloat(writer, "wall_ink", gate.Metrics.WallInk);
            WriteNullableFloat(writer, "room_ink", gate.Metrics.RoomInk);
            writer.WriteEndObject();
            writer.WriteStartObject("source");
            JsonFields.WriteSourceFields(writer, source);
            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        /// <summary>review.json: the accepted samples a person should look at, and why.</summary>
        public static void WriteReview(Stream output, IReadOnlyList<(string Id, IReadOnlyList<GateFinding> Findings)> samples)
        {
            using var writer = new Utf8JsonWriter(output, JsonFields.Options);
            writer.WriteStartObject();
            writer.WriteStartArray("samples");
            foreach ((string id, IReadOnlyList<GateFinding> findings) in samples)
            {
                writer.WriteStartObject();
                writer.WriteString("id", id);
                WriteFindings(writer, "review", findings);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        /// <summary>Reads back the review findings of a report written by <see cref="WriteReport"/>.</summary>
        public static IReadOnlyList<GateFinding> ReadReviewFindings(Stream report)
        {
            using JsonDocument json = JsonDocument.Parse(report);
            return json.RootElement.GetProperty("review").EnumerateArray()
                .Select(f => new GateFinding(f.GetProperty("check").GetString()!, f.GetProperty("message").GetString()!))
                .ToList();
        }

        private static void WriteFindings(Utf8JsonWriter writer, string name, IReadOnlyList<GateFinding> findings)
        {
            writer.WriteStartArray(name);
            foreach (GateFinding finding in findings)
            {
                writer.WriteStartObject();
                writer.WriteString("check", finding.Check);
                writer.WriteString("message", finding.Message);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
        }

        private static void WriteNullableFloat(Utf8JsonWriter writer, string name, double? value)
        {
            if (value.HasValue)
            {
                JsonFields.WriteFloat(writer, name, value.Value);
            }
            else
            {
                writer.WriteNull(name);
            }
        }

        private static void WriteStrings(Utf8JsonWriter writer, string name, IEnumerable<string> values)
        {
            writer.WriteStartArray(name);
            foreach (string value in values)
            {
                writer.WriteStringValue(value);
            }

            writer.WriteEndArray();
        }
    }
}
