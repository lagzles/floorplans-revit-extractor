using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace FloorplansRevitExtractor.Commands
{
    [Transaction(TransactionMode.ReadOnly)]
    public class CountLevelsAndSheetsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            Document doc = commandData.Application.ActiveUIDocument?.Document;
            if (doc == null)
            {
                message = "Nenhum documento ativo.";
                return Result.Failed;
            }

            int levelCount = new FilteredElementCollector(doc)
                .OfClass(typeof(Level))
                .WhereElementIsNotElementType()
                .GetElementCount();

            int sheetCount = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSheet))
                .WhereElementIsNotElementType()
                .GetElementCount();

            TaskDialog.Show(
                "Floorplans Extractor",
                $"Documento: {doc.Title}\nNíveis: {levelCount}\nFolhas montadas: {sheetCount}");

            return Result.Succeeded;
        }
    }
}
