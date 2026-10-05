using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using FloorplansRevitExtractor.Core.Geometry;
using FloorplansRevitExtractor.Core.Model;
using FloorplansRevitExtractor.Core.Raster;

namespace FloorplansRevitExtractor.Core.Json
{
    /// <summary>
    /// Reads geom/NNNNNN.json strictly: a wrong type, a missing or unknown
    /// key, a non-finite number or an element outside the contract is an
    /// <see cref="InvalidDataException"/> naming its JSON path, never a guess
    /// and never another exception type.
    /// </summary>
    public static class GeomJsonReader
    {
        private static readonly string[] RootKeys = { "view", "transform", "elements", "warnings" };
        private static readonly string[] ViewKeys = { "id", "name", "scale", "cut_plane_m" };
        private static readonly string[] TransformKeys = { "x0", "y1", "px_per_m", "width", "height" };
        private static readonly string[] ElementKeys = { "cls", "revit_id", "polygons", "holes", "polylines", "closed", "stroke_m", "kind", "text" };
        private static readonly string[] WarningKeys = { "revit_id", "msg" };

        public static GeomDocument Read(Stream input)
        {
            using JsonDocument json = ParseOrThrow(input);
            JsonElement root = RequireObject(json.RootElement, "$", RootKeys);

            return new GeomDocument
            {
                View = ReadView(Property(root, "view", "$")),
                Transform = ReadTransform(Property(root, "transform", "$")),
                Elements = RequireArray(Property(root, "elements", "$"), "$.elements")
                    .Select((e, i) => ReadElement(e, $"$.elements[{i}]"))
                    .ToList(),
                Warnings = RequireArray(Property(root, "warnings", "$"), "$.warnings")
                    .Select((w, i) => ReadWarning(w, $"$.warnings[{i}]"))
                    .ToList(),
            };
        }

        private static JsonDocument ParseOrThrow(Stream input)
        {
            try
            {
                return JsonDocument.Parse(input);
            }
            catch (JsonException ex)
            {
                throw new InvalidDataException($"geom JSON is malformed: {ex.Message}", ex);
            }
        }

        private static GeomView ReadView(JsonElement value)
        {
            const string path = "$.view";
            JsonElement view = RequireObject(value, path, ViewKeys);
            return Construct(path, () => new GeomView(
                Int64(Property(view, "id", path), $"{path}.id"),
                NonEmptyString(Property(view, "name", path), $"{path}.name"),
                Int32(Property(view, "scale", path), $"{path}.scale"),
                FiniteDouble(Property(view, "cut_plane_m", path), $"{path}.cut_plane_m")));
        }

        private static PixelTransform ReadTransform(JsonElement value)
        {
            const string path = "$.transform";
            JsonElement t = RequireObject(value, path, TransformKeys);
            return Construct(path, () => new PixelTransform(
                FiniteDouble(Property(t, "x0", path), $"{path}.x0"),
                FiniteDouble(Property(t, "y1", path), $"{path}.y1"),
                FiniteDouble(Property(t, "px_per_m", path), $"{path}.px_per_m"),
                Int32(Property(t, "width", path), $"{path}.width"),
                Int32(Property(t, "height", path), $"{path}.height")));
        }

        private static GeomElement ReadElement(JsonElement value, string path)
        {
            JsonElement e = RequireObject(value, path, ElementKeys);
            string name = NonEmptyString(Property(e, "cls", path), $"{path}.cls");
            if (!StructureClassNames.TryParse(name, out StructureClass cls))
            {
                throw new InvalidDataException($"{path}.cls: unknown class \"{name}\".");
            }

            if (cls == StructureClass.Background)
            {
                throw new InvalidDataException($"{path}.cls: background is never painted and has no geometry.");
            }

            long revitId = Int64(Property(e, "revit_id", path), $"{path}.revit_id");
            string? kind = e.TryGetProperty("kind", out JsonElement kindValue) ? NonEmptyString(kindValue, $"{path}.kind") : null;
            string? text = e.TryGetProperty("text", out JsonElement textValue) ? NonEmptyString(textValue, $"{path}.text") : null;
            bool hasPolygons = e.TryGetProperty("polygons", out JsonElement polygons);
            bool hasHoles = e.TryGetProperty("holes", out JsonElement holes);
            bool hasPolylines = e.TryGetProperty("polylines", out JsonElement polylines);
            bool hasStroke = e.TryGetProperty("stroke_m", out JsonElement stroke);
            bool hasClosed = e.TryGetProperty("closed", out JsonElement closed);
            if (hasClosed && !hasPolylines)
            {
                throw new InvalidDataException($"{path}: closed without polylines.");
            }

            if (hasPolylines)
            {
                if (hasPolygons || hasHoles)
                {
                    throw new InvalidDataException($"{path}: a stroked element has polylines only, not polygons or holes.");
                }

                if (!hasStroke)
                {
                    throw new InvalidDataException($"{path}: polylines without stroke_m.");
                }

                IReadOnlyList<IReadOnlyList<Point2D>> lines = Rings(polylines, $"{path}.polylines", GeomElement.MinPolylinePoints);
                double strokeM = FiniteDouble(stroke, $"{path}.stroke_m");
                bool[]? flags = hasClosed ? RequireArray(closed, $"{path}.closed").Select((c, i) => Bool(c, $"{path}.closed[{i}]")).ToArray() : null;
                return Construct(path, () => GeomElement.Stroked(cls, revitId, lines, strokeM, flags).WithLabel(kind, text));
            }

            if (hasStroke)
            {
                throw new InvalidDataException($"{path}: stroke_m without polylines.");
            }

            if (!hasPolygons)
            {
                throw new InvalidDataException($"{path}: an element needs polygons or polylines.");
            }

            IReadOnlyList<IReadOnlyList<Point2D>> rings = Rings(polygons, $"{path}.polygons", GeomElement.MinPolygonPoints);
            IReadOnlyList<IReadOnlyList<Point2D>>? holeRings = hasHoles
                ? Rings(holes, $"{path}.holes", GeomElement.MinPolygonPoints)
                : null;
            return Construct(path, () => GeomElement.Filled(cls, revitId, rings, holeRings).WithLabel(kind, text));
        }

