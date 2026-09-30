using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using Autodesk.Revit.DB;
using WinForm = System.Windows.Forms.Form;

namespace FloorplansRevitExtractor.UI
{
    /// <summary>Minimal level picker, used only when more than one level has rooms.</summary>
    internal sealed class LevelPickerForm : WinForm
    {
        private readonly ListBox _listBox;
        private readonly IReadOnlyList<Level> _levels;

        internal LevelPickerForm(IReadOnlyList<(Level Level, int RoomCount)> candidates)
        {
            _levels = ToLevelList(candidates);

            Text = "Escolher nível";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            MaximizeBox = false;
            ClientSize = new Size(320, 220);

            _listBox = new ListBox { Left = 12, Top = 12, Width = 296, Height = 160 };
            foreach ((Level level, int roomCount) in candidates)
            {
                _listBox.Items.Add($"{level.Name} ({roomCount} ambientes)");
            }

            if (_listBox.Items.Count > 0)
            {
                _listBox.SelectedIndex = 0;
            }

            var okButton = new Button { Text = "OK", Left = 132, Top = 182, Width = 80, DialogResult = DialogResult.OK };
            var cancelButton = new Button { Text = "Cancelar", Left = 220, Top = 182, Width = 88, DialogResult = DialogResult.Cancel };

            Controls.Add(_listBox);
            Controls.Add(okButton);
            Controls.Add(cancelButton);
            AcceptButton = okButton;
            CancelButton = cancelButton;
        }

        internal Level SelectedLevel =>
            _listBox.SelectedIndex >= 0 ? _levels[_listBox.SelectedIndex] : null;

        private static IReadOnlyList<Level> ToLevelList(IReadOnlyList<(Level Level, int RoomCount)> candidates)
        {
            var levels = new List<Level>(candidates.Count);
            foreach ((Level level, int _) in candidates)
            {
                levels.Add(level);
            }

            return levels;
        }
    }
}
