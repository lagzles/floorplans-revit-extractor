using System;
using System.IO;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using FloorplansRevitExtractor.Core.Sample;
using FloorplansRevitExtractor.ViewExport;

namespace FloorplansRevitExtractor.Commands
{
    /// <summary>
    /// Exports the active plan view as one training sample (spec v2, first
    /// delivery: rooms and cut walls). Never starts a transaction: the model
    /// is only read, and Revit has nothing to save afterwards.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class ExportViewCommand : IExternalCommand
    {
        /// <summary>Spec v2, evaluation "typical" band: 40 to 55 px/m.</summary>
        private const double TargetPxPerM = 48.0;

        private const int MaxFindingsShown = 8;

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uiDoc = commandData.Application.ActiveUIDocument;
            if (uiDoc == null)
            {
                message = "Nenhum documento ativo.";
                return Result.Failed;
            }

            Document doc = uiDoc.Document;
            string outDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "floorplans-dataset");
            try
            {
                ViewSetup setup = ViewSetup.Read(uiDoc.ActiveView);
                (SampleRequest request, string diagnostics) = ViewSampleBuilder.Build(
                    doc,
                    setup,
                    TargetPxPerM,
                    commandData.Application.Application.VersionNumber,
                    Path.Combine(outDir, "debug"));
                ExportResult result = SampleExporter.Export(outDir, request);
                ShowResult(result, diagnostics, outDir);
                return Result.Succeeded;
            }
            catch (ViewNotExportableException ex)
            {
                TaskDialog.Show("Floorplans Extractor", ex.Message);
                return Result.Cancelled;
            }
            catch (Exception ex) when (!(ex is Autodesk.Revit.Exceptions.OperationCanceledException))
            {
                // Revit API, disk or geometry failure: say what happened instead of Revit's raw error dialog.
                var dialog = new TaskDialog("Floorplans Extractor")
                {
                    MainInstruction = "A exportação falhou; nenhuma amostra nova entrou no lote.",
                    MainContent = $"{ex.GetType().Name}: {ex.Message}",
                    ExpandedContent = ex.ToString(),
                };
                dialog.Show();
                message = ex.Message;
                return Result.Failed;
            }
        }

        private static void ShowResult(ExportResult result, string diagnostics, string outDir)
        {
            GateResult gate = result.Gate;
            string verdict = result.Accepted
                ? $"Amostra {result.Id} gravada no lote."
                : $"Amostra {result.Id} REJEITADA pelo gate: gravada em rejected\\{result.Id} com o motivo.";

            string Lines(string title, System.Collections.Generic.IReadOnlyList<GateFinding> findings) =>
                findings.Count == 0
                    ? string.Empty
                    : $"{title} ({findings.Count}):\n" + string.Join("\n", findings.Take(MaxFindingsShown).Select(f => $"• [{f.Check}] {f.Message}"))
                      + (findings.Count > MaxFindingsShown ? $"\n• ... e mais {findings.Count - MaxFindingsShown} em reports" : string.Empty) + "\n\n";

            var dialog = new TaskDialog("Floorplans Extractor")
            {
                MainInstruction = verdict,
                MainContent = Lines("Motivos da rejeição", gate.Rejections)
                              + Lines("Para revisão", gate.Review)
                              + $"Parede: {gate.Metrics.WallFraction:P2} da imagem. Pasta: {outDir}\n"
                              + "Confira o alinhamento em qa\\" + result.Id + ".png.",
                ExpandedContent = diagnostics,
            };
            dialog.Show();
        }
    }
}
