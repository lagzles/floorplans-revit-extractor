using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Autodesk.Revit.DB;
using FloorplansRevitExtractor.Core.Sample;
using FloorplansRevitExtractor.ViewExport;
using WinForm = System.Windows.Forms.Form;

namespace FloorplansRevitExtractor.UI
{
    /// <summary>
    /// Picks the views and resolutions of a batch: a tree grouped like the
    /// Project Browser, one checkbox per group and per view. Views on sheets
    /// come checked; views that cannot be exported are greyed out with the
    /// reason and cannot be checked.
    /// </summary>
    internal sealed class BatchExportForm : WinForm
    {
        private readonly TreeView _tree;
        private readonly List<CheckBox> _resolutionBoxes = new List<CheckBox>();
        private readonly Label _summary;
        private readonly Button _okButton;
        private bool _cascading;

        internal BatchExportForm(IReadOnlyList<ViewEntry> entries, string outDir)
        {
            Text = "Exportar várias vistas";
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            ClientSize = new Size(640, 600);
            MinimumSize = new Size(520, 480);

            _tree = new TreeView
            {
                CheckBoxes = true,
                Left = 12,
                Top = 12,
                Width = 616,
                Height = 380,
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                ShowNodeToolTips = true,
            };
            _tree.BeforeCheck += (s, e) => e.Cancel = e.Node.Tag is ViewEntry entry && !entry.Exportable;
            _tree.AfterCheck += OnAfterCheck;
            Fill(entries);

            // Checkbox states set before the window exists are lost by the TreeView: the defaults go in once it is shown.
            Shown += (s, e) => CheckViews(entry => entry.OnSheet, expandChecked: true);

            var onSheets = new Button { Text = "Marcar as que estão em folhas", Left = 12, Top = 400, Width = 200, Anchor = AnchorStyles.Bottom | AnchorStyles.Left };
            onSheets.Click += (s, e) => CheckViews(entry => entry.OnSheet, expandChecked: true);
            var all = new Button { Text = "Marcar todas", Left = 220, Top = 400, Width = 110, Anchor = AnchorStyles.Bottom | AnchorStyles.Left };
            all.Click += (s, e) => CheckViews(entry => true);
            var none = new Button { Text = "Desmarcar todas", Left = 338, Top = 400, Width = 120, Anchor = AnchorStyles.Bottom | AnchorStyles.Left };
            none.Click += (s, e) => CheckViews(entry => false);

            var resolutions = new GroupBox { Text = "Resoluções (spec v4 §6)", Left = 12, Top = 436, Width = 616, Height = 56, Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right };
            int left = 12;
            foreach (SampleResolution resolution in SampleResolution.Bands)
            {
                var box = new CheckBox
                {
                    Text = $"{resolution.Name}: {resolution.PxPerM:0} px/m ({resolution.Band})",
                    Left = left,
                    Top = 22,
                    Width = 196,
                    Checked = resolution.PxPerM == SampleResolution.DefaultPxPerM,
                    Tag = resolution,
                };
                box.CheckedChanged += (s, e) => UpdateSummary();
                _resolutionBoxes.Add(box);
                resolutions.Controls.Add(box);
                left += 200;
            }

            _summary = new Label { Left = 12, Top = 500, Width = 616, Height = 40, Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right };
            var folder = new Label { Text = "Pasta do lote: " + outDir, Left = 12, Top = 540, Width = 420, Height = 40, Anchor = AnchorStyles.Bottom | AnchorStyles.Left, ForeColor = SystemColors.GrayText };
            _okButton = new Button { Text = "Exportar", Left = 448, Top = 560, Width = 88, DialogResult = DialogResult.OK, Anchor = AnchorStyles.Bottom | AnchorStyles.Right };
            var cancelButton = new Button { Text = "Cancelar", Left = 540, Top = 560, Width = 88, DialogResult = DialogResult.Cancel, Anchor = AnchorStyles.Bottom | AnchorStyles.Right };

            Controls.AddRange(new System.Windows.Forms.Control[] { _tree, onSheets, all, none, resolutions, _summary, folder, _okButton, cancelButton });
            AcceptButton = _okButton;
            CancelButton = cancelButton;
            UpdateSummary();
        }

        internal IReadOnlyList<ViewPlan> SelectedViews =>
            ViewNodes().Where(n => n.Checked).Select(n => ((ViewEntry)n.Tag).View).ToList();

        internal IReadOnlyList<SampleResolution> SelectedResolutions =>
            _resolutionBoxes.Where(b => b.Checked).Select(b => (SampleResolution)b.Tag).ToList();

        private void Fill(IReadOnlyList<ViewEntry> entries)
        {
            _tree.BeginUpdate();
            foreach (IGrouping<string, ViewEntry> group in entries.GroupBy(e => e.Group))
            {
                var groupNode = new TreeNode(group.Key);
                foreach (ViewEntry entry in group)
                {
                    string sheets = entry.OnSheet ? $"  (folha {string.Join(", ", entry.Sheets)})" : string.Empty;
                    var node = new TreeNode(entry.View.Name + sheets)
                    {
                        Tag = entry,
                        ToolTipText = entry.Blocked ?? string.Empty,
                    };
                    if (!entry.Exportable)
                    {
                        // The reason is in the tooltip: the list stays as readable as the Project Browser.
                        node.ForeColor = SystemColors.GrayText;
                        node.Text += "  — não exportável";
                    }

                    groupNode.Nodes.Add(node);
                }

                _tree.Nodes.Add(groupNode);
            }

            _tree.EndUpdate();
        }

        /// <summary>A group's checkbox checks or clears its exportable views; a view's keeps its group in step.</summary>
        private void OnAfterCheck(object sender, TreeViewEventArgs e)
        {
            if (_cascading)
            {
                return;
            }

            _cascading = true;
            if (e.Node.Tag == null)
            {
                foreach (TreeNode child in e.Node.Nodes)
                {
                    child.Checked = e.Node.Checked && ((ViewEntry)child.Tag).Exportable;
                }
            }
            else
            {
                e.Node.Parent.Checked = e.Node.Parent.Nodes.Cast<TreeNode>().Any(n => n.Checked);
            }

            _cascading = false;
            UpdateSummary();
        }

        /// <param name="expandChecked">Open the groups with a checked view and close the others, as on opening.</param>
        private void CheckViews(Func<ViewEntry, bool> predicate, bool expandChecked = false)
        {
            _cascading = true;
            foreach (TreeNode node in ViewNodes())
            {
                var entry = (ViewEntry)node.Tag;
                node.Checked = entry.Exportable && predicate(entry);
            }

            foreach (TreeNode group in _tree.Nodes)
            {
                group.Checked = group.Nodes.Cast<TreeNode>().Any(n => n.Checked);
                if (expandChecked && group.Checked)
                {
                    group.Expand();
                }
                else if (expandChecked)
                {
                    group.Collapse();
                }
            }

            _cascading = false;
            UpdateSummary();
        }

        private IEnumerable<TreeNode> ViewNodes() => _tree.Nodes.Cast<TreeNode>().SelectMany(g => g.Nodes.Cast<TreeNode>());

        private void UpdateSummary()
        {
            int views = ViewNodes().Count(n => n.Checked);
            int resolutions = _resolutionBoxes.Count(b => b.Checked);
            _summary.Text = $"{views} vista(s) × {resolutions} resolução(ões) = {views * resolutions} amostra(s). "
                + "Cada vista leva de alguns segundos a um minuto; o Revit fica ocupado até o fim (dá para cancelar entre vistas).";
            _okButton.Enabled = views * resolutions > 0;
        }
    }
}
