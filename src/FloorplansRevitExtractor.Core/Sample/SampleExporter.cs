using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using FloorplansRevitExtractor.Core.Geometry;
using FloorplansRevitExtractor.Core.Json;
using FloorplansRevitExtractor.Core.Label;
using FloorplansRevitExtractor.Core.Model;
using FloorplansRevitExtractor.Core.Png;
using FloorplansRevitExtractor.Core.Raster;

namespace FloorplansRevitExtractor.Core.Sample
{
    /// <param name="Folder">Where the sample's files ended up: the batch root, or rejected/NNNNNN.</param>
    public sealed record ExportResult(string Id, bool Accepted, GateResult Gate, string Folder);

    /// <summary>
    /// Turns one view's request into a sample of the batch. The order is what
    /// keeps the batch consistent if anything fails or Revit dies half way:
    /// 1. leftovers of a crashed export are cleared;
    /// 2. the id is reserved and the registry saved before anything can fail,
    ///    so no other view can ever get the same id;
    /// 3. masks, label and gate are computed and every file written to
    ///    .staging/NNNNNN;
    /// 4. the files replace the old ones one by one, the label last: a sample
    ///    counts as present only when its label is (spec v1: all four files or
    ///    none), and an old file is replaced only once its new one exists;
    /// 5. manifest, classes and review are rebuilt from the folders even when
    ///    the export failed.
    /// A rejected sample leaves the batch and goes to rejected/NNNNNN with its report.
    /// </summary>
    public static class SampleExporter
    {
        /// <summary>Promotion order: everything that is not a dataset file, then image and masks, the label last.</summary>
        private static readonly string[] PromotionOrder =
        {
            SampleFiles.Geometry,
            SampleFiles.Qa,
            SampleFiles.Reports,
            SampleFiles.Images,
            SampleFiles.SemanticMasks,
            SampleFiles.InstanceMasks,
            SampleFiles.Labels,
        };

        public static ExportResult Export(string outDir, SampleRequest request)
        {
            Directory.CreateDirectory(outDir);
            ClearStaging(outDir);
            string id = ReserveId(outDir, request.Key);
            try
            {
                Built built = BuildSample(request, id);
                string staging = Path.Combine(outDir, SampleFiles.Staging, id);
                WriteStaged(staging, id, built, request);
                string folder = built.Gate.Accepted ? Promote(staging, outDir, id) : Reject(staging, outDir, id);
                return new ExportResult(id, built.Gate.Accepted, built.Gate, folder);
            }
            finally
            {
                ClearStaging(outDir);
                RefreshBatchFiles(outDir);
            }
        }

        private static string ReserveId(string outDir, SampleKey key)
        {
            string registryPath = Path.Combine(outDir, SampleFiles.Registry);
            (DatasetRegistry registry, int number) = DatasetRegistry.Load(registryPath).Resolve(key);
            registry.Save(registryPath);
            return SampleFiles.IdText(number);
        }

        private static Built BuildSample(SampleRequest request, string id)
        {
            RgbImage image = PngDecoder.DecodeRgb(request.ImagePng);
            PixelTransform transform = request.Geom.Transform;
            SemanticMask semantic = GeomRasterizer.PaintSemantic(request.Geom);
            InstanceMask instance = PaintInstances(request.Rooms, transform);
            SampleLabel label = LabelAssembler.Build(new LabelInputs
            {
                Source = request.Source,
                Building = request.Building,
                Rooms = request.Rooms,
                Doors = request.Doors,
                Windows = request.Windows,
                Furniture = request.Furniture,
                RenderStyle = request.RenderStyle,
                Transform = transform,
                InstanceMask = instance,
                File = SampleFiles.FileName(id, SampleFiles.ImageExtension),
            });
            GateResult gate = SampleGate.Check(image, semantic, instance, label, request.Geom, request.ReviewNotes);
            return new Built(image, semantic, instance, label, gate);
        }

        private static InstanceMask PaintInstances(IReadOnlyList<RoomPreviewRecord> rooms, PixelTransform t) =>
            InstanceMaskPainter.Paint(t.Width, t.Height, rooms.Select(r => new RoomInstance(r.Id, new[]
            {
                new PolygonShape(
                    r.Polygon.Select(t.ToPixel).ToArray(),
                    r.Holes.Select(h => (IReadOnlyList<Point2D>)h.Select(t.ToPixel).ToArray()).ToArray()),
            })).ToList());

        private static void WriteStaged(string staging, string id, Built built, SampleRequest request)
        {
            foreach ((string folder, _) in SampleFiles.PerSample)
            {
                Directory.CreateDirectory(Path.Combine(staging, folder));
            }

            string Staged(string folder, string extension) => SampleFiles.PathOf(staging, folder, id, extension);

            RgbImage image = built.Image;
            SemanticMask semantic = built.Semantic;
            File.WriteAllBytes(Staged(SampleFiles.Images, ".png"), PngEncoder.EncodeRgb(image.Width, image.Height, image.Pixels));
            File.WriteAllBytes(
                Staged(SampleFiles.SemanticMasks, ".png"),
                PngEncoder.EncodePalette(semantic.Width, semantic.Height, semantic.Pixels, StructureClassPalette.Colors));
            File.WriteAllBytes(
                Staged(SampleFiles.InstanceMasks, ".png"),
                PngEncoder.EncodeGray16(built.Instance.Width, built.Instance.Height, built.Instance.Pixels));
            WriteJson(Staged(SampleFiles.Labels, ".json"), s => LabelJsonWriter.Write(s, built.Label));
            WriteJson(Staged(SampleFiles.Geometry, ".json"), s => GeomJsonWriter.Write(s, request.Geom));
            WriteJson(Staged(SampleFiles.Reports, ".json"), s => DatasetJsonWriter.WriteReport(s, id, built.Gate, request.Source));

            // A size mismatch is already a rejection; there is nothing to overlay then.
            if (image.Width == semantic.Width && image.Height == semantic.Height)
            {
                RgbImage qa = QaOverlay.Compose(image, semantic);
                File.WriteAllBytes(Staged(SampleFiles.Qa, ".png"), PngEncoder.EncodeRgb(qa.Width, qa.Height, qa.Pixels));
            }
        }

