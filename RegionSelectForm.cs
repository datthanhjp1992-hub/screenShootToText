using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace ScreenOcr
{
    /// <summary>
    /// Màn hình phủ toàn bộ (mọi monitor) hiển thị ảnh đã "đóng băng",
    /// người dùng kéo chuột để chọn vùng. Esc / chuột phải = huỷ.
    /// </summary>
    internal sealed class RegionSelectForm : Form
    {
        private readonly Bitmap _screen;
        private readonly Bitmap _dimmed;
        private Point _start;
        private Rectangle _sel = Rectangle.Empty;
        private bool _dragging;

        public Bitmap Result { get; private set; }

        public static Bitmap CaptureVirtualScreen()
        {
            var vs = SystemInformation.VirtualScreen;
            var bmp = new Bitmap(vs.Width, vs.Height, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
                g.CopyFromScreen(vs.Left, vs.Top, 0, 0, vs.Size, CopyPixelOperation.SourceCopy);
            return bmp;
        }

        public RegionSelectForm(Bitmap screen)
        {
            _screen = screen;
            _dimmed = new Bitmap(screen.Width, screen.Height, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(_dimmed))
            using (var b = new SolidBrush(Color.FromArgb(120, 0, 0, 0)))
            {
                g.DrawImage(screen, new Rectangle(0, 0, screen.Width, screen.Height));
                g.FillRectangle(b, 0, 0, screen.Width, screen.Height);
            }

            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            KeyPreview = true;
            Cursor = Cursors.Cross;
            DoubleBuffered = true;
            Bounds = SystemInformation.VirtualScreen;
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Bounds = SystemInformation.VirtualScreen;
            Activate();
        }

        protected override void WndProc(ref Message m)
        {
            // Không để WinForms tự co giãn cửa sổ khi đi qua màn hình khác DPI
            if (m.Msg == NativeMethods.WM_DPICHANGED) return;
            base.WndProc(ref m);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.DrawImage(_dimmed, new Rectangle(0, 0, _dimmed.Width, _dimmed.Height));

            if (_sel.Width > 0 && _sel.Height > 0)
            {
                g.DrawImage(_screen, _sel, _sel, GraphicsUnit.Pixel);
                using (var pen = new Pen(Color.DeepSkyBlue, 2))
                    g.DrawRectangle(pen, _sel);

                var label = $"{_sel.Width} × {_sel.Height}";
                var size = g.MeasureString(label, Font);
                var y = _sel.Top - size.Height - 4 < 0 ? _sel.Bottom + 4 : _sel.Top - size.Height - 4;
                g.FillRectangle(Brushes.Black, _sel.Left, y, size.Width + 6, size.Height + 2);
                g.DrawString(label, Font, Brushes.White, _sel.Left + 3, y + 1);
            }
            else
            {
                const string hint = "Kéo chuột để chọn vùng cần OCR  —  Esc / chuột phải để huỷ";
                using (var f = new Font("Segoe UI", 14f))
                {
                    var size = g.MeasureString(hint, f);
                    // hiển thị ở giữa màn hình chính
                    var p = Screen.PrimaryScreen.Bounds;
                    var vs = SystemInformation.VirtualScreen;
                    float x = p.Left - vs.Left + (p.Width - size.Width) / 2;
                    float y = p.Top - vs.Top + 40;
                    g.FillRectangle(Brushes.Black, x - 10, y - 6, size.Width + 20, size.Height + 12);
                    g.DrawString(hint, f, Brushes.White, x, y);
                }
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right) { Cancel(); return; }
            if (e.Button != MouseButtons.Left) return;
            _dragging = true;
            _start = e.Location;
            _sel = new Rectangle(e.Location, Size.Empty);
            Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (!_dragging) return;
            var old = _sel;
            _sel = Rectangle.FromLTRB(
                Math.Min(_start.X, e.X), Math.Min(_start.Y, e.Y),
                Math.Max(_start.X, e.X), Math.Max(_start.Y, e.Y));
            _sel.Intersect(new Rectangle(0, 0, _screen.Width, _screen.Height));

            var dirty = Rectangle.Union(old, _sel);
            dirty.Inflate(4, 40);
            Invalidate(dirty);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (!_dragging || e.Button != MouseButtons.Left) return;
            _dragging = false;

            if (_sel.Width < 5 || _sel.Height < 5)
            {
                _sel = Rectangle.Empty;
                Invalidate();
                return;
            }

            Result = _screen.Clone(_sel, PixelFormat.Format32bppArgb);
            DialogResult = DialogResult.OK;
            Close();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape) Cancel();
            base.OnKeyDown(e);
        }

        private void Cancel()
        {
            DialogResult = DialogResult.Cancel;
            Close();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _dimmed.Dispose();
            base.Dispose(disposing);
        }
    }
}
