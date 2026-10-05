using System.Collections.Generic;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using FloorplansRevitExtractor.Core.Geometry;
using FloorplansRevitExtractor.Core.Model;
using FloorplansRevitExtractor.Core.Raster;

namespace FloorplansRevitExtractor.Core.Json
{
    /// <summary>
    /// Writes geom/NNNNNN.json as UTF-8 without a BOM, in the contract's key
    /// order. Unlike the label (3 decimals), coordinates keep full precision:
    /// geom/ is the Core's regression input, and a mask painted from the file
    /// must equal the one painted in memory.
    /// </summary>
    public static class GeomJsonWriter
    {
        public static void Write(Stream output, GeomDocument document)
        {
            var options = new JsonWriterOptions
            {
                Indented = true,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            };

            using var writer = new Utf8JsonWriter(output, options);
            writer.WriteStartObject();

            writer.WriteStartObject("view");
            writer.WriteNumber("id", document.View.Id);
            writer.WriteString("name", document.View.Name);
            writer.WriteNumber("scale", document.View.Scale);
            WriteExact(writer, "cut_plane_m", document.View.CutPlaneM);
            writer.WriteEndObject();

            writer.WriteStartObject("transform");
            WriteExact(writer, "x0", document.Transform.X0);
            WriteExact(writer, "y1", document.Transform.Y1);
            WriteExact(writer, "px_per_m", document.Transform.PxPerM);
            writer.WriteNumber("width", document.Transform.Width);
            writer.WriteNumber("height", document.Transform.Height);
            writer.WriteEndObject();

            writer.WriteStartArray("elements");
            foreach (GeomElement element in document.Elements)
            {
                WriteElement(writer, element);
            }

            writer.WriteEndArray();

            writer.WriteStartArray("warnings");
            foreach (GeomWarning warning in document.Warnings)
            {
                writer.WriteStartObject();
                if (warning.RevitId.HasValue)
                {
                    writer.WriteNumber("revit_id", warning.RevitId.Value);
                }
                else
                {
                    writer.WriteNull("revit_id");
                }

                writer.WriteString("msg", warning.Msg);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
            writer.Flush();
        }

        private static void WriteElement(Utf8JsonWriter writer, GeomElement element)
        {
            writer.WriteStartObject();
            writer.WriteString("cls", StructureClassNames.ToName(element.Class));
            writer.WriteNumber("revit_id", element.RevitId);

            if (element.Polygons.Count > 0)
            {
                WriteRings(writer, "polygons", element.Polygons);
            }

            if (element.Holes.Count > 0)
            {
                WriteRings(writer, "holes", element.Holes);
            }

            if (element.Polylines.Count > 0)
            {
                WriteRings(writer, "polylines", element.Polylines);
                if (element.Class == StructureClass.Grid || element.Class == StructureClass.Dimension || element.Class == StructureClass.LowWall)
                {
                    // Spec v3/v4: annotation and guardrail strokes say which polylines close (the grid bubble).
                    writer.WriteStartArray("closed");
                    foreach (bool closed in element.Closed)
                    {
                        writer.WriteBooleanValue(closed);
                    }

                    writer.WriteEndArray();
                }

                WriteExact(writer, "stroke_m", element.StrokeM!.Value);
            }

            if (element.Kind != null)
            {
                writer.WriteString("kind", element.Kind);
            }

            if (element.Text != null)
            {
                writer.WriteString("text", element.Text);
            }

            writer.WriteEndObject();
        }

        private static void WriteRings(Utf8JsonWriter writer, string name, IReadOnlyList<IReadOnlyList<Point2D>> rings)
        {
            writer.WriteStartArray(name);
            foreach (IReadOnlyList<Point2D> ring in rings)
            {
                writer.WriteStartArray();
                foreach (Point2D p in ring)
                {
                    writer.WriteStartArray();
                    WriteExact(writer, p.X);
                    WriteExact(writer, p.Y);
                    writer.WriteEndArray();
                }

                writer.WriteEndArray();
            }

            writer.WriteEndArray();
        }

        private static void WriteExact(Utf8JsonWriter writer, string name, double value)
        {
            writer.WritePropertyName(name);
            WriteExact(writer, value);
        }

        /// <summary>
        /// Utf8JsonWriter emits the shortest round-trippable text (the tests pin
        /// that down). Values are finite: the model types reject NaN and infinity.
        /// </summary>
        private static void WriteExact(Utf8JsonWriter writer, double value) => writer.WriteNumberValue(value);
    }
}
