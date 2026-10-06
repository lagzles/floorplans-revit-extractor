using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using FloorplansRevitExtractor.Core.Geometry;
using FloorplansRevitExtractor.Core.Png;
using FloorplansRevitExtractor.Core.Sample;

namespace FloorplansRevitExtractor.ViewExport
{
    /// <summary>The exported PNG and the transform measured on it.</summary>
    internal sealed record ExportedImage(byte[] Png, PixelTransform Transform, ViewRegion Region, string Diagnostics);

    /// <summary>
    /// Exports the view as Revit draws it (office graphics, annotations
    /// included: the model must learn to ignore them) at the target px/m.
    /// The pixel size is computed from px/m, never the other way round
    /// (spec v2: a fixed 1024 px left the dense band empty). x0, y1 and
    /// px_per_m come from the region the PNG really covers, found by
    /// measuring the file: the candidate whose aspect matches the PNG within
    /// one pixel. If none does, the export stops rather than write a mask
    /// that does not line up.
    /// </summary>
    internal static class ViewImageExporter
    {
        /// <summary>Above this, spec v2 exports 1,600 px windows instead (not implemented yet).</summary>
        internal const int MaxSidePx = 8000;

        internal static ExportedImage Export(Document doc, ViewSetup setup, double targetPxPerM, string debugFolder)
        {
            ViewRegion requested = setup.ImageRegions[0];
            int widthPx = (int)Math.Round(requested.Width * targetPxPerM);
            int expectedHeightPx = (int)Math.Round(requested.Height * targetPxPerM);
            if (Math.Max(widthPx, expectedHeightPx) > MaxSidePx)
            {
                throw new ViewNotExportableException(
                    $"A imagem teria {widthPx} x {expectedHeightPx} px a {targetPxPerM} px/m. Acima de {MaxSidePx} px o spec pede "
                    + "janelas de 1.600 px, que ainda não foram implementadas: reduza o recorte da vista.");
            }

            if (widthPx < 1)
            {
                throw new ViewNotExportableException("A região de recorte é pequena demais para virar imagem.");
            }

            string folder = Path.Combine(Path.GetTempPath(), "fpx-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(folder);
            try
            {
                byte[] png = ExportWithCropVisible(doc, setup.View, folder, widthPx);
                RgbImage image = PngDecoder.DecodeRgb(png);
                string debugName = "view_" + SampleResolution.ViewVariant(setup.View.Id.Value, targetPxPerM).Replace('@', '_');
                try
                {
                    (PixelTransform transform, ViewRegion region, string diagnostics) = Measure(setup.ImageRegions, image, targetPxPerM);
                    SaveDebug(debugFolder, debugName, png, diagnostics);
                    return new ExportedImage(png, transform, region, diagnostics);
                }
                catch (ViewNotExportableException ex)
                {
                    SaveDebug(debugFolder, debugName, png, ex.Message);
                    throw;
                }
            }
            finally
            {
                TryDelete(folder);
            }
        }

        /// <summary>
        /// Revit fits the exported image to the extents of what is drawn. With
        /// the crop boundary hidden those extents depend on the content; shown,
        /// the boundary is the outermost thing drawn, so the image covers the
        /// crop. The boundary is shown inside a transaction group that is rolled
        /// back after the export (spec v1 section 9): the model ends exactly as
        /// it was, and Revit has nothing to save.
        /// </summary>
        private static byte[] ExportWithCropVisible(Document doc, View view, string folder, int widthPx)
        {
            if (view.CropBoxVisible)
            {
                return ExportPng(doc, view, folder, widthPx);
            }

            using var group = new TransactionGroup(doc, "Floorplans Extractor: exportar vista");
            group.Start();
            try
            {
                using (var transaction = new Transaction(doc, "Mostrar região de recorte"))
                {
                    transaction.Start();
                    view.CropBoxVisible = true;
                    transaction.Commit();
                }

                return ExportPng(doc, view, folder, widthPx);
            }
            finally
            {
                group.RollBack();
            }
        }

        /// <summary>The raw export and the measurements, kept next to the batch: what M0 needs to see.</summary>
        private static void SaveDebug(string debugFolder, string name, byte[] png, string diagnostics)
        {
            Directory.CreateDirectory(debugFolder);
            File.WriteAllBytes(Path.Combine(debugFolder, name + "_export.png"), png);
            File.WriteAllText(Path.Combine(debugFolder, name + "_medidas.txt"), diagnostics);
        }

        /// <summary>Antivirus or the indexer may still hold the PNG: a leftover temp folder is not a failed export.</summary>
        private static void TryDelete(string folder)
        {
            try
            {
                Directory.Delete(folder, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        private static byte[] ExportPng(Document doc, View view, string folder, int widthPx)
        {
            var options = new ImageExportOptions
            {
                ZoomType = ZoomFitType.FitToPage,
                FitDirection = FitDirectionType.Horizontal,
                PixelSize = widthPx,
                ExportRange = ExportRange.SetOfViews,
                HLRandWFViewsFileType = ImageFileType.PNG,
                ShadowViewsFileType = ImageFileType.PNG,
                ImageResolution = ImageResolution.DPI_150,
                FilePath = Path.Combine(folder, "view"),
            };
            options.SetViewsAndSheets(new List<ElementId> { view.Id });
            doc.ExportImage(options);

            string[] files = Directory.GetFiles(folder, "*.png");
            if (files.Length != 1)
            {
                throw new InvalidOperationException($"O Revit gerou {files.Length} arquivos PNG em {folder}; esperava 1.");
            }

            return File.ReadAllBytes(files[0]);
        }

        private static (PixelTransform, ViewRegion, string) Measure(IReadOnlyList<ViewRegion> regions, RgbImage image, double targetPxPerM)
        {
            var lines = new List<string> { $"PNG exportado: {image.Width} x {image.Height} px; alvo {targetPxPerM} px/m." };
            foreach (ViewRegion region in regions)
            {
                double k = image.Width / region.Width;
                lines.Add($"{region.Name}: {region.Width:F3} x {region.Height:F3} m -> {k:F4} px/m, altura esperada {region.Height * k:F1} px.");
            }

            foreach (ViewRegion region in regions)
            {
                try
                {
                    PixelTransform t = PixelTransform.Measure(region.MinX, region.MinY, region.MaxX, region.MaxY, image.Width, image.Height);
                    lines.Add($"Usado: {region.Name}.");
                    return (t, region, string.Join(Environment.NewLine, lines));
                }
                catch (ArgumentException)
                {
                    // Not this region; try the next candidate.
                }
            }

            throw new ViewNotExportableException(
                "A imagem exportada não corresponde a nenhuma região conhecida da vista, então a máscara não ficaria alinhada. "
                + "Nenhuma amostra foi gravada. Medidas:" + Environment.NewLine + string.Join(Environment.NewLine, lines.Select(l => "  " + l)));
        }
    }
}
