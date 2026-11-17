using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace KebuzLoncher
{
    internal sealed class RetroScrollBar : Control
    {
        private enum ScrollPart { None, Up, Down, Thumb, PageUp, PageDown }

        private readonly Timer repeatTimer = new Timer();
        private ScrollPart pressed;
        private int value;
        private int maximum;
        private int pageSize = 1;
        private int dragOffset;
        private int wheelRemainder;
        private bool dark;

        public event EventHandler ValueChanged;
        public int Maximum => maximum;
        public int SmallChange { get; set; } = 24;
        public int Value
        {
            get => value;
            set
            {
                int next = Math.Max(0, Math.Min(maximum, value));
                if (this.value == next) return;
                this.value = next;
                Invalidate();
                ValueChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public RetroScrollBar()
        {
            Width = SystemInformation.VerticalScrollBarWidth;
            AccessibleRole = AccessibleRole.ScrollBar;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            repeatTimer.Tick += (s, e) =>
            {
                repeatTimer.Interval = 60;
                if (HitTest(PointToClient(Cursor.Position)) == pressed) Step();
            };
        }

        public void SetRange(int maximum, int pageSize, int value)
        {
            this.maximum = Math.Max(0, maximum);
            this.pageSize = Math.Max(1, pageSize);
            this.value = Math.Max(0, Math.Min(this.maximum, value));
            Enabled = this.maximum > 0;
            Invalidate();
        }

        public void ApplyTheme(bool dark)
        {
            this.dark = dark;
            BackColor = dark ? Color.FromArgb(70, 70, 74) : Color.Silver;
            ForeColor = dark ? Color.Gainsboro : Color.Black;
            Invalidate();
        }

        private int ArrowSize => Math.Min(Width, Height / 2);
        private Rectangle Track => new Rectangle(0, ArrowSize, Width, Math.Max(0, Height - ArrowSize * 2));
        private Rectangle Thumb
        {
            get
            {
                var track = Track;
                int height = Math.Min(track.Height, Math.Max(Width, (int)((long)track.Height * pageSize / ((long)maximum + pageSize))));
                int y = maximum == 0 ? 0 : (int)((long)(track.Height - height) * value / maximum);
                return new Rectangle(0, track.Top + y, Width, height);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var track = Track;
            using (var hatch = new HatchBrush(HatchStyle.Percent50,
                dark ? Color.FromArgb(42, 42, 45) : Color.Silver,
                dark ? Color.FromArgb(62, 62, 66) : Color.White))
                e.Graphics.FillRectangle(hatch, ClientRectangle);
            DrawArrow(e.Graphics, new Rectangle(0, 0, Width, ArrowSize), false, pressed == ScrollPart.Up);
            DrawArrow(e.Graphics, new Rectangle(0, Height - ArrowSize, Width, ArrowSize), true, pressed == ScrollPart.Down);
            if (maximum > 0 && track.Height > 0)
            {
                var thumb = Thumb;
                using (var brush = new SolidBrush(BackColor)) e.Graphics.FillRectangle(brush, thumb);
                RetroButton.DrawRetro3D(e.Graphics, thumb, true, BackColor);
                if (Focused) ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(thumb, -3, -3), ForeColor, BackColor);
            }
        }

        private void DrawArrow(Graphics graphics, Rectangle bounds, bool down, bool isPressed)
        {
            if (bounds.Height <= 0) return;
            using (var face = new SolidBrush(BackColor)) graphics.FillRectangle(face, bounds);
            RetroButton.DrawRetro3D(graphics, bounds, !isPressed, BackColor);
            int x = bounds.Left + bounds.Width / 2 + (isPressed ? 1 : 0);
            int y = bounds.Top + bounds.Height / 2 + (isPressed ? 1 : 0);
            using (var brush = new SolidBrush(Enabled ? ForeColor : ControlPaint.Dark(BackColor)))
            {
                graphics.FillPolygon(brush, down
                    ? new[] { new Point(x - 3, y - 2), new Point(x + 3, y - 2), new Point(x, y + 1) }
                    : new[] { new Point(x - 3, y + 1), new Point(x + 3, y + 1), new Point(x, y - 2) });
            }
        }

        private ScrollPart HitTest(Point point)
        {
            if (!ClientRectangle.Contains(point)) return ScrollPart.None;
            if (point.Y < ArrowSize) return ScrollPart.Up;
            if (point.Y >= Height - ArrowSize) return ScrollPart.Down;
            if (Thumb.Contains(point)) return ScrollPart.Thumb;
            return point.Y < Thumb.Top ? ScrollPart.PageUp : ScrollPart.PageDown;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left || !Enabled) return;
            Focus();
            pressed = HitTest(e.Location);
            dragOffset = e.Y - Thumb.Top;
            Capture = true;
            if (pressed != ScrollPart.Thumb)
            {
                Step();
                repeatTimer.Interval = 350;
                repeatTimer.Start();
            }
            Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (pressed != ScrollPart.Thumb || !Capture) return;
            int travel = Track.Height - Thumb.Height;
            if (travel > 0)
                Value = (int)((long)Math.Max(0, Math.Min(travel, e.Y - dragOffset - Track.Top)) * maximum / travel);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button == MouseButtons.Left) Capture = false;
        }

        protected override void OnMouseCaptureChanged(EventArgs e)
        {
            base.OnMouseCaptureChanged(e);
            if (Capture) return;
            repeatTimer.Stop();
            pressed = ScrollPart.None;
            Invalidate();
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            wheelRemainder += e.Delta;
            int notches = wheelRemainder / SystemInformation.MouseWheelScrollDelta;
            wheelRemainder %= SystemInformation.MouseWheelScrollDelta;
            int lines = SystemInformation.MouseWheelScrollLines;
            Value -= notches * (lines < 0 ? pageSize : lines * SmallChange);
        }

        protected override bool IsInputKey(Keys keyData)
        {
            switch (keyData & Keys.KeyCode)
            {
                case Keys.Up: case Keys.Down: case Keys.PageUp: case Keys.PageDown: case Keys.Home: case Keys.End:
                    return true;
                default: return base.IsInputKey(keyData);
            }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            switch (e.KeyCode)
            {
                case Keys.Up: Value -= SmallChange; break;
                case Keys.Down: Value += SmallChange; break;
                case Keys.PageUp: Value -= pageSize; break;
                case Keys.PageDown: Value += pageSize; break;
                case Keys.Home: Value = 0; break;
                case Keys.End: Value = maximum; break;
                default: return;
            }
            e.Handled = true;
            e.SuppressKeyPress = true;
        }

        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

        private void Step()
        {
            switch (pressed)
            {
                case ScrollPart.Up: Value -= SmallChange; break;
                case ScrollPart.Down: Value += SmallChange; break;
                case ScrollPart.PageUp: Value -= pageSize; break;
                case ScrollPart.PageDown: Value += pageSize; break;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) repeatTimer.Dispose();
            base.Dispose(disposing);
        }
    }

    internal sealed class RetroScrollHost : Panel
    {
        private readonly Panel viewport = new Panel();
        private readonly Padding contentPadding;
        private bool synchronizing;
        private bool synchronizationPending;

        public FlowLayoutPanel Content { get; }
        public RetroScrollBar ScrollBar { get; } = new RetroScrollBar();

        public RetroScrollHost(FlowLayoutPanel content)
        {
            Content = content;
            contentPadding = content.Padding;
            content.Dock = DockStyle.None;
            viewport.Controls.Add(content);
            Controls.Add(viewport);
            Controls.Add(ScrollBar);
            content.Layout += (s, e) => ScheduleSynchronization();
            content.Scroll += (s, e) => UpdateScrollBar();
            content.MouseWheel += (s, e) => UpdateScrollBar();
            content.Paint += (s, e) => UpdateScrollBar();
            ScrollBar.ValueChanged += (s, e) =>
            {
                Content.AutoScrollPosition = new Point(0, ScrollBar.Value);
                UpdateScrollBar();
            };
        }

        public void ApplyTheme(bool dark)
        {
            BackColor = dark ? Color.FromArgb(37, 37, 38) : SystemColors.AppWorkspace;
            viewport.BackColor = BackColor;
            ScrollBar.ApplyTheme(dark);
        }

        protected override void OnLayout(LayoutEventArgs levent)
        {
            base.OnLayout(levent);
            Synchronize();
        }

        private void Synchronize()
        {
            if (synchronizing || Content == null || IsDisposed || Disposing) return;
            synchronizing = true;
            try
            {
                int width = SystemInformation.VerticalScrollBarWidth;
                for (int pass = 0; pass < 3; pass++)
                {
                    bool visible = Content.VerticalScroll.Visible;
                    ScrollBar.Visible = visible;
                    viewport.Bounds = new Rectangle(0, 0, Math.Max(0, ClientSize.Width - (visible ? width : 0)), ClientSize.Height);
                    ScrollBar.Bounds = new Rectangle(viewport.Right, 0, width, ClientSize.Height);
                    Content.Bounds = new Rectangle(0, 0, viewport.Width + width, viewport.Height);
                    Content.Padding = new Padding(contentPadding.Left, contentPadding.Top,
                        contentPadding.Right + (visible ? 0 : width), contentPadding.Bottom);
                    Content.PerformLayout();
                    int contentHeight = 0;
                    int offset = -Content.AutoScrollPosition.Y;
                    foreach (Control tile in Content.Controls)
                        contentHeight = Math.Max(contentHeight, tile.Bottom + offset + tile.Margin.Bottom + contentPadding.Bottom);
                    Content.AutoScrollMinSize = new Size(0, contentHeight);
                }
                UpdateScrollBar();
            }
            finally { synchronizing = false; }
        }

        private void ScheduleSynchronization()
        {
            if (synchronizing || synchronizationPending || !IsHandleCreated || IsDisposed || Disposing) return;
            synchronizationPending = true;
            BeginInvoke(new Action(() =>
            {
                synchronizationPending = false;
                Synchronize();
            }));
        }

        private void UpdateScrollBar()
        {
            if (Content == null || IsDisposed) return;
            int maximum = Content.VerticalScroll.Visible
                ? Math.Max(0, Content.VerticalScroll.Maximum - Content.VerticalScroll.LargeChange + 1) : 0;
            ScrollBar.SetRange(maximum, Content.ClientSize.Height, -Content.AutoScrollPosition.Y);
        }
    }
}
