using System;
using System.Collections.Generic;
using System.Linq;
using FloorplansRevitExtractor.Core.Geometry;
using FloorplansRevitExtractor.Core.Label;
using FloorplansRevitExtractor.Core.Model;
using FloorplansRevitExtractor.Core.Raster;
using FloorplansRevitExtractor.Core.Vocabulary;

namespace FloorplansRevitExtractor.Core.Tests.Label
{
    /// <summary>Key lists copied from a real struct7k_v4 label (labels/000001.json).</summary>
    internal static class DatasetV4
    {
        internal static readonly string[] RootKeys =
        {
            "seed", "params", "width", "height", "built_area", "wall_ext", "wall_int", "envelope", "outline",
            "rooms", "doors", "windows", "furniture", "connectivity_graph", "caption", "render_style",
            "image_size", "transform", "mask_mode", "instances_px", "file",
        };

        internal static readonly string[] ParamsKeys =
        {
            "seed", "n_bedrooms", "n_bathrooms", "total_area", "garage", "style", "building", "thin_partitions",
            "corner_cut", "corner_kind", "wall_pillars", "thin_share", "notch_count", "notch_depth", "notch_width",
            "wall_int_min",
        };

        internal static readonly string[] RenderStyleKeys =
        {
            "scale_den", "ink", "bg", "lw_scale", "lw_jitter", "pen_wall", "pen_open", "pen_furn", "pen_dim",
            "pen_thin", "wall_mode", "hatch_kind", "hatch_angle", "hatch_spacing_m", "hatch_pen", "wall_ink",
            "color_rooms", "font", "lang", "label_mode", "label_uppercase", "text_room_mm", "text_area_mm",
            "text_dim_mm", "text_title_mm", "show_dims", "show_axes", "show_north", "show_scale", "show_title",
            "show_furniture", "show_room_labels", "show_roof", "dims_all_sides", "show_door_codes",
            "show_window_codes", "show_levels", "show_diagonals", "show_axis_lines", "show_lot", "show_pavement",
            "show_legend", "show_entry_steps", "show_sheet_title", "label_color", "room_tint", "room_fill_mode",
            "show_grid_mesh", "show_elec", "show_section_marks", "show_notes", "show_area_table", "aug_grayscale",
            "aug_noise", "aug_blur", "aug_contrast", "aug_brightness", "aug_jpeg", "aug_rotate", "aug_speckle",
        };

        internal static readonly string[] RoomKeys =
        {
            "id", "type", "name", "zone", "class_id", "open", "suite_of", "polygon", "holes", "axis_rects", "area",
            "centroid", "bbox_m",
        };

        internal static readonly string[] DoorKeys =
        {
            "id", "room_a", "room_b", "kind", "position", "rotation", "width", "hinge", "swing_into", "exterior",
        };

        internal static readonly string[] WindowKeys = { "id", "room", "kind", "position", "rotation", "width" };

        internal static readonly string[] FurnitureKeys = { "id", "kind", "room", "position", "rotation", "width", "depth" };

        internal static readonly string[] InstanceKeys =
        {
            "instance_id", "room_id", "type", "name", "class_id", "polygon_px", "bbox_px", "bbox_coco", "bbox_yolo",
            "centroid_px", "center_px", "area_px",
        };

        internal static readonly string[] TransformKeys = { "x0", "y1", "px_per_m", "width", "height", "rotated" };
    }

    internal static class LabelTestData
    {
        // 10 px/m over a 20 x 10 m region.
        internal static readonly PixelTransform Transform = new PixelTransform(0, 10, 10, 200, 100);

        internal static RoomPreviewRecord Room(int id, string? type, double x0, double y0, double x1, double y1, string? name = null)
        {
            IReadOnlyList<Point2D> polygon = new[] { new Point2D(x0, y0), new Point2D(x1, y0), new Point2D(x1, y1), new Point2D(x0, y1) };
            return new RoomPreviewRecord
            {
                Id = id,
                Type = type,
                Name = name ?? (type ?? "DEPÓSITO").ToUpperInvariant(),
                Zone = type == null ? null : RoomZoneTable.GetZone(type),
                ClassId = type == null ? (int?)null : RoomTypeCatalog.GetClassId(type),
                Polygon = polygon,
                Holes = Array.Empty<IReadOnlyList<Point2D>>(),
                AxisRects = Array.Empty<(double, double, double, double)>(),
                Area = PolygonGeometry.Area(polygon),
                Centroid = PolygonGeometry.Centroid(polygon),
                BBoxM = PolygonGeometry.BBox(polygon),
                RevitAreaM2 = PolygonGeometry.Area(polygon),
            };
        }

        internal static SourceFragment Source() => new SourceFragment
        {
            DocTitle = "EDIF-A_ARQ.rvt",
            DocGuid = "a1b2c3",
            RevitVersion = "2024",
            Mode = "level",
            LevelId = 123456,
            LevelName = "TÉRREO",
            LevelElevationM = 0.0,
            ViewId = 234567,
            ViewName = "PLANTA TÉRREO",
            ViewScaleDen = 100,
            ExportedAt = "2026-10-01T10:00:00-03:00",
            PluginVersion = "0.2.0",
            PxPerMTarget = 48.0,
            Alignment = "direto",
        };

        internal static LabelInputs Inputs(
            IReadOnlyList<RoomPreviewRecord>? rooms = null,
            IReadOnlyList<DoorRecord>? doors = null,
            IReadOnlyList<WindowRecord>? windows = null,
            IReadOnlyList<FurnitureRecord>? furniture = null,
            BuildingMeasures? building = null)
        {
            IReadOnlyList<RoomPreviewRecord> roomList = rooms ?? new[]
            {
                Room(0, "bedroom", 1, 1, 5, 5),
                Room(1, "bathroom", 5, 1, 8, 5),
                Room(2, "living_room", 8, 1, 15, 5),
            };

            InstanceMask mask = InstanceMaskPainter.Paint(
                Transform.Width,
                Transform.Height,
                roomList.Select(r => new RoomInstance(r.Id, new[]
                {
                    new PolygonShape(r.Polygon.Select(Transform.ToPixel).ToArray(), Array.Empty<IReadOnlyList<Point2D>>()),
                })).ToArray());

            return new LabelInputs
            {
                Source = Source(),
                Building = building ?? new BuildingMeasures
                {
                    Width = 14.2,
                    Height = 4.2,
                    BuiltArea = 59.64,
                    TotalArea = 64.0,
                    WallExt = 0.2,
                    WallInt = 0.15,
                    WallIntMin = 0.1,
                    ThinPartitions = false,
                    Outline = new[] { new Point2D(0.9, 0.9), new Point2D(15.1, 0.9), new Point2D(15.1, 5.1), new Point2D(0.9, 5.1) },
                },
                Rooms = roomList,
                Doors = doors ?? new[]
                {
                    new DoorRecord(0, 0, 2, "single", new Point2D(5, 3), 90, 0.8, new Point2D(5, 2.6), 0, false),
                    new DoorRecord(1, 2, null, "double", new Point2D(12, 1), 0, 1.6, new Point2D(11.2, 1), 2, true),
                },
                Windows = windows ?? new[] { new WindowRecord(0, 0, "single", new Point2D(3, 1), 0, 1.2) },
                Furniture = furniture ?? new[] { new FurnitureRecord(0, "Bed", 0, new Point2D(3, 3), 90, 1.4, 1.9) },
                RenderStyle = RenderStyle.Empty.With("scale_den", 100).With("lang", "pt"),
                Transform = Transform,
                InstanceMask = mask,
                File = "900001.png",
            };
        }
    }
}
