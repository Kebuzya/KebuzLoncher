using System;
using System.Drawing;
using System.Windows.Forms;

namespace KebuzLoncher
{
    internal static class ModpackNotice
    {
        private static string Status(Game game) => (game.mod_pack_status ?? "").Trim().ToLowerInvariant();
        public static bool HasWarning(Game game) => Status(game).Length > 0 && Status(game) != "none";

        public static string Title(Game game)
        {
            switch (Status(game))
            {
                case "outdated": return ModMessages.Get("ModpackOutdatedTitle");
                case "broken": return ModMessages.Get("ModpackBrokenTitle");
                default: return ModMessages.Get("ModpackUnverifiedTitle");
            }
        }

        private static string Message(Game game)
        {
            if (!string.IsNullOrWhiteSpace(game.mod_pack_warning)) return game.mod_pack_warning.Trim();
            switch (Status(game))
            {
                case "outdated": return ModMessages.Get("ModpackOutdatedMessage");
                case "broken": return ModMessages.Get("ModpackBrokenMessage");
                default: return ModMessages.Get("ModpackUnverifiedMessage");
            }
        }

        public static string FullText(Game game) => Title(game) + Environment.NewLine + Message(game);

        public static Panel CreatePanel(Game game, int width)
        {
            var panel = new Panel
            {
                Name = "modpackWarning",
                Tag = Status(game),
                Size = new Size(width, 22),
                Padding = new Padding(5, 2, 5, 2),
                AccessibleDescription = FullText(game)
            };
            var title = new Label { Name = "warningTitle", Text = Title(game), Dock = DockStyle.Fill,
                Font = new Font("MS Sans Serif", 8f, FontStyle.Bold), AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft };
            panel.Controls.Add(title);
            panel.Paint += (s, e) => RetroButton.DrawRetro3D(e.Graphics, panel.ClientRectangle, false, panel.BackColor);
            return panel;
        }

        public static void ApplyTheme(Panel panel, bool dark)
        {
            bool broken = (string)panel.Tag == "broken";
            panel.BackColor = broken
                ? (dark ? Color.FromArgb(75, 40, 40) : Color.FromArgb(255, 225, 225))
                : (dark ? Color.FromArgb(65, 55, 32) : Color.FromArgb(255, 245, 205));
            var foreground = broken ? (dark ? Color.LightSalmon : Color.Maroon) : (dark ? Color.Khaki : Color.FromArgb(105, 70, 0));
            foreach (Control child in panel.Controls)
            {
                child.BackColor = Color.Transparent;
                child.ForeColor = foreground;
            }
        }
    }
}
