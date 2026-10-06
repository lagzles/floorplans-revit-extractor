using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows.Forms;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using FloorplansRevitExtractor.Core.Sample;
using FloorplansRevitExtractor.UI;
using FloorplansRevitExtractor.ViewExport;

namespace FloorplansRevitExtractor.Commands
{
    /// <summary>
    /// Exports many plan views of the open model at one or more resolutions,
    /// without opening them: each view is read and exported by id. A view
    /// that fails is recorded and the batch goes on; the outcome of every view
    /// goes to lotes/lote_*.json. Like the single-view command, it never
    /// changes the model (the crop boundary is shown in a rolled-back group).
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class ExportViewsCommand : IExternalCommand
    {
        private const int MaxProblemsShown = 8;

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            Document doc = commandData.Application.ActiveUIDocument?.Document;
            if (doc == null)
            {
                message = "Nenhum documento ativo.";
                return Result.Failed;
            }

            IReadOnlyList<ViewEntry> catalog = ViewCatalog.Collect(doc);
            if (catalog.Count == 0)
            {
                TaskDialog.Show("Floorplans Extractor", "O modelo não tem plantas para exportar.");
                return Result.Cancelled;
            }

            var owner = new RevitWindowHandle(commandData.Application.MainWindowHandle);
            IReadOnlyList<ViewPlan> views;
            IReadOnlyList<SampleResolution> resolutions;
            using (var form = new BatchExportForm(catalog, SampleRun.OutputFolder))
            {
                if (form.ShowDialog(owner) != DialogResult.OK)
                {
                    return Result.Cancelled;
                }

                views = form.SelectedViews;
                resolutions = form.SelectedResolutions;
            }

            DateTimeOffset startedAt = DateTimeOffset.Now;
            List<BatchEntry> entries = Run(doc, views, resolutions, commandData.Application.Application.VersionNumber, owner);
            string reportPath = BatchReport.Write(SampleRun.OutputFolder, doc.Title, startedAt, entries);
            ShowSummary(entries, views.Count * resolutions.Count, reportPath);
            return Result.Succeeded;
        }

        private static List<BatchEntry> Run(
            Document doc,
            IReadOnlyList<ViewPlan> views,
            IReadOnlyList<SampleResolution> resolutions,
            string revitVersion,
            IWin32Window owner)
        {
            var entries = new List<BatchEntry>();
            int total = views.Count * resolutions.Count;
            using var progress = new BatchProgressForm(total);
            progress.Show(owner);
            try
            {
                foreach (ViewPlan view in views)
                {
                    foreach (SampleResolution resolution in resolutions)
                    {
                        if (progress.CancelRequested)
                        {
                            return entries;
                        }

                        progress.Step(entries.Count, $"{entries.Count + 1} de {total}: {view.Name} a {resolution.PxPerM:0} px/m");
                        entries.Add(ExportOne(doc, view, resolution.PxPerM, revitVersion));
                    }
                }

                return entries;
            }
            finally
            {
                progress.Close();
            }
        }

        private static BatchEntry ExportOne(Document doc, ViewPlan view, double pxPerM, string revitVersion)
        {
            var clock = Stopwatch.StartNew();
            BatchEntry Entry(BatchOutcome outcome, string id, string text) =>
                new BatchEntry(view.Id.Value, view.Name, pxPerM, outcome, id, text, clock.Elapsed.TotalSeconds);

            try
            {
                (ExportResult result, string _) = SampleRun.Export(doc, ViewSetup.Read(view), pxPerM, revitVersion);
                return result.Accepted
                    ? Entry(BatchOutcome.Accepted, result.Id, string.Empty)
                    : Entry(BatchOutcome.Rejected, result.Id, string.Join(" | ", result.Gate.Rejections.Select(r => $"[{r.Check}] {r.Message}")));
            }
            catch (ViewNotExportableException ex)
            {
                return Entry(BatchOutcome.Skipped, null, ex.Message);
            }
            catch (Exception ex) when (!(ex is Autodesk.Revit.Exceptions.OperationCanceledException))
            {
                // One view's API, disk or geometry failure must not stop the batch.
                return Entry(BatchOutcome.Failed, null, $"{ex.GetType().Name}: {ex.Message}");
            }
        }

        private static void ShowSummary(IReadOnlyList<BatchEntry> entries, int planned, string reportPath)
        {
            string Count(BatchOutcome outcome) => $"{entries.Count(e => e.Outcome == outcome)} {BatchReport.Name(outcome)}(s)";
            List<BatchEntry> problems = entries.Where(e => e.Outcome != BatchOutcome.Accepted).ToList();
            string cancelled = entries.Count < planned ? $"Cancelado: {entries.Count} de {planned} feitas.\n" : string.Empty;

            var dialog = new TaskDialog("Floorplans Extractor")
            {
                MainInstruction = $"Lote concluído: {entries.Count} amostra(s) de {entries.Select(e => e.ViewId).Distinct().Count()} vista(s): "
                    + $"{Count(BatchOutcome.Accepted)}, {Count(BatchOutcome.Rejected)}, {Count(BatchOutcome.Failed)}, {Count(BatchOutcome.Skipped)}.",
                MainContent = cancelled
                    + string.Join("\n", problems.Take(MaxProblemsShown).Select(p => $"• {p.ViewName} ({p.PxPerM:0} px/m), {BatchReport.Name(p.Outcome)}: {p.Message}"))
                    + (problems.Count > MaxProblemsShown ? $"\n• ... e mais {problems.Count - MaxProblemsShown}" : string.Empty)
                    + $"\n\nRelatório do lote: {reportPath}\nDetalhe de cada amostra em reports\\ e qa\\.",
            };
            dialog.Show();
        }
    }
}
