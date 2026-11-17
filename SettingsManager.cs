using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace KebuzLoncher
{
    public class SettingsManager
    {
        private string appDataDir;
        private string settingsPath;
        private string cacheDir;
        private string backupsDir;

        public SettingsManager()
        {
            LauncherData.Initialize();
            appDataDir = LauncherData.Root;

            settingsPath = Path.Combine(appDataDir, "settings.json");
            cacheDir = Path.Combine(appDataDir, "cache");
            backupsDir = Path.Combine(appDataDir, "backups");

            EnsureDirectories();
        }

        private void EnsureDirectories()
        {
            try
            {
                Directory.CreateDirectory(appDataDir);
                Directory.CreateDirectory(cacheDir);
                Directory.CreateDirectory(backupsDir);
            }
            catch (Exception ex)
            {
                LogError($"Ошибка создания папок: {ex.Message}");
            }
        }

        public LauncherSettings LoadSettings()
        {
            try
            {
                if (File.Exists(settingsPath))
                {
                    string json = File.ReadAllText(settingsPath, Encoding.UTF8);
                    var settings = JsonConvert.DeserializeObject<LauncherSettings>(json);
                    return settings ?? new LauncherSettings();
                }
            }
            catch (Exception ex)
            {
                LogError($"Ошибка загрузки настроек: {ex.Message}");
            }

            return new LauncherSettings();
        }

        public void SaveSettings(LauncherSettings settings)
        {
            try
            {
                string json = JsonConvert.SerializeObject(settings, Formatting.Indented);
                string temporary = settingsPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    File.WriteAllText(temporary, json, new UTF8Encoding(false));
                    if (File.Exists(settingsPath)) File.Replace(temporary, settingsPath, null);
                    else File.Move(temporary, settingsPath);
                }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка сохранения настроек: {ex.Message}", "Ошибка");
                LogError($"Ошибка сохранения настроек: {ex.Message}");
            }
        }

        public string GetBackupsDir()
        {
            return backupsDir;
        }

        public string GetCacheDir()
        {
            return cacheDir;
        }

        public string GetOrSelectGamePath(Game game, LauncherSettings settings)
        {
            // Проверяем, есть ли сохраненный путь для этой игры
            if (settings.GamePaths.ContainsKey(game.id))
            {
                string savedPath = settings.GamePaths[game.id];

                if (!Directory.Exists(savedPath))
                {
                    MessageBox.Show("Сохраненная папка не найдена. Выберите новую папку.", "Информация");
                    return SelectOrDetectGameFolder(game, settings);
                }

                var result = MessageBox.Show(
                    $"Использовать сохраненную папку?\n{savedPath}\n\n" +
                    "Да - использовать сохраненную\nНет - выбрать другую папку",
                    "Установка модов",
                    MessageBoxButtons.YesNoCancel,
                    MessageBoxIcon.Question);

                if (result == DialogResult.Yes)
                    return savedPath;
                else if (result == DialogResult.No)
                    return SelectOrDetectGameFolder(game, settings);
                else
                    return null;
            }
            else
            {
                return SelectOrDetectGameFolder(game, settings);
            }
        }

        private string SelectOrDetectGameFolder(Game game, LauncherSettings settings)
        {
            // Пробуем найти через Steam
            if (game.steam_app_id > 0 && !string.IsNullOrEmpty(game.executable))
            {
                string detected = SteamDetector.FindGamePath(game.steam_app_id, game.executable);
                if (detected != null)
                {
                    var result = MessageBox.Show(
                        $"Игра найдена автоматически:\n{detected}\n\nИспользовать этот путь?",
                        "Обнаружена игра",
                        MessageBoxButtons.YesNoCancel,
                        MessageBoxIcon.Question);

                    if (result == DialogResult.Yes)
                    {
                        settings.GamePaths[game.id] = detected;
                        SaveSettings(settings);
                        return detected;
                    }
                    else if (result == DialogResult.Cancel)
                    {
                        return null;
                    }
                    // Не нашли - идём к ручному выбору
                }
            }

            return SelectGameFolder(game, settings);
        }

        public string SelectGameFolder(Game game, LauncherSettings settings = null)
        {
            using (var dialog = new OpenFileDialog())
            {
                dialog.Title = $"Выберите папку с игрой {game.name}";
                dialog.Filter = "Папки|*.несуществующий"; // Фильтр для папок
                dialog.CheckFileExists = false;
                dialog.CheckPathExists = true;
                dialog.FileName = "Выберите эту папку"; // Подсказка
                dialog.ValidateNames = false;

                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    string selectedPath = Path.GetDirectoryName(dialog.FileName);

                    if (settings != null)
                    {
                        settings.GamePaths[game.id] = selectedPath;
                        SaveSettings(settings);
                    }

                    return selectedPath;
                }
            }
            return null;
        }

        private void LogError(string message)
        {
            try
            {
                Directory.CreateDirectory(Path.Combine(appDataDir, "logs"));
                string logFile = Path.Combine(appDataDir, "logs", "launcher.log");
                string logEntry = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - {message}{Environment.NewLine}";
                File.AppendAllText(logFile, logEntry, Encoding.UTF8);
            }
            catch
            {
                // Игнор
            }
        }

        // Метод для очистки кэша
        public void ClearCache()
        {
            try
            {
                if (Directory.Exists(cacheDir))
                {
                    Directory.Delete(cacheDir, true);
                    Directory.CreateDirectory(cacheDir);
                }
            }
            catch (Exception ex)
            {
                LogError($"Ошибка очистки кэша: {ex.Message}");
            }
        }
    }
}