        private static GeomWarning ReadWarning(JsonElement value, string path)
        {
            JsonElement w = RequireObject(value, path, WarningKeys);
            JsonElement id = Property(w, "revit_id", path);
            long? revitId = id.ValueKind == JsonValueKind.Null ? (long?)null : Int64(id, $"{path}.revit_id");
            string msg = NonEmptyString(Property(w, "msg", path), $"{path}.msg");
            return Construct(path, () => new GeomWarning(revitId, msg));
        }

        private static IReadOnlyList<IReadOnlyList<Point2D>> Rings(JsonElement value, string path, int minPoints) =>
            RequireArray(value, path).Select((ring, i) =>
            {
                string ringPath = $"{path}[{i}]";
                Point2D[] points = RequireArray(ring, ringPath).Select((p, j) => ReadPoint(p, $"{ringPath}[{j}]")).ToArray();
                if (points.Length < minPoints)
                {
                    throw new InvalidDataException($"{ringPath} has {points.Length} points, needs at least {minPoints}.");
                }

                return (IReadOnlyList<Point2D>)points;
            }).ToList();

        private static Point2D ReadPoint(JsonElement p, string path)
        {
            if (p.ValueKind != JsonValueKind.Array || p.GetArrayLength() != 2)
            {
                throw new InvalidDataException($"{path}: every point must be [x, y].");
            }

            return new Point2D(FiniteDouble(p[0], $"{path}[0]"), FiniteDouble(p[1], $"{path}[1]"));
        }

        /// <summary>Turns a constructor's own validation into the reader's single exception type.</summary>
        private static T Construct<T>(string path, Func<T> build)
        {
            try
            {
                return build();
            }
            catch (ArgumentException ex)
            {
                throw new InvalidDataException($"{path}: {ex.Message}", ex);
            }
        }

        private static JsonElement RequireObject(JsonElement value, string path, string[] allowedKeys)
        {
            if (value.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidDataException($"{path} must be an object, got {value.ValueKind}.");
            }

            string? unknown = value.EnumerateObject().Select(p => p.Name).FirstOrDefault(n => !allowedKeys.Contains(n));
            if (unknown != null)
            {
                throw new InvalidDataException($"{path}.{unknown} is not part of the geom contract.");
            }

            return value;
        }

        private static IEnumerable<JsonElement> RequireArray(JsonElement value, string path)
        {
            if (value.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidDataException($"{path} must be an array, got {value.ValueKind}.");
            }

            return value.EnumerateArray();
        }

        private static JsonElement Property(JsonElement parent, string name, string path)
        {
            if (!parent.TryGetProperty(name, out JsonElement value))
            {
                throw new InvalidDataException($"{path}.{name} is missing.");
            }

            return value;
        }

        private static long Int64(JsonElement value, string path) =>
            value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out long result)
                ? result
                : throw new InvalidDataException($"{path} must be an integer, got {value.GetRawText()}.");

        private static int Int32(JsonElement value, string path) =>
            value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int result)
                ? result
                : throw new InvalidDataException($"{path} must be a 32-bit integer, got {value.GetRawText()}.");

        private static double FiniteDouble(JsonElement value, string path) =>
            value.ValueKind == JsonValueKind.Number
                && value.TryGetDouble(out double result)
                && !double.IsNaN(result)
                && !double.IsInfinity(result)
                ? result
                : throw new InvalidDataException($"{path} must be a finite number, got {value.GetRawText()}.");

        private static bool Bool(JsonElement value, string path) =>
            value.ValueKind == JsonValueKind.True || value.ValueKind == JsonValueKind.False
                ? value.GetBoolean()
                : throw new InvalidDataException($"{path} must be true or false, got {value.GetRawText()}.");

        private static string NonEmptyString(JsonElement value, string path)
        {
            string? text = value.ValueKind == JsonValueKind.String ? value.GetString() : null;
            return string.IsNullOrWhiteSpace(text)
                ? throw new InvalidDataException($"{path} must be a non-empty string, got {value.GetRawText()}.")
                : text!;
        }
    }
}
