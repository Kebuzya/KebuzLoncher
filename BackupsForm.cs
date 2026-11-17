using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;

namespace KebuzLoncher
{
    public class BackupsForm : Form
    {
        private ListView listView;
        private Button openBtn;
        private Button deleteBtn;
        private Button closeBtn;
        private Label infoLabel;

        private readonly string backupsDir;

        public BackupsForm(string backupsDir)
        {
            this.backupsDir = backupsDir;
            InitializeComponent();
            LoadBackups();
        }

        private void InitializeComponent()
        {
            this.Text = "Управление бекапами";
            this.Size = new System.Drawing.Size(600, 420);
            this.StartPosition = FormStartPosition.CenterParent;
            this.MinimizeBox = false;
            this.MaximizeBox = false;

            listView = new ListView();
            listView.View = View.Details;
            listView.FullRowSelect = true;
            listView.GridLines = true;
            listView.Columns.Add("Дата", 160);
            listView.Columns.Add("Игра", 160);
            listView.Columns.Add("Размер", 90);
            listView.Columns.Add("Путь", 150);
            listView.Location = new System.Drawing.Point(10, 10);
            listView.Size = new System.Drawing.Size(565, 310);
            listView.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
            listView.SelectedIndexChanged += (s, e) => UpdateButtons();

            infoLabel = new Label();
            infoLabel.Text = "";
            infoLabel.Location = new System.Drawing.Point(10, 325);
            infoLabel.Size = new System.Drawing.Size(565, 20);
            infoLabel.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;

            openBtn = new Button();
            openBtn.Text = "Открыть в проводнике";
            openBtn.Size = new System.Drawing.Size(170, 28);
            openBtn.Location = new System.Drawing.Point(10, 350);
            openBtn.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            openBtn.Enabled = false;
            openBtn.Click += OpenBtn_Click;

            deleteBtn = new Button();
            deleteBtn.Text = "Удалить";
            deleteBtn.Size = new System.Drawing.Size(100, 28);
            deleteBtn.Location = new System.Drawing.Point(190, 350);
            deleteBtn.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            deleteBtn.Enabled = false;
            deleteBtn.Click += DeleteBtn_Click;

            closeBtn = new Button();
            closeBtn.Text = "Закрыть";
            closeBtn.Size = new System.Drawing.Size(100, 28);
            closeBtn.Location = new System.Drawing.Point(475, 350);
            closeBtn.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            closeBtn.Click += (s, e) => this.Close();

            this.Controls.AddRange(new Control[] { listView, infoLabel, openBtn, deleteBtn, closeBtn });
        }

        private void LoadBackups()
        {
            listView.Items.Clear();

            if (!Directory.Exists(backupsDir))
            {
                infoLabel.Text = "Папка бекапов не найдена.";
                return;
            }

            string[] dirs = Directory.GetDirectories(backupsDir);
            long totalSize = 0;

            foreach (string dir in dirs)
            {
                string folderName = Path.GetFileName(dir);
                string gameId = ParseGameId(folderName);
                string dateStr = ParseDate(folderName);
                long sizeBytes = GetDirectorySize(dir);
                totalSize += sizeBytes;

                var item = new ListViewItem(dateStr);
                item.SubItems.Add(gameId);
                item.SubItems.Add(FormatSize(sizeBytes));
                item.SubItems.Add(dir);
                item.Tag = dir;
                listView.Items.Add(item);
            }

            infoLabel.Text = $"Всего бекапов: {dirs.Length}, суммарный размер: {FormatSize(totalSize)}";
        }

        private string ParseGameId(string folderName)
        {
            // Формат: {gameId}_backup_{timestamp} или backup_{timestamp}
            int idx = folderName.IndexOf("_backup_");
            if (idx > 0)
                return folderName.Substring(0, idx);
            return "—";
        }

        private string ParseDate(string folderName)
        {
            // Парсим дату из {something}_backup_YYYYMMDD_HHmmss или backup_YYYYMMDD_HHmmss
            int prefixLen = folderName.IndexOf("backup_");
            if (prefixLen < 0) return folderName;

            string datePart = folderName.Substring(prefixLen + 7); // после "backup_"
            if (datePart.Length >= 15 &&
                DateTime.TryParseExact(datePart.Substring(0, 15), "yyyyMMdd_HHmmss",
                    null, System.Globalization.DateTimeStyles.None, out DateTime dt))
            {
                return dt.ToString("dd.MM.yyyy HH:mm:ss");
            }

            return folderName;
        }

        private long GetDirectorySize(string path)
        {
            try
            {
                long size = 0;
                foreach (string file in Directory.GetFiles(path, "*", SearchOption.AllDirectories))
                    size += new FileInfo(file).Length;
                return size;
            }
            catch { return 0; }
        }

        private string FormatSize(long bytes)
        {
            if (bytes < 1024) return $"{bytes} Б";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:0.0} КБ";
            if (bytes < 1024 * 1024 * 1024) return $"{bytes / 1024.0 / 1024:0.0} МБ";
            return $"{bytes / 1024.0 / 1024 / 1024:0.0} ГБ";
        }

        private void UpdateButtons()
        {
            bool hasSelection = listView.SelectedItems.Count > 0;
            openBtn.Enabled = hasSelection;
            deleteBtn.Enabled = hasSelection;
        }

        private void OpenBtn_Click(object sender, EventArgs e)
        {
            if (listView.SelectedItems.Count == 0) return;
            string path = listView.SelectedItems[0].Tag as string;
            if (Directory.Exists(path))
                Process.Start("explorer.exe", path);
        }

        private void DeleteBtn_Click(object sender, EventArgs e)
        {
            if (listView.SelectedItems.Count == 0) return;
            string path = listView.SelectedItems[0].Tag as string;
            string name = listView.SelectedItems[0].Text;

            var confirm = MessageBox.Show(
                $"Удалить бекап от {name}?\n{path}",
                "Подтверждение",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);

            if (confirm != DialogResult.Yes) return;

            try
            {
                Directory.Delete(path, true);
                LoadBackups();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Не удалось удалить: {ex.Message}", "Ошибка",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
