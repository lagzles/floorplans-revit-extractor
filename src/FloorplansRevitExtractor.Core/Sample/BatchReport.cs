using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace FloorplansRevitExtractor.Core.Sample
{
    /// <summary>What happened to one view at one resolution in a batch.</summary>
    public enum BatchOutcome
    {
        Accepted,
        Rejected,
        Failed,
        Skipped,
    }

    /// <param name="SampleId">The sample's id (900001...), null when nothing was written.</param>
    /// <param name="Message">Why it failed or was skipped; empty otherwise.</param>
    public sealed record BatchEntry(
        long ViewId,
        string ViewName,
        double PxPerM,
        BatchOutcome Outcome,
        string? SampleId,
        string Message,
        double Seconds);

    /// <summary>
    /// lotes/lote_AAAAMMDD_HHMMSS.json: one line per view and resolution of a
    /// batch export, so a long batch can be checked afterwards without the
    /// dialogs. A failure in one view never stops the batch; it is recorded here.
    /// </summary>
    public static class BatchReport
    {
        public const string Folder = "lotes";

        public static string Write(string outDir, string docTitle, DateTimeOffset startedAt, IReadOnlyList<BatchEntry> entries)
        {
            string folder = Path.Combine(outDir, Folder);
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, "lote_" + startedAt.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture) + ".json");
            using (FileStream stream = File.Create(path))
            {
                Write(stream, docTitle, startedAt, entries);
            }

            return path;
        }

        public static void Write(Stream output, string docTitle, DateTimeOffset startedAt, IReadOnlyList<BatchEntry> entries)
        {
            using var writer = new Utf8JsonWriter(output, new JsonWriterOptions { Indented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
            writer.WriteStartObject();
            writer.WriteString("documento", docTitle);
            writer.WriteString("inicio", startedAt.ToString("o", CultureInfo.InvariantCulture));
            writer.WriteStartObject("total");
            foreach (BatchOutcome outcome in (BatchOutcome[])Enum.GetValues(typeof(BatchOutcome)))
            {
                writer.WriteNumber(Name(outcome), entries.Count(e => e.Outcome == outcome));
            }

            writer.WriteEndObject();
            writer.WriteStartArray("vistas");
            foreach (BatchEntry entry in entries)
            {
                writer.WriteStartObject();
                writer.WriteNumber("view_id", entry.ViewId);
                writer.WriteString("vista", entry.ViewName);
                writer.WriteNumber("px_per_m", entry.PxPerM);
                writer.WriteString("resultado", Name(entry.Outcome));
                if (entry.SampleId == null)
                {
                    writer.WriteNull("amostra");
                }
                else
                {
                    writer.WriteString("amostra", entry.SampleId);
                }

                writer.WriteString("mensagem", entry.Message);
                writer.WriteNumber("segundos", Math.Round(entry.Seconds, 1));
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        public static string Name(BatchOutcome outcome) => outcome switch
        {
            BatchOutcome.Accepted => "aceita",
            BatchOutcome.Rejected => "rejeitada",
            BatchOutcome.Failed => "erro",
            _ => "pulada",
        };
    }
}
