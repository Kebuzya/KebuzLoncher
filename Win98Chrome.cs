using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace KebuzLoncher
{
    internal enum TitleButtonType { Minimize, Maximize, Close }

    internal sealed class Win98Button : Control
    {
        private bool _pressed;
        public readonly TitleButtonType ButtonType;
        public bool IsRestore { get; set; }
        public new event EventHandler Click;

        public Win98Button(TitleButtonType type)
        {
            ButtonType = type;
            Size = new Size(20, 18);
            BackColor = Color.Silver;
            ForeColor = Color.Black;
            TabStop = false;
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.UserPaint |
                ControlStyles.OptimizedDoubleBuffer, true);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left) { _pressed = true; Invalidate(); }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            bool was = _pressed;
            _pressed = false;
            Invalidate();
            if (was && e.Button == MouseButtons.Left && ClientRectangle.Contains(e.Location))
                Click?.Invoke(this, EventArgs.Empty);
        }

        protected override void OnMouseLeave(EventArgs e) { _pressed = false; Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            var r = ClientRectangle;
            int d = _pressed ? 1 : 0;

            using (var brush = new SolidBrush(BackColor)) g.FillRectangle(brush, r);
            RetroButton.DrawRetro3D(g, r, !_pressed, BackColor);

            int cx = r.Width / 2 + d;
            int cy = r.Height / 2 + d;

            using (var pen = new Pen(ForeColor, 1))
            {
                switch (ButtonType)
                {
                    case TitleButtonType.Close:
                        g.DrawLine(pen, cx - 4, cy - 4, cx + 4, cy + 4);
                        g.DrawLine(pen, cx - 3, cy - 4, cx + 4, cy + 3);
                        g.DrawLine(pen, cx + 4, cy - 4, cx - 4, cy + 4);
                        g.DrawLine(pen, cx + 3, cy - 4, cx - 4, cy + 3);
                        break;

                    case TitleButtonType.Minimize:
                        using (var brush = new SolidBrush(ForeColor)) g.FillRectangle(brush, cx - 4, cy + 1, 9, 2);
                        break;

                    case TitleButtonType.Maximize:
                        if (!IsRestore)
                        {
                            g.DrawRectangle(pen, cx - 5, cy - 4, 10, 7);
                            g.DrawLine(pen, cx - 4, cy - 3, cx + 4, cy - 3);
                        }
                        else
                        {
                            g.DrawRectangle(pen, cx - 2, cy - 5, 6, 5);
                            g.DrawLine(pen, cx - 1, cy - 4, cx + 3, cy - 4);
                            using (var brush = new SolidBrush(BackColor)) g.FillRectangle(brush, cx - 5, cy - 2, 5, 3);
                            g.DrawRectangle(pen, cx - 5, cy - 2, 6, 5);
                            g.DrawLine(pen, cx - 4, cy - 1, cx - 2, cy - 1);
                        }
                        break;
                }
            }
        }

    }

    internal sealed class Win98TitleBar : Panel
    {
        private bool _isActive = true;
        private readonly Form _form;
        private readonly Label _titleLabel;
        public readonly Win98Button BtnMin, BtnMax, BtnClose;

        private Color _activeGradientStart;
        private Color _activeGradientEnd;
        private Color _inactiveGradientStart;
        private Color _inactiveGradientEnd;

        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int message, IntPtr wParam, IntPtr lParam);

        public Win98TitleBar(Form form)
        {
            _form = form;
            Height = 28;
            Dock = DockStyle.Top;

            // Установка цветов по умолчанию (классическая тема)
            _activeGradientStart = Color.FromArgb(10, 36, 106);
            _activeGradientEnd = Color.FromArgb(166, 202, 240);
            _inactiveGradientStart = Color.FromArgb(119, 119, 119);
            _inactiveGradientEnd = Color.FromArgb(185, 185, 185);

            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.UserPaint |
                ControlStyles.OptimizedDoubleBuffer, true);
            ResizeRedraw = true;

            _titleLabel = new Label
            {
                AutoSize = false,
                Location = new Point(6, 5),
                Height = 18,
                Font = new Font("MS Sans Serif", 9f, FontStyle.Bold),
                ForeColor = Color.White,
                BackColor = Color.Transparent,
                Text = form.Text
            };

            form.TextChanged += (s, e) => _titleLabel.Text = form.Text;
            form.Activated += (s, e) => { _isActive = true; Invalidate(); };
            form.Deactivate += (s, e) => { _isActive = false; Invalidate(); };
            form.Resize += (s, e) => UpdateMaxButton();

            BtnMin = new Win98Button(TitleButtonType.Minimize);
            BtnMax = new Win98Button(TitleButtonType.Maximize);
            BtnClose = new Win98Button(TitleButtonType.Close);

            BtnMin.Click += (s, e) => form.WindowState = FormWindowState.Minimized;
            BtnMax.Click += (s, e) =>
                form.WindowState = form.WindowState == FormWindowState.Maximized
                    ? FormWindowState.Normal : FormWindowState.Maximized;
            BtnClose.Click += (s, e) => form.Close();

            Controls.AddRange(new Control[] { _titleLabel, BtnMin, BtnMax, BtnClose });

            Hook(this);
            Hook(_titleLabel);

            SizeChanged += (s, e) => Arrange();
        }

        public void UpdateTheme(string theme)
        {
            foreach (var button in new[] { BtnMin, BtnMax, BtnClose })
            {
                button.BackColor = theme == "Dark" ? Color.FromArgb(70, 70, 74) : Color.Silver;
                button.ForeColor = theme == "Dark" ? Color.Gainsboro : Color.Black;
                button.Invalidate();
            }
            if (theme == "Dark")
            {
                _activeGradientStart = Color.FromArgb(0, 70, 140);
                _activeGradientEnd = Color.FromArgb(0, 120, 200);
                _inactiveGradientStart = Color.FromArgb(70, 70, 70);
                _inactiveGradientEnd = Color.FromArgb(100, 100, 100);
            }
            else
            {
                _activeGradientStart = Color.FromArgb(10, 36, 106);
                _activeGradientEnd = Color.FromArgb(166, 202, 240);
                _inactiveGradientStart = Color.FromArgb(119, 119, 119);
                _inactiveGradientEnd = Color.FromArgb(185, 185, 185);
            }
            Invalidate();
        }

        private void Hook(Control c)
        {
            c.MouseDown += OnDragStart;
            c.DoubleClick += OnTitleDoubleClick;
        }

        private void OnTitleDoubleClick(object sender, EventArgs e)
        {
            _form.WindowState = _form.WindowState == FormWindowState.Maximized
                ? FormWindowState.Normal : FormWindowState.Maximized;
        }

        private void Arrange()
        {
            BtnMin.Size = new Size(20, 18);
            BtnMax.Size = new Size(20, 18);
            BtnClose.Size = new Size(20, 18);

            int x = Width - 6;
            foreach (var btn in new Win98Button[] { BtnClose, BtnMax, BtnMin })
            {
                x -= btn.Width + 2;
                btn.Location = new Point(x, (Height - btn.Height) / 2);
            }
            _titleLabel.Width = BtnMin.Left - 10;
        }

        private void UpdateMaxButton()
        {
            BtnMax.IsRestore = _form.WindowState == FormWindowState.Maximized;
            BtnMax.Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            var r = ClientRectangle;
            if (r.Width <= 0 || r.Height <= 0) return;

            Color c1 = _isActive ? _activeGradientStart : _inactiveGradientStart;
            Color c2 = _isActive ? _activeGradientEnd : _inactiveGradientEnd;

            // Inflate на 1px чтобы WrapMode.Clamp не давал артефактов на краях
            var brushRect = new Rectangle(r.X, r.Y, r.Width + 1, r.Height + 1);
            using (var brush = new LinearGradientBrush(brushRect, c1, c2, LinearGradientMode.Horizontal))
            {
                brush.WrapMode = System.Drawing.Drawing2D.WrapMode.TileFlipX;
                g.FillRectangle(brush, r);
            }
        }

        private void OnDragStart(object sender, MouseEventArgs e)
        {
            var screenPoint = ((Control)sender).PointToScreen(e.Location);
            var packed = (IntPtr)((screenPoint.Y << 16) | (screenPoint.X & 0xffff));
            if (e.Button == MouseButtons.Right)
            {
                SendMessage(_form.Handle, 0x00A5, (IntPtr)2, packed); // WM_NCRBUTTONUP, HTCAPTION
                return;
            }
            if (e.Button != MouseButtons.Left) return;
            ReleaseCapture();
            int hit = _form.WindowState == FormWindowState.Normal &&
                _form.PointToClient(screenPoint).Y < Math.Max(4, SystemInformation.FrameBorderSize.Height)
                ? 12 : 2; // HTTOP or HTCAPTION
            SendMessage(_form.Handle, 0x00A1, (IntPtr)hit, packed); // native move/resize loop, including Aero Snap
        }

        public static Win98TitleBar AttachToForm(Form form)
        {
            var bar = new Win98TitleBar(form);
            form.Controls.Add(bar);
            bar.BringToFront();
            return bar;
        }
    }

    internal class DarkPanel : Panel
    {
        public DarkPanel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer, true);
            this.BorderStyle = BorderStyle.FixedSingle;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            // Просто вызываем базовую отрисовку
            base.OnPaint(e);

            // Перерисовываем границу поверх стандартной
            Rectangle rect = new Rectangle(0, 0, this.Width - 1, this.Height - 1);

            // Левая и верхняя - светлые
            using (Pen lightPen = new Pen(Color.FromArgb(100, 100, 100)))
            {
                e.Graphics.DrawLine(lightPen, rect.Left, rect.Top, rect.Right, rect.Top);
                e.Graphics.DrawLine(lightPen, rect.Left, rect.Top, rect.Left, rect.Bottom);
            }

            // Правая и нижняя - темные
            using (Pen darkPen = new Pen(Color.FromArgb(45, 45, 48)))
            {
                e.Graphics.DrawLine(darkPen, rect.Right, rect.Top, rect.Right, rect.Bottom);
                e.Graphics.DrawLine(darkPen, rect.Left, rect.Bottom, rect.Right, rect.Bottom);
            }
        }
    }

    internal sealed class RetroButton : Button
    {
        private bool _pressed;

        public RetroButton()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.UserPaint |
                ControlStyles.OptimizedDoubleBuffer, true);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left) { _pressed = true; Invalidate(); }
            base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            _pressed = false; Invalidate();
            base.OnMouseUp(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _pressed = false; Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            var r = ClientRectangle;

            using (var b = new SolidBrush(BackColor))
                g.FillRectangle(b, r);

            DrawRetro3D(g, r, !_pressed, BackColor);

            int d = _pressed ? 1 : 0;
            Color fg = Enabled ? ForeColor : SystemColors.GrayText;
            TextRenderer.DrawText(g, Text, Font,
                new Rectangle(r.X + d, r.Y + d, r.Width, r.Height), fg,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);

            if (Focused && ShowFocusCues)
                ControlPaint.DrawFocusRectangle(g, new Rectangle(r.X + 3, r.Y + 3, r.Width - 7, r.Height - 7));
        }

        internal static void DrawRetro3D(Graphics g, Rectangle r, bool raised, Color baseColor)
        {
            Color tlOut = raised ? ControlPaint.LightLight(baseColor) : ControlPaint.DarkDark(baseColor);
            Color tlIn = raised ? ControlPaint.Light(baseColor) : ControlPaint.Dark(baseColor);
            Color brIn = raised ? ControlPaint.Dark(baseColor) : ControlPaint.Light(baseColor);
            Color brOut = raised ? ControlPaint.DarkDark(baseColor) : ControlPaint.LightLight(baseColor);

            using (var pTlOut = new Pen(tlOut))
            using (var pTlIn = new Pen(tlIn))
            using (var pBrIn = new Pen(brIn))
            using (var pBrOut = new Pen(brOut))
            {
                g.DrawLine(pTlOut, r.Left, r.Top, r.Right - 1, r.Top);
                g.DrawLine(pTlOut, r.Left, r.Top, r.Left, r.Bottom - 1);
                g.DrawLine(pBrOut, r.Right - 1, r.Top, r.Right - 1, r.Bottom - 1);
                g.DrawLine(pBrOut, r.Left, r.Bottom - 1, r.Right - 1, r.Bottom - 1);
                g.DrawLine(pTlIn, r.Left + 1, r.Top + 1, r.Right - 2, r.Top + 1);
                g.DrawLine(pTlIn, r.Left + 1, r.Top + 1, r.Left + 1, r.Bottom - 2);
                g.DrawLine(pBrIn, r.Right - 2, r.Top + 1, r.Right - 2, r.Bottom - 2);
                g.DrawLine(pBrIn, r.Left + 1, r.Bottom - 2, r.Right - 2, r.Bottom - 2);
            }
        }
    }

    internal static class Win98Chrome
    {
        public static Win98TitleBar Attach(Form form)
        {
            var bar = new Win98TitleBar(form);
            form.Controls.Add(bar);
            bar.BringToFront();
            return bar;
        }
    }
}
