using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using FloorplansRevitExtractor.Core.Geometry;
using FloorplansRevitExtractor.Core.Label;
using FloorplansRevitExtractor.Core.Model;

namespace FloorplansRevitExtractor.Core.Json
{
    /// <summary>
    /// Writes labels/NNNNNN.json: UTF-8 without a BOM, the training dataset's
    /// key order (struct7k_v4) with source right after seed, meters and
    /// degrees rounded to 3 decimals only here. The transform keeps full
    /// precision: it is what maps meters to pixels.
    /// </summary>
    public static class LabelJsonWriter
    {
        private const double FullTurn = 360.0;

        public static void Write(Stream output, SampleLabel label)
        {
            using var writer = new Utf8JsonWriter(output, JsonFields.Options);
            writer.WriteStartObject();

            writer.WriteNull("seed");
            WriteSource(writer, label.Source);
            writer.WritePropertyName("params");
            JsonFields.WriteParams(writer, label.Params);

            JsonFields.WriteNullable(writer, "width", label.Width);
            JsonFields.WriteNullable(writer, "height", label.Height);
            JsonFields.WriteNullable(writer, "built_area", label.BuiltArea);
            JsonFields.WriteNullable(writer, "wall_ext", label.WallExt);
            JsonFields.WriteNullable(writer, "wall_int", label.WallInt);
            JsonFields.WriteNullable(writer, "envelope", label.Envelope);
            writer.WritePropertyName("outline");
            JsonFields.WriteRing(writer, label.Outline);

            writer.WriteStartArray("rooms");
            foreach (RoomPreviewRecord room in label.Rooms)
            {
                JsonFields.WriteRoom(writer, room);
            }

            writer.WriteEndArray();

            WriteArray(writer, "doors", label.Doors, WriteDoor);
            WriteArray(writer, "windows", label.Windows, WriteWindow);
            WriteArray(writer, "furniture", label.Furniture, WriteFurniture);
            WriteGraph(writer, label.ConnectivityGraph);
            writer.WriteString("caption", label.Caption);
            WriteRenderStyle(writer, label.RenderStyle);

            writer.WriteStartArray("image_size");
            writer.WriteNumberValue(label.ImageSize.Width);
            writer.WriteNumberValue(label.ImageSize.Height);
            writer.WriteEndArray();

            WriteTransform(writer, label.Transform);
            writer.WriteString("mask_mode", SampleLabel.MaskMode);
            WriteArray(writer, "instances_px", label.InstancesPx, WriteInstance);
            writer.WriteString("file", label.File);

            writer.WriteEndObject();
            writer.Flush();
        }

        private static void WriteSource(Utf8JsonWriter writer, SourceFragment source)
        {
            writer.WriteStartObject("source");
            JsonFields.WriteSourceFields(writer, source);
            JsonFields.WriteNullable(writer, "px_per_m_alvo", source.PxPerMTarget);
            JsonFields.WriteNullable(writer, "alinhamento", source.Alignment);
            writer.WriteEndObject();
        }

        private static void WriteDoor(Utf8JsonWriter writer, DoorRecord door)
        {
            writer.WriteNumber("id", door.Id);
            JsonFields.WriteNullable(writer, "room_a", door.RoomA);
            JsonFields.WriteNullable(writer, "room_b", door.RoomB);
            writer.WriteString("kind", door.Kind);
            writer.WritePropertyName("position");
            JsonFields.WritePoint(writer, door.Position);
            WriteRotation(writer, door.Rotation);
            JsonFields.WriteRounded(writer, "width", door.Width);
            JsonFields.WriteNullablePoint(writer, "hinge", door.Hinge);
            JsonFields.WriteNullable(writer, "swing_into", door.SwingInto);
            writer.WriteBoolean("exterior", door.Exterior);
        }

        private static void WriteWindow(Utf8JsonWriter writer, WindowRecord window)
        {
            writer.WriteNumber("id", window.Id);
            JsonFields.WriteNullable(writer, "room", window.Room);
            writer.WriteString("kind", window.Kind);
            writer.WritePropertyName("position");
            JsonFields.WritePoint(writer, window.Position);
            WriteRotation(writer, window.Rotation);
            JsonFields.WriteRounded(writer, "width", window.Width);
        }

        private static void WriteFurniture(Utf8JsonWriter writer, FurnitureRecord item)
        {
            writer.WriteNumber("id", item.Id);
            writer.WriteString("kind", item.Kind);
            JsonFields.WriteNullable(writer, "room", item.Room);
            writer.WritePropertyName("position");
            JsonFields.WritePoint(writer, item.Position);
            WriteRotation(writer, item.Rotation);
            JsonFields.WriteRounded(writer, "width", item.Width);
            JsonFields.WriteRounded(writer, "depth", item.Depth);
        }

