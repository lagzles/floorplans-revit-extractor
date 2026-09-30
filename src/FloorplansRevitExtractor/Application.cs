using System.Reflection;
using Autodesk.Revit.UI;
using FloorplansRevitExtractor.Commands;

namespace FloorplansRevitExtractor
{
    public class Application : IExternalApplication
    {
        private const string TabName = "Floorplans Extractor";
        private const string DiagnosticsPanelName = "Diagnóstico";
        private const string ExtractionPanelName = "Extração";

        public Result OnStartup(UIControlledApplication application)
        {
            application.CreateRibbonTab(TabName);

            string assemblyPath = Assembly.GetExecutingAssembly().Location;

            RibbonPanel diagnosticsPanel = application.CreateRibbonPanel(TabName, DiagnosticsPanelName);
            diagnosticsPanel.AddItem(new PushButtonData(
                "CountLevelsAndSheets",
                "Contar\nNíveis e Folhas",
                assemblyPath,
                typeof(CountLevelsAndSheetsCommand).FullName));

            RibbonPanel extractionPanel = application.CreateRibbonPanel(TabName, ExtractionPanelName);
            extractionPanel.AddItem(new PushButtonData(
                "ExtractRoomsPreview",
                "Extrair Rooms\n(preview)",
                assemblyPath,
                typeof(ExtractRoomsPreviewCommand).FullName));

            return Result.Succeeded;
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            return Result.Succeeded;
        }
    }
}
