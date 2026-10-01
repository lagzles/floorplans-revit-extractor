using System;
using System.Collections.Generic;
using System.Linq;
using FloorplansRevitExtractor.Core.Geometry;
using FloorplansRevitExtractor.Core.Label;
using FloorplansRevitExtractor.Core.Model;
using FloorplansRevitExtractor.Core.Png;
using FloorplansRevitExtractor.Core.Raster;

namespace FloorplansRevitExtractor.Core.Sample
{
    /// <summary>
    /// The acceptance checks the exporter can run itself (spec v2 table). The
    /// image-to-mask "casamento" score lives in the Python batch gate, which
    /// must be the very function the training notebook runs.
    /// </summary>
    public static class SampleGate
    {
        /// <summary>v1: polygon area within 2% of Room.Area.</summary>
        public const double MaxAreaMismatch = 0.02;

        /// <summary>v1: room centroid projected by the transform within 1.5 px of centroid_px.</summary>
        public const double MaxRoundtripPx = 1.5;

        /// <summary>Gray below this is ink, as in the training repo's analisar_dataset.py.</summary>
        public const byte InkThreshold = 160;

        /// <summary>analisar_dataset.py treats a class as wall when more than this share of it is on ink.</summary>
        public const double WallInkShare = 0.55;

        // Spec v2, whole sheet: outside these the sample goes to review, not out.
        private const double MinWallFraction = 0.008;
        private const double MaxWallFraction = 0.12;
        private const double MinOpeningFraction = 0.001;
        private const double MaxOpeningFraction = 0.05;

        public static GateResult Check(
            RgbImage image,
            SemanticMask semantic,
            InstanceMask instance,
            SampleLabel label,
            GeomDocument geom,
            IReadOnlyList<string> notes)
        {
            var rejections = new List<GateFinding>();
            var review = new List<GateFinding>();

            if (!SizesAgree(image, semantic, instance, label, rejections))
            {
                return new GateResult(rejections, review, new GateMetrics(0, 0, null, null));
            }

            CheckInstanceIds(instance, label, rejections);
            Ink ink = MeasureInk(image, semantic);
            int total = semantic.Width * semantic.Height;
            int wall = semantic.CountPixels(StructureClass.Wall);
            int openings = semantic.CountPixels(StructureClass.Door) + semantic.CountPixels(StructureClass.Window);
            var metrics = new GateMetrics((double)wall / total, (double)openings / total, ink.Wall, ink.Room);

            if (wall == 0)
            {
                rejections.Add(new GateFinding("estrutura", "A máscara não tem nenhum pixel de parede (classe 2)."));
            }

            CheckAreas(label, rejections);
            CheckRoundtrip(label, rejections);
            CheckInk(ink, rejections, review);
            CheckDistribution(metrics, label, review);
            AddReviewNotes(label, geom, notes, review);
            return new GateResult(rejections, review, metrics);
        }

        private static bool SizesAgree(RgbImage image, SemanticMask semantic, InstanceMask instance, SampleLabel label, List<GateFinding> rejections)
        {
            var sizes = new[]
            {
                ("imagem", image.Width, image.Height),
                ("máscara semântica", semantic.Width, semantic.Height),
                ("máscara de instância", instance.Width, instance.Height),
                ("image_size do label", label.ImageSize.Width, label.ImageSize.Height),
            };

            if (sizes.All(s => s.Item2 == sizes[0].Item2 && s.Item3 == sizes[0].Item3))
            {
                return true;
            }

            rejections.Add(new GateFinding(
                "arquivos",
                "Tamanhos diferentes: " + string.Join(", ", sizes.Select(s => $"{s.Item1} {s.Item2}x{s.Item3}")) + "."));
            return false;
        }

        private static void CheckInstanceIds(InstanceMask instance, SampleLabel label, List<GateFinding> rejections)
        {
            var expected = new HashSet<int>(label.InstancesPx.Select(i => i.InstanceId));
            var present = new HashSet<int>(Enumerable.Range(1, ushort.MaxValue).Where(id => instance.CountPixels((ushort)id) > 0));
            bool idsMatchRooms = label.InstancesPx.All(i => i.InstanceId == i.RoomId + 1);
            if (!expected.SetEquals(present) || !idsMatchRooms)
            {
                rejections.Add(new GateFinding(
                    "classes",
                    $"Instâncias na máscara ({string.Join(",", present.OrderBy(i => i))}) não batem com instances_px ({string.Join(",", expected.OrderBy(i => i))})."));
            }
        }

        private static void CheckAreas(SampleLabel label, List<GateFinding> rejections)
        {
            foreach (RoomPreviewRecord room in label.Rooms.Where(r => r.RevitAreaM2 > 0))
            {
                double mismatch = Math.Abs(room.Area - room.RevitAreaM2) / room.RevitAreaM2;
                if (mismatch > MaxAreaMismatch)
                {
                    rejections.Add(new GateFinding(
                        "area",
                        $"Ambiente {room.Id} ({room.Name}): polígono {room.Area:F2} m² contra Room.Area {room.RevitAreaM2:F2} m² ({mismatch:P1})."));
                }
            }
        }

