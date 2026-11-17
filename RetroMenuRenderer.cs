using System.Drawing;
using System.Windows.Forms;

namespace KebuzLoncher
{
    internal sealed class RetroMenuRenderer : ToolStripSystemRenderer
    {
        private readonly Color face;
        private readonly Color text;
        private readonly Color selection;

        public RetroMenuRenderer(bool dark)
        {
            face = dark ? Color.FromArgb(45, 45, 48) : Color.Silver;
            text = dark ? Color.Gainsboro : Color.Black;
            selection = dark ? Color.FromArgb(0, 70, 140) : Color.Navy;
        }

        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            using (var brush = new SolidBrush(face)) e.Graphics.FillRectangle(brush, e.AffectedBounds);
        }

        protected override void OnRenderImageMargin(ToolStripRenderEventArgs e)
        {
            using (var brush = new SolidBrush(face)) e.Graphics.FillRectangle(brush, e.AffectedBounds);
        }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            var bounds = new Rectangle(Point.Empty, e.Item.Size);
            bool dropdown = e.ToolStrip is ToolStripDropDown;
            using (var brush = new SolidBrush(dropdown && e.Item.Selected && e.Item.Enabled ? selection : face))
                e.Graphics.FillRectangle(brush, bounds);
            if (!dropdown && (e.Item.Selected || e.Item.Pressed))
                RetroButton.DrawRetro3D(e.Graphics, bounds, !e.Item.Pressed, face);
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = !e.Item.Enabled ? ControlPaint.Dark(face)
                : e.ToolStrip is ToolStripDropDown && e.Item.Selected ? Color.White : text;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            var bounds = e.ImageRectangle;
            int x = bounds.Left + bounds.Width / 2;
            int y = bounds.Top + bounds.Height / 2;
            using (var pen = new Pen(e.Item.Selected ? Color.White : text, 2))
                e.Graphics.DrawLines(pen, new[] { new Point(x - 4, y), new Point(x - 1, y + 3), new Point(x + 5, y - 4) });
        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = e.Item.Selected ? Color.White : text;
            base.OnRenderArrow(e);
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            int y = e.Item.Height / 2;
            using (var shadow = new Pen(ControlPaint.Dark(face)))
            using (var highlight = new Pen(ControlPaint.LightLight(face)))
            {
                e.Graphics.DrawLine(shadow, 2, y, e.Item.Width - 3, y);
                e.Graphics.DrawLine(highlight, 2, y + 1, e.Item.Width - 3, y + 1);
            }
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            if (e.ToolStrip is ToolStripDropDown)
                RetroButton.DrawRetro3D(e.Graphics, e.ToolStrip.ClientRectangle, true, face);
        }
    }
}
