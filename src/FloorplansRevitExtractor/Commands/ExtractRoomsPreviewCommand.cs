using System;
using System.IO;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using FloorplansRevitExtractor.Core.Json;
using FloorplansRevitExtractor.Core.Model;
using FloorplansRevitExtractor.RevitInterop;

namespace FloorplansRevitExtractor.Commands
{
    /// <summary>
    /// Read-only preview of Room extraction for a chosen Level. This is NOT
    /// a struct7k sample (no image/masks) - just a debug JSON to validate the
    /// extraction pipeline before the wall/door/window/image increments.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    public class ExtractRoomsPreviewCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            Document doc = commandData.Application.ActiveUIDocument?.Document;
            if (doc == null)
            {
                message = "Nenhum documento ativo.";
                return Result.Failed;
            }

            Level level = LevelSelectionService.Resolve(doc);
            if (level == null)
            {
                TaskDialog.Show("Floorplans Extractor", "Nenhum ambiente encontrado no documento (ou seleção cancelada).");
                return Result.Cancelled;
            }

            RoomsPreviewDocument previewDocument = RoomExtractionAssembler.Build(doc, commandData.Application, level);

            string outputPath = BuildOutputPath(doc, level);
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath));
            using (FileStream stream = File.Create(outputPath))
            {
                Utf8JsonFragmentWriter.Write(stream, previewDocument);
            }

            TaskDialog.Show(
                "Floorplans Extractor",
                $"Nível: {level.Name}\n"
                    + $"Ambientes válidos: {previewDocument.Rooms.Count}\n"
                    + $"Descartados: {previewDocument.Debug.DiscardedRooms.Count}\n"
                    + $"Nomes não mapeados: {previewDocument.Debug.UnmappedNames.Count}\n"
                    + $"Arquivo: {outputPath}");

            return Result.Succeeded;
        }

        private static string BuildOutputPath(Document doc, Level level)
        {
            string baseDir = string.IsNullOrEmpty(doc.PathName)
                ? Path.Combine(Path.GetTempPath(), "floorplans-extractor-debug")
                : Path.Combine(Path.GetDirectoryName(doc.PathName), "floorplans-extractor-debug");

            string sanitizedLevelName = SanitizeForFileName(level.Name);
            string timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            return Path.Combine(baseDir, $"rooms_{sanitizedLevelName}_{timestamp}.json");
        }

        private static string SanitizeForFileName(string name)
        {
            char[] invalid = Path.GetInvalidFileNameChars();
            return new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        }
    }
}
