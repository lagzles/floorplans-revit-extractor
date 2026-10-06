using System.Drawing;
using System.Windows.Forms;
using WinForm = System.Windows.Forms.Form;

namespace FloorplansRevitExtractor.UI
{
    /// <summary>
    /// Modeless progress of a batch. The export runs on Revit's thread, so the
    /// window is repainted, and Cancel read, between views (Step); the view in
    /// progress always finishes.
    /// </summary>
    internal sealed class BatchProgressForm : WinForm
    {
        private readonly Label _label;
        private readonly ProgressBar _bar;

        internal BatchProgressForm(int total)
        {
            Text = "Exportando vistas";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            MaximizeBox = false;
            ControlBox = false;
            ClientSize = new Size(460, 120);

            _label = new Label { Left = 12, Top = 12, Width = 436, Height = 36 };
            _bar = new ProgressBar { Left = 12, Top = 52, Width = 436, Height = 20, Minimum = 0, Maximum = total };
            var cancel = new Button { Text = "Cancelar após esta vista", Left = 288, Top = 84, Width = 160 };
            cancel.Click += (s, e) =>
            {
                CancelRequested = true;
                cancel.Enabled = false;
                _label.Text += "\nCancelando: o lote para depois desta vista.";
            };
            Controls.AddRange(new System.Windows.Forms.Control[] { _label, _bar, cancel });
        }

        internal bool CancelRequested { get; private set; }

        internal void Step(int done, string text)
        {
            _bar.Value = done;
            _label.Text = text;
            System.Windows.Forms.Application.DoEvents();
        }
    }
}
