using System.Collections.Generic;

namespace FloorplansRevitExtractor.Core.Model
{
    /// <summary>source fragment, spec section 3.1.</summary>
    public sealed record SourceFragment
    {
        public required string DocTitle { get; init; }

        public required string DocGuid { get; init; }

        public required string RevitVersion { get; init; }

        public string Mode { get; init; } = "level";

        public required long LevelId { get; init; }

        public required string LevelName { get; init; }

        public required double LevelElevationM { get; init; }

        public long? ViewId { get; init; }

        public string? ViewName { get; init; }

        public int? ViewScaleDen { get; init; }

        public string? SheetNumber { get; init; } = null;

        public required string ExportedAt { get; init; }

        public required string PluginVersion { get; init; }

        /// <summary>Spec v2: the px/m requested for the export (the measured one is in transform).</summary>
        public double? PxPerMTarget { get; init; }

        /// <summary>Spec v2: "direto" or "registrado" (the transform came from automatic registration).</summary>
        public string? Alignment { get; init; }
    }

    /// <summary>
    /// params fragment, spec v1 section 3.2, with the keys of the current
    /// training dataset (struct7k_v4). Generator knobs the Revit model cannot
    /// measure stay null.
    /// </summary>
    public sealed record ParamsFragment
    {
        public int NBedrooms { get; init; }

        public int NBathrooms { get; init; }

        public required double TotalArea { get; init; }

        public bool Garage { get; init; }

        public string? Style { get; init; } = null;

        public string? Building { get; init; } = null;

        public bool? ThinPartitions { get; init; } = null;

        public double? CornerCut { get; init; } = null;

        public string? CornerKind { get; init; } = null;

        public int? WallPillars { get; init; } = null;

        public double? ThinShare { get; init; } = null;

        public int? NotchCount { get; init; } = null;

        public double? NotchDepth { get; init; } = null;

        public double? NotchWidth { get; init; } = null;

        public double? WallIntMin { get; init; } = null;

        public int? Seed { get; init; } = null;
    }

    public sealed record DiscardedRoom(long ElementId, string Reason);

    public sealed record UnmappedRoomName(long ElementId, string RawName, string NormalizedName);

    public sealed record AreaMismatchWarning(int RoomId, double PolygonAreaM2, double RevitAreaM2, double DiffPct);

    /// <summary>
    /// Not part of the struct7k schema - a "debug" key kept deliberately
    /// outside it so this preview file is never confused with a real label.
    /// </summary>
    public sealed record DebugFragment
    {
        public required IReadOnlyList<DiscardedRoom> DiscardedRooms { get; init; }

        public required IReadOnlyList<UnmappedRoomName> UnmappedNames { get; init; }

        public required IReadOnlyList<AreaMismatchWarning> AreaMismatchWarnings { get; init; }
    }

    public sealed record RoomsPreviewDocument
    {
        public required SourceFragment Source { get; init; }

        public required ParamsFragment Params { get; init; }

        public required IReadOnlyList<RoomPreviewRecord> Rooms { get; init; }

        public required DebugFragment Debug { get; init; }
    }
}
