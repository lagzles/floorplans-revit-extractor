using System.Collections.Generic;

namespace FloorplansRevitExtractor.Core.Sample
{
    /// <param name="Check">The spec's check name: arquivos, classes, estrutura, area, roundtrip, tinta, distribuicao, ...</param>
    public sealed record GateFinding(string Check, string Message);

    /// <param name="WallInk">Share of wall pixels on dark ink (gray &lt; 160), or null without wall pixels.</param>
    /// <param name="RoomInk">Same, under room pixels.</param>
    public sealed record GateMetrics(double WallFraction, double OpeningFraction, double? WallInk, double? RoomInk);

    /// <summary>
    /// The acceptance gate's verdict: any rejection keeps the sample out of
    /// the batch; review findings let it in but list it in review.json.
    /// </summary>
    public sealed record GateResult(IReadOnlyList<GateFinding> Rejections, IReadOnlyList<GateFinding> Review, GateMetrics Metrics)
    {
        public bool Accepted => Rejections.Count == 0;
    }
}
