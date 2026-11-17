using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace KebuzLoncher
{
    internal static class LegalNotices
    {
        internal static string ReadText()
        {
            var assembly = typeof(LegalNotices).Assembly;
            var text = new StringBuilder();
            foreach (string name in assembly.GetManifestResourceNames().Where(name => name.StartsWith("KebuzLoncher.Licenses.", StringComparison.Ordinal)).OrderBy(name => name))
            {
                using (var stream = assembly.GetManifestResourceStream(name))
                using (var reader = new StreamReader(stream, Encoding.UTF8))
                    text.AppendLine(reader.ReadToEnd()).AppendLine();
            }
            return text.ToString();
        }

        public static Form Create(string theme)
        {
            var form = new Form { Text = ModMessages.Get("LegalNoticesTitle"), Size = new Size(760, 580), MinimumSize = new Size(480, 320), StartPosition = FormStartPosition.CenterParent };
            var content = new TextBox { Name = "legalText", Text = ReadText(), Dock = DockStyle.Fill, Multiline = true, ReadOnly = true,
                WordWrap = true, ScrollBars = ScrollBars.Vertical, Font = new Font("Consolas", 10f) };
            var close = new Button { Text = ModMessages.Get("CloseLegalNotices"), Dock = DockStyle.Bottom, Height = 32, DialogResult = DialogResult.Cancel };
            form.Controls.Add(content);
            form.Controls.Add(close);
            form.CancelButton = close;
            ThemeManager.Apply(form, theme);
            return form;
        }
    }
}