        /// <summary>Rounding can turn 359.9996 into 360; the label keeps [0, 360).</summary>
        private static void WriteRotation(Utf8JsonWriter writer, double degrees)
        {
            double rounded = PythonRounding.Round(degrees, JsonFields.MeterDecimals);
            JsonFields.WriteFloat(writer, "rotation", rounded >= FullTurn ? rounded - FullTurn : rounded);
        }

        private static void WriteGraph(Utf8JsonWriter writer, ConnectivityGraph graph)
        {
            writer.WriteStartObject("connectivity_graph");
            WriteArray(writer, "nodes", graph.Nodes, (w, node) =>
            {
                w.WriteNumber("id", node.Id);
                JsonFields.WriteNullable(w, "type", node.Type);
                w.WriteString("name", node.Name);
            });
            WriteArray(writer, "edges", graph.Edges, (w, edge) =>
            {
                w.WriteNumber("source", edge.Source);
                w.WriteNumber("target", edge.Target);
                w.WriteNumber("door_id", edge.DoorId);
                w.WriteString("kind", edge.Kind);
            });
            writer.WriteStartArray("external_doors");
            foreach (int doorId in graph.ExternalDoors)
            {
                writer.WriteNumberValue(doorId);
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        private static void WriteRenderStyle(Utf8JsonWriter writer, RenderStyle style)
        {
            writer.WriteStartObject("render_style");
            foreach (string key in style.Keys)
            {
                switch (style[key])
                {
                    case null:
                        writer.WriteNull(key);
                        break;
                    case bool b:
                        writer.WriteBoolean(key, b);
                        break;
                    case int i:
                        writer.WriteNumber(key, i);
                        break;
                    case double d:
                        JsonFields.WriteFloat(writer, key, d);
                        break;
                    case string s:
                        writer.WriteString(key, s);
                        break;
                    case int[] rgb:
                        WriteInts(writer, key, rgb);
                        break;
                }
            }

            writer.WriteEndObject();
        }

        private static void WriteTransform(Utf8JsonWriter writer, PixelTransform t)
        {
            writer.WriteStartObject("transform");
            JsonFields.WriteFloat(writer, "x0", t.X0);
            JsonFields.WriteFloat(writer, "y1", t.Y1);
            JsonFields.WriteFloat(writer, "px_per_m", t.PxPerM);
            writer.WriteNumber("width", t.Width);
            writer.WriteNumber("height", t.Height);
            writer.WriteBoolean("rotated", false);
            writer.WriteEndObject();
        }

        /// <summary>The pixel fields are already rounded the generator's way; written as they are.</summary>
        private static void WriteInstance(Utf8JsonWriter writer, InstancePx px)
        {
            writer.WriteNumber("instance_id", px.InstanceId);
            writer.WriteNumber("room_id", px.RoomId);
            JsonFields.WriteNullable(writer, "type", px.Type);
            writer.WriteString("name", px.Name);
            JsonFields.WriteNullable(writer, "class_id", px.ClassId);

            writer.WriteStartArray("polygon_px");
            foreach (Point2D p in px.PolygonPx)
            {
                writer.WriteStartArray();
                JsonFields.WriteFloat(writer, p.X);
                JsonFields.WriteFloat(writer, p.Y);
                writer.WriteEndArray();
            }

            writer.WriteEndArray();
            WriteInts(writer, "bbox_px", px.BBoxPx.X0, px.BBoxPx.Y0, px.BBoxPx.X1, px.BBoxPx.Y1);
            WriteInts(writer, "bbox_coco", px.BBoxCoco.X, px.BBoxCoco.Y, px.BBoxCoco.W, px.BBoxCoco.H);

            writer.WriteStartArray("bbox_yolo");
            JsonFields.WriteFloat(writer, px.BBoxYolo.XC);
            JsonFields.WriteFloat(writer, px.BBoxYolo.YC);
            JsonFields.WriteFloat(writer, px.BBoxYolo.W);
            JsonFields.WriteFloat(writer, px.BBoxYolo.H);
            writer.WriteEndArray();

            writer.WriteStartArray("centroid_px");
            JsonFields.WriteFloat(writer, px.CentroidPx.X);
            JsonFields.WriteFloat(writer, px.CentroidPx.Y);
            writer.WriteEndArray();

            WriteInts(writer, "center_px", px.CenterPx.X, px.CenterPx.Y);
            writer.WriteNumber("area_px", px.AreaPx);
        }

        private static void WriteInts(Utf8JsonWriter writer, string name, params int[] values)
        {
            writer.WriteStartArray(name);
            foreach (int v in values)
            {
                writer.WriteNumberValue(v);
            }

            writer.WriteEndArray();
        }

        private static void WriteArray<T>(
            Utf8JsonWriter writer,
            string name,
            IReadOnlyList<T> items,
            Action<Utf8JsonWriter, T> writeFields)
        {
            writer.WriteStartArray(name);
            foreach (T item in items)
            {
                writer.WriteStartObject();
                writeFields(writer, item);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
        }
    }
}
