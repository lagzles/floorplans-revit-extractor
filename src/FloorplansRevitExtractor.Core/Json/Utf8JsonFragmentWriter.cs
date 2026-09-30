using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using FloorplansRevitExtractor.Core.Geometry;
using FloorplansRevitExtractor.Core.Model;

namespace FloorplansRevitExtractor.Core.Json
{
    /// <summary>
    /// Writes the rooms-preview debug document as UTF-8 without a BOM, with
    /// key order matching the struct7k spec exactly (Utf8JsonWriter emits
    /// properties in the order they're written, not reflection order), and
    /// 3-decimal rounding applied only here, never earlier in the pipeline.
    /// </summary>
    public static class Utf8JsonFragmentWriter
    {
        public static void Write(Stream output, RoomsPreviewDocument document)
        {
            var options = new JsonWriterOptions
            {
                Indented = true,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            };

            using var writer = new Utf8JsonWriter(output, options);
            writer.WriteStartObject();

            writer.WritePropertyName("source");
            WriteSource(writer, document.Source);

            writer.WritePropertyName("params");
            WriteParams(writer, document.Params);

            writer.WritePropertyName("rooms");
            writer.WriteStartArray();
            foreach (RoomPreviewRecord room in document.Rooms)
            {
                WriteRoom(writer, room);
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
            writer.WriteString("doc_title", source.DocTitle);
            writer.WriteString("doc_guid", source.DocGuid);
            writer.WriteString("revit_version", source.RevitVersion);
            writer.WriteString("mode", source.Mode);
            writer.WriteNumber("level_id", source.LevelId);
            writer.WriteString("level_name", source.LevelName);
            WriteRoundedNumber(writer, "level_elevation_m", source.LevelElevationM);
            WriteNullableNumber(writer, "view_id", source.ViewId);
            WriteNullableString(writer, "view_name", source.ViewName);
            WriteNullableNumber(writer, "view_scale_den", source.ViewScaleDen);
            WriteNullableString(writer, "sheet_number", source.SheetNumber);
            writer.WriteString("exported_at", source.ExportedAt);
            writer.WriteString("plugin_version", source.PluginVersion);
            writer.WriteEndObject();
        }

        private static void WriteParams(Utf8JsonWriter writer, ParamsFragment p)
        {
            writer.WriteStartObject();
            WriteNullableNumber(writer, "seed", p.Seed);
            writer.WriteNumber("n_bedrooms", p.NBedrooms);
            writer.WriteNumber("n_bathrooms", p.NBathrooms);
            WriteRoundedNumber(writer, "total_area", p.TotalArea);
            writer.WriteBoolean("garage", p.Garage);
            WriteNullableString(writer, "style", p.Style);
            WriteNullableString(writer, "building", p.Building);
            WriteNullableBoolean(writer, "thin_partitions", p.ThinPartitions);
            WriteNullableString(writer, "corner_cut", p.CornerCut);
            WriteNullableString(writer, "corner_kind", p.CornerKind);
            WriteNullableNumber(writer, "wall_int_min", p.WallIntMin, decimals: 3);
            writer.WriteEndObject();
        }

        private static void WriteRoom(Utf8JsonWriter writer, RoomPreviewRecord room)
        {
            writer.WriteStartObject();
            writer.WriteNumber("id", room.Id);
            WriteNullableString(writer, "type", room.Type);
            writer.WriteString("name", room.Name);
            WriteNullableString(writer, "zone", room.Zone);
            WriteNullableNumber(writer, "class_id", room.ClassId);
            writer.WriteBoolean("open", room.Open);
            WriteNullableNumber(writer, "suite_of", room.SuiteOf);

            writer.WritePropertyName("polygon");
            WriteRing(writer, room.Polygon);

            writer.WritePropertyName("holes");
            writer.WriteStartArray();
            foreach (IReadOnlyList<Point2D> hole in room.Holes)
            {
                WriteRing(writer, hole);
            }

            writer.WriteEndArray();

            writer.WritePropertyName("axis_rects");
            writer.WriteStartArray();
            foreach ((double x, double y, double w, double h) in room.AxisRects)
            {
                writer.WriteStartArray();
                WriteRoundedNumber(writer, x);
                WriteRoundedNumber(writer, y);
                WriteRoundedNumber(writer, w);
                WriteRoundedNumber(writer, h);
                writer.WriteEndArray();
            }

            writer.WriteEndArray();

            WriteRoundedNumber(writer, "area", room.Area);
            writer.WritePropertyName("centroid");
            WritePoint(writer, room.Centroid);

            writer.WritePropertyName("bbox_m");
            writer.WriteStartArray();
            WriteRoundedNumber(writer, room.BBoxM.MinX);
            WriteRoundedNumber(writer, room.BBoxM.MinY);
            WriteRoundedNumber(writer, room.BBoxM.MaxX);
            WriteRoundedNumber(writer, room.BBoxM.MaxY);
            writer.WriteEndArray();

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
                WriteRoundedNumber(writer, "polygon_area_m2", w.PolygonAreaM2);
                WriteRoundedNumber(writer, "revit_area_m2", w.RevitAreaM2);
                WriteRoundedNumber(writer, "diff_pct", w.DiffPct);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();

            writer.WriteEndObject();
        }

        private static void WriteRing(Utf8JsonWriter writer, IReadOnlyList<Point2D> ring)
        {
            writer.WriteStartArray();
            foreach (Point2D p in ring)
            {
                WritePoint(writer, p);
            }

            writer.WriteEndArray();
        }

        private static void WritePoint(Utf8JsonWriter writer, Point2D p)
        {
            writer.WriteStartArray();
            WriteRoundedNumber(writer, p.X);
            WriteRoundedNumber(writer, p.Y);
            writer.WriteEndArray();
        }

        private static void WriteRoundedNumber(Utf8JsonWriter writer, double value, int decimals = 3) =>
            writer.WriteNumberValue(Math.Round(value, decimals, MidpointRounding.AwayFromZero));

        private static void WriteRoundedNumber(Utf8JsonWriter writer, string propertyName, double value, int decimals = 3) =>
            writer.WriteNumber(propertyName, Math.Round(value, decimals, MidpointRounding.AwayFromZero));

        private static void WriteNullableNumber(Utf8JsonWriter writer, string propertyName, double? value, int decimals = 3)
        {
            if (value.HasValue)
            {
                WriteRoundedNumber(writer, propertyName, value.Value, decimals);
            }
            else
            {
                writer.WriteNull(propertyName);
            }
        }

        private static void WriteNullableNumber(Utf8JsonWriter writer, string propertyName, long? value)
        {
            if (value.HasValue)
            {
                writer.WriteNumber(propertyName, value.Value);
            }
            else
            {
                writer.WriteNull(propertyName);
            }
        }

        private static void WriteNullableNumber(Utf8JsonWriter writer, string propertyName, int? value)
        {
            if (value.HasValue)
            {
                writer.WriteNumber(propertyName, value.Value);
            }
            else
            {
                writer.WriteNull(propertyName);
            }
        }

        private static void WriteNullableString(Utf8JsonWriter writer, string propertyName, string? value)
        {
            if (value is not null)
            {
                writer.WriteString(propertyName, value);
            }
            else
            {
                writer.WriteNull(propertyName);
            }
        }

        private static void WriteNullableBoolean(Utf8JsonWriter writer, string propertyName, bool? value)
        {
            if (value.HasValue)
            {
                writer.WriteBoolean(propertyName, value.Value);
            }
            else
            {
                writer.WriteNull(propertyName);
            }
        }
    }
}