        private static string Promote(string staging, string outDir, string id)
        {
            foreach (string folder in PromotionOrder)
            {
                string extension = ExtensionOf(folder);
                string from = SampleFiles.PathOf(staging, folder, id, extension);
                string to = SampleFiles.PathOf(outDir, folder, id, extension);
                if (File.Exists(from))
                {
                    Directory.CreateDirectory(Path.Combine(outDir, folder));
                    ReplaceOrMove(from, to);
                }
                else if (File.Exists(to))
                {
                    File.Delete(to); // the new sample has no such file (e.g. no qa): drop the stale one
                }
            }

            DeleteDirectory(Path.Combine(outDir, SampleFiles.Rejected, id));
            return outDir;
        }

        /// <summary>
        /// The label goes first, so the old accepted copy stops counting as a
        /// sample before its other files are removed.
        /// </summary>
        private static string Reject(string staging, string outDir, string id)
        {
            foreach (string folder in PromotionOrder.Reverse())
            {
                string path = SampleFiles.PathOf(outDir, folder, id, ExtensionOf(folder));
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }

            string target = Path.Combine(outDir, SampleFiles.Rejected, id);
            DeleteDirectory(target);
            Directory.CreateDirectory(Path.Combine(outDir, SampleFiles.Rejected));
            Directory.Move(staging, target);
            File.Move(Path.Combine(target, SampleFiles.Reports, SampleFiles.FileName(id, ".json")), Path.Combine(target, "report.json"));
            DeleteIfEmpty(Path.Combine(target, SampleFiles.Reports));
            return target;
        }

        private static void RefreshBatchFiles(string outDir)
        {
            IReadOnlyList<string> ids = CompleteSamples(outDir);
            WriteJson(Path.Combine(outDir, SampleFiles.Manifest), s => DatasetJsonWriter.WriteManifest(s, ids));
            WriteJson(Path.Combine(outDir, SampleFiles.Classes), DatasetJsonWriter.WriteClasses);

            var review = new List<(string, IReadOnlyList<GateFinding>)>();
            foreach (string id in ids)
            {
                IReadOnlyList<GateFinding> findings = ReadReview(SampleFiles.PathOf(outDir, SampleFiles.Reports, id, ".json"));
                if (findings.Count > 0)
                {
                    review.Add((id, findings));
                }
            }

            WriteJson(Path.Combine(outDir, SampleFiles.Review), s => DatasetJsonWriter.WriteReview(s, review));
        }

        /// <summary>A missing or unreadable report is itself a reason to look at the sample, not a reason to stop the batch.</summary>
        private static IReadOnlyList<GateFinding> ReadReview(string reportPath)
        {
            if (!File.Exists(reportPath))
            {
                return new[] { new GateFinding("relatorio", "Amostra sem relatório.") };
            }

            try
            {
                using FileStream stream = File.OpenRead(reportPath);
                return DatasetJsonWriter.ReadReviewFindings(stream);
            }
            catch (Exception ex) when (ex is JsonException || ex is KeyNotFoundException || ex is InvalidOperationException)
            {
                return new[] { new GateFinding("relatorio", $"O relatório da amostra está ilegível: {ex.Message}") };
            }
        }

        /// <summary>Ids that have all four dataset files: image, both masks, label.</summary>
        private static IReadOnlyList<string> CompleteSamples(string outDir)
        {
            string labels = Path.Combine(outDir, SampleFiles.Labels);
            if (!Directory.Exists(labels))
            {
                return Array.Empty<string>();
            }

            return Directory.GetFiles(labels, "*.json")
                .Select(Path.GetFileNameWithoutExtension)
                .Where(id => File.Exists(SampleFiles.PathOf(outDir, SampleFiles.Images, id, ".png"))
                             && File.Exists(SampleFiles.PathOf(outDir, SampleFiles.SemanticMasks, id, ".png"))
                             && File.Exists(SampleFiles.PathOf(outDir, SampleFiles.InstanceMasks, id, ".png")))
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToList();
        }

        /// <summary>Writes next to the target and swaps it in, so a crash never leaves a half-written file.</summary>
        internal static void WriteJson(string path, Action<Stream> write)
        {
            string temporary = path + ".tmp";
            using (FileStream stream = File.Create(temporary))
            {
                write(stream);
            }

            ReplaceOrMove(temporary, path);
        }

        private static void ReplaceOrMove(string from, string to)
        {
            if (File.Exists(to))
            {
                File.Replace(from, to, null);
            }
            else
            {
                File.Move(from, to);
            }
        }

        private static string ExtensionOf(string folder) => SampleFiles.PerSample.First(f => f.Folder == folder).Extension;

        private static void ClearStaging(string outDir) => DeleteDirectory(Path.Combine(outDir, SampleFiles.Staging));

        private static void DeleteDirectory(string directory)
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        private static void DeleteIfEmpty(string directory)
        {
            if (Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any())
            {
                Directory.Delete(directory);
            }
        }

        private sealed record Built(RgbImage Image, SemanticMask Semantic, InstanceMask Instance, SampleLabel Label, GateResult Gate);
    }
}
