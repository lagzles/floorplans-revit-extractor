using System.IO;
using System.Text.Json;
using FloorplansRevitExtractor.Core.Model;

namespace FloorplansRevitExtractor.Core.Json
{
    /// <summary>
    /// Writes the rooms-preview debug document as UTF-8 without a BOM, with
    /// key order matching the struct7k spec exactly (Utf8JsonWriter emits
    /// properties in the order they're written, not reflection order).
    /// Source, params and rooms come from <see cref="JsonFields"/>, shared
    /// with the label writer.
    /// </summary>
    public static class Utf8JsonFragmentWriter
    {
        public static void Write(Stream output, RoomsPreviewDocument document)
        {
            using var writer = new Utf8JsonWriter(output, JsonFields.Options);
            writer.WriteStartObject();

            writer.WritePropertyName("source");
            WriteSource(writer, document.Source);

            writer.WritePropertyName("params");
            JsonFields.WriteParams(writer, document.Params);

            writer.WritePropertyName("rooms");
            writer.WriteStartArray();
            foreach (RoomPreviewRecord room in document.Rooms)
            {
                JsonFields.WriteRoom(writer, room);
            }

            writer.WriteEndArray();

            writer.WritePropertyName("debug");
            WriteDebug(writer, document.Debug);

            writer.WriteEndObject();
            writer.Flush();
        }

        private static void WriteSource(Utf8JsonWriter writer, SourceFragment source)
        {
            writer.WriteStartObject();
            JsonFields.WriteSourceFields(writer, source);
            writer.WriteEndObject();
        }

        private static void WriteDebug(Utf8JsonWriter writer, DebugFragment debug)
        {
            writer.WriteStartObject();

            writer.WritePropertyName("discarded_rooms");
            writer.WriteStartArray();
            foreach (DiscardedRoom d in debug.DiscardedRooms)
            {
                writer.WriteStartObject();
                writer.WriteNumber("element_id", d.ElementId);
                writer.WriteString("reason", d.Reason);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();

            writer.WritePropertyName("unmapped_names");
            writer.WriteStartArray();
            foreach (UnmappedRoomName u in debug.UnmappedNames)
            {
                writer.WriteStartObject();
                writer.WriteNumber("element_id", u.ElementId);
                writer.WriteString("raw_name", u.RawName);
                writer.WriteString("normalized_name", u.NormalizedName);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();

            writer.WritePropertyName("area_mismatch_warnings");
            writer.WriteStartArray();
            foreach (AreaMismatchWarning w in debug.AreaMismatchWarnings)
            {
                writer.WriteStartObject();
                writer.WriteNumber("room_id", w.RoomId);
                JsonFields.WriteRounded(writer, "polygon_area_m2", w.PolygonAreaM2);
                JsonFields.WriteRounded(writer, "revit_area_m2", w.RevitAreaM2);
                JsonFields.WriteRounded(writer, "diff_pct", w.DiffPct);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();

            writer.WriteEndObject();
        }
    }
}
