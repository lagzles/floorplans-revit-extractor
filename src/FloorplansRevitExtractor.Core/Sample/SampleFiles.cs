using System.Globalization;
using System.IO;

namespace FloorplansRevitExtractor.Core.Sample
{
    /// <summary>
    /// The batch folder layout (spec v1 section 1, plus geom/, qa/, reports/
    /// and rejected/, which never enter training). Every name lives here.
    /// </summary>
    public static class SampleFiles
    {
        public const string Images = "images";
        public const string SemanticMasks = "masks_semantic";
        public const string InstanceMasks = "masks_instance";
        public const string Labels = "labels";
        public const string Geometry = "geom";
        public const string Qa = "qa";
        public const string Reports = "reports";
        public const string Rejected = "rejected";
        public const string Staging = ".staging";

        public const string Manifest = "manifest.json";
        public const string Classes = "classes.json";
        public const string Registry = "registry.json";
        public const string Review = "review.json";

        public const string ImageExtension = ".png";

        /// <summary>The per-sample files: folder, extension.</summary>
        internal static readonly (string Folder, string Extension)[] PerSample =
        {
            (Images, ImageExtension),
            (SemanticMasks, ".png"),
            (InstanceMasks, ".png"),
            (Labels, ".json"),
            (Geometry, ".json"),
            (Qa, ".png"),
            (Reports, ".json"),
        };

        public static string IdText(int id) => id.ToString("D6", CultureInfo.InvariantCulture);

        internal static string FileName(string id, string extension) => id + extension;

        internal static string PathOf(string root, string folder, string id, string extension) =>
            Path.Combine(root, folder, FileName(id, extension));
    }
}
