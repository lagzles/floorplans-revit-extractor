using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using FloorplansRevitExtractor.Core.Geometry;
using FloorplansRevitExtractor.RevitInterop;

namespace FloorplansRevitExtractor.ViewExport
{
    /// <summary>A rectangle in the view frame, meters.</summary>
    internal readonly record struct ViewRegion(string Name, double MinX, double MinY, double MaxX, double MaxY)
    {
        internal double Width => MaxX - MinX;

        internal double Height => MaxY - MinY;
    }

    /// <summary>
    /// What the export needs to know about the active plan view, read once:
    /// the crop-box frame every coordinate goes to (spec v2: never project or
    /// shared coordinates), the regions the exported image may cover, and the
    /// cut plane height. Throws <see cref="ViewNotExportableException"/> with
    /// a user-facing reason when the view cannot be a sample.
    /// </summary>
    internal sealed class ViewSetup
    {
        private ViewSetup(
            ViewPlan view,
            Level level,
            ElementId phaseId,
            ViewFrame frame,
            Transform cropTransform,
            IReadOnlyList<ViewRegion> imageRegions,
            double cutPlaneZFeet,
            double bottomZFeet,
            double sameFloorLimitZFeet)
        {
            SameFloorLimitZFeet = sameFloorLimitZFeet;
            View = view;
            Level = level;
            PhaseId = phaseId;
            Frame = frame;
            CropTransform = cropTransform;
            ImageRegions = imageRegions;
            CutPlaneZFeet = cutPlaneZFeet;
            BottomZFeet = bottomZFeet;
        }

        internal ViewPlan View { get; }

        internal Level Level { get; }

        internal ElementId PhaseId { get; }

        internal ViewFrame Frame { get; }

        /// <summary>Crop box local to model, in feet: maps points straight into the frame.</summary>
        internal Transform CropTransform { get; }

        /// <summary>
        /// Candidate extents of the exported image, largest first: the annotation
        /// crop (when active) and the model crop. Which one Revit really exports
        /// is decided by measuring the PNG.
        /// </summary>
        internal IReadOnlyList<ViewRegion> ImageRegions { get; }

        /// <summary>Where Revit clips model graphics: the mask is clipped here too, whatever the image covers.</summary>
        internal ViewRegion ModelRegion => ImageRegions[ImageRegions.Count - 1];

        /// <summary>Absolute height of the cut plane, internal units (feet).</summary>
        internal double CutPlaneZFeet { get; }

        /// <summary>
        /// Absolute height of the view range's bottom: what lies between it and
        /// the cut plane is drawn in projection (furniture, stairs, floor
        /// openings), whatever level each block of the plan sits on.
        /// </summary>
        internal double BottomZFeet { get; }

        internal double CutPlaneM => UnitsConverter.ToMeters(CutPlaneZFeet - Level.ProjectElevation);

        /// <summary>
        /// Levels above the view's level but below this height belong to the
        /// same floor: the blocks of a building on a slope ("L1 - Block 35",
        /// "L1 - Block 43"). The view range's top when it is a level, else the
        /// cut plane plus its own offset again.
        /// </summary>
        internal double SameFloorLimitZFeet { get; }

        private readonly Dictionary<long, double> _shiftByLevel = new Dictionary<long, double>();

        /// <summary>The same-floor levels whose elements were cut at their own height, for the review notes.</summary>
        internal IReadOnlyCollection<string> ShiftedLevelNames =>
            _shiftByLevel.Where(p => p.Value != 0).Select(p => View.Document.GetElement(new ElementId(p.Key))?.Name).Where(n => n != null).ToList();

        /// <summary>
        /// Revit draws each block of a split-level floor cut at the view's cut
        /// offset above the block's own level (a plan region's view range, which
        /// the API does not expose). An element on such a level is cut there.
        /// </summary>
        internal double CutZFor(Element element) => CutPlaneZFeet + LevelShiftFeet(element);

        internal double BottomZFor(Element element) => BottomZFeet + LevelShiftFeet(element);

        private double LevelShiftFeet(Element element)
        {
            ElementId levelId = LevelOf(element);
            if (levelId == ElementId.InvalidElementId || levelId == Level.Id)
            {
                return 0;
            }

            if (!_shiftByLevel.TryGetValue(levelId.Value, out double shift))
            {
                shift = 0;
                if (View.Document.GetElement(levelId) is Level level)
                {
                    double above = level.ProjectElevation - Level.ProjectElevation;
                    shift = above > 0 && level.ProjectElevation < SameFloorLimitZFeet ? above : 0;
                }

                _shiftByLevel[levelId.Value] = shift;
            }

            return shift;
        }

        private static ElementId LevelOf(Element element)
        {
            switch (element)
            {
                case StairsRun run:
                    return LevelOf(run.GetStairs());
                case StairsLanding landing:
                    return LevelOf(landing.GetStairs());
                case Stairs stairs:
                    return stairs.get_Parameter(BuiltInParameter.STAIRS_BASE_LEVEL_PARAM)?.AsElementId() ?? ElementId.InvalidElementId;
                case FamilyInstance instance when instance.LevelId == ElementId.InvalidElementId && instance.Host != null:
                    return instance.Host.LevelId;
                default:
                    return element.LevelId;
            }
        }

