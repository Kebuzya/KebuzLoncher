using System.Drawing;
using System.Windows.Forms;

namespace KebuzLoncher
{
    public enum AppTheme { Dark, Classic }

    public static class ThemeManager
    {
        // Цвета тёмной темы
        private static readonly Color DarkBackground    = Color.FromArgb(45, 45, 48);
        private static readonly Color DarkPanel         = Color.FromArgb(37, 37, 38);
        private static readonly Color DarkTile          = Color.FromArgb(62, 62, 66);
        private static readonly Color DarkForeground    = Color.White;
        private static readonly Color DarkSubtext       = Color.LightGray;
        private static readonly Color DarkButtonPrimary = Color.FromArgb(83, 83, 87);
        private static readonly Color DarkButtonSecond  = Color.FromArgb(100, 100, 100);
        private static readonly Color DarkButtonInfo    = Color.FromArgb(70, 70, 74);
        private static readonly Color DarkStatusBar     = Color.FromArgb(0, 0, 128);

        // Цвета классической темы
        private static readonly Color ClassicBackground = Color.Silver;
        private static readonly Color ClassicForeground = SystemColors.ControlText;
        private static readonly Color ClassicStatusBar  = Color.FromArgb(0, 0, 128);
        private static readonly Font MenuFont = new Font("MS Sans Serif", 8f);

        public static void Apply(Form form, AppTheme theme)
        {
            if (theme == AppTheme.Dark)
                ApplyDark(form);
            else
                ApplyClassic(form);
        }

        public static void Apply(Form form, string themeName)
        {
            Apply(form, themeName == "Classic" ? AppTheme.Classic : AppTheme.Dark);
        }

        // Тёмная

        private static void ApplyDark(Form form)
        {
            form.BackColor = DarkBackground;
            form.ForeColor = DarkForeground;

            foreach (Control ctrl in form.Controls)
                ApplyDarkControl(ctrl);
        }

        private static void ApplyDarkControl(Control ctrl)
        {
            switch (ctrl)
            {
                case Win98TitleBar title:
                    title.UpdateTheme("Dark");
                    return;

                case RetroScrollHost host:
                    host.ApplyTheme(true);
                    ApplyDarkControl(host.Content);
                    return;

                case MenuStrip menu:
                    menu.BackColor = DarkBackground;
                    menu.ForeColor = DarkForeground;
                    menu.Font = MenuFont;
                    menu.Renderer  = new RetroMenuRenderer(true);
                    foreach (ToolStripMenuItem item in menu.Items)
                        ApplyDarkMenuItem(item);
                    break;

                case StatusStrip status:
                    status.BackColor = DarkStatusBar;
                    status.ForeColor = DarkForeground;
                    break;

                case Button button:
                    button.BackColor = DarkButtonInfo;
                    button.ForeColor = DarkForeground;
                    button.UseVisualStyleBackColor = false;
                    break;

                case FlowLayoutPanel panel when panel.Name == "gamesPanel":
                    panel.BackColor = DarkPanel;
                    foreach (Control tile in panel.Controls)
                        ApplyDarkTile(tile);
                    break;

                case FlowLayoutPanel panel:
                    panel.BackColor = DarkBackground;
                    foreach (Control c in panel.Controls)
                        ApplyDarkControl(c);
                    break;

                case Panel panel:
                    panel.BackColor = DarkBackground;
                    foreach (Control c in panel.Controls)
                        ApplyDarkControl(c);
                    break;
            }
        }

        private static void ApplyDarkTile(Control tile)
        {
            if (!(tile is Panel tilePanel)) return;
            tilePanel.BackColor   = DarkTile;
            tilePanel.BorderStyle = BorderStyle.None;

            foreach (Control c in tilePanel.Controls)
                ApplyDarkTileChild(c);
        }

        private static void ApplyDarkTileChild(Control c)
        {
            ApplyContextMenu(c.ContextMenuStrip, true);
            var boldFont  = new Font("MS Sans Serif", 10f, FontStyle.Bold);
            var smallFont = new Font("MS Sans Serif", 8f);

            switch (c)
            {
                case Panel panel when panel.Name == "modpackWarning":
                    ModpackNotice.ApplyTheme(panel, true);
                    break;

                case Panel panel:
                    panel.BackColor = Color.Black;
                    foreach (Control child in panel.Controls)
                        ApplyDarkTileChild(child);
                    break;

                case PictureBox pic:
                    pic.BackColor = Color.Black;
                    break;

                case Label lbl when lbl.BackColor == Color.OrangeRed:
                    break;

                // Название игры 
                case Label lbl when lbl.Font.Size >= 10:
                    lbl.ForeColor = DarkForeground;
                    lbl.BackColor = Color.Transparent;
                    lbl.Font      = boldFont;
                    break;

                // Версия/описание
                case Label lbl:
                    lbl.ForeColor = DarkSubtext;
                    lbl.BackColor = Color.Transparent;
                    lbl.Font      = smallFont;
                    break;

                case Button btn when btn.Text.Contains("Установить"):
                    btn.BackColor = DarkButtonPrimary;
                    btn.ForeColor = DarkForeground;
                    btn.Font      = smallFont;
                    break;

                case Button btn when btn.Text.Contains("Сменить") || btn.Text.Contains("папку"):
                    btn.BackColor = DarkButtonSecond;
                    btn.ForeColor = DarkForeground;
                    btn.Font      = smallFont;
                    break;

                case Button btn:
                    btn.BackColor = DarkButtonInfo;
                    btn.ForeColor = DarkForeground;
                    btn.Font      = smallFont;
                    break;
            }
        }

