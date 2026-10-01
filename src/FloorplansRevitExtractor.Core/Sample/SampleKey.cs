using System;

namespace FloorplansRevitExtractor.Core.Sample
{
    /// <summary>
    /// What makes two exports the same sample (spec v1 section 9): the model,
    /// the mode and the view. Re-exporting it reuses its id and overwrites it.
    /// </summary>
    public sealed record SampleKey
    {
        public SampleKey(string docGuid, string mode, string viewId)
        {
            DocGuid = Require(docGuid, nameof(docGuid));
            Mode = Require(mode, nameof(mode));
            ViewId = Require(viewId, nameof(viewId));
        }

        public string DocGuid { get; }

        public string Mode { get; }

        public string ViewId { get; }

        public override string ToString() => $"{DocGuid}|{Mode}|{ViewId}";

        private static string Require(string value, string name) =>
            string.IsNullOrWhiteSpace(value) || value.IndexOf('|') >= 0
                ? throw new ArgumentException($"{name} must be non-empty and free of '|', got \"{value}\".", name)
                : value;
    }
}