        /// <summary>
        /// centroid_px is a mean of pixel indices, and pixel i spans [i, i+1):
        /// the projected centroid sits half a pixel past it on each axis.
        /// </summary>
        private static void CheckRoundtrip(SampleLabel label, List<GateFinding> rejections)
        {
            foreach (InstancePx px in label.InstancesPx)
            {
                Point2D projected = label.Transform.ToPixel(label.Rooms[px.RoomId].Centroid);
                double dx = projected.X - 0.5 - px.CentroidPx.X;
                double dy = projected.Y - 0.5 - px.CentroidPx.Y;
                double error = Math.Sqrt((dx * dx) + (dy * dy));
                if (error > MaxRoundtripPx)
                {
                    rejections.Add(new GateFinding(
                        "roundtrip",
                        $"Ambiente {px.RoomId} ({px.Name}): centróide projetado a {error:F1} px do centróide da máscara."));
                }
            }
        }

        /// <summary>
        /// Walls lighter than rooms means swapped numbering: rejected. Walls that
        /// are light in absolute terms (outline-only walls, no cut pattern) are
        /// legitimate in Revit, so they only go to review.
        /// </summary>
        private static void CheckInk(Ink ink, List<GateFinding> rejections, List<GateFinding> review)
        {
            if (ink.Wall == null || ink.Room == null)
            {
                return;
            }

            if (ink.Wall == 0 && ink.Room == 0)
            {
                rejections.Add(new GateFinding(
                    "tinta",
                    "Imagem sem tinta sob parede nem ambiente: exportação em branco ou máscara fora da imagem."));
            }
            else if (ink.Wall <= ink.Room)
            {
                rejections.Add(new GateFinding(
                    "tinta",
                    $"Parede sobre menos tinta ({ink.Wall:P0}) que ambiente ({ink.Room:P0}): numeração trocada ou máscara desalinhada."));
            }
            else if (ink.Wall < WallInkShare)
            {
                review.Add(new GateFinding(
                    "tinta",
                    $"Só {ink.Wall:P0} da parede está sobre traço escuro: parede só com contorno, ou máscara deslocada."));
            }
        }

        private static void CheckDistribution(GateMetrics metrics, SampleLabel label, List<GateFinding> review)
        {
            if (metrics.WallFraction < MinWallFraction || metrics.WallFraction > MaxWallFraction)
            {
                review.Add(new GateFinding(
                    "distribuicao",
                    $"Parede ocupa {metrics.WallFraction:P2} da imagem (faixa do sintético: {MinWallFraction:P1} a {MaxWallFraction:P0})."));
            }

            bool hasOpenings = label.Doors.Count + label.Windows.Count > 0;
            if (hasOpenings && (metrics.OpeningFraction < MinOpeningFraction || metrics.OpeningFraction > MaxOpeningFraction))
            {
                review.Add(new GateFinding(
                    "distribuicao",
                    $"Porta + janela ocupam {metrics.OpeningFraction:P2} da imagem (faixa: {MinOpeningFraction:P1} a {MaxOpeningFraction:P0})."));
            }
        }

        private static void AddReviewNotes(SampleLabel label, GeomDocument geom, IReadOnlyList<string> notes, List<GateFinding> review)
        {
            foreach (RoomPreviewRecord room in label.Rooms.Where(r => r.Type == null))
            {
                review.Add(new GateFinding("vocabulario", $"Ambiente {room.Id} \"{room.Name}\" não mapeia para nenhum tipo do vocabulário."));
            }

            foreach (int roomId in label.RoomsWithoutPixels)
            {
                review.Add(new GateFinding("instancia", $"Ambiente {roomId} ({label.Rooms[roomId].Name}) não tem nenhum pixel na máscara de instância."));
            }

            foreach (GeomWarning warning in geom.Warnings)
            {
                string element = warning.RevitId.HasValue ? $"elemento {warning.RevitId}" : "vista";
                review.Add(new GateFinding("geometria", $"{element}: {warning.Msg}"));
            }

            review.AddRange(notes.Select(n => new GateFinding("extracao", n)));
        }

        private static Ink MeasureInk(RgbImage image, SemanticMask semantic)
        {
            byte[] classes = semantic.Pixels;
            long wallInk = 0;
            long wallCount = 0;
            long roomInk = 0;
            long roomCount = 0;
            for (int i = 0; i < classes.Length; i++)
            {
                bool dark = image.GrayAt(i) < InkThreshold;
                if (classes[i] == (byte)StructureClass.Wall)
                {
                    wallCount++;
                    wallInk += dark ? 1 : 0;
                }
                else if (classes[i] == (byte)StructureClass.Room)
                {
                    roomCount++;
                    roomInk += dark ? 1 : 0;
                }
            }

            return new Ink(
                wallCount == 0 ? (double?)null : (double)wallInk / wallCount,
                roomCount == 0 ? (double?)null : (double)roomInk / roomCount);
        }

        private readonly record struct Ink(double? Wall, double? Room);
    }
}