        internal static ViewSetup Read(View activeView)
        {
            if (!(activeView is ViewPlan view) || view.ViewType != ViewType.FloorPlan || view.IsTemplate)
            {
                throw new ViewNotExportableException("Abra uma planta baixa (Floor Plan) como vista ativa.");
            }

            if (!view.CropBoxActive)
            {
                throw new ViewNotExportableException(
                    "A vista precisa de região de recorte ativa (Crop View): é ela que define o enquadramento da imagem.");
            }

            ViewCropRegionShapeManager shape = view.GetCropRegionShapeManager();
            if (shape.ShapeSet || shape.Split)
            {
                throw new ViewNotExportableException("A região de recorte precisa ser um retângulo simples (sem forma editada nem divisão).");
            }

            Level level = view.GenLevel ?? throw new ViewNotExportableException("A vista não está associada a um nível.");
            ElementId phaseId = view.get_Parameter(BuiltInParameter.VIEW_PHASE)?.AsElementId() ?? ElementId.InvalidElementId;

            BoundingBoxXYZ crop = view.CropBox;
            Transform t = crop.Transform;
            RequireScreenAligned(view, t);
            var frame = new ViewFrame(
                UnitsConverter.ToMetersPoint(t.Origin),
                new Vector2D(t.BasisX.X, t.BasisX.Y),
                new Vector2D(t.BasisY.X, t.BasisY.Y));

            double cutZ = PlaneZ(view, level, PlanViewPlane.CutPlane);
            return new ViewSetup(
                view,
                level,
                phaseId,
                frame,
                t,
                ImageRegionsOf(view, crop, shape),
                cutZ,
                PlaneZ(view, level, PlanViewPlane.BottomClipPlane),
                SameFloorLimit(view, level, cutZ));
        }

        /// <summary>
        /// The crop frame must be the screen's right and up, or the image and
        /// the mask would be rotated against each other. Holds for plan views,
        /// rotated crops and true north included; checked anyway because a
        /// silent rotation is the costliest failure there is.
        /// </summary>
        private static void RequireScreenAligned(View view, Transform t)
        {
            const double Tolerance = 1e-6;
            if (!t.BasisX.IsAlmostEqualTo(view.RightDirection, Tolerance) || !t.BasisY.IsAlmostEqualTo(view.UpDirection, Tolerance))
            {
                throw new ViewNotExportableException(
                    $"O referencial do recorte ({t.BasisX}, {t.BasisY}) não coincide com a direita/cima da vista "
                    + $"({view.RightDirection}, {view.UpDirection}); a imagem sairia girada em relação à máscara.");
            }
        }

        /// <summary>A model point (feet) in the view frame (meters).</summary>
        internal Point2D ToFrame(XYZ modelFeet) => Frame.ToView(UnitsConverter.ToMetersPoint(modelFeet));

        /// <summary>A model point already in meters, in the view frame.</summary>
        internal Point2D ToFrameMeters(Point2D modelMeters) => Frame.ToView(modelMeters);

        private static IReadOnlyList<ViewRegion> ImageRegionsOf(ViewPlan view, BoundingBoxXYZ crop, ViewCropRegionShapeManager shape)
        {
            var model = new ViewRegion(
                "recorte do modelo",
                UnitsConverter.ToMeters(crop.Min.X),
                UnitsConverter.ToMeters(crop.Min.Y),
                UnitsConverter.ToMeters(crop.Max.X),
                UnitsConverter.ToMeters(crop.Max.Y));

            bool annotationActive = shape.CanHaveAnnotationCrop
                && view.get_Parameter(BuiltInParameter.VIEWER_ANNOTATION_CROP_ACTIVE)?.AsInteger() == 1;
            if (!annotationActive)
            {
                return new[] { model };
            }

            // Annotation offsets are paper lengths: times the view scale for model lengths.
            double M(double paperFeet) => UnitsConverter.ToMeters(paperFeet * view.Scale);
            var annotation = new ViewRegion(
                "recorte de anotação",
                model.MinX - M(shape.LeftAnnotationCropOffset),
                model.MinY - M(shape.BottomAnnotationCropOffset),
                model.MaxX + M(shape.RightAnnotationCropOffset),
                model.MaxY + M(shape.TopAnnotationCropOffset));
            return new[] { annotation, model };
        }

        private static double SameFloorLimit(ViewPlan view, Level level, double cutZ)
        {
            PlanViewRange range = view.GetViewRange();
            return view.Document.GetElement(range.GetLevelId(PlanViewPlane.TopClipPlane)) is Level top
                ? top.ProjectElevation + range.GetOffset(PlanViewPlane.TopClipPlane)
                : cutZ + (cutZ - level.ProjectElevation);
        }

        /// <summary>Absolute height (feet) of one plane of the view range: its level plus its offset.</summary>
        private static double PlaneZ(ViewPlan view, Level viewLevel, PlanViewPlane plane)
        {
            PlanViewRange range = view.GetViewRange();
            ElementId levelId = range.GetLevelId(plane);
            Level planeLevel = view.Document.GetElement(levelId) as Level ?? viewLevel;
            return planeLevel.ProjectElevation + range.GetOffset(plane);
        }
    }

    /// <summary>The active view cannot become a sample; the message is shown to the user as is.</summary>
    internal sealed class ViewNotExportableException : Exception
    {
        internal ViewNotExportableException(string message)
            : base(message)
        {
        }
    }
}
