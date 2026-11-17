using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace KebuzLoncher
{
    internal static class LegalNotices
    {
        private static readonly string[] documents = { "Launcher", "Newtonsoft.Json", "Fody", "Costura.Fody", "Notices" };

        private static string ReadDocument(string name)
        {
            using (var stream = typeof(LegalNotices).Assembly.GetManifestResourceStream("KebuzLoncher.Licenses." + name + ".txt"))
            using (var reader = new StreamReader(stream, Encoding.UTF8))
                return reader.ReadToEnd().Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", Environment.NewLine);
        }

        private static string Title(string name) => name == "Launcher" ? ModMessages.Get("LauncherLicenseTitle") :
            name == "Notices" ? ModMessages.Get("ThirdPartyNoticesTitle") : name;

        internal static string ReadText()
        {
            var text = new StringBuilder();
            foreach (string name in documents) text.AppendLine(ReadDocument(name)).AppendLine();
            return text.ToString();
        }

        private static void AppendLine(RichTextBox reader, string text, bool heading = false)
        {
            using (var font = new Font(reader.Font.FontFamily, heading ? 11f : reader.Font.Size, heading ? FontStyle.Bold : FontStyle.Regular))
            {
                reader.SelectionFont = font;
                reader.AppendText(text + Environment.NewLine);
            }
        }

        private static void ShowDocument(RichTextBox reader, string name)
        {
            reader.Clear();
            reader.AccessibleName = Title(name);
            AppendLine(reader, Title(name), true);
            AppendLine(reader, "");
            string text = ReadDocument(name);
            if (name != "Notices") reader.AppendText(text);
            else
            {
                string[] lines = text.Split(new[] { Environment.NewLine }, StringSplitOptions.None);
                bool tableHeader = true;
                foreach (string line in lines.Skip(lines[0].StartsWith("# ", StringComparison.Ordinal) ? 1 : 0))
                {
                    string value = line.Replace("`", "");
                    if (value.StartsWith("|", StringComparison.Ordinal))
                    {
                        var cells = value.Trim('|').Split('|').Select(cell => cell.Trim()).ToArray();
                        if (cells.All(cell => cell.Trim('-', ':', ' ').Length == 0)) { tableHeader = false; continue; }
                        if (tableHeader) continue;
                        AppendLine(reader, string.Join("   ", cells.Take(3)), true);
                        foreach (string cell in cells.Skip(3)) AppendLine(reader, cell);
                        AppendLine(reader, "");
                    }
                    else
                    {
                        tableHeader = true;
                        if (value.StartsWith("#", StringComparison.Ordinal)) AppendLine(reader, value.TrimStart('#', ' '), true);
                        else AppendLine(reader, value);
                    }
                }
            }
            reader.SelectionStart = 0;
            reader.SelectionLength = 0;
            reader.ScrollToCaret();
        }

        public static Form Create(string theme)
        {
            bool dark = theme != "Classic";
            var form = new Form { Text = ModMessages.Get("LegalNoticesTitle"), Size = new Size(900, 600), MinimumSize = new Size(640, 380), StartPosition = FormStartPosition.CenterParent,
                Icon = Icon.ExtractAssociatedIcon(typeof(LegalNotices).Assembly.Location) };
            var split = new SplitContainer { Dock = DockStyle.Fill, Size = new Size(884, 520), FixedPanel = FixedPanel.Panel1, SplitterDistance = 190, Panel1MinSize = 160, Panel2MinSize = 300 };
            var selection = new ListBox { Name = "legalDocuments", Dock = DockStyle.Fill, IntegralHeight = false, Font = new Font("MS Sans Serif", 9f),
                AccessibleName = ModMessages.Get("LicenseDocuments"), BackColor = dark ? Color.FromArgb(45, 45, 48) : Color.Silver, ForeColor = dark ? Color.White : Color.Black };
            selection.Items.AddRange(documents.Select(Title).Cast<object>().ToArray());
            var content = new RichTextBox { Name = "legalText", Dock = DockStyle.Fill, ReadOnly = true, BorderStyle = BorderStyle.None,
                DetectUrls = true, WordWrap = true, ScrollBars = RichTextBoxScrollBars.Vertical, Font = new Font("Segoe UI", 10f),
                BackColor = dark ? Color.FromArgb(37, 37, 38) : Color.White, ForeColor = dark ? Color.Gainsboro : Color.Black };
            content.Resize += (s, e) => content.RightMargin = Math.Max(1, Math.Min(840, content.ClientSize.Width - 16));
            content.LinkClicked += (s, e) =>
            {
                if (Uri.TryCreate(e.LinkText, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps)
                    Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
            };
            selection.SelectedIndexChanged += (s, e) => { if (selection.SelectedIndex >= 0) ShowDocument(content, documents[selection.SelectedIndex]); };
            split.Panel1.Controls.Add(selection);
            split.Panel2.Padding = new Padding(12);
            split.Panel2.BackColor = content.BackColor;
            split.Panel2.Controls.Add(content);
            var close = new Button { Text = ModMessages.Get("CloseLegalNotices"), Dock = DockStyle.Bottom, Height = 32, DialogResult = DialogResult.Cancel };
            form.Controls.Add(split);
            form.Controls.Add(close);
            form.CancelButton = close;
            ThemeManager.Apply(form, theme);
            selection.SelectedIndex = 0;
            return form;
        }
    }
}
