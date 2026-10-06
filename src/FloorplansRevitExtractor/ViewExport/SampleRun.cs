using System;
using System.IO;
using Autodesk.Revit.DB;
using FloorplansRevitExtractor.Core.Sample;

namespace FloorplansRevitExtractor.ViewExport
{
    /// <summary>One view at one resolution, from Revit to the batch folder: what both export commands run.</summary>
    internal static class SampleRun
    {
        /// <summary>Not under Documents: it is often redirected to OneDrive, whose sync locks files mid-write.</summary>
        internal static string OutputFolder { get; } =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "floorplans-dataset");

        internal static (ExportResult Result, string Diagnostics) Export(Document doc, ViewSetup setup, double pxPerM, string revitVersion)
        {
            (SampleRequest request, string diagnostics) = ViewSampleBuilder.Build(
                doc,
                setup,
                pxPerM,
                revitVersion,
                Path.Combine(OutputFolder, "debug"));
            return (SampleExporter.Export(OutputFolder, request), diagnostics);
        }
    }
}
