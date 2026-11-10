using System;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SW2URDF.URDFExport
{
    /// <summary>
    /// Runs the 3DXML to OBJ conversion on a worker thread. When a window is open, a small modal
    /// dialog with a progress bar and a Cancel button keeps the UI thread free to pump messages;
    /// otherwise (tests, command-line tools) the caller simply waits for the worker.
    /// </summary>
    internal static class ObjConversionRunner
    {
        public static void Run(string source, string target, string linkName, CancellationToken externalToken)
        {
            using (var cancel = CancellationTokenSource.CreateLinkedTokenSource(externalToken))
            {
                IProgress<double> progress = null;
                ProgressDialog dialog = null;
                if (Application.OpenForms.Count > 0)
                {
                    dialog = new ProgressDialog(cancel);
                    progress = new Progress<double>(dialog.SetFraction);   // captures the UI context
                }
                Task<ObjConversionResult> task = ObjConverter.ConvertAsync(source, target, new ObjConversionOptions
                {
                    LinkName = linkName,
                    Progress = progress,
                    CancellationToken = cancel.Token
                });
                if (dialog != null)
                {
                    task.ContinueWith(t => dialog.CloseFromWorker(), TaskScheduler.Default);
                    using (dialog) dialog.ShowDialog(Form.ActiveForm);
                }
                try
                {
                    task.GetAwaiter().GetResult();
                }
                catch (OperationCanceledException)
                {
                    throw new OperationCanceledException("OBJ conversion was cancelled; existing output was kept.");
                }
            }
        }

        private sealed class ProgressDialog : Form
        {
            private readonly ProgressBar bar = new ProgressBar { Dock = DockStyle.Top, Maximum = 1000, Height = 24 };
            private readonly CancellationTokenSource cancel;
            private volatile bool finished;

            public ProgressDialog(CancellationTokenSource cancel)
            {
                this.cancel = cancel;
                Text = "Converting mesh to OBJ";
                ClientSize = new Size(360, 96);
                FormBorderStyle = FormBorderStyle.FixedDialog;
                StartPosition = FormStartPosition.CenterParent;
                ControlBox = false;
                MinimizeBox = MaximizeBox = false;
                ShowInTaskbar = false;
                var button = new Button { Text = "Cancel", Anchor = AnchorStyles.Bottom | AnchorStyles.Right, Location = new Point(270, 60) };
                button.Click += (s, e) => { button.Enabled = false; cancel.Cancel(); };
                var label = new Label { Text = "Converting 3DXML to OBJ + MTL...", Dock = DockStyle.Top, Height = 28, Padding = new Padding(8, 8, 8, 0) };
                Controls.Add(button);
                Controls.Add(bar);
                Controls.Add(label);
            }

            public void SetFraction(double fraction)
            {
                if (InvokeRequired) { BeginInvoke(new Action<double>(SetFraction), fraction); return; }
                bar.Value = Math.Max(0, Math.Min(bar.Maximum, (int)(fraction * bar.Maximum)));
            }

            public void CloseFromWorker()
            {
                // The task may complete before the dialog handle exists.
                finished = true;
                if (IsHandleCreated) BeginInvoke(new Action(Close));
            }

            protected override void OnShown(EventArgs e)
            {
                base.OnShown(e);
                if (finished) Close();
            }
        }
    }
}
