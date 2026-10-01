using System;
using System.Collections.Generic;
using FloorplansRevitExtractor.Core.Geometry;

namespace FloorplansRevitExtractor.Core.Model
{
    /// <summary>
    /// The geometry records handed from the Revit extraction to the Core
    /// (spec v2 contract, also written to geom/NNNNNN.json). Coordinates are
    /// meters in the view frame, Y up.
    /// </summary>
    public sealed record GeomDocument
    {
        public required GeomView View { get; init; }

        public required PixelTransform Transform { get; init; }

        public required IReadOnlyList<GeomElement> Elements { get; init; }

        public required IReadOnlyList<GeomWarning> Warnings { get; init; }
    }

    public sealed record GeomView
    {
        public GeomView(long id, string name, int scale, double cutPlaneM)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("View name must not be empty.", nameof(name));
            }

            if (scale <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(scale), scale, "View scale must be positive (100 = 1:100).");
            }

            if (double.IsNaN(cutPlaneM) || double.IsInfinity(cutPlaneM))
            {
                throw new ArgumentException($"Cut plane height must be finite, got {cutPlaneM}.", nameof(cutPlaneM));
            }

            Id = id;
            Name = name;
            Scale = scale;
            CutPlaneM = cutPlaneM;
        }

        public long Id { get; }

        public string Name { get; }

        public int Scale { get; }

        public double CutPlaneM { get; }
    }

    public sealed record GeomWarning
    {
        /// <param name="revitId">Null for a warning about the view rather than one element.</param>
        public GeomWarning(long? revitId, string msg)
        {
            if (string.IsNullOrWhiteSpace(msg))
            {
                throw new ArgumentException("Warning message must not be empty.", nameof(msg));
            }

            RevitId = revitId;
            Msg = msg;
        }

        public long? RevitId { get; }

        public string Msg { get; }
    }
}