        private static void ApplyDarkMenuItem(ToolStripMenuItem item)
        {
            item.BackColor = DarkBackground;
            item.ForeColor = DarkForeground;
            foreach (ToolStripItem sub in item.DropDownItems)
            {
                sub.BackColor = Color.FromArgb(62, 62, 66);
                sub.ForeColor = DarkForeground;
                if (sub is ToolStripMenuItem subItem)
                    ApplyDarkMenuItem(subItem);
            }
        }

        // Классический

        private static void ApplyClassic(Form form)
        {
            form.BackColor = ClassicBackground;
            form.ForeColor = ClassicForeground;

            foreach (Control ctrl in form.Controls)
                ApplyClassicControl(ctrl);
        }

        private static void ApplyClassicControl(Control ctrl)
        {
            switch (ctrl)
            {
                case Win98TitleBar title:
                    title.UpdateTheme("Classic");
                    return;

                case RetroScrollHost host:
                    host.ApplyTheme(false);
                    ApplyClassicControl(host.Content);
                    return;

                case MenuStrip menu:
                    menu.BackColor   = ClassicBackground;
                    menu.ForeColor   = ClassicForeground;
                    menu.Font = MenuFont;
                    menu.Renderer = new RetroMenuRenderer(false);
                    foreach (ToolStripMenuItem item in menu.Items)
                        ApplyClassicMenuItem(item);
                    break;

                case StatusStrip status:
                    status.BackColor = ClassicStatusBar;
                    status.ForeColor = Color.White;
                    break;

                case Button button:
                    button.BackColor = ClassicBackground;
                    button.ForeColor = ClassicForeground;
                    button.UseVisualStyleBackColor = false;
                    break;

                case FlowLayoutPanel panel when panel.Name == "gamesPanel":
                    panel.BackColor = SystemColors.AppWorkspace;
                    foreach (Control tile in panel.Controls)
                        ApplyClassicTile(tile);
                    break;

                case FlowLayoutPanel panel:
                    panel.BackColor = ClassicBackground;
                    foreach (Control c in panel.Controls)
                        ApplyClassicControl(c);
                    break;

                case Panel panel:
                    panel.BackColor = ClassicBackground;
                    foreach (Control c in panel.Controls)
                        ApplyClassicControl(c);
                    break;
            }
        }

        private static void ApplyClassicTile(Control tile)
        {
            if (!(tile is Panel tilePanel)) return;
            tilePanel.BackColor   = ClassicBackground;
            tilePanel.BorderStyle = BorderStyle.None;

            foreach (Control c in tilePanel.Controls)
                ApplyClassicTileChild(c);
        }

        private static void ApplyClassicTileChild(Control c)
        {
            ApplyContextMenu(c.ContextMenuStrip, false);
            var boldFont  = new Font("MS Sans Serif", 10f, FontStyle.Bold);
            var smallFont = new Font("MS Sans Serif", 8f);

            switch (c)
            {
                case Panel panel when panel.Name == "modpackWarning":
                    ModpackNotice.ApplyTheme(panel, false);
                    break;

                case Panel panel:
                    panel.BackColor = Color.Silver;
                    foreach (Control child in panel.Controls)
                        ApplyClassicTileChild(child);
                    break;

                case PictureBox pic:
                    pic.BackColor = Color.Silver;
                    break;

                // Бейдж не трогаем
                case Label lbl when lbl.BackColor == Color.OrangeRed:
                    break;

                // Название игры
                case Label lbl when lbl.Font.Size >= 10:
                    lbl.ForeColor = Color.Navy;
                    lbl.BackColor = Color.Transparent;
                    lbl.Font      = boldFont;
                    break;

                // Версия / описание
                case Label lbl:
                    lbl.ForeColor = ClassicForeground;
                    lbl.BackColor = Color.Transparent;
                    lbl.Font      = smallFont;
                    break;

                case Button btn:
                    btn.BackColor = ClassicBackground;
                    btn.ForeColor = ClassicForeground;
                    btn.Font      = smallFont;
                    break;
            }
        }

        private static void ApplyContextMenu(ContextMenuStrip menu, bool dark)
        {
            if (menu == null) return;
            menu.Renderer = new RetroMenuRenderer(dark);
            menu.Font = MenuFont;
            menu.BackColor = dark ? DarkBackground : ClassicBackground;
            menu.ForeColor = dark ? DarkForeground : ClassicForeground;
        }

        private static void ApplyClassicMenuItem(ToolStripMenuItem item)
        {
            item.BackColor = ClassicBackground;
            item.ForeColor = ClassicForeground;
            foreach (ToolStripItem sub in item.DropDownItems)
            {
                sub.BackColor = ClassicBackground;
                sub.ForeColor = ClassicForeground;
                if (sub is ToolStripMenuItem subItem)
                    ApplyClassicMenuItem(subItem);
            }
        }
    }

}
