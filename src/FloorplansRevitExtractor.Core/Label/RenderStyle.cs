using System;
using System.Collections.Generic;
using System.Linq;

namespace FloorplansRevitExtractor.Core.Label
{
    /// <summary>
    /// The label's render_style: the same keys as the training dataset's
    /// labels, nothing more, nothing less, each with the generator's type.
    /// For a Revit sample it describes what the exported view really shows;
    /// generator knobs the view has no equivalent for (ink, pen_*, aug_*...)
    /// stay null. Immutable: <see cref="With"/> returns a new style.
    /// </summary>
    public sealed class RenderStyle
    {
        private enum ValueKind
        {
            Int,
            Float,
            Bool,
            Text,
            Rgb,
        }

        /// <summary>
        /// Keys and order from a struct7k_v4 label; types from the generator's
        /// style.py (hatch_* and show_room_labels from the v4 values). Spec v2
        /// asks for the struct7k_v7 keys: replace this table when a v7 label is
        /// available.
        /// </summary>
        private static readonly (string Key, ValueKind Kind)[] Schema =
        {
            ("scale_den", ValueKind.Int), ("ink", ValueKind.Float), ("bg", ValueKind.Float),
            ("lw_scale", ValueKind.Float), ("lw_jitter", ValueKind.Float), ("pen_wall", ValueKind.Float),
            ("pen_open", ValueKind.Float), ("pen_furn", ValueKind.Float), ("pen_dim", ValueKind.Float),
            ("pen_thin", ValueKind.Float), ("wall_mode", ValueKind.Text), ("hatch_kind", ValueKind.Text),
            ("hatch_angle", ValueKind.Float), ("hatch_spacing_m", ValueKind.Float), ("hatch_pen", ValueKind.Float),
            ("wall_ink", ValueKind.Float), ("color_rooms", ValueKind.Bool), ("font", ValueKind.Text),
            ("lang", ValueKind.Text), ("label_mode", ValueKind.Text), ("label_uppercase", ValueKind.Bool),
            ("text_room_mm", ValueKind.Float), ("text_area_mm", ValueKind.Float), ("text_dim_mm", ValueKind.Float),
            ("text_title_mm", ValueKind.Float), ("show_dims", ValueKind.Bool), ("show_axes", ValueKind.Bool),
            ("show_north", ValueKind.Bool), ("show_scale", ValueKind.Bool), ("show_title", ValueKind.Bool),
            ("show_furniture", ValueKind.Bool), ("show_room_labels", ValueKind.Bool), ("show_roof", ValueKind.Bool),
            ("dims_all_sides", ValueKind.Bool), ("show_door_codes", ValueKind.Bool), ("show_window_codes", ValueKind.Bool),
            ("show_levels", ValueKind.Bool), ("show_diagonals", ValueKind.Bool), ("show_axis_lines", ValueKind.Bool),
            ("show_lot", ValueKind.Bool), ("show_pavement", ValueKind.Bool), ("show_legend", ValueKind.Bool),
            ("show_entry_steps", ValueKind.Bool), ("show_sheet_title", ValueKind.Bool), ("label_color", ValueKind.Rgb),
            ("room_tint", ValueKind.Float), ("room_fill_mode", ValueKind.Text), ("show_grid_mesh", ValueKind.Bool),
            ("show_elec", ValueKind.Bool), ("show_section_marks", ValueKind.Bool), ("show_notes", ValueKind.Bool),
            ("show_area_table", ValueKind.Bool), ("aug_grayscale", ValueKind.Bool), ("aug_noise", ValueKind.Float),
            ("aug_blur", ValueKind.Float), ("aug_contrast", ValueKind.Float), ("aug_brightness", ValueKind.Float),
            ("aug_jpeg", ValueKind.Int), ("aug_rotate", ValueKind.Float), ("aug_speckle", ValueKind.Float),
        };

        private static readonly string[] KeyOrder = Schema.Select(s => s.Key).ToArray();

        private static readonly Dictionary<string, int> IndexByKey =
            KeyOrder.Select((key, i) => (key, i)).ToDictionary(p => p.key, p => p.i, StringComparer.Ordinal);

        private readonly object?[] _values;

        private RenderStyle(object?[] values)
        {
            _values = values;
        }

        public static RenderStyle Empty { get; } = new RenderStyle(new object?[KeyOrder.Length]);

        public IReadOnlyList<string> Keys => KeyOrder;

        /// <summary>null, or the key's type: int, double, bool, string, or an int[3] RGB for label_color.</summary>
        public object? this[string key] => _values[IndexOf(key)] is int[] rgb ? rgb.ToArray() : _values[IndexOf(key)];

        public RenderStyle With(string key, object? value)
        {
            int index = IndexOf(key);
            var copy = (object?[])_values.Clone();
            copy[index] = value == null ? null : Coerce(key, Schema[index].Kind, value);
            return new RenderStyle(copy);
        }

        private static object Coerce(string key, ValueKind kind, object value)
        {
            switch (kind)
            {
                case ValueKind.Int when value is int:
                case ValueKind.Bool when value is bool:
                case ValueKind.Text when value is string:
                    return value;
                case ValueKind.Float when value is int i:
                    return (double)i;
                case ValueKind.Float when value is double d && !double.IsNaN(d) && !double.IsInfinity(d):
                    return d;
                case ValueKind.Rgb when value is int[] rgb && rgb.Length == 3 && rgb.All(c => c >= 0 && c <= 255):
                    return rgb.ToArray();
                default:
                    throw new ArgumentException(
                        $"render_style.{key} takes {Describe(kind)}, got {value} ({value.GetType().Name}).", nameof(value));
            }
        }

        private static string Describe(ValueKind kind) => kind switch
        {
            ValueKind.Int => "an int",
            ValueKind.Float => "a finite number",
            ValueKind.Bool => "a bool",
            ValueKind.Text => "a string",
            _ => "an [r, g, b] array of 0-255 ints",
        };

        private static int IndexOf(string key) =>
            IndexByKey.TryGetValue(key, out int index)
                ? index
                : throw new ArgumentException($"\"{key}\" is not a render_style key of the training dataset.", nameof(key));
    }
}
