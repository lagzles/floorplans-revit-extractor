using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using FloorplansRevitExtractor.Core.Geometry;
using FloorplansRevitExtractor.Core.Model;
using FloorplansRevitExtractor.Core.Raster;
using FloorplansRevitExtractor.RevitInterop;

namespace FloorplansRevitExtractor.ViewExport
{
    /// <summary>What the text of a view contributes to the mask.</summary>
    internal sealed record TextReading(IReadOnlyList<GeomElement> Elements, IReadOnlyList<GeomWarning> Warnings, IReadOnlyList<string> Notes);

    /// <summary>
    /// Spec v4 class 13: every letter that is not a dimension value or grid
    /// letter, as the tight box of its ink (InkBoxes): text notes and the text
    /// of tags (rooms, doors, windows and every other tag). A tag's leader and
    /// frame stay out: a leadered tag is boxed around its head only, and the
    /// box is inset from the frame. Spot elevations stay out in this version.
    /// </summary>
    internal static class TextReader
    {
        /// <summary>Share of the box height cut from each side of a tag's box: its frame sits there.</summary>
        private const double TagFrameInset = 0.15;

        /// <summary>Half the box kept around a leadered tag's head, on paper (8 mm).</summary>
        private const double LeaderedHeadHalfPaperFeet = 0.026;

        internal static TextReading Read(Document doc, ViewSetup setup, InkBoxes ink)
        {
            var elements = new List<GeomElement>();
            var warnings = new List<GeomWarning>();
            int noInk = 0;
            int spots = 0;
            View view = setup.View;
            var viewX = new Vector2D(1, 0);

            void Add(Element element, IReadOnlyList<Point2D> box, string text)
            {
                if (box == null)
                {
                    noInk++;
                    return;
                }

                List<IReadOnlyList<Point2D>> clipped = CutSection.Clip(new[] { box }, setup.ModelRegion);
                if (clipped.Count > 0)
                {
                    elements.Add(GeomElement.Filled(StructureClass.Text, element.Id.Value, clipped).WithLabel(null, text));
                }
            }

            IEnumerable<TextNote> notes = new FilteredElementCollector(doc, view.Id).OfClass(typeof(TextNote)).Cast<TextNote>().OrderBy(t => t.Id.Value);
            ElementGuard.Each(notes, warnings, note =>
            {
                Vector2D direction = setup.Frame.ToViewDirection(new Vector2D(note.BaseDirection.X, note.BaseDirection.Y));
                Add(note, ink.FitBoundingBox(note, view, setup, direction, 0), note.Text?.Trim());
            });

            IEnumerable<IndependentTag> tags = new FilteredElementCollector(doc, view.Id).OfClass(typeof(IndependentTag)).Cast<IndependentTag>().OrderBy(t => t.Id.Value);
            ElementGuard.Each(tags, warnings, tag =>
            {
                Vector2D direction = tag.TagOrientation == TagOrientation.Vertical ? new Vector2D(0, 1) : viewX;
                Add(tag, TagBox(tag, tag.HasLeader, tag.TagHeadPosition, direction, setup, ink), tag.TagText);
            });

            IEnumerable<SpatialElementTag> roomTags = new FilteredElementCollector(doc, view.Id).OfClass(typeof(SpatialElementTag)).Cast<SpatialElementTag>().OrderBy(t => t.Id.Value);
            ElementGuard.Each(roomTags, warnings, tag =>
            {
                Vector2D direction = tag.TagOrientation == SpatialElementTagOrientation.Vertical ? new Vector2D(0, 1) : viewX;
                Add(tag, TagBox(tag, tag.HasLeader, tag.TagHeadPosition, direction, setup, ink), tag.TagText);
            });

            spots = new FilteredElementCollector(doc, view.Id).OfCategory(BuiltInCategory.OST_SpotElevations).WhereElementIsNotElementType().GetElementCount();

            var readingNotes = new List<string> { $"Texto (13): {elements.Count} caixa(s) apertada(s) pela tinta da imagem." };
            if (noInk > 0)
            {
                readingNotes.Add($"{noInk} texto(s)/tag(s) sem tinta visível na caixa ficaram fora (fora do recorte ou oculto).");
            }

            if (spots > 0)
            {
                readingNotes.Add($"{spots} cota(s) de nível (spot elevation) ficaram fora do texto nesta versão.");
            }

            return new TextReading(elements, warnings, readingNotes);
        }

        /// <summary>
        /// The tag's view box, inset from its frame. With a leader, the box
        /// would run along the leader: a square around the head is used instead.
        /// </summary>
        private static IReadOnlyList<Point2D> TagBox(Element tag, bool hasLeader, XYZ head, Vector2D direction, ViewSetup setup, InkBoxes ink)
        {
            if (!hasLeader || head == null)
            {
                return ink.FitBoundingBox(tag, setup.View, setup, direction, TagFrameInset);
            }

            double half = UnitsConverter.ToMeters(LeaderedHeadHalfPaperFeet * setup.View.Scale);
            return ink.Fit(setup.ToFrame(head), direction, half, half);
        }
    }
}
