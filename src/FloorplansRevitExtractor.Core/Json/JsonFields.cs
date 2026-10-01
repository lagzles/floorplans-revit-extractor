using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using FloorplansRevitExtractor.Core.Geometry;
using FloorplansRevitExtractor.Core.Label;
using FloorplansRevitExtractor.Core.Model;

namespace FloorplansRevitExtractor.Core.Json
{
    /// <summary>
    /// Field writers shared by the label and the rooms preview, so both write
    /// source, params and rooms identically: v1 key order, meters rounded to
    /// 3 decimals only here (never mid-calculation), half to even like the
    /// generator's Python round().
    /// </summary>
    internal static class JsonFields
    {
        internal const int MeterDecimals = 3;

        /// <summary>Below this, a whole double converts to long exactly.</summary>
        private const double WholeNumberLimit = 1e15;

        internal static readonly JsonWriterOptions Options = new JsonWriterOptions
        {
            Indented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        /// <summary>The v1 section 3.1 fields, without the surrounding object.</summary>
        internal static void WriteSourceFields(Utf8JsonWriter writer, SourceFragment source)
        {
            writer.WriteString("doc_title", source.DocTitle);
            writer.WriteString("doc_guid", source.DocGuid);
            writer.WriteString("revit_version", source.RevitVersion);
            writer.WriteString("mode", source.Mode);
            writer.WriteNumber("level_id", source.LevelId);
            writer.WriteString("level_name", source.LevelName);
            WriteRounded(writer, "level_elevation_m", source.LevelElevationM);
            WriteNullable(writer, "view_id", source.ViewId);
            WriteNullable(writer, "view_name", source.ViewName);
            WriteNullable(writer, "view_scale_den", source.ViewScaleDen);
            WriteNullable(writer, "sheet_number", source.SheetNumber);
            writer.WriteString("exported_at", source.ExportedAt);
            writer.WriteString("plugin_version", source.PluginVersion);
        }

        /// <summary>params in the training dataset's key order (struct7k_v4).</summary>
        internal static void WriteParams(Utf8JsonWriter writer, ParamsFragment p)
        {
            writer.WriteStartObject();
            WriteNullable(writer, "seed", p.Seed);
            writer.WriteNumber("n_bedrooms", p.NBedrooms);
            writer.WriteNumber("n_bathrooms", p.NBathrooms);
            WriteRounded(writer, "total_area", p.TotalArea);
            writer.WriteBoolean("garage", p.Garage);
            WriteNullable(writer, "style", p.Style);
            WriteNullable(writer, "building", p.Building);
            WriteNullable(writer, "thin_partitions", p.ThinPartitions);
            WriteNullable(writer, "corner_cut", p.CornerCut);
            WriteNullable(writer, "corner_kind", p.CornerKind);
            WriteNullable(writer, "wall_pillars", p.WallPillars);
            WriteNullable(writer, "thin_share", p.ThinShare);
            WriteNullable(writer, "notch_count", p.NotchCount);
            WriteNullable(writer, "notch_depth", p.NotchDepth);
            WriteNullable(writer, "notch_width", p.NotchWidth);
            WriteNullable(writer, "wall_int_min", p.WallIntMin);
            writer.WriteEndObject();
        }

        internal static void WriteRoom(Utf8JsonWriter writer, RoomPreviewRecord room)
        {
            writer.WriteStartObject();
            writer.WriteNumber("id", room.Id);
            WriteNullable(writer, "type", room.Type);
            writer.WriteString("name", room.Name);
            WriteNullable(writer, "zone", room.Zone);
            WriteNullable(writer, "class_id", room.ClassId);
            writer.WriteBoolean("open", room.Open);
            WriteNullable(writer, "suite_of", room.SuiteOf);

            writer.WritePropertyName("polygon");
            WriteRing(writer, room.Polygon);

            writer.WriteStartArray("holes");
            foreach (IReadOnlyList<Point2D> hole in room.Holes)
            {
                WriteRing(writer, hole);
            }

            writer.WriteEndArray();

            writer.WriteStartArray("axis_rects");
            foreach ((double x, double y, double w, double h) in room.AxisRects)
            {
                writer.WriteStartArray();
                WriteRounded(writer, x);
                WriteRounded(writer, y);
                WriteRounded(writer, w);
                WriteRounded(writer, h);
                writer.WriteEndArray();
            }

            writer.WriteEndArray();

            WriteRounded(writer, "area", room.Area);
            writer.WritePropertyName("centroid");
            WritePoint(writer, room.Centroid);

            writer.WriteStartArray("bbox_m");
            WriteRounded(writer, room.BBoxM.MinX);
            WriteRounded(writer, room.BBoxM.MinY);
            WriteRounded(writer, room.BBoxM.MaxX);
            WriteRounded(writer, room.BBoxM.MaxY);
            writer.WriteEndArray();

            writer.WriteEndObject();
        }

        internal static void WriteRing(Utf8JsonWriter writer, IReadOnlyList<Point2D> ring, int decimals = MeterDecimals)
        {
            writer.WriteStartArray();
            foreach (Point2D p in ring)
            {
                WritePoint(writer, p, decimals);
            }

            writer.WriteEndArray();
        }

        internal static void WritePoint(Utf8JsonWriter writer, Point2D p, int decimals = MeterDecimals)
        {
            writer.WriteStartArray();
            WriteRounded(writer, p.X, decimals);
            WriteRounded(writer, p.Y, decimals);
            writer.WriteEndArray();
        }

        internal static void WriteNullablePoint(Utf8JsonWriter writer, string propertyName, Point2D? p)
        {
            writer.WritePropertyName(propertyName);
            if (p.HasValue)
            {
                WritePoint(writer, p.Value);
            }
            else
            {
                writer.WriteNullValue();
            }
        }

        /// <summary>
        /// The rounded value as its short decimal text ("0.8", "14.2", "90.0").
        /// .NET Framework would write 17 digits (0.80000000000000004); the
        /// rounded double is the nearest one to that short decimal, so Python
        /// reads the short text back to the very same double.
        /// </summary>
        internal static void WriteRounded(Utf8JsonWriter writer, double value, int decimals = MeterDecimals) =>
            writer.WriteRawValue(FixedText(PythonRounding.Round(value, decimals), decimals));

        internal static void WriteRounded(Utf8JsonWriter writer, string propertyName, double value, int decimals = MeterDecimals)
        {
            writer.WritePropertyName(propertyName);
            WriteRounded(writer, value, decimals);
        }

        private static string FixedText(double rounded, int decimals)
        {
            string text = rounded.ToString("F" + decimals.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
            if (text.IndexOf('.') >= 0)
            {
                text = text.TrimEnd('0');
            }

            return text.EndsWith(".", StringComparison.Ordinal) ? text + "0" : text.IndexOf('.') < 0 ? text + ".0" : text;
        }

        /// <summary>
        /// A double always reads back as a float in Python: 90.0, never 90. Python
        /// parses 90 as int, numpy then builds an int64 array from such points, and
        /// an in-place float operation on it truncates without an error.
        /// </summary>
        internal static void WriteFloat(Utf8JsonWriter writer, double value)
        {
            if (value == Math.Floor(value) && Math.Abs(value) < WholeNumberLimit)
            {
                writer.WriteRawValue(((long)value).ToString(CultureInfo.InvariantCulture) + ".0");
            }
            else
            {
                writer.WriteNumberValue(value);
            }
        }

        internal static void WriteFloat(Utf8JsonWriter writer, string propertyName, double value)
        {
            writer.WritePropertyName(propertyName);
            WriteFloat(writer, value);
        }

        internal static void WriteNullable(Utf8JsonWriter writer, string propertyName, double? value, int decimals = MeterDecimals)
        {
            if (value.HasValue)
            {
                WriteRounded(writer, propertyName, value.Value, decimals);
            }
            else
            {
                writer.WriteNull(propertyName);
            }
        }

        internal static void WriteNullable(Utf8JsonWriter writer, string propertyName, long? value)
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

        internal static void WriteNullable(Utf8JsonWriter writer, string propertyName, int? value) =>
            WriteNullable(writer, propertyName, (long?)value);

        internal static void WriteNullable(Utf8JsonWriter writer, string propertyName, string? value)
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

        internal static void WriteNullable(Utf8JsonWriter writer, string propertyName, bool? value)
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
