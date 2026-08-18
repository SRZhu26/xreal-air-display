using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using PhoenixAirViewer.Platform;

namespace PhoenixAirViewer.App
{
    public sealed class FullscreenOutputWindow : Form
    {
        private readonly DisplayInfo _display;

        public FullscreenOutputWindow(DisplayInfo display)
        {
            if (display == null)
            {
                throw new ArgumentNullException("display");
            }

            _display = display;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            KeyPreview = true;
            BackColor = Color.FromArgb(12, 16, 22);
            Bounds = display.Bounds;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
            KeyDown += FullscreenOutputWindow_KeyDown;
        }

        protected override void OnShown(EventArgs e)
        {
            Bounds = _display.Bounds;
            base.OnShown(e);
            Activate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Rectangle client = ClientRectangle;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.Clear(BackColor);

            using (Pen gridPen = new Pen(Color.FromArgb(45, 75, 92), 1.0f))
            using (Pen axisPen = new Pen(Color.FromArgb(180, 220, 235), 2.0f))
            using (Pen borderPen = new Pen(Color.FromArgb(90, 150, 170), 3.0f))
            using (Brush textBrush = new SolidBrush(Color.FromArgb(220, 235, 240)))
            using (Font textFont = new Font(FontFamily.GenericSansSerif, 18.0f, FontStyle.Regular))
            {
                for (int x = 0; x <= client.Width; x += Math.Max(1, client.Width / 12))
                {
                    e.Graphics.DrawLine(gridPen, x, 0, x, client.Height);
                }

                for (int y = 0; y <= client.Height; y += Math.Max(1, client.Height / 8))
                {
                    e.Graphics.DrawLine(gridPen, 0, y, client.Width, y);
                }

                e.Graphics.DrawLine(axisPen, client.Width / 2, 0, client.Width / 2, client.Height);
                e.Graphics.DrawLine(axisPen, 0, client.Height / 2, client.Width, client.Height / 2);
                e.Graphics.DrawRectangle(borderPen, 2, 2, Math.Max(0, client.Width - 5), Math.Max(0, client.Height - 5));
                e.Graphics.DrawString("Phoenix Air Viewer output test", textFont, textBrush, 28, 28);
                e.Graphics.DrawString(_display.DeviceName + "  " + client.Width + "x" + client.Height, textFont, textBrush, 28, 58);
            }
        }

        private void FullscreenOutputWindow_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape)
            {
                Close();
            }
        }
    }
}
