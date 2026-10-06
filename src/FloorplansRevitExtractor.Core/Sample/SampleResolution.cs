using System.Collections.Generic;
using System.Globalization;

namespace FloorplansRevitExtractor.Core.Sample
{
    /// <summary>One evaluation band of spec v4 section 6, exported at a px/m inside it.</summary>
    public sealed record SampleResolution(string Name, double PxPerM, string Band)
    {
        /// <summary>The typical band, the single-view command's resolution and the first samples' (900001...).</summary>
        public const double DefaultPxPerM = 48.0;

        /// <summary>Spec v4 section 6, evaluation: sparse 20-25, typical 40-55, dense 95-105 px/m.</summary>
        public static IReadOnlyList<SampleResolution> Bands { get; } = new[]
        {
            new SampleResolution("ralo", 22.0, "20 a 25 px/m"),
            new SampleResolution("típico", DefaultPxPerM, "40 a 55 px/m"),
            new SampleResolution("denso", 100.0, "95 a 105 px/m"),
        };

        /// <summary>
        /// The view part of the sample key: the same view at another resolution
        /// is another sample. The default resolution keeps the bare view id, so
        /// samples exported before resolutions existed keep their ids.
        /// </summary>
        public static string ViewVariant(long viewId, double pxPerM)
        {
            string id = viewId.ToString(CultureInfo.InvariantCulture);
            return pxPerM == DefaultPxPerM ? id : id + "@" + pxPerM.ToString("0.###", CultureInfo.InvariantCulture);
        }
    }
}
